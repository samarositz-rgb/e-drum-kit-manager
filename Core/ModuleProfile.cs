using System.Text.Json;

namespace TD12KitManager;

/// <summary>One playing area of a pad (head, rim, bow, edge...) that "Learn my kit" asks the player to hit.</summary>
/// <param name="Id">Stable key stored in profiles.</param>
/// <param name="Prompt">What the player is asked to hit.</param>
/// <param name="Pad">Pad it belongs to (pad levels list).</param>
/// <param name="Slot">EZdrummer kit slot it plays, or -1.</param>
/// <param name="MapLine">EZdrummer e-drum map line; {0} is the note the module sends.</param>
/// <param name="Optional">Can be skipped when the module has no such zone.</param>
public sealed record Zone(string Id, string Prompt, string Pad, int Slot, string MapLine, bool Optional);

/// <summary>A drum pad as the player thinks of it, with every note it sends.</summary>
public sealed record ModulePad(string Name, int[] Notes, int Slot, bool CanPlayOneShot);

/// <summary>
/// Everything Kit Manager needs to know about a drum module: which notes each pad sends,
/// its drum channel and how many kits it has. Built in for the Roland TD-12; other modules are
/// created with "Learn my kit".
/// </summary>
public sealed class ModuleProfile
{
    public string Name { get; set; } = "";
    /// <summary>MIDI channel the pads send on, 1-16, or 0 for any.</summary>
    public int DrumChannel { get; set; } = 10;
    /// <summary>Number of kits on the module (the kit buttons wrap around after this).</summary>
    public int KitCount { get; set; } = 100;
    /// <summary>Roland bulk dump backups (TD-12 style).</summary>
    public bool RolandBackup { get; set; }
    /// <summary>
    /// False: keep the e-drum map already set up in EZdrummer (the TD-12 profile uses the map the
    /// player made). True: Kit Manager writes a map from the learned notes.
    /// </summary>
    public bool WriteMap { get; set; } = true;
    /// <summary>Zone id to the MIDI note the module sends for it.</summary>
    public Dictionary<string, int> Notes { get; set; } = [];

    /// <summary>Output note EZdrummer uses for cross-stick, which the rim split sends soft rim hits to.</summary>
    public const int CrossStickNote = 37;

    /// <summary>Soft rim hits become cross-sticks only when the module has no cross-stick note of its own.</summary>
    public bool UsesRimSplit => Notes.ContainsKey("snareRim") && !Notes.ContainsKey("snareXstick");

    public static readonly Zone[] Zones =
    [
        new("kick", "the kick pedal", "Kick", 0, "map {0} kickO kickR kick1R 36", false),
        new("snare", "the middle of the snare head", "Snare head", 1, "usermap {0} snareR 38 -1", false),
        new("snareRim", "a rimshot on the snare (head and rim together)", "Snare rim", 1, "usermap {0} snareFX 40 -1", true),
        new("snareXstick", "a cross-stick on the snare. Skip if your module has no separate cross-stick", "Snare rim", 1, "map {0} snareSL snareR 37", true),
        new("tom1", "the head of tom 1, the highest tom", "Tom 1", 3, "map {0} tom1R racktom1R 48", true),
        new("tom1Rim", "the rim of tom 1", "Tom 1", 3, "map {0} tom1FX tom1R racktom1FX racktom1R 82", true),
        new("tom2", "the head of tom 2", "Tom 2", 4, "map {0} tom2R racktom2R 47", true),
        new("tom2Rim", "the rim of tom 2", "Tom 2", 4, "map {0} tom2FX tom2R racktom2FX racktom2R 80", true),
        new("tom3", "the head of tom 3, usually the floor tom", "Tom 3", 6, "usermap {0} tom4R 43 -1", true),
        new("tom3Rim", "the rim of tom 3", "Tom 3", 6, "usermap {0} tom4FX 75 -1", true),
        new("tom4", "the head of tom 4. Skip if you have only three toms", "Tom 4", 7, "usermap {0} tom5R 41 -1", true),
        new("tom4Rim", "the rim of tom 4", "Tom 4", 7, "usermap {0} tom5FX 73 -1", true),
        new("hhBowClosed", "the top of the hi-hat, with the pedal pressed down", "Hi-hat", 2, "map {0} hatsTipTrig hatsTrig 42", true),
        new("hhBowOpen", "the top of the hi-hat, with the pedal up (open)", "Hi-hat", 2, "map {0} hatsTipTrig hatsTrig 46", true),
        new("hhEdgeClosed", "the edge of the hi-hat, pedal pressed down", "Hi-hat", 2, "map {0} hatsTrig 22", true),
        new("hhEdgeOpen", "the edge of the hi-hat, pedal up", "Hi-hat", 2, "map {0} hatsTrig 26", true),
        new("hhPedal", "the hi-hat pedal: step on it", "Hi-hat", 2, "map {0} hatsPL 44", true),
        new("crash1", "the top of crash 1, usually next to the hi-hat", "Crash 1", 9, "usermap {0} crash1RT 84 -1", true),
        new("crash1Edge", "the edge of crash 1", "Crash 1", 9, "usermap {0} crash1 86 -1", true),
        new("crash2", "the top of crash 2", "Crash 2", 11, "map {0} crash10RT crash10 crash9RT crash9 crash5RT crash5 crash3RT crash3 crash4RT crash4 china6RT ride5 ride5RT ride5CR ride4 ride4PU 57", true),
        new("crash2Edge", "the edge of crash 2", "Crash 2", 11, "map {0} crash10 crash9 crash5 crash3 crash4 china6 ride5PU ride5CR ride4PU 57", true),
        new("ride", "the top of the ride (bow)", "Ride", 8, "map {0} ride7 ride6 ride4 ride4RD ride4RT crash4RT 51", true),
        new("rideEdge", "the edge of the ride", "Ride", 8, "map {0} ride7PU ride7CR ride6PU ride6CR ride4PU ride4CR crash4 crash4CR 59", true),
        new("rideBell", "the bell of the ride", "Ride", 8, "map {0} ride7BL ride7BT ride7 ride6BL ride6BT ride6 ride4BL ride4BT ride4 crash4BL crash4BT crash4RT 53", true),
        new("aux1", "the head of extra pad 1 (aux 1). Skip if you have none", "Aux 1", 7, "usermap {0} tom5R 41 -1", true),
        new("aux1Rim", "the rim of extra pad 1", "Aux 1", 7, "usermap {0} tom5FX 73 -1", true),
        new("aux2", "extra pad 2 (aux 2). Skip if you have none", "Aux 2", 12, "usermap {0} crash6 98 -1", true),
        new("aux2Rim", "the rim or edge of extra pad 2", "Aux 2", 12, "usermap {0} crash6 98 -1", true),
    ];

    /// <summary>Pads in display order, built from the learned notes.</summary>
    public List<ModulePad> Pads()
    {
        var pads = new List<ModulePad>();
        foreach (var name in Zones.Select(z => z.Pad).Distinct())
        {
            var zones = Zones.Where(z => z.Pad == name && Notes.ContainsKey(z.Id)).ToList();
            if (zones.Count == 0) continue;
            var notes = zones.Select(z => Notes[z.Id]).ToList();
            // The rim split turns soft rim hits into the cross-stick note before the pad levels see them.
            if (name == "Snare rim" && UsesRimSplit) notes.Add(CrossStickNote);
            pads.Add(new ModulePad(name, [.. notes.Distinct()], zones[0].Slot, name.StartsWith("Aux")));
        }
        return pads;
    }

    /// <summary>Pads that play a kit slot (for the per-kit level control).</summary>
    public List<ModulePad> PadsForSlot(int slot) => Pads().Where(p => p.Slot == slot).ToList();

    /// <summary>EZdrummer e-drum map lines for the learned notes (used when <see cref="WriteMap"/> is on).</summary>
    public List<string> MapLines()
    {
        var lines = new List<string>();
        foreach (var zone in Zones)
            if (Notes.TryGetValue(zone.Id, out int note) && !lines.Any(l => l.Split(' ')[1] == note.ToString()))
                lines.Add(string.Format(zone.MapLine, note)); // first zone wins when two zones send the same note
        // The rim split sends soft rim hits as the cross-stick note, so EZdrummer needs to know it.
        if (UsesRimSplit && !lines.Any(l => l.Split(' ')[1] == CrossStickNote.ToString()))
            lines.Add($"map {CrossStickNote} snareSL snareR {CrossStickNote}");
        return lines;
    }

    /// <summary>The Roland TD-12 with its standard notes (the first module Kit Manager was built for).</summary>
    public static ModuleProfile RolandTd12() => new()
    {
        Name = "Roland TD-12",
        DrumChannel = 10,
        KitCount = 50,
        RolandBackup = true,
        WriteMap = false,
        Notes = new()
        {
            ["kick"] = 36, ["snare"] = 38, ["snareRim"] = 40,
            ["tom1"] = 48, ["tom1Rim"] = 50, ["tom2"] = 45, ["tom2Rim"] = 47, ["tom3"] = 43, ["tom3Rim"] = 58,
            ["hhBowClosed"] = 42, ["hhBowOpen"] = 46, ["hhEdgeClosed"] = 22, ["hhEdgeOpen"] = 26, ["hhPedal"] = 44,
            ["crash1"] = 49, ["crash1Edge"] = 55, ["crash2"] = 57, ["crash2Edge"] = 52,
            ["ride"] = 51, ["rideEdge"] = 59, ["rideBell"] = 53,
            ["aux1"] = 27, ["aux1Rim"] = 28, ["aux2"] = 29, ["aux2Rim"] = 30,
        },
    };
}

/// <summary>Saved module profiles plus the machine settings (active profile, MIDI ports).</summary>
public sealed class HardwareSettings
{
    public string Profile { get; set; } = "Roland TD-12";
    /// <summary>Part of the REAPER MIDI input name the module is on; empty = all inputs.</summary>
    public string MidiInput { get; set; } = "";
    /// <summary>Part of the REAPER MIDI output name that goes to the module's MIDI IN.</summary>
    public string MidiOutput { get; set; } = "";

    static readonly string SettingsFile = Path.Combine(KitStore.AppDir, "hardware.json");
    static readonly string ProfileDir = Path.Combine(KitStore.AppDir, "profiles");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static HardwareSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
                return JsonSerializer.Deserialize<HardwareSettings>(File.ReadAllText(SettingsFile)) ?? new();
        }
        catch (JsonException) { }
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(KitStore.AppDir);
        File.WriteAllText(SettingsFile, JsonSerializer.Serialize(this, Json));
    }

    public static List<ModuleProfile> AllProfiles()
    {
        // EZdrummer's own module list; the built-in TD-12 profile (with its tested settings) replaces EZdrummer's TD-12.
        var list = EzdModules.Load().Where(p => p.Name != "Roland TD-12").Append(ModuleProfile.RolandTd12())
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        if (Directory.Exists(ProfileDir))
            foreach (var f in Directory.GetFiles(ProfileDir, "*.json"))
            {
                try
                {
                    if (JsonSerializer.Deserialize<ModuleProfile>(File.ReadAllText(f)) is { Name.Length: > 0 } p)
                    {
                        list.RemoveAll(x => x.Name == p.Name);
                        list.Add(p);
                    }
                }
                catch (JsonException) { }
            }
        return list;
    }

    public static void SaveProfile(ModuleProfile profile)
    {
        Directory.CreateDirectory(ProfileDir);
        string safe = string.Concat(profile.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        File.WriteAllText(Path.Combine(ProfileDir, safe + ".json"), JsonSerializer.Serialize(profile, Json));
    }

    public ModuleProfile ActiveProfile() =>
        AllProfiles().FirstOrDefault(p => p.Name == Profile) ?? ModuleProfile.RolandTd12();

    /// <summary>Settings the REAPER bridge reads (key=value lines).</summary>
    public void WriteBridgeSettings()
    {
        var p = ActiveProfile();
        Directory.CreateDirectory(KitStore.BridgeDir);
        File.WriteAllLines(Path.Combine(KitStore.BridgeDir, "module.txt"),
        [
            $"name={p.Name}",
            $"drumChannel={p.DrumChannel}",
            $"kitCount={p.KitCount}",
            $"midiIn={MidiInput}",
            $"midiOut={MidiOutput}",
            $"rimSplit={(p.UsesRimSplit ? 1 : 0)}",
            $"rimNote={(p.Notes.TryGetValue("snareRim", out int rim) ? rim : -1)}",
            $"headNote={(p.Notes.TryGetValue("snare", out int head) ? head : -1)}",
            $"crossNote={ModuleProfile.CrossStickNote}",
        ]);
    }
}
