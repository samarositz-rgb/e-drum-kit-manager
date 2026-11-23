using System.Globalization;
using System.Text.RegularExpressions;

namespace TD12KitManager;

/// <summary>One entry in the groove tree: a library, style, feel, song, part or a playable variation (File set).</summary>
public sealed class GrooveNode
{
    public string Name { get; init; } = "";
    public string? File { get; init; }
    public int Bpm { get; set; }
    public List<GrooveNode> Children { get; } = [];
    public override string ToString() => Name;
}

/// <summary>A groove's notes, ready to send to REAPER. Positions and lengths are in quarter notes.</summary>
public sealed record GrooveMidi(double Bpm, int Beats, int BeatUnit, double LengthQn, List<(double Qn, double LenQn, int Pitch, int Velocity)> Notes)
{
    public int Bars => (int)Math.Round(LengthQn / (Beats * 4.0 / BeatUnit));
}

/// <summary>
/// Reads EZdrummer's groove (MIDI) library. Folders are named like "205@STRAIGHT_4#4" or "057-S162@VERSE": a number
/// that sets the order and groups headers with their contents, then the name ("_" = space, "#" = "/"). A name starting
/// with "_" is a heading: a style ("200@_POP#ROCK"), whose feels are 201-299, or a song ("057-S160@_DIVA_BALLAD_57_BPM"),
/// whose parts are S161-S169 beside it.
/// </summary>
public static class Grooves
{
    static IEnumerable<string> MidiFolders() =>
        LibraryScanner.LibraryFolders().Select(h => Path.Combine(h, "Midi")).Where(Directory.Exists);

    public static List<GrooveNode> Load()
    {
        var libraries = new List<GrooveNode>();
        foreach (var root in MidiFolders())
            foreach (var lib in Sorted(root))
            {
                var (_, name, heading) = Parse(lib);
                if (heading) continue; // "EZdrummer libraries" / "EZX libraries" captions
                var node = new GrooveNode { Name = name };
                AddStyles(lib, node);
                if (node.Children.Count > 0) libraries.Add(node);
            }
        return libraries;
    }

    static void AddStyles(string lib, GrooveNode library)
    {
        GrooveNode? style = null;
        int styleGroup = -1;
        foreach (var dir in Sorted(lib))
        {
            var (num, name, heading) = Parse(dir);
            if (heading)
            {
                style = new GrooveNode { Name = name };
                styleGroup = num / 100;
                library.Children.Add(style);
                continue;
            }
            var feel = new GrooveNode { Name = name };
            AddSongs(dir, feel);
            if (feel.Children.Count == 0) continue;
            if (style != null && num / 100 == styleGroup) style.Children.Add(feel);
            else library.Children.Add(feel);
        }
        library.Children.RemoveAll(n => n.Children.Count == 0);
    }

    static readonly Regex SongPart = new(@"^(\d+)-S(\d+)@", RegexOptions.Compiled);

    static void AddSongs(string feelDir, GrooveNode feel)
    {
        GrooveNode? song = null;
        int songGroup = -1;
        foreach (var dir in Sorted(feelDir))
        {
            var (_, name, heading) = Parse(dir);
            var m = SongPart.Match(Path.GetFileName(dir));
            int group = m.Success ? int.Parse(m.Groups[2].Value) / 10 : -2;
            int bpm = m.Success ? int.Parse(m.Groups[1].Value) : 0;
            if (heading)
            {
                song = new GrooveNode { Name = Regex.Replace(name, @"\s+\d+\s*BPM$", "", RegexOptions.IgnoreCase) + $", {bpm} BPM", Bpm = bpm };
                songGroup = group;
                feel.Children.Add(song);
                continue;
            }
            var part = new GrooveNode { Name = name, Bpm = bpm };
            AddVariations(dir, part);
            if (part.Children.Count == 0) continue;
            if (song != null && group == songGroup) song.Children.Add(part);
            else feel.Children.Add(part);
        }
        feel.Children.RemoveAll(n => n.Children.Count == 0);
    }

    static void AddVariations(string partDir, GrooveNode part)
    {
        foreach (var file in Directory.GetFiles(partDir, "*.mid").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            string n = Path.GetFileNameWithoutExtension(file);
            var m = Regex.Match(n, @"(\d+)$");
            part.Children.Add(new GrooveNode { Name = m.Success ? $"Variation {int.Parse(m.Value)}" : Pretty(n), File = file, Bpm = part.Bpm });
        }
        foreach (var dir in Sorted(partDir)) AddVariations(dir, part); // deeper folders, if any
    }

    static IEnumerable<string> Sorted(string dir) =>
        Directory.GetDirectories(dir).OrderBy(d => Parse(d).Number).ThenBy(d => d, StringComparer.OrdinalIgnoreCase);

    static (int Number, string Name, bool Heading) Parse(string dir)
    {
        string folder = Path.GetFileName(dir);
        int at = folder.IndexOf('@');
        string prefix = at >= 0 ? folder[..at] : "";
        string raw = at >= 0 ? folder[(at + 1)..] : folder;
        var num = Regex.Match(prefix, @"\d+");
        bool heading = raw.StartsWith('_');
        return (num.Success ? int.Parse(num.Value) : 0, Pretty(raw), heading);
    }

    static string Pretty(string raw)
    {
        string s = raw.Trim('_').Replace('_', ' ').Replace('#', '/');
        s = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());
        s = Regex.Replace(s, @"(?<=\d)([A-Z][a-z]*)", m => m.Value.ToLowerInvariant()); // 1960s, 8ths
        s = Regex.Replace(s, @"\bBpm\b", "BPM");
        s = Regex.Replace(s, @"\bEdm\b", "EDM");
        s = Regex.Replace(s, @"\bR&b\b", "R&B");
        s = Regex.Replace(s, @"\bEzx\b", "EZX");
        s = Regex.Replace(s, @"\bEzdrummer\b", "EZdrummer");
        return s;
    }

    /// <summary>Reads a groove MIDI file (format 0 or 1): notes on every track, first tempo and time signature.</summary>
    public static GrooveMidi ReadMidi(string path)
    {
        byte[] d = System.IO.File.ReadAllBytes(path);
        int division = (d[12] << 8) | d[13];
        double bpm = 120; int beats = 4, unit = 4;
        bool tempoSet = false, sigSet = false;
        var notes = new List<(double, double, int, int)>();
        long maxTick = 0;
        int pos = 14;
        while (pos + 8 <= d.Length)
        {
            int len = (d[pos + 4] << 24) | (d[pos + 5] << 16) | (d[pos + 6] << 8) | d[pos + 7];
            bool isTrack = d[pos] == 'M' && d[pos + 1] == 'T' && d[pos + 2] == 'r' && d[pos + 3] == 'k';
            int p = pos + 8, end = Math.Min(d.Length, p + len);
            pos = end;
            if (!isTrack) continue;
            long tick = 0;
            int status = 0;
            var open = new Dictionary<int, (long Tick, int Vel)>();
            while (p < end)
            {
                long delta = 0;
                byte b;
                do { b = d[p++]; delta = (delta << 7) | (uint)(b & 0x7F); } while ((b & 0x80) != 0 && p < end);
                tick += delta;
                if (p >= end) break;
                if (d[p] >= 0x80) status = d[p++];
                if (status == 0xFF)
                {
                    int type = d[p++];
                    long mlen = 0;
                    do { b = d[p++]; mlen = (mlen << 7) | (uint)(b & 0x7F); } while ((b & 0x80) != 0);
                    if (type == 0x51 && mlen == 3 && !tempoSet) { bpm = 60000000.0 / ((d[p] << 16) | (d[p + 1] << 8) | d[p + 2]); tempoSet = true; }
                    if (type == 0x58 && mlen >= 2 && !sigSet) { beats = d[p]; unit = 1 << d[p + 1]; sigSet = true; }
                    if (type == 0x2F) maxTick = Math.Max(maxTick, tick);
                    p += (int)mlen;
                    status = 0;
                    continue;
                }
                if (status is 0xF0 or 0xF7)
                {
                    long slen = 0;
                    do { b = d[p++]; slen = (slen << 7) | (uint)(b & 0x7F); } while ((b & 0x80) != 0);
                    p += (int)slen;
                    continue;
                }
                int kind = status & 0xF0;
                int a1 = d[p++];
                int a2 = kind is 0xC0 or 0xD0 ? 0 : d[p++];
                if (kind == 0x90 && a2 > 0) open[a1] = (tick, a2);
                else if ((kind == 0x80 || (kind == 0x90 && a2 == 0)) && open.Remove(a1, out var on))
                    notes.Add((on.Tick / (double)division, Math.Max(1, tick - on.Tick) / (double)division, a1, on.Vel));
                maxTick = Math.Max(maxTick, tick);
            }
            foreach (var (pitch, on) in open) notes.Add((on.Tick / (double)division, 0.1, pitch, on.Vel));
        }
        double barQn = beats * 4.0 / unit;
        double length = Math.Max(barQn, Math.Ceiling(maxTick / (double)division / barQn - 0.01) * barQn);
        return new GrooveMidi(Math.Round(bpm, 2), beats, unit, length, notes.OrderBy(n => n.Item1).ToList());
    }

    /// <summary>Writes the notes for the REAPER bridge: one "startQN lengthQN pitch velocity" line each.</summary>
    public static string WriteNotes(GrooveMidi g)
    {
        string path = Path.Combine(KitStore.BridgeDir, "groove.txt");
        var inv = CultureInfo.InvariantCulture;
        System.IO.File.WriteAllLines(path, g.Notes.Select(n =>
            $"{n.Qn.ToString("0.#####", inv)} {n.LenQn.ToString("0.#####", inv)} {n.Pitch} {n.Velocity}"));
        return path;
    }
}
