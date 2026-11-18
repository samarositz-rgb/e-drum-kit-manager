using System.Text.Json;

namespace TD12KitManager;

/// <summary>Global volume per pad (percent of hit strength), shared by all kits.</summary>
public static class PadLevels
{
    static readonly string LevelsFile = Path.Combine(KitStore.BridgeDir, "padlevels.json");
    static readonly string OldFile = Path.Combine(KitStore.BridgeDir, "padlevels.txt");
    // Order of the older padlevels.txt (TD-12 only).
    static readonly string[] OldOrder =
        ["Kick", "Snare head", "Snare rim", "Tom 1", "Tom 2", "Tom 3", "Hi-hat", "Crash 1", "Crash 2", "Ride", "Aux 1", "Aux 2"];

    public static Dictionary<string, int> Load()
    {
        try
        {
            if (File.Exists(LevelsFile))
                return JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(LevelsFile)) ?? [];
            if (File.Exists(OldFile))
            {
                var lines = File.ReadAllLines(OldFile);
                var old = new Dictionary<string, int>();
                for (int i = 0; i < OldOrder.Length && i < lines.Length; i++)
                    if (int.TryParse(lines[i], out int v)) old[OldOrder[i]] = Math.Clamp(v, 25, 300);
                return old;
            }
        }
        catch (Exception e) when (e is IOException or JsonException) { }
        return [];
    }

    public static void Save(Dictionary<string, int> levels)
    {
        Directory.CreateDirectory(KitStore.BridgeDir);
        File.WriteAllText(LevelsFile, JsonSerializer.Serialize(levels));
    }
}
