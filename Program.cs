namespace TD12KitManager;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Diagnostic: EDrumKitManager.exe --dump-catalog out.txt lists every drum found in the installed libraries.
        if (args.Length == 2 && args[0] == "--dump-catalog")
        {
            var expansions = Catalog.LoadInstalledLibraries();
            var lines = new List<string> { "Expansions: " + string.Join(", ", expansions) };
            lines.AddRange(Catalog.Rooms.Select(r => "Room: " + r.Lib + " = " + r.Name));
            foreach (var slot in Catalog.Slots)
                lines.Add($"{slot.Label}: {Catalog.ForSlot(slot).Count()} drums");
            lines.AddRange(Catalog.Drums.Select(d => $"{d.Lib}\t{d.Pos}\t{d.Id}\t{d.Type}\t{d.DisplayName}"));
            File.WriteAllLines(args[1], lines);
            return;
        }
        // Diagnostic: EDrumKitManager.exe --check out.txt loads the kits and writes any error.
        if (args.Length == 2 && args[0] == "--check")
        {
            try
            {
                Catalog.LoadInstalledLibraries();
                var store = new KitStore();
                store.Load();
                using var res = typeof(KitStore).Assembly.GetManifestResourceStream("DefaultBase.bin")!;
                using var ms = new MemoryStream();
                res.CopyTo(ms);
                var def = new EzdState(ms.ToArray());
                def.BuildChunkLines(store.Kits[0], store.Profile);
                File.WriteAllText(args[1], $"OK, {store.Kits.Count} kits; default base map: {def.EdrumMapName}");
            }
            catch (Exception ex) { File.WriteAllText(args[1], ex.ToString()); }
            return;
        }
        // Diagnostic: EDrumKitManager.exe --maplines out.txt writes the e-drum map lines generated for the TD-12 profile.
        if (args.Length == 2 && args[0] == "--maplines")
        {
            File.WriteAllLines(args[1], ModuleProfile.RolandTd12().MapLines());
            return;
        }
        // Diagnostic: EDrumKitManager.exe --modules out.txt lists the drum modules found in EZdrummer, with their notes.
        if (args.Length == 2 && args[0] == "--modules")
        {
            File.WriteAllLines(args[1], EzdModules.Load().Select(p =>
                $"{p.Name}: {string.Join(", ", p.Notes.Select(n => $"{n.Key} {n.Value}"))}"));
            return;
        }
        // Run by the uninstaller: take the bridge out of REAPER's startup.
        if (args.Length is 1 or 2 && args[0] == "--uninstall-reaper-bridge") { try { ReaperBridge.Uninstall(args.Length == 2 ? args[1] : null); } catch (Exception) { } return; }
        // Diagnostic: --update-check out.txt [as-version] looks up the latest release, as if this were as-version.
        if (args.Length is 2 or 3 && args[0] == "--update-check")
        {
            var current = args.Length == 3 ? Version.Parse(args[2]) : typeof(Program).Assembly.GetName().Version!;
            try { var u = Updates.CheckAsync(current).GetAwaiter().GetResult(); File.WriteAllText(args[1], u == null ? "up to date" : $"{u.Version} {u.Size} {u.DownloadUrl}"); }
            catch (Exception ex) { File.WriteAllText(args[1], ex.ToString()); }
            return;
        }
        // Diagnostic: --expansion-kits out.txt shows the kits that would be made for each expansion (nothing is saved).
        if (args.Length == 2 && args[0] == "--expansion-kits")
        {
            Catalog.LoadInstalledLibraries();
            var lines = new List<string>();
            foreach (var (lib, name) in Catalog.Rooms.Where(r => !r.Lib.StartsWith("EZX3_EZdrummer3_")))
                foreach (var k in ExpansionKits.Build(lib, name))
                    lines.Add(k.Name + ": " + string.Join("; ", k.Slots.Select((s, i) => Catalog.Slots[i].Label + " = " + Catalog.Describe(s))));
            File.WriteAllLines(args[1], lines);
            return;
        }
        // Diagnostic: --grooves out.txt writes the groove tree (and reads the first groove's MIDI).
        if (args.Length == 2 && args[0] == "--grooves")
        {
            var lines = new List<string>();
            GrooveNode? first = null;
            void Walk(GrooveNode n, int depth) { if (depth < 4 || n.File == null) lines.Add(new string(' ', depth * 2) + n.Name + (n.File != null ? "  [file]" : "")); first ??= n.File != null ? n : null; foreach (var c in n.Children) Walk(c, depth + 1); }
            foreach (var n in Grooves.Load()) Walk(n, 0);
            if (first?.File is { } f) { var m = Grooves.ReadMidi(f); lines.Insert(0, $"First groove: {f} -> {m.Bpm} BPM, {m.Beats}/{m.BeatUnit}, {m.LengthQn} QN, {m.Bars} bars, {m.Notes.Count} notes"); }
            File.WriteAllLines(args[1], lines);
            return;
        }
        // Diagnostic: --speech-check out.txt says whether the NVDA controller client loads and NVDA is running.
        if (args.Length == 2 && args[0] == "--speech-check") { File.WriteAllText(args[1], Speech.Check()); return; }
        // Diagnostic: --genkit profile.json kitIndex out.txt builds one kit for another module profile.
        if (args.Length == 4 && args[0] == "--genkit")
        {
            var profile = System.Text.Json.JsonSerializer.Deserialize<ModuleProfile>(File.ReadAllText(args[1]))!;
            Catalog.LoadInstalledLibraries();
            var store = new KitStore();
            store.Load();
            store.Profile = profile;
            store.WriteKitState(store.Kits[int.Parse(args[2])], args[3]);
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
