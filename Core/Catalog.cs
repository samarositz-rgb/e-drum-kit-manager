namespace TD12KitManager;

public static partial class Catalog
{
    public static readonly Slot[] Slots =
    [
        new(0, "Kick", ["Kick"]),
        new(1, "Snare", ["Snare"]),
        new(2, "Hi-hat", ["Hi-Hat"]),
        new(3, "Rack tom 1", ["Rack Tom"]),
        new(4, "Rack tom 2", ["Rack Tom"]),
        new(5, "Rack tom 3", ["Rack Tom"]),
        new(6, "Floor tom 1", ["Floor Tom"]),
        new(7, "Floor tom 2", ["Floor Tom"]),
        new(8, "Ride", ["Ride"]),
        new(9, "Crash 1", ["Crash"]),
        new(10, "Crash 2", ["Crash"]),
        new(11, "Crash 3", ["Crash"]),
        new(12, "Extra cymbal", ["Crash", "Splash", "China", "Spock"]),
    ];

    // Descriptions for the core rooms; expansions show their own library name.
    static readonly Dictionary<string, string> CoreRooms = new()
    {
        ["EZX3_EZdrummer3_MainRoom"] = "Main Room: medium, balanced studio",
        ["EZX3_EZdrummer3_BrightRoom"] = "Bright Room: large and open",
        ["EZX3_EZdrummer3_TightRoom"] = "Tight Room: small, dry and close",
    };

    static readonly HashSet<string> CymbalTypes = ["Crash", "Splash", "China", "Spock", "Ride"];
    static readonly Dictionary<string, string> LibraryNames = [];

    /// <summary>Every drum in the installed libraries (falls back to the core library list).</summary>
    public static Drum[] Drums => installed ?? BuiltIn;
    static Drum[]? installed;

    /// <summary>Installed libraries ("rooms"), core rooms first.</summary>
    public static (string Lib, string Name)[] Rooms { get; private set; } = [.. CoreRooms.Select(r => (r.Key, r.Value))];

    /// <summary>Reads the installed EZdrummer libraries. Returns the names of expansions found beyond the core library.</summary>
    public static List<string> LoadInstalledLibraries()
    {
        var libraries = LibraryScanner.Scan();
        if (libraries.Count == 0) return [];
        var drums = libraries.SelectMany(l => l.Drums).ToList();
        // Keep core drums available even if a core room failed to read.
        drums.AddRange(BuiltIn.Where(b => !drums.Any(d => d.Lib == b.Lib && d.Id == b.Id)));
        installed = [.. drums];
        foreach (var l in libraries)
        {
            LibraryNames[l.Lib] = l.Name;
            CymbalTypes.UnionWith(l.CymbalTypes);
        }
        Rooms = [.. CoreRooms.Select(r => (r.Key, r.Value)),
                 .. libraries.Where(l => !CoreRooms.ContainsKey(l.Lib)).OrderBy(l => l.Name).Select(l => (l.Lib, l.Name))];
        return libraries.Where(l => !CoreRooms.ContainsKey(l.Lib)).Select(l => l.Name).ToList();
    }

    /// <summary>The extra cymbal slot also takes cymbal types only found in expansions (stacks, bells...).</summary>
    static bool Fits(Slot slot, Drum d) =>
        slot.Types.Contains(d.Type) || (slot.Index == 12 && d.Type != "Ride" && CymbalTypes.Contains(d.Type));

    public const string PercussionLib = "EZXP_EZdrummer2_Percussion";

    /// <summary>EZdrummer 3 percussion one-shots (one per kit), from the percussion library's config.</summary>
    public static readonly (string Id, string Name)[] OneShots =
    [
        ("CB33_01", "Cowbell"), ("CB34_01", "Cowbell with zills"), ("Cowbell", "Cowbell (EZdrummer 2)"),
        ("WB33_01", "Jam block, medium"), ("WB34_01", "Wood block, oak"), ("FX33_01", "Sticks"),
        ("HC33_01", "Single clap"), ("HC34_01", "Double claps"), ("HC35_01", "Triple claps"), ("HC39_01", "Group claps"),
        ("HC36_01", "Single soft clap"), ("HC40_01", "Group soft claps"),
        ("FN33_01", "Single finger snap"), ("FN36_01", "Group finger snaps"),
        ("FS33_01", "Single stomp"), ("FS36_01", "Group stomps"),
        ("TA33_01", "Tambourine hit, steel"), ("TA36_01", "Tambourine hit, brass"), ("TA38_01", "Tambourine hit, mounted steel"),
        ("TA35_01", "Tambourine hit, hi-hat mounted"),
    ];

    // EZdrummer position each kit slot normally holds (Main Room layout, which the TD-12 map is built on).
    static readonly string[] SlotPos =
        ["Kick", "Snare", "Hats", "Racktom1", "Racktom2", "Racktom3", "Floortom1", "Floortom2", "Ride4", "Crash1", "Crash2", "Crash5", "Crash6"];

    /// <summary>EZdrummer position a kit slot normally holds, like "Racktom1".</summary>
    public static string SlotPosition(int slot) => SlotPos[slot];

    /// <summary>
    /// Swaps each drum for one recorded in <paramref name="roomLib"/>, keeping the kit layout (and so the TD-12 map).
    /// Returns the labels of slots the room has no drum for; those keep their current drum.
    /// </summary>
    public static List<string> SwitchRoom(List<KitSlot> slots, string roomLib)
    {
        var missing = new List<string>();
        var used = new HashSet<Drum>();
        foreach (var slot in Slots)
        {
            var current = Find(slots[slot.Index].Lib, slots[slot.Index].Drum);
            if (current?.Lib == roomLib) { used.Add(current); continue; }
            var best = ForSlot(slot)
                .Where(d => d.Lib == roomLib)
                .OrderBy(d => used.Contains(d) ? 1 : 0)
                .ThenBy(d => d.Pos == current?.Pos ? 0 : d.Pos == SlotPos[slot.Index] ? 1 : 2)
                .ThenBy(d => current != null && d.Type == current.Type ? 0 : 1)
                .FirstOrDefault();
            if (best == null) { missing.Add(slot.Label); continue; }
            used.Add(best);
            slots[slot.Index] = new KitSlot { Lib = best.Lib, Drum = best.Id };
        }
        return missing;
    }

    /// <summary>Summary like "Tight Room 10, Bright Room 3".</summary>
    public static string RoomsUsed(IEnumerable<KitSlot> slots) => string.Join(", ",
        slots.GroupBy(s => RoomName(s.Lib)).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}"));

    public static string RoomName(string lib) =>
        CoreRooms.TryGetValue(lib, out var core) ? core.Split(':')[0]
        : LibraryNames.TryGetValue(lib, out var name) ? name : lib;

    public static Drum? Find(string lib, string id) =>
        Drums.FirstOrDefault(d => d.Lib == lib && d.Id == id);

    public static IEnumerable<Drum> ForSlot(Slot slot) =>
        Drums.Where(d => Fits(slot, d))
             .OrderBy(d => Array.IndexOf(slot.Types, d.Type) is var i and >= 0 ? i : slot.Types.Length)
             .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
             .ThenBy(d => d.Room);

    public static string Describe(KitSlot s) =>
        Find(s.Lib, s.Drum)?.DisplayName ?? $"{s.Drum} ({s.Lib})";
}
