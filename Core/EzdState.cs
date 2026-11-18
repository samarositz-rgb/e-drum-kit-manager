using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace TD12KitManager;

/// <summary>
/// Reads and writes EZdrummer 3 plugin state as REAPER stores it.
/// Layout: [REAPER pin header, ends with u32 LE state length][state][0 + preset name + 5 zero bytes].
/// The state holds VST chunk wrappers (VstW/CcnK/FBCh) around EZdrummer's plain-text settings,
/// which include the kit (instbox 0-12), mixer and the TD-12 e-drum map.
/// </summary>
public sealed class EzdState
{
    static readonly Encoding Latin1 = Encoding.Latin1;

    readonly byte[] header;
    readonly byte[] state;
    readonly int textStart;
    readonly int textEnd;

    public EzdState(byte[] blob)
    {
        int vst = IndexOf(blob, "VstW"u8, 0);
        if (vst < 8) throw new InvalidDataException("Not an EZdrummer state (no VstW chunk).");
        int headerLen = vst - 8;
        header = blob[..headerLen];
        int stateLen = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(headerLen - 12));
        if (headerLen + stateLen > blob.Length) throw new InvalidDataException("EZdrummer state is truncated.");
        state = blob[headerLen..(headerLen + stateLen)];
        textStart = IndexOf(state, "PlugVersion"u8, 0);
        if (textStart < 0) throw new InvalidDataException("EZdrummer settings text not found.");
        textEnd = Array.IndexOf(state, (byte)0, textStart);
        if (textEnd < 0) throw new InvalidDataException("EZdrummer settings text is not terminated.");
    }

    public string Text => Latin1.GetString(state, textStart, textEnd - textStart);

    public bool HasEdrumMap => Text.Contains("EdrumPreset ");

    public string? EdrumMapName => Regex.Match(Text, "^EdrumPreset \\d+ \"([^\"]*)\"", RegexOptions.Multiline) is { Success: true } m
        ? m.Groups[1].Value : null;

    public byte[] ToBlob(string presetName) => [.. Build(Text, presetName).SelectMany(r => r)];

    /// <summary>Builds REAPER chunk base64 lines for this state with the kit's drums swapped in.</summary>
    public string[] BuildChunkLines(Kit kit, ModuleProfile profile)
    {
        string text = Text;
        for (int i = 0; i < Catalog.Slots.Length; i++)
        {
            var slot = kit.Slots[i];
            var drum = Catalog.Find(slot.Lib, slot.Drum)
                ?? throw new InvalidDataException($"Unknown drum {slot.Drum} in {kit.Name}.");
            var (start, end) = FindInstbox(text, i);
            // EZdrummer fills in mics and articulations itself when it loads a minimal block.
            string block = $"instbox {i} {{\nxpad 0 {drum.Lib} {{\npos \"{drum.Pos}\"\ndrum \"{drum.Id}\"\n}}\n}}";
            text = text[..start] + block + text[end..];
        }
        // The built-in TD-12 profile keeps an e-drum map the player made in EZdrummer (any map not named like the shipped
        // default). With the shipped default, it gets the map generated from the TD-12 notes, saved as a preset EZdrummer knows.
        if (profile.WriteMap || (profile.Name == "Roland TD-12" && !Regex.IsMatch(text, "^EdrumPreset \\d+ \"(?!Roland TD-12 Map\")", RegexOptions.Multiline)))
            text = ApplyMap(text, profile);
        text = ApplyOneShot(text, kit, profile);
        text = ApplyTuning(text, kit);
        text = ScaleMixer(text, ["Amb", "AmbR", "AmbMono"], kit.Room / 100.0);
        text = ScaleMixer(text, ["Oh", "OhR", "OhMono"], kit.Overheads / 100.0);
        return ToChunkLines(text, kit.Name);
    }

    /// <summary>
    /// Puts a percussion one-shot in instbox 15 and points the chosen pad's notes at it
    /// (tested: "usermap note oneshot 56" plays it).
    /// </summary>
    static string ApplyOneShot(string text, Kit kit, ModuleProfile profile)
    {
        if (string.IsNullOrEmpty(kit.OneShot)) return text;
        var pad = profile.Pads().FirstOrDefault(p => p.Name == kit.OneShotPad && p.CanPlayOneShot);
        if (pad == null) return text;
        try
        {
            var (start, end) = FindInstbox(text, 15);
            text = text[..start] + $"instbox 15 {{\nxpad 0 {Catalog.PercussionLib} {{\npos \"Oneshot\"\ndrum \"{kit.OneShot}\"\n}}\n}}" + text[end..];
        }
        catch (InvalidDataException) { return text; }
        foreach (int note in pad.Notes)
        {
            var line = Regex.Match(text, $"^(usermap|map) {note} [^\\n]*$", RegexOptions.Multiline);
            string replacement = $"usermap {note} oneshot 56 -1";
            if (line.Success) text = text.Remove(line.Index, line.Length).Insert(line.Index, replacement);
            else text = InsertMapLine(text, replacement);
        }
        return text;
    }

    /// <summary>Replaces the note lines of the e-drum map with the module's learned notes, keeping its other settings.</summary>
    static string ApplyMap(string text, ModuleProfile profile)
    {
        var block = Regex.Match(text, @"^EdrumPreset \d+ ""[^""]*"" \{\n", RegexOptions.Multiline);
        if (!block.Success) return text;
        int start = block.Index + block.Length, depth = 1, end = start;
        for (; end < text.Length && depth > 0; end++)
        {
            if (text[end] == '{') depth++;
            else if (text[end] == '}') depth--;
        }
        string body = text[start..(end - 1)];
        var kept = body.Split('\n').Where(l => !Regex.IsMatch(l, @"^(usermap|map|note) ")).ToList();
        string newBody = string.Join("\n", profile.MapLines().Concat(kept.Where(l => l.Length > 0))) + "\n";
        // EZdrummer only applies a map to its sound engine when the state names a saved preset it knows. It names a saved
        // preset by its file name with spaces and punctuation removed, so use the same id and make sure the file exists.
        // Windows keeps the original name, which is what its EZdrummer has always been given.
        string id = OperatingSystem.IsMacOS()
            ? Regex.Replace($"{profile.Name} (Kit Manager)", "[^A-Za-z0-9-]", "")
            : $"{profile.Name} (Kit Manager)";
        EdrumPresetFile.Ensure(id, newBody);
        string header = $"EdrumPreset 1 \"{id}\" {{\n";
        return text[..block.Index] + header + newBody + text[(end - 1)..];
    }

    /// <summary>Adds a line at the start of the e-drum map block.</summary>
    static string InsertMapLine(string text, string line)
    {
        var block = Regex.Match(text, @"^EdrumPreset \d+ ""[^""]*"" \{\n", RegexOptions.Multiline);
        return block.Success ? text.Insert(block.Index + block.Length, line + "\n") : text;
    }

    /// <summary>Per-drum tuning lives in drumProps3 as "index pitch ratio" (1 = as recorded).</summary>
    static string ApplyTuning(string text, Kit kit)
    {
        var block = Regex.Match(text, @"^drumProps3 \{\n(.*?)^\}", RegexOptions.Multiline | RegexOptions.Singleline);
        if (!block.Success) return text;
        string body = block.Groups[1].Value;
        for (int i = 0; i < kit.Slots.Count; i++)
        {
            string ratio = Math.Pow(2, kit.Slots[i].Tune / 12.0).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
            string line = $"{i} pitch {ratio}";
            var existing = Regex.Match(body, $"^{i} pitch [^\\n]*$", RegexOptions.Multiline);
            body = existing.Success ? body.Remove(existing.Index, existing.Length).Insert(existing.Index, line) : body + line + "\n";
        }
        var g = block.Groups[1];
        return text[..g.Index] + body + text[(g.Index + g.Length)..];
    }

    /// <summary>Multiplies the volume of the named EZdrummer mixer channels.</summary>
    static string ScaleMixer(string text, string[] channels, double factor)
    {
        if (Math.Abs(factor - 1) < 0.001) return text;
        foreach (var name in channels)
        {
            text = Regex.Replace(text, $"(\\n{name} \\{{\\npan [^\\n]*\\nvolume )([-0-9.eE+]+)", m =>
            {
                double v = double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                return m.Groups[1].Value + (v * factor).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
            });
        }
        return text;
    }

    /// <summary>Reads which drum is in each of the 13 kit positions.</summary>
    public List<KitSlot> ReadSlots()
    {
        string text = Text;
        var slots = new List<KitSlot>();
        for (int i = 0; i < Catalog.Slots.Length; i++)
        {
            var (start, end) = FindInstbox(text, i);
            string block = text[start..end];
            slots.Add(new KitSlot
            {
                Lib = Regex.Match(block, @"xpad 0 (\S+)").Groups[1].Value,
                Drum = Regex.Match(block, "drum \"([^\"]+)\"").Groups[1].Value,
            });
        }
        return slots;
    }

    string[] ToChunkLines(string text, string presetName)
    {
        var lines = new List<string>();
        foreach (var run in Build(text, presetName))
        {
            string b64 = Convert.ToBase64String(run);
            for (int i = 0; i < b64.Length; i += 128)
                lines.Add(b64.Substring(i, Math.Min(128, b64.Length - i)));
        }
        return [.. lines];
    }

    /// <summary>Returns the three runs REAPER stores: pin header, plugin state, preset name trailer.</summary>
    byte[][] Build(string text, string presetName)
    {
        byte[] newText = Latin1.GetBytes(text);
        int delta = newText.Length - (textEnd - textStart);

        var st = new byte[state.Length + delta];
        state.AsSpan(0, textStart).CopyTo(st);
        newText.CopyTo(st, textStart);
        state.AsSpan(textEnd).CopyTo(st.AsSpan(textStart + newText.Length));

        AddLE(st, 0, delta);
        int ccnk = IndexOf(st, "CcnK"u8, 0);
        AddBE(st, ccnk + 4, delta);
        AddBE(st, textStart - 4, delta);

        var hdr = (byte[])header.Clone();
        AddLE(hdr, hdr.Length - 12, delta);

        byte[] name = Latin1.GetBytes(presetName);
        var trailer = new byte[name.Length + 6];
        name.CopyTo(trailer, 1);
        return [hdr, st, trailer];
    }

    static (int start, int end) FindInstbox(string text, int index)
    {
        var m = Regex.Match(text, $"^instbox {index}\\b[^\\n]*\\{{\\n", RegexOptions.Multiline);
        if (!m.Success) throw new InvalidDataException($"Kit position {index} not found in EZdrummer state.");
        int depth = 0;
        for (int i = m.Index; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0) return (m.Index, i + 1);
        }
        throw new InvalidDataException($"Kit position {index} is not closed.");
    }

    static void AddLE(byte[] b, int at, int delta) =>
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at), (uint)((int)BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at)) + delta));

    static void AddBE(byte[] b, int at, int delta) =>
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(at), (uint)((int)BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(at)) + delta));

    static int IndexOf(byte[] hay, ReadOnlySpan<byte> needle, int from)
    {
        int i = hay.AsSpan(from).IndexOf(needle);
        return i < 0 ? -1 : i + from;
    }

    /// <summary>Decodes REAPER chunk base64 lines (each line decodes on its own).</summary>
    public static EzdState FromChunkLines(IEnumerable<string> lines)
    {
        using var ms = new MemoryStream();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length > 0) ms.Write(Convert.FromBase64String(line));
        }
        return new EzdState(ms.ToArray());
    }

    /// <summary>Loads EZdrummer presets saved in REAPER (presets\vst3-EZdrummer 3.ini).</summary>
    public static List<(string Name, EzdState State)> LoadReaperPresets(string iniPath)
    {
        var result = new List<(string, EzdState)>();
        if (!File.Exists(iniPath)) return result;
        string? name = null;
        var data = new MemoryStream();
        void Flush()
        {
            if (name != null && data.Length > 0)
            {
                try { result.Add((name, new EzdState(data.ToArray()))); } catch (InvalidDataException) { }
            }
            name = null;
            data = new MemoryStream();
        }
        bool inPreset = false;
        foreach (var line in File.ReadLines(iniPath))
        {
            if (line.StartsWith('['))
            {
                Flush();
                inPreset = line.StartsWith("[Preset", StringComparison.Ordinal);
                continue;
            }
            if (!inPreset) continue;
            int eq = line.IndexOf('=');
            if (eq < 0) continue;
            string key = line[..eq], value = line[(eq + 1)..];
            if (key == "Name") name = value;
            else if (key == "Data" || key.StartsWith("Data_", StringComparison.Ordinal))
            {
                byte[] bytes = Convert.FromHexString(value);
                data.Write(bytes, 0, bytes.Length - 1); // last byte of each line is a checksum
            }
        }
        Flush();
        return result;
    }
}
