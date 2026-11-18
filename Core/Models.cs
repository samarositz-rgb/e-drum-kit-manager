namespace TD12KitManager;

/// <summary>One drum sample set from an installed EZdrummer library.</summary>
public sealed record Drum(string Lib, string Pos, string Id, string Name, string Type)
{
    public string Room => Catalog.RoomName(Lib);

    public string DisplayName => $"{Name}, {Room}";

    public override string ToString() => DisplayName;
}

/// <summary>A kit position in EZdrummer (instbox 0-12) and which drum types it accepts.</summary>
public sealed record Slot(int Index, string Label, string[] Types);

public sealed class KitSlot
{
    public string Lib { get; set; } = "";
    public string Drum { get; set; } = "";
    /// <summary>Tuning in semitones (0 = as recorded).</summary>
    public int Tune { get; set; }
    /// <summary>Hit strength for this drum's pad in this kit, percent (100 = unchanged).</summary>
    public int Level { get; set; } = 100;
}

public sealed class Kit
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Source { get; set; } = "";
    /// <summary>TD-12 module kit number (1-50) that switches to this kit, or 0 for none.</summary>
    public int Td12KitNumber { get; set; }
    public List<KitSlot> Slots { get; set; } = [];
    /// <summary>Room (ambience mics) level, percent of the EZdrummer mixer setting.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("RoomAmount")] // "Room" was a room name in older versions
    public int Room { get; set; } = 100;
    /// <summary>Overhead mics level, percent of the EZdrummer mixer setting.</summary>
    /// <summary>EZdrummer percussion one-shot (cowbell, claps...) id, or "" for none.</summary>
    public string OneShot { get; set; } = "";
    /// <summary>TD-12 pad that plays the one-shot: "Aux 1" or "Aux 2".</summary>
    public string OneShotPad { get; set; } = "Aux 2";
    /// <summary>Hit strength for the one-shot pad, percent.</summary>
    public int OneShotLevel { get; set; } = 100;
    [System.Text.Json.Serialization.JsonPropertyName("OverheadsAmount")]
    public int Overheads { get; set; } = 100;

    public Kit Clone() => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = Name,
        Source = Source,
        Td12KitNumber = 0,
        Room = Room,
        Overheads = Overheads,
        OneShot = OneShot,
        OneShotPad = OneShotPad,
        OneShotLevel = OneShotLevel,
        Slots = Slots.Select(s => new KitSlot { Lib = s.Lib, Drum = s.Drum, Tune = s.Tune, Level = s.Level }).ToList(),
    };

    public override string ToString() =>
        Td12KitNumber > 0 ? $"{Name} (TD-12 kit {Td12KitNumber})" : Name;
}
