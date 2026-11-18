using AppKit;
using CoreGraphics;
using Foundation;
using ObjCRuntime;

namespace TD12KitManager.Mac;

/// <summary>The kit list. Enter loads the kit, Delete removes it, Option+Up/Down moves it, like the Windows window.</summary>
sealed class KitTable : NSTableView
{
    public Action? OnLoad, OnDelete, OnEdit;
    public Action<int>? OnMove;

    public override void KeyDown(NSEvent e)
    {
        bool option = e.ModifierFlags.HasFlag(NSEventModifierMask.AlternateKeyMask);
        switch (e.KeyCode)
        {
            case 36 or 76: OnLoad?.Invoke(); return;          // Return, keypad Enter
            case 51 or 117: OnDelete?.Invoke(); return;       // Delete, forward delete
            case 120: OnEdit?.Invoke(); return;               // F2
            case 126 when option: OnMove?.Invoke(-1); return; // Option+Up
            case 125 when option: OnMove?.Invoke(1); return;  // Option+Down
        }
        base.KeyDown(e);
    }
}

sealed class KitSource(KitStore store) : NSTableViewDataSource
{
    public override nint GetRowCount(NSTableView tableView) => store.Kits.Count;

    public override NSObject GetObjectValue(NSTableView tableView, NSTableColumn? tableColumn, nint row) =>
        new NSString(store.Kits[(int)row].ToString());
}

sealed class KitDelegate(Action selectionChanged) : NSTableViewDelegate
{
    public override void SelectionDidChange(NSNotification notification) => selectionChanged();
}

/// <summary>Main window: kit list, kit details, status line and the commands that do not need a dialog yet.</summary>
sealed class MainWindowController
{
    readonly KitStore store = new();
    HardwareSettings hardware = HardwareSettings.Load();
    ModuleProfile Profile => store.Profile;

    readonly NSWindow window;
    readonly KitTable table = new();
    readonly NSTextView details;
    readonly NSTextField status;
    readonly List<NSButton> reaperButtons = [];
    readonly MetronomeControl metronome;
    NSTimer? statusWatch;
    string lastPcStatus = "";
    bool busy;

    public MainWindowController()
    {
        window = new NSWindow(new CGRect(0, 0, 940, 650),
            NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Miniaturizable | NSWindowStyle.Resizable,
            NSBackingStore.Buffered, false)
        {
            Title = "E-Drum Kit Manager",
        };
        window.ReleaseWhenClosed(false);
        window.Center();
        var content = window.ContentView!;

        NSTextField Label(string text, CGRect frame) => new(frame)
        {
            StringValue = text, Editable = false, Bordered = false, DrawsBackground = false, Selectable = false,
            AutoresizingMask = NSViewResizingMask.MinYMargin,
        };
        content.AddSubview(Label("Kit list (Enter loads the kit in REAPER):", new CGRect(10, 622, 380, 20)));
        content.AddSubview(Label("Kit details:", new CGRect(400, 622, 300, 20)));

        // Kit list.
        var column = new NSTableColumn("kit") { Title = "Kits", ResizingMask = NSTableColumnResizing.Autoresizing };
        table.AddColumn(column);
        table.HeaderView = null;
        table.DataSource = new KitSource(store);
        table.Delegate = new KitDelegate(ShowDetails);
        table.AllowsEmptySelection = false;
        // NSTableView.AccessibilityLabel is read-only in the .NET binding; set it through the Objective-C property.
        table.SetValueForKey(new NSString("Kits"), new NSString("accessibilityLabel"));
        table.AccessibilityHelp ="Press Return to load the selected kit in REAPER. F2 edits it. Delete removes it. Option Up or Down moves it.";
        table.OnLoad = () => _ = LoadSelected();
        table.OnDelete = () => DeleteSelected();
        table.OnEdit = EditSelected;
        table.OnMove = MoveSelected;
        var listScroll = new NSScrollView(new CGRect(10, 120, 380, 494))
        {
            DocumentView = table, HasVerticalScroller = true, BorderType = NSBorderType.BezelBorder,
            AutoresizingMask = NSViewResizingMask.HeightSizable | NSViewResizingMask.MaxXMargin,
            AccessibilityLabel = "Kit list",
        };
        content.AddSubview(listScroll);

        // Kit details (read-only, but focusable so VoiceOver can read it line by line).
        var detailsScroll = NSTextView.CreateScrollableTextView();
        detailsScroll.Frame = new CGRect(400, 120, 530, 494);
        detailsScroll.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable;
        detailsScroll.BorderType = NSBorderType.BezelBorder;
        details = (NSTextView)detailsScroll.DocumentView!;
        details.Editable = false;
        details.Selectable = true;
        details.AccessibilityLabel = "Kit details";
        content.AddSubview(detailsScroll);

        // Buttons for the commands that work without a dialog; the menus carry the same commands.
        NSButton AddButton(string title, nfloat x, nfloat width, Action action, bool usesReaper)
        {
            var b = new NSButton(new CGRect(x, 46, width, 28)) { Title = title, BezelStyle = NSBezelStyle.Rounded };
            b.AutoresizingMask = NSViewResizingMask.MaxYMargin | NSViewResizingMask.MaxXMargin;
            b.Activated += (_, _) => action();
            content.AddSubview(b);
            if (usesReaper) reaperButtons.Add(b);
            return b;
        }
        AddButton("Load in REAPER", 10, 140, () => _ = LoadSelected(), true);
        AddButton("New kit...", 160, 100, () => NewKit(null), false);
        AddButton("Edit kit...", 270, 100, EditSelected, false);
        AddButton("Duplicate", 380, 100, Duplicate, false);
        AddButton("Delete", 490, 80, DeleteSelected, false);
        AddButton("Check REAPER connection", 580, 210, () => _ = Ping(), true);

        AddButton("Pad levels...", 800, 120, AdjustPadLevels, false);

        // Metronome row: tempo, time signature, volume, start/stop and tap tempo.
        metronome = new MetronomeControl(async (waiting, request) =>
        {
            var reply = await AskReaper(waiting, TimeSpan.FromSeconds(5), request);
            if (reply is { Ok: true } r) Status(r.Message);
            return reply is { Ok: true };
        });
        reaperButtons.Add(metronome.StartStopButton);
        metronome.View.Frame = new CGRect(10, 82, 920, 30);
        metronome.View.AutoresizingMask = NSViewResizingMask.MaxYMargin | NSViewResizingMask.MaxXMargin;
        content.AddSubview(metronome.View);

        content.AddSubview(Label("Status:", new CGRect(10, 26, 200, 18)));
        status = new NSTextField(new CGRect(10, 6, 920, 20))
        {
            Editable = false, Bordered = true, DrawsBackground = true, Selectable = true,
            AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.MaxYMargin,
            AccessibilityLabel = "Status",
        };
        content.AddSubview(status);

        NSApplication.SharedApplication.MainMenu = BuildMenu();
    }

    public void Show()
    {
        window.MakeKeyAndOrderFront(null);
        _ = Startup();
    }

    NSMenu BuildMenu()
    {
        NSMenuItem Item(string title, Action action, string key = "") =>
            new(title, key, (_, _) => action());
        NSMenuItem Submenu(string title, params NSMenuItem[] items)
        {
            var menu = new NSMenu(title);
            foreach (var i in items) menu.AddItem(i);
            return new NSMenuItem(title) { Submenu = menu };
        }

        var bar = new NSMenu();
        // App menu: About and Quit use the standard responder actions.
        var app = new NSMenu();
        app.AddItem(Item("About E-Drum Kit Manager", ShowAbout));
        app.AddItem(NSMenuItem.SeparatorItem);
        app.AddItem(new NSMenuItem("Quit E-Drum Kit Manager", new Selector("terminate:"), "q"));
        bar.AddItem(new NSMenuItem("E-Drum Kit Manager") { Submenu = app });

        bar.AddItem(Submenu("File",
            Item("New Kit...", () => NewKit(null), "n"),
            Item("Import Current EZdrummer Kit from REAPER", () => _ = ImportFromReaper()),
            NSMenuItem.SeparatorItem,
            Item("Back Up Module Settings...", () => _ = BackupTd12()),
            Item("Restore Module Backup...", () => _ = RestoreTd12())));
        bar.AddItem(Submenu("Kit",
            Item("Load in REAPER", () => _ = LoadSelected()),
            Item("Edit...", EditSelected),
            Item("Duplicate", Duplicate, "d"),
            Item("Delete", DeleteSelected),
            NSMenuItem.SeparatorItem,
            Item("Move Up", () => MoveSelected(-1)),
            Item("Move Down", () => MoveSelected(1))));
        bar.AddItem(Submenu("Tools",
            Item("Hardware Setup (Drum Module, MIDI)...", () => _ = HardwareSetup()),
            Item("Pad Levels...", AdjustPadLevels),
            Item("Reconnect Drum Module MIDI", () => _ = ReconnectMidi()),
            Item("Update Map and Mixer from REAPER", () => _ = UpdateBase()),
            NSMenuItem.SeparatorItem,
            Item("Check REAPER Connection", () => _ = Ping())));
        // F5 starts or stops the click, as on Windows.
        var startStop = Item("Start or Stop", () => _ = metronome.Toggle(), "");
        startStop.KeyEquivalentModifierMask = 0;
        bar.AddItem(Submenu("Metronome", startStop, Item("Tap Tempo", metronome.Tap)));
        bar.AddItem(Submenu("Help", Item("User Guide", OpenGuide, "?")));
        return bar;
    }

    // ----- startup -----------------------------------------------------------------------------

    async Task Startup()
    {
        List<string> expansions = [];
        try { expansions = Catalog.LoadInstalledLibraries(); }
        catch (Exception) { /* keep the built-in core library list */ }
        try
        {
            store.Profile = hardware.ActiveProfile();
            hardware.WriteBridgeSettings();
            store.Load();
        }
        catch (Exception ex) { Status("Could not load kits: " + ex.Message); }
        RefreshList();
        window.MakeFirstResponder(table);

        statusWatch = NSTimer.CreateRepeatingScheduledTimer(0.5, _ =>
        {
            // Kits loaded from the module's buttons are announced here too.
            if (ReaperBridge.ReadStatus() is { } st && st.Id.StartsWith("pc") && st.Id != lastPcStatus)
            {
                bool first = lastPcStatus == "";
                lastPcStatus = st.Id;
                if (!first) Status($"From the {Profile.Name}: " + st.Message);
            }
        });

        bool firstInstall = false;
        try { firstInstall = ReaperBridge.Install(); }
        catch (Exception ex) { Status("Could not install the REAPER bridge script: " + ex.Message); return; }

        if (store.Base == null)
        {
            Status("No EZdrummer settings were found yet. Open REAPER with EZdrummer, then update the map and mixer from REAPER.");
            return;
        }
        if (store.IsFirstRun)
        {
            // New user: start with hardware setup (choose or learn the drum module, pick MIDI ports).
            Status("Welcome to E-Drum Kit Manager. First, tell it about your drum module.");
            await Task.Delay(800);
            await HardwareSetup();
        }
        if (firstInstall)
        {
            Status($"{store.Kits.Count} kits ready. The REAPER bridge was just installed: restart REAPER once so it can receive kits.");
            return;
        }
        string libraries = expansions.Count == 0 ? "" : $" Expansions found: {string.Join(", ", expansions)}.";
        var reply = await ReaperBridge.WaitForReply(ReaperBridge.Send("PING"), TimeSpan.FromSeconds(2));
        Status(reply is { Ok: true }
            ? $"{store.Kits.Count} kits ready.{libraries} Connected to REAPER."
            : $"{store.Kits.Count} kits ready.{libraries} REAPER is not connected yet; open REAPER to load kits.");
    }

    // ----- list and details --------------------------------------------------------------------

    Kit? Selected => table.SelectedRow is var r and >= 0 && r < store.Kits.Count ? store.Kits[(int)r] : null;

    void RefreshList(Kit? select = null)
    {
        var keep = select ?? Selected;
        table.ReloadData();
        int index = keep != null ? store.Kits.IndexOf(keep) : -1;
        if (index < 0 && store.Kits.Count > 0) index = 0;
        if (index >= 0) table.SelectRow(index, false);
        ShowDetails();
    }

    void ShowDetails()
    {
        if (Selected is not { } kit) { details.Value = ""; return; }
        var lines = new List<string> { kit.Name };
        lines.Add("Rooms: " + Catalog.RoomsUsed(kit.Slots));
        if (kit.Room != 100 || kit.Overheads != 100) lines.Add($"Room amount {kit.Room}%, overheads {kit.Overheads}%");
        if (Catalog.OneShots.FirstOrDefault(o => o.Id == kit.OneShot) is { Name: not null } shot)
            lines.Add($"{kit.OneShotPad} pad plays: {shot.Name}" + (kit.OneShotLevel != 100 ? $", level {kit.OneShotLevel}%" : ""));
        lines.Add(kit.Td12KitNumber > 0 ? $"Switches when module kit {kit.Td12KitNumber} is selected" : "No module kit number");
        for (int i = 0; i < Catalog.Slots.Length && i < kit.Slots.Count; i++)
        {
            var s = kit.Slots[i];
            string extra = (s.Tune != 0 ? $", tuned {(s.Tune > 0 ? "+" : "")}{s.Tune}" : "") + (s.Level != 100 ? $", level {s.Level}%" : "");
            lines.Add($"{Catalog.Slots[i].Label}: {Catalog.Describe(s)}{extra}");
        }
        details.Value = string.Join("\n", lines);
    }

    /// <summary>Shows the text in the status line and has VoiceOver speak it.</summary>
    void Status(string text)
    {
        status.StringValue = text;
        Announcer.Say(window, text);
    }

    // ----- REAPER ------------------------------------------------------------------------------

    void SetReaperButtons(bool enabled) => reaperButtons.ForEach(b => b.Enabled = enabled);

    async Task<(bool Ok, string Message)?> AskReaper(string waiting, TimeSpan timeout, params string[] request)
    {
        if (busy) { Status("Still waiting for REAPER, please try again in a moment."); return null; }
        busy = true;
        SetReaperButtons(false);
        try
        {
            Status(waiting);
            var reply = await ReaperBridge.WaitForReply(ReaperBridge.Send(request), timeout);
            if (reply == null)
                Status("REAPER did not answer. Make sure REAPER is open. If you just installed this app, restart REAPER once.");
            else if (!reply.Value.Ok)
                Status("REAPER: " + reply.Value.Message);
            return reply;
        }
        finally
        {
            busy = false;
            SetReaperButtons(true);
        }
    }

    async Task LoadKit(Kit kit, string path, string label)
    {
        try { store.WriteKitState(kit, path); }
        catch (Exception ex) { Status(ex.Message); return; }
        var reply = await AskReaper($"Loading {label}...", TimeSpan.FromSeconds(45), "APPLY", label, path);
        if (reply is { Ok: true } r) Status(r.Message);
    }

    async Task LoadSelected()
    {
        if (Selected is { } kit) await LoadKit(kit, store.KitStatePath(kit), kit.Name);
    }

    Task Preview(Kit kit) => LoadKit(kit, store.PreviewStatePath, "Preview: " + kit.Name);

    async Task Ping()
    {
        var reply = await AskReaper("Checking REAPER...", TimeSpan.FromSeconds(3), "PING");
        if (reply is { Ok: true } r) Status(r.Message);
    }

    // ----- kit commands ------------------------------------------------------------------------

    bool NameTaken(string name, Kit self) =>
        store.Kits.Any(k => k != self && string.Equals(k.Name, name, StringComparison.OrdinalIgnoreCase));

    void NewKit(List<KitSlot>? slots)
    {
        var kit = new Kit
        {
            Name = slots == null ? "" : "Kit from REAPER",
            Source = slots == null ? "Created in E-Drum Kit Manager" : "Imported from REAPER",
            Slots = slots ?? (Selected?.Slots ?? store.Kits.FirstOrDefault()?.Slots ?? [])
                .Select(s => new KitSlot { Lib = s.Lib, Drum = s.Drum }).ToList(),
        };
        OpenEditor(kit, isNew: true);
    }

    void EditSelected()
    {
        if (Selected is { } kit) OpenEditor(kit, isNew: false);
    }

    void OpenEditor(Kit kit, bool isNew)
    {
        var editor = new KitEditorSheet(kit, isNew, Profile, Preview, name => NameTaken(name, kit));
        editor.Show(window, saved =>
        {
            if (!saved) { window.MakeFirstResponder(table); return; }
            if (isNew) store.Kits.Add(kit);
            string note = "";
            if (kit.Td12KitNumber > 0)
            {
                foreach (var other in store.Kits.Where(k => k != kit && k.Td12KitNumber == kit.Td12KitNumber))
                {
                    other.Td12KitNumber = 0;
                    note = $" Module kit {kit.Td12KitNumber} was moved from {other.Name}.";
                }
            }
            SaveAll();
            RefreshList(kit);
            window.MakeFirstResponder(table);
            Status($"Saved {kit.Name}.{note}");
        });
    }

    // ----- import, update, backup and restore ---------------------------------------------------

    bool Confirm(string title, string text, string yes, bool defaultIsYes = false)
    {
        var alert = new NSAlert { MessageText = title, InformativeText = text, AlertStyle = NSAlertStyle.Warning };
        if (defaultIsYes) { alert.AddButton(yes); alert.AddButton("Cancel"); return alert.RunModal() == 1000; }
        alert.AddButton("Cancel"); // first button is the default, so Return cancels
        alert.AddButton(yes);
        return alert.RunModal() == 1001;
    }

    async Task<EzdState?> CaptureFromReaper()
    {
        File.Delete(ReaperBridge.CaptureFile);
        var reply = await AskReaper("Reading EZdrummer from REAPER...", TimeSpan.FromSeconds(10), "CAPTURE", ReaperBridge.CaptureFile);
        if (reply is not { Ok: true }) return null;
        try { return EzdState.FromChunkLines(File.ReadAllLines(ReaperBridge.CaptureFile)); }
        catch (Exception ex) { Status("Could not read EZdrummer's settings: " + ex.Message); return null; }
    }

    async Task ImportFromReaper()
    {
        var state = await CaptureFromReaper();
        if (state == null) return;
        List<KitSlot> slots;
        try { slots = state.ReadSlots(); }
        catch (Exception ex) { Status(ex.Message); return; }
        var unknown = slots.Where(sl => Catalog.Find(sl.Lib, sl.Drum) == null).Select(sl => sl.Drum).ToList();
        if (unknown.Count > 0)
            Status($"Some drums are not in the EZdrummer 3 core library and were replaced: {string.Join(", ", unknown)}.");
        else
            Status("Read the kit from REAPER. Name it and save.");
        NewKit(slots);
    }

    async Task UpdateBase()
    {
        var state = await CaptureFromReaper();
        if (state == null) return;
        if (!state.HasEdrumMap &&
            !Confirm("EZdrummer in REAPER has no e-drum map loaded.", "Use its settings anyway?", "Use Anyway"))
        {
            Status("Not updated.");
            return;
        }
        try
        {
            state.ReadSlots();
            store.SetBase(state);
            SaveAll();
            Status($"Updated from REAPER. All kits now use map \"{state.EdrumMapName ?? "none"}\" and the current mixer.");
        }
        catch (Exception ex) { Status("Not updated: " + ex.Message); }
    }

    static readonly string BackupDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TD-12 Backups");

    /// <summary>Like AskReaper, for a request that was already sent.</summary>
    async Task<(bool Ok, string Message)?> AskReaperWait(string waiting, string id, TimeSpan timeout)
    {
        busy = true;
        SetReaperButtons(false);
        try
        {
            Status(waiting);
            var reply = await ReaperBridge.WaitForReply(id, timeout);
            if (reply == null) Status("No answer in time. Nothing was saved or changed.");
            else if (!reply.Value.Ok) Status("REAPER: " + reply.Value.Message);
            return reply;
        }
        finally
        {
            busy = false;
            SetReaperButtons(true);
        }
    }

    async Task BackupTd12()
    {
        if (!Profile.RolandBackup) { Status($"Backups work with Roland modules that support bulk dump. {Profile.Name} is not set up for it."); return; }
        if (!Confirm("Back up TD-12 settings",
                "Press OK, then on the TD-12:\n\n1. Press SETUP.\n2. Press F1 (MIDI).\n3. Press F5 (BULK). The type is normally already set to ALL.\n4. Press F5 (EXECUTE).\n\n" +
                "Kit Manager waits up to 15 minutes and saves the backup when the TD-12 has finished sending.", "OK", defaultIsYes: true)) return;
        Directory.CreateDirectory(BackupDir);
        string path = Path.Combine(BackupDir, $"TD-12 backup {DateTime.Now:yyyy-MM-dd HHmm}.syx");
        string id = ReaperBridge.Send("SYSEXREC", "900", path);
        var started = await ReaperBridge.WaitForReply(id + ":started", TimeSpan.FromSeconds(5));
        if (started == null) { Status("REAPER did not answer. Make sure REAPER is open, then try again."); return; }
        var reply = await AskReaperWait("Waiting for the TD-12. Start the bulk dump on the module now: SETUP, F1, F5, then F5.", id, TimeSpan.FromMinutes(16));
        if (reply is not { Ok: true }) return;
        var (count, bad) = Sysex.Check(File.ReadAllBytes(path));
        Status(bad == 0
            ? $"Backup saved: {count} messages, all checks passed. File: {Path.GetFileName(path)} in Documents, TD-12 Backups."
            : $"Backup saved, but {bad} of {count} messages failed their check. Try the backup again. File: {path}");
    }

    async Task RestoreTd12()
    {
        if (!Profile.RolandBackup) { Status($"Backups work with Roland modules that support bulk dump. {Profile.Name} is not set up for it."); return; }
        var panel = NSOpenPanel.OpenPanel;
        panel.Title = "Choose a TD-12 backup to restore";
        panel.CanChooseFiles = true;
        panel.CanChooseDirectories = false;
        panel.AllowsMultipleSelection = false;
        panel.AllowedFileTypes = ["syx"];
        panel.DirectoryUrl = NSUrl.FromFilename(Directory.Exists(BackupDir) ? BackupDir : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        if (panel.RunModal() != 1 || panel.Url?.Path is not { } file) return;
        var (count, bad) = Sysex.Check(File.ReadAllBytes(file));
        if (count == 0 || bad > 0)
        {
            Status($"That file can't be restored: {count} messages, {bad} failed their check.");
            return;
        }
        if (!Confirm("Restore TD-12 backup",
                $"This replaces the settings on the TD-12 with the backup \"{Path.GetFileName(file)}\" ({count} messages). " +
                "Whatever is in the backup (kits, triggers, setup) is overwritten on the module.\n\nRestore now?", "Restore")) return;
        int seconds = (int)Math.Ceiling(count * 0.06) + 5;
        string id = ReaperBridge.Send("SYSEXSEND", file, hardware.MidiOutput);
        var reply = await AskReaperWait($"Sending the backup to the TD-12. This takes about {seconds} seconds; don't touch the module.", id, TimeSpan.FromSeconds(seconds + 30));
        if (reply is { Ok: true } r) Status(r.Message + ". Restore finished.");
    }

    // ----- help ----------------------------------------------------------------------------------

    void OpenGuide()
    {
        string guide = Path.Combine(NSBundle.MainBundle.ResourcePath ?? AppContext.BaseDirectory, "User Guide.html");
        if (File.Exists(guide)) NSWorkspace.SharedWorkspace.OpenUrl(NSUrl.FromFilename(guide));
        else Status("The user guide was not found inside the app. Reinstall E-Drum Kit Manager to restore it.");
    }

    void ShowAbout()
    {
        string version = NSBundle.MainBundle.ObjectForInfoDictionary("CFBundleShortVersionString")?.ToString() ?? "1.0.0";
        var alert = new NSAlert
        {
            MessageText = $"E-Drum Kit Manager, version {version}",
            InformativeText = "Builds and switches EZdrummer 3 drum kits in REAPER for electronic drum kits, with screen readers in mind.\n\n" +
                "Not made or supported by Toontrack or Roland. It reads and writes EZdrummer's own settings, which are not publicly documented, so an EZdrummer update could affect it.",
            AlertStyle = NSAlertStyle.Informational,
        };
        alert.AddButton("OK");
        alert.RunModal();
    }

    async Task ReconnectMidi()
    {
        var reply = await AskReaper("Resetting REAPER's MIDI devices...", TimeSpan.FromSeconds(5), "MIDIRESET");
        if (reply is { Ok: true } r) Status(r.Message);
    }

    async Task HardwareSetup()
    {
        // Ask REAPER which MIDI ports it has (they must be enabled in REAPER's MIDI device preferences).
        List<string> ins = [], outs = [];
        var reply = await AskReaper("Asking REAPER for its MIDI ports...", TimeSpan.FromSeconds(3), "MIDIDEVICES");
        if (reply is { Ok: true } r)
        {
            foreach (var part in r.Message.Split(';'))
            {
                var kv = part.Split('=', 2);
                if (kv.Length < 2) continue;
                var names = kv[1].Split('|', StringSplitOptions.RemoveEmptyEntries).ToList();
                if (kv[0] == "IN") ins = names; else if (kv[0] == "OUT") outs = names;
            }
        }
        var sheet = new HardwareSheet(hardware, ins, outs, LearnKit);
        sheet.Show(window, result =>
        {
            window.MakeFirstResponder(table);
            if (result == null) return;
            hardware = result;
            hardware.Save();
            hardware.WriteBridgeSettings();
            store.Profile = hardware.ActiveProfile();
            SaveAll();
            Status($"Hardware set: {Profile.Name}, MIDI in {Describe(hardware.MidiInput)}, MIDI out {Describe(hardware.MidiOutput)}. Load a kit to apply it in REAPER.");

            static string Describe(string port) => port.Length == 0 ? "any" : port;
        });
    }

    /// <summary>"Learn my kit": asks REAPER for the next hit, so the wizard can record each pad's note.</summary>
    async Task<(int Note, int Channel)?> LearnKit(TimeSpan timeout)
    {
        string id = ReaperBridge.Send("LEARN", ((int)timeout.TotalSeconds).ToString());
        if (await ReaperBridge.WaitForReply(id + ":started", TimeSpan.FromSeconds(3)) == null) return null;
        var reply = await ReaperBridge.WaitForReply(id, timeout + TimeSpan.FromSeconds(2));
        if (reply is not { Ok: true } r) return null;
        var parts = r.Message.Split(' ');
        return parts.Length >= 2 && int.TryParse(parts[0], out int note) && int.TryParse(parts[1], out int ch) ? (note, ch) : null;
    }

    void AdjustPadLevels()
    {
        var sheet = new PadLevelsSheet(Profile, async () =>
        {
            store.WriteKitFiles(); // each kit's per-note levels include the pad levels
            var reply = await ReaperBridge.WaitForReply(ReaperBridge.Send("PADLEVELS"), TimeSpan.FromSeconds(3));
            if (reply is not { Ok: true })
                Status(reply == null ? "REAPER did not answer; the levels are saved and apply when REAPER is open." : "REAPER: " + reply.Value.Message);
        });
        sheet.Show(window, () =>
        {
            window.MakeFirstResponder(table);
            Status("Pad levels saved.");
        });
    }

    void Duplicate()
    {
        if (Selected is not { } kit) return;
        var copy = kit.Clone();
        string name = kit.Name + " copy";
        for (int i = 2; NameTaken(name, copy); i++) name = $"{kit.Name} copy {i}";
        copy.Name = name;
        store.Kits.Insert(store.Kits.IndexOf(kit) + 1, copy);
        SaveAll();
        RefreshList(copy);
        Status($"Created {copy.Name}.");
    }

    void DeleteSelected()
    {
        if (Selected is not { } kit) return;
        var alert = new NSAlert
        {
            MessageText = $"Delete the kit \"{kit.Name}\"?",
            InformativeText = "This removes the kit from the list.",
            AlertStyle = NSAlertStyle.Warning,
        };
        alert.AddButton("Cancel"); // first button is the default, so Return cancels
        alert.AddButton("Delete");
        if (alert.RunModal() != 1001) return;

        int index = (int)table.SelectedRow;
        store.Kits.Remove(kit);
        SaveAll();
        RefreshList();
        if (store.Kits.Count > 0) table.SelectRow(Math.Min(index, store.Kits.Count - 1), false);
        window.MakeFirstResponder(table);
        Status($"Deleted {kit.Name}.");
    }

    void MoveSelected(int delta)
    {
        if (Selected is not { } kit) return;
        store.Move(kit, delta);
        SaveAll();
        RefreshList(kit);
        Status($"{kit.Name} moved to position {store.Kits.IndexOf(kit) + 1} of {store.Kits.Count}.");
    }

    void SaveAll()
    {
        try
        {
            store.Save();
            store.WriteKitFiles();
        }
        catch (Exception ex) { Status("Could not save: " + ex.Message); }
    }
}
