using System.Reflection;
using System.Text.Json;

namespace TD12KitManager;

/// <summary>Kits, the base EZdrummer state (mixer + TD-12 map) and the per-kit files the REAPER bridge loads.</summary>
public sealed class KitStore
{
    /// <summary>Per-user settings folder: %APPDATA% on Windows, Application Support on a Mac.</summary>
    static readonly string UserDataRoot = OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
        : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    public static readonly string AppDir = Environment.GetEnvironmentVariable("EDRUMKM_DATA") is { Length: > 0 } testDir
        ? testDir // for testing a fresh install without touching real settings
        : Path.Combine(UserDataRoot, "EDrumKitManager");
    /// <summary>Folder used before the rename to E-Drum Kit Manager; moved across on first start.</summary>
    static readonly string OldAppDir = Path.Combine(UserDataRoot, "TD12KitManager");

    /// <summary>Copies settings from the TD-12 Kit Manager folder the first time E-Drum Kit Manager starts.</summary>
    public static bool MigrateFromOldName()
    {
        if (Directory.Exists(AppDir) || !Directory.Exists(OldAppDir)) return false;
        foreach (var dir in Directory.GetDirectories(OldAppDir, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(OldAppDir, AppDir));
        Directory.CreateDirectory(AppDir);
        foreach (var file in Directory.GetFiles(OldAppDir, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(OldAppDir, AppDir), true);
        return true;
    }
    public static readonly string BridgeDir = Path.Combine(AppDir, "bridge");
    static readonly string KitStateDir = Path.Combine(BridgeDir, "kits");
    static readonly string KitsFile = Path.Combine(AppDir, "kits.json");
    static readonly string BaseFile = Path.Combine(AppDir, "base.bin");
    public static readonly string ReaperDir = Path.Combine(UserDataRoot, "REAPER");
    static readonly string ReaperPresetIni = Path.Combine(ReaperDir, "presets", "vst3-EZdrummer 3.ini");

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public List<Kit> Kits { get; private set; } = [];
    /// <summary>True when this start created the kit list (a new user).</summary>
    public bool IsFirstRun { get; private set; }
    /// <summary>The drum module in use; decides pad notes and the e-drum map.</summary>
    public ModuleProfile Profile { get; set; } = ModuleProfile.RolandTd12();
    public EzdState? Base { get; private set; }

    public void Load()
    {
        Directory.CreateDirectory(KitStateDir);

        if (File.Exists(BaseFile))
            Base = new EzdState(File.ReadAllBytes(BaseFile));
        else
        {
            // First run: use an EZdrummer preset saved in REAPER that carries the TD-12 map.
            var preset = EzdState.LoadReaperPresets(ReaperPresetIni).FirstOrDefault(p => p.State.HasEdrumMap);
            if (preset.State != null) SetBase(preset.State);
            else
            {
                // New users: start from the EZdrummer settings shipped with Kit Manager (Main Room mixer, TD-12 map).
                using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("DefaultBase.bin")!;
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                SetBase(new EzdState(ms.ToArray()));
            }
        }

        if (File.Exists(KitsFile))
        {
            try { Kits = JsonSerializer.Deserialize<List<Kit>>(File.ReadAllText(KitsFile), Json) ?? []; }
            catch (JsonException)
            {
                // Keep a copy so a later save can never lose the kits.
                File.Copy(KitsFile, KitsFile + $".unreadable-{DateTime.Now:yyyyMMdd-HHmmss}", true);
                throw;
            }
        }
        else
        {
            IsFirstRun = true;
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("DefaultKits.json")!;
            Kits = JsonSerializer.Deserialize<List<Kit>>(s, Json) ?? [];
            foreach (var k in Kits) k.Id = Guid.NewGuid().ToString("N");
            Save();
        }
        WriteKitFiles();
    }

    public void SetBase(EzdState state)
    {
        Directory.CreateDirectory(AppDir);
        if (File.Exists(BaseFile)) File.Copy(BaseFile, BaseFile + ".previous", true);
        File.WriteAllBytes(BaseFile, state.ToBlob("TD-12 base"));
        Base = state;
    }

    public void Save()
    {
        Directory.CreateDirectory(AppDir);
        string tmp = KitsFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Kits, Json));
        File.Move(tmp, KitsFile, true);
    }

    public string KitStatePath(Kit kit) => Path.Combine(KitStateDir, kit.Id + ".txt");

    public string PreviewStatePath => Path.Combine(KitStateDir, "preview.txt");

    public void WriteKitState(Kit kit, string path)
    {
        if (Base == null) throw new InvalidOperationException("No EZdrummer base settings yet. Use \"Update map and mixer from REAPER\" first.");
        File.WriteAllLines(path, Base.BuildChunkLines(kit, Profile));
        // One percentage per MIDI note for the pad levels effect: the global pad level times this kit's drum level.
        var global = PadLevels.Load();
        var notes = Enumerable.Repeat(100.0, 128).ToArray();
        foreach (var pad in Profile.Pads())
        {
            double kitLevel = pad.Slot >= 0 && pad.Slot < kit.Slots.Count ? kit.Slots[pad.Slot].Level : 100;
            if (pad.CanPlayOneShot && !string.IsNullOrEmpty(kit.OneShot) && kit.OneShotPad == pad.Name) kitLevel = kit.OneShotLevel;
            double value = Math.Clamp(global.GetValueOrDefault(pad.Name, 100) * kitLevel / 100, 25, 300);
            foreach (int n in pad.Notes) if (n is >= 0 and < 128) notes[n] = value;
        }
        File.WriteAllLines(Path.ChangeExtension(path, ".levels"), notes.Select(v => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));
    }

    /// <summary>Regenerates every kit file and the TD-12 program change map used by the bridge.</summary>
    public void WriteKitFiles()
    {
        if (Base == null) return;
        Directory.CreateDirectory(KitStateDir);
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { PreviewStatePath };
        var programs = new List<string>();
        foreach (var kit in Kits)
        {
            string path = KitStatePath(kit);
            keep.Add(path);
            WriteKitState(kit, path);
            if (kit.Td12KitNumber > 0)
                programs.Add($"{kit.Td12KitNumber}\t{path}\t{kit.Name}");
        }
        foreach (var f in Directory.GetFiles(KitStateDir, "*.txt"))
            if (!keep.Contains(f)) File.Delete(f);
        foreach (var f in Directory.GetFiles(KitStateDir, "*.levels"))
            if (!keep.Contains(Path.ChangeExtension(f, ".txt"))) File.Delete(f);
        File.WriteAllLines(Path.Combine(BridgeDir, "programs.txt"), programs);
        WriteKitList();
        try { EzdPresets.WriteAll(Kits); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    static readonly string StepFile = Path.Combine(AppDir, "step-with-td12.txt");
    static readonly string KitListFile = Path.Combine(BridgeDir, "kitlist.txt");

    /// <summary>When on, the TD-12's kit up/down buttons step through the kit list in REAPER.</summary>
    public bool StepWithKitButtons
    {
        get => !File.Exists(StepFile) || File.ReadAllText(StepFile).Trim() != "off";
        set { File.WriteAllText(StepFile, value ? "on" : "off"); WriteKitList(); }
    }

    /// <summary>Kit order for stepping; the bridge only steps while this file exists.</summary>
    public void WriteKitList()
    {
        if (!StepWithKitButtons || Base == null) { File.Delete(KitListFile); return; }
        File.WriteAllLines(KitListFile, Kits.Select(k => $"{KitStatePath(k)}\t{k.Name}"));
    }

    public void Move(Kit kit, int delta)
    {
        int i = Kits.IndexOf(kit), j = i + delta;
        if (i < 0 || j < 0 || j >= Kits.Count) return;
        (Kits[i], Kits[j]) = (Kits[j], Kits[i]);
    }
}
