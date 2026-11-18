using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TD12KitManager;

/// <summary>
/// The drum modules EZdrummer 3 has built-in e-drum maps for (Alesis, Roland, Yamaha and so on). EZdrummer keeps
/// them as text inside its plug-in file: <c>submenu "Brand"</c>, then <c>"Model" id { USE Articulation note ... }</c>.
/// They're read from the user's own EZdrummer install and turned into module profiles, so nothing of Toontrack's is shipped.
/// </summary>
public static class EzdModules
{
    static readonly string CacheFile = Path.Combine(KitStore.AppDir, "ezdrummer-modules.json");

    static IEnumerable<string> PluginFiles()
    {
        if (OperatingSystem.IsMacOS())
        {
            yield return "/Library/Audio/Plug-Ins/VST3/EZdrummer 3.vst3/Contents/MacOS/EZdrummer 3";
            yield return "/Library/Audio/Plug-Ins/Components/EZdrummer 3.component/Contents/MacOS/EZdrummer 3";
        }
        else
        {
            string common = Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles);
            yield return Path.Combine(common, "VST3", "EZdrummer 3.vst3");
            yield return Path.Combine(common, "VST3", "EZdrummer 3.vst3", "Contents", "x86_64-win", "EZdrummer 3.vst3");
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Toontrack", "EZdrummer 3", "EZdrummer 3.exe");
        }
    }

    // EZdrummer articulation -> Learn my kit zones it fills.
    static readonly Dictionary<string, string[]> ZoneFor = new()
    {
        ["Kick1Head"] = ["kick"],
        ["SnareHead"] = ["snare"], ["SnareHeadCC"] = ["snare"],
        ["SnareRimshot"] = ["snareRim"], ["SnareRimFX"] = ["snareXstick"],
        ["Tom1Head"] = ["tom1"], ["Tom1Rimshot"] = ["tom1Rim"],
        ["Tom2Head"] = ["tom2"], ["Tom2Rimshot"] = ["tom2Rim"],
        ["Tom3Head"] = ["tom3"], ["Tom3Rimshot"] = ["tom3Rim"],
        ["Tom4Head"] = ["tom4"], ["Tom4Rimshot"] = ["tom4Rim"],
        ["HatsTipCC"] = ["hhBowClosed", "hhBowOpen"], ["HatsTipClosed"] = ["hhBowClosed"], ["HatsTipOpen"] = ["hhBowOpen"],
        ["HatsEdgeCC"] = ["hhEdgeClosed", "hhEdgeOpen"], ["HatsEdgeClosed"] = ["hhEdgeClosed"], ["HatsEdgeOpen"] = ["hhEdgeOpen"],
        ["HatsPedal"] = ["hhPedal"],
        ["Crash1Bow"] = ["crash1"], ["Crash1Edge"] = ["crash1Edge"],
        ["Crash2Bow"] = ["crash2"], ["Crash2Edge"] = ["crash2Edge"],
        ["RideBow"] = ["ride"], ["RideEdge"] = ["rideEdge"], ["RideBell"] = ["rideBell"],
    };

    // Groups in EZdrummer's list that are drum module makers (the rest are generic or creative maps).
    static readonly HashSet<string> Brands =
        ["2box", "Alesis", "ddrum", "Drum Workshop", "EFNOTE", "KAT", "Medeli", "Millenium", "Pearl", "Roland", "Simmons", "Yamaha", "Zildjian", "Others"];

    sealed record Cache(string Source, long Size, DateTime Modified, List<ModuleProfile> Profiles);

    /// <summary>Module profiles for every drum module EZdrummer knows, or none if EZdrummer isn't installed.</summary>
    public static List<ModuleProfile> Load()
    {
        var file = PluginFiles().FirstOrDefault(File.Exists);
        if (file == null) return [];
        var info = new FileInfo(file);
        try
        {
            if (File.Exists(CacheFile) && JsonSerializer.Deserialize<Cache>(File.ReadAllText(CacheFile)) is { } c
                && c.Source == file && c.Size == info.Length && c.Modified == info.LastWriteTimeUtc)
                return c.Profiles;
        }
        catch (Exception) { }
        List<ModuleProfile> profiles;
        try { profiles = Parse(File.ReadAllBytes(file)); }
        catch (Exception) { return []; }
        try
        {
            Directory.CreateDirectory(KitStore.AppDir);
            File.WriteAllText(CacheFile, JsonSerializer.Serialize(new Cache(file, info.Length, info.LastWriteTimeUtc, profiles)));
        }
        catch (Exception) { }
        return profiles;
    }

    public static List<ModuleProfile> Parse(byte[] plugin)
    {
        int start = IndexOf(plugin, Encoding.ASCII.GetBytes("submenu \"Generic Controller\""));
        if (start < 0) return [];
        string text = Encoding.Latin1.GetString(plugin, start, Math.Min(400_000, plugin.Length - start));
        var profiles = new List<ModuleProfile>();
        string? brand = null;
        foreach (Match m in Regex.Matches(text, "submenu \"([^\"]+)\"|\n\"([^\"]+)\" \\w+_id \\{([^}]*)\\}"))
        {
            if (m.Groups[1].Success)
            {
                brand = m.Groups[1].Value;
                if (brand == "Note Mapping") break; // the module list ends here
                continue;
            }
            if (brand == null || !Brands.Contains(brand)) continue;
            var notes = new Dictionary<string, int>();
            foreach (Match use in Regex.Matches(m.Groups[3].Value, @"USE (\w+) (\d+)"))
                if (ZoneFor.TryGetValue(use.Groups[1].Value, out var zones))
                    foreach (var z in zones) notes.TryAdd(z, int.Parse(use.Groups[2].Value));
            if (!notes.ContainsKey("kick") || !notes.ContainsKey("snare")) continue;
            string model = m.Groups[2].Value;
            profiles.Add(new ModuleProfile
            {
                Name = brand == "Others" || model.StartsWith(brand, StringComparison.OrdinalIgnoreCase) ? model : $"{brand} {model}",
                Notes = notes,
            });
        }
        return profiles;
    }

    static int IndexOf(byte[] data, byte[] find)
    {
        for (int i = Array.IndexOf(data, find[0]); i >= 0 && i <= data.Length - find.Length; i = Array.IndexOf(data, find[0], i + 1))
            if (data.AsSpan(i, find.Length).SequenceEqual(find)) return i;
        return -1;
    }
}
