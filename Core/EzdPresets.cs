using System.Globalization;
using System.Text;

namespace TD12KitManager;

/// <summary>
/// Saves kits as EZdrummer 3 user kit presets, so they show in EZdrummer's own preset list in any host
/// (GarageBand, Logic, Cubase...), not only REAPER. Only the drums and tuning travel; the e-drum map,
/// mixer and pad levels are EZdrummer or host settings.
/// </summary>
public static class EzdPresets
{
    const string LayoutLib = "EZX3_EZdrummer3_MainRoom"; // every kit uses the Main Room layout

    // Windows keeps EZdrummer's user presets in Documents; on a Mac they live in ~/Library/Application Support
    // (checked on a Mac with EZdrummer 3.1.2: that is where EZdrummer itself saves its e-drum presets and where its empty
    // DrumKitPresets folder is).
    public static readonly string Folder = Path.Combine(
        OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Toontrack", "EZdrummer 3", "PresetsEZX", LayoutLib, "DrumKitPresets");

    /// <summary>Names written last time, so presets of deleted or renamed kits can be removed (and nothing else).</summary>
    static readonly string WrittenList = Path.Combine(KitStore.AppDir, "ezdrummer-presets.txt");

    static readonly string[] SlotPos =
        ["Kick", "Snare", "Hats", "Racktom1", "Racktom2", "Racktom3", "Floortom1", "Floortom2", "Ride4", "Crash1", "Crash2", "Crash5", "Crash6"];

    public static string FileName(Kit kit)
    {
        var name = new string(kit.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c is '/' or '\\' or ':' ? '-' : c).ToArray());
        return name.Trim().TrimEnd('.');
    }

    public static string Text(Kit kit)
    {
        var sb = new StringBuilder();
        sb.Append('"').Append(kit.Name.Replace("\"", "'")).Append("\" {\r\ns3kit\r\n");
        for (int i = 0; i < SlotPos.Length && i < kit.Slots.Count; i++)
        {
            var s = kit.Slots[i];
            if (s.Drum.Length == 0) continue;
            string pos = Catalog.Find(s.Lib, s.Drum)?.Pos ?? SlotPos[i];
            sb.Append($"\"{SlotPos[i]}\" \"{s.Lib}\" \"{pos}\" \"{s.Drum}\"\r\n");
        }
        sb.Append("drumprops {\r\n");
        for (int i = 0; i < kit.Slots.Count; i++)
            if (kit.Slots[i].Tune != 0)
                sb.Append(i).Append(" pitch ").Append(Math.Pow(2, kit.Slots[i].Tune / 12.0).ToString("0.#####", CultureInfo.InvariantCulture)).Append("\r\n");
        sb.Append("}\r\n}\r\n");
        return sb.ToString();
    }

    /// <summary>Writes every kit as a preset. Presets the user made in EZdrummer with the same name are left alone.</summary>
    public static void WriteAll(IEnumerable<Kit> kits)
    {
        if (!Directory.Exists(Path.GetFullPath(Path.Combine(Folder, "..", "..", "..")))) return; // no EZdrummer 3 user folder yet
        Directory.CreateDirectory(Folder);
        var previous = File.Exists(WrittenList) ? File.ReadAllLines(WrittenList).ToHashSet(StringComparer.OrdinalIgnoreCase) : [];
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kit in kits)
        {
            string name = FileName(kit);
            if (name.Length == 0 || written.Contains(name)) continue;
            string path = Path.Combine(Folder, name);
            if (File.Exists(path) && !previous.Contains(name)) continue;
            File.WriteAllText(path, Text(kit));
            written.Add(name);
        }
        foreach (var old in previous.Except(written, StringComparer.OrdinalIgnoreCase))
        {
            string path = Path.Combine(Folder, old);
            if (File.Exists(path)) File.Delete(path);
        }
        File.WriteAllLines(WrittenList, written);
    }
}
