using System.Windows.Forms.Automation;

namespace TD12KitManager;

public sealed class MainForm : Form
{
    readonly KitStore store = new();
    HardwareSettings hardware = HardwareSettings.Load();
    ModuleProfile Profile => store.Profile;
    readonly ListBox kitList = new() { Width = 360, Height = 300, AccessibleName = "Kits", IntegralHeight = false };
    readonly TextBox details = new()
    {
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Width = 520, Height = 300,
        AccessibleName = "Kit details", TabStop = true,
    };
    readonly TextBox statusBox = new() { ReadOnly = true, Width = 890, AccessibleName = "Status", TabStop = true };
    readonly List<Button> reaperButtons = [];
    readonly MetronomePanel metronome;
    readonly CheckBox stepCheck = new() { Text = "Step through kits &with the module's kit buttons", AutoSize = true, Margin = new Padding(3, 7, 3, 3) };
    readonly System.Windows.Forms.Timer statusWatch = new() { Interval = 500 };
    string lastPcStatus = "";
    bool loading = true;
    bool busy;

    public MainForm()
    {
        Text = "E-Drum Kit Manager";
        AutoScaleMode = AutoScaleMode.Font;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        var root = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Padding = new Padding(10), Dock = DockStyle.Fill };
        root.Controls.Add(new Label { Text = "Kit l&ist (Enter loads the kit):", AutoSize = true }, 0, 0);
        root.Controls.Add(new Label { Text = "Kit &details:", AutoSize = true }, 1, 0);
        root.Controls.Add(kitList, 0, 1);
        root.Controls.Add(details, 1, 1);

        var buttons = new RowPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(900, 0), Margin = new Padding(0, 8, 0, 8) };
        Button Add(string text, Func<Task> action, bool usesReaper = false)
        {
            var b = new Button { Text = text, AutoSize = true };
            b.Click += async (_, _) => await action();
            buttons.Controls.Add(b);
            if (usesReaper) reaperButtons.Add(b);
            return b;
        }
        Add("&Load kit", LoadSelected, true);
        Add("&New kit...", () => NewKit(null));
        Add("&Edit kit...", EditSelected);
        Add("Duplicate (Ctrl+D)", Duplicate);
        Add("Delete (Del key)", DeleteSelected);
        Add("Import current kit from EZdrummer", ImportFromReaper, true);
        Add("&Update map and mixer from EZdrummer", UpdateBase, true);
        Add("&Check connection", Ping, true);
        Add("Hardware setup (drum module, MIDI)...", HardwareSetup, true);
        Add("Reconnect drum module MIDI (&X)", ReconnectMidi, true);
        Add("Ad&just pad levels...", AdjustPadLevels);
        Add("&Back up module settings...", BackupTd12, true);
        Add("&Restore module backup...", RestoreTd12, true);
        stepCheck.CheckedChanged += (_, _) =>
        {
            if (loading) return;
            store.StepWithKitButtons = stepCheck.Checked;
            Status(stepCheck.Checked ? "The module's kit buttons now step through this list." : "The module's kit buttons no longer change kits, except kits with a module kit number.");
        };
        buttons.Controls.Add(stepCheck);
        root.Controls.Add(buttons, 0, 2);
        root.SetColumnSpan(buttons, 2);

        metronome = new MetronomePanel(async (waiting, request) =>
        {
            var reply = await AskReaper(waiting, TimeSpan.FromSeconds(5), request);
            if (reply is { Ok: true } r) Status(r.Message);
            return reply is { Ok: true };
        });
        reaperButtons.Add(metronome.StartStopButton);
        root.Controls.Add(metronome, 0, 3);
        root.SetColumnSpan(metronome, 2);

        root.Controls.Add(new Label { Text = "Status (F6):", AutoSize = true }, 0, 4);
        root.Controls.Add(statusBox, 0, 5);
        root.SetColumnSpan(statusBox, 2);

        // Menu bar: every command, grouped, for screen reader users who browse with Alt.
        // Shortcut keys already handled elsewhere (Enter, F2, Del, Ctrl+N, F5) are only displayed here.
        var menu = new MenuStrip { Dock = DockStyle.Fill };
        ToolStripMenuItem Item(string text, Func<Task> action, string? keys = null, Keys shortcut = Keys.None)
        {
            var item = new ToolStripMenuItem(text);
            item.Click += async (_, _) => await action();
            if (shortcut != Keys.None) item.ShortcutKeys = shortcut;
            if (keys != null) item.ShortcutKeyDisplayString = keys;
            return item;
        }
        // Grouped by what you're doing; nothing here is tied to one recording program.
        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.AddRange([
            Item("&New kit...", () => NewKit(null), "Ctrl+N"),
            new ToolStripSeparator(),
            Item("E&xit", () => { Close(); return Task.CompletedTask; }, "Alt+F4"),
        ]);
        var kitMenu = new ToolStripMenuItem("&Kits");
        kitMenu.DropDownItems.AddRange([
            Item("&Load", LoadSelected, "Enter"),
            Item("&Edit...", EditSelected, "F2"),
            Item("Du&plicate", Duplicate, shortcut: Keys.Control | Keys.D),
            Item("&Delete", DeleteSelected, "Del"),
            new ToolStripSeparator(),
            Item("Move &up", () => { MoveSelected(-1); return Task.CompletedTask; }, "Alt+Up"),
            Item("Move do&wn", () => { MoveSelected(1); return Task.CompletedTask; }, "Alt+Down"),
        ]);
        var practice = new ToolStripMenuItem("&Practice");
        practice.DropDownItems.AddRange([
            Item("&Grooves (play along)...", OpenGrooves, shortcut: Keys.Control | Keys.G),
            new ToolStripSeparator(),
            Item("&Start or stop metronome", () => metronome.Toggle(), "F5"),
            Item("&Tap tempo", () => { metronome.Tap(); return Task.CompletedTask; }),
        ]);
        var stepItem = new ToolStripMenuItem("&Step through kits with the module's kit buttons") { CheckOnClick = true };
        stepItem.Click += (_, _) => stepCheck.Checked = stepItem.Checked;
        var module = new ToolStripMenuItem("Drum &module");
        module.DropDownOpening += (_, _) => stepItem.Checked = stepCheck.Checked;
        module.DropDownItems.AddRange([
            Item("&Hardware setup (drum module, MIDI)...", HardwareSetup),
            Item("&Pad levels...", AdjustPadLevels),
            stepItem,
            new ToolStripSeparator(),
            Item("&Reconnect drum module MIDI", ReconnectMidi),
            new ToolStripSeparator(),
            Item("&Back up module settings...", BackupTd12),
            Item("Res&tore module backup...", RestoreTd12),
        ]);
        var ezdrummer = new ToolStripMenuItem("E&Zdrummer");
        ezdrummer.DropDownItems.AddRange([
            Item("&Import current kit from EZdrummer", ImportFromReaper),
            Item("&Update map and mixer from EZdrummer", UpdateBase),
            Item("&Check connection", Ping),
        ]);
        var updatesItem = new ToolStripMenuItem("Check for updates at &startup") { CheckOnClick = true, Checked = Updates.CheckAtStartup };
        updatesItem.Click += (_, _) => { Updates.CheckAtStartup = updatesItem.Checked; Status(updatesItem.Checked ? "E-Drum Kit Manager will check for updates when it starts." : "Automatic update checks are off. You can still check from the Help menu."); };
        var help = new ToolStripMenuItem("&Help");
        help.DropDownItems.AddRange([
            Item("&User guide", OpenGuide, shortcut: Keys.F1),
            Item("Check for up&dates...", () => CheckForUpdates(startup: false)),
            updatesItem,
            new ToolStripSeparator(),
            Item("&About E-Drum Kit Manager", ShowAbout),
        ]);
        menu.Items.AddRange([file, kitMenu, practice, module, ezdrummer, help]);
        MainMenuStrip = menu;

        var outer = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Fill, Padding = Padding.Empty };
        outer.Controls.Add(menu, 0, 0);
        outer.Controls.Add(root, 0, 1);
        Controls.Add(outer);

        kitList.SelectedIndexChanged += (_, _) => ShowDetails();
        kitList.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await LoadSelected(); }
            else if (e.KeyCode == Keys.Delete) { e.SuppressKeyPress = true; await DeleteSelected(); }
            else if (e.KeyCode == Keys.F2) { e.SuppressKeyPress = true; await EditSelected(); }
            else if (e.Alt && e.KeyCode is Keys.Up or Keys.Down) { e.SuppressKeyPress = true; MoveSelected(e.KeyCode == Keys.Up ? -1 : 1); }
        };
        // Kits loaded from the TD-12's buttons are announced here too.
        statusWatch.Tick += (_, _) =>
        {
            if (ReaperBridge.ReadStatus() is { } st && st.Id.StartsWith("pc") && st.Id != lastPcStatus)
            {
                bool first = lastPcStatus == "";
                lastPcStatus = st.Id;
                if (!first) Status($"From the {Profile.Name}: " + st.Message);
            }
        };
        statusWatch.Start();
        KeyDown += async (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.N) { e.SuppressKeyPress = true; await NewKit(null); }
            else if (e.KeyCode == Keys.F5) { e.SuppressKeyPress = true; await metronome.Toggle(); }
            else if (e.KeyCode == Keys.F6) { e.SuppressKeyPress = true; statusBox.Focus(); } // Alt+S means Save in the other windows
        };
        Shown += async (_, _) =>
        {
            await Startup();
            // Quietly look for a newer version once the startup messages have been spoken.
            if (Updates.CheckAtStartup && !store.IsFirstRun) { await Task.Delay(4000); await CheckForUpdates(startup: true); }
        };
    }

    async Task Startup()
    {
        bool migrated = false;
        try { migrated = KitStore.MigrateFromOldName(); }
        catch (Exception ex) { Status("Could not move settings from TD-12 Kit Manager: " + ex.Message); }
        if (migrated) hardware = HardwareSettings.Load();
        List<string> expansions = [];
        try { expansions = Catalog.LoadInstalledLibraries(); }
        catch (Exception) { /* keep the built-in core library list */ }
        try
        {
            store.Profile = hardware.ActiveProfile();
            hardware.WriteBridgeSettings();
            store.Load();
        }
        catch (Exception ex)
        {
            Status("Could not load kits: " + ex.Message);
        }
        List<string> newKits = [];
        try { newKits = store.AddExpansionKits(); }
        catch (Exception ex) { Status("Could not add kits for a new expansion: " + ex.Message); }
        RefreshList();
        stepCheck.Checked = store.StepWithKitButtons;
        loading = false;
        kitList.Focus();

        bool firstInstall = false;
        try { firstInstall = ReaperBridge.Install(); }
        catch (Exception ex) { Status("Could not install the REAPER bridge script: " + ex.Message); return; }

        if (store.Base == null)
        {
            Status("No EZdrummer settings were found yet. Open REAPER with EZdrummer, then use Update map and mixer from REAPER.");
            return;
        }
        if (store.IsFirstRun)
        {
            // New user: start with hardware setup (choose or learn the drum module, pick MIDI ports).
            Status("Welcome to E-Drum Kit Manager. First, tell it about your drum module.");
            BeginInvoke(new Action(async () => { await Task.Delay(800); await HardwareSetup(); }));
        }
        if (firstInstall)
        {
            Status($"{store.Kits.Count} kits ready. The REAPER bridge was just installed{(migrated ? " and your TD-12 Kit Manager settings were moved across" : "")}: restart REAPER once so it can receive kits.");
            return;
        }
        string libraries = newKits.Count > 0 ? $" New expansion: added {string.Join(" and ", newKits)} to the end of the list."
            : expansions.Count == 0 ? "" : $" Expansions found: {string.Join(", ", expansions)}.";
        var reply = await ReaperBridge.WaitForReply(ReaperBridge.Send("PING"), TimeSpan.FromSeconds(2));
        Status(reply is { Ok: true }
            ? $"{store.Kits.Count} kits ready.{libraries} Connected to REAPER."
            : $"{store.Kits.Count} kits ready.{libraries} REAPER is not connected yet; open REAPER to load kits.");
    }

    Kit? Selected => kitList.SelectedItem as Kit;

    void RefreshList(Kit? select = null)
    {
        var keep = select ?? Selected;
        kitList.BeginUpdate();
        kitList.Items.Clear();
        foreach (var k in store.Kits) kitList.Items.Add(k);
        kitList.EndUpdate();
        if (keep != null && store.Kits.Contains(keep)) kitList.SelectedItem = keep;
        else if (kitList.Items.Count > 0) kitList.SelectedIndex = 0;
        ShowDetails();
    }

    void ShowDetails()
    {
        if (Selected is not { } kit) { details.Text = ""; return; }
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
        details.Text = string.Join(Environment.NewLine, lines);
    }

    void Status(string text)
    {
        statusBox.Text = text;
        Speech.Say(statusBox, text);
    }

    async Task<(bool Ok, string Message)?> AskReaper(string waiting, TimeSpan timeout, params string[] request)
    {
        if (busy) { Status("Still waiting for REAPER, please try again in a moment."); return null; }
        busy = true;
        reaperButtons.ForEach(b => b.Enabled = false);
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
            reaperButtons.ForEach(b => b.Enabled = true);
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

    bool NameTaken(string name, Kit self) =>
        store.Kits.Any(k => k != self && string.Equals(k.Name, name, StringComparison.OrdinalIgnoreCase));

    Task NewKit(List<KitSlot>? slots)
    {
        var kit = new Kit
        {
            Name = slots == null ? "" : "Kit from REAPER",
            Source = slots == null ? "Created in E-Drum Kit Manager" : "Imported from REAPER",
            Slots = slots ?? (Selected?.Slots ?? store.Kits.FirstOrDefault()?.Slots ?? [])
                .Select(s => new KitSlot { Lib = s.Lib, Drum = s.Drum }).ToList(),
        };
        return OpenEditor(kit, isNew: true);
    }

    Task EditSelected() => Selected is { } kit ? OpenEditor(kit, isNew: false) : Task.CompletedTask;

    Task OpenEditor(Kit kit, bool isNew)
    {
        using var editor = new KitEditorForm(kit, isNew, Profile, Preview, name => NameTaken(name, kit));
        if (editor.ShowDialog(this) != DialogResult.OK) { kitList.Focus(); return Task.CompletedTask; }

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
        kitList.Focus();
        Status($"Saved {kit.Name}.{note}");
        return Task.CompletedTask;
    }

    Task Duplicate()
    {
        if (Selected is not { } kit) return Task.CompletedTask;
        var copy = kit.Clone();
        string name = kit.Name + " copy";
        for (int i = 2; NameTaken(name, copy); i++) name = $"{kit.Name} copy {i}";
        copy.Name = name;
        store.Kits.Insert(store.Kits.IndexOf(kit) + 1, copy);
        SaveAll();
        RefreshList(copy);
        Status($"Created {copy.Name}.");
        return Task.CompletedTask;
    }

    Task DeleteSelected()
    {
        if (Selected is not { } kit) return Task.CompletedTask;
        if (MessageBox.Show(this, $"Delete the kit \"{kit.Name}\"?", "Delete kit",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return Task.CompletedTask;
        int index = kitList.SelectedIndex;
        store.Kits.Remove(kit);
        SaveAll();
        RefreshList();
        if (kitList.Items.Count > 0) kitList.SelectedIndex = Math.Min(index, kitList.Items.Count - 1);
        kitList.Focus();
        Status($"Deleted {kit.Name}.");
        return Task.CompletedTask;
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
        var unknown = slots.Where(s => Catalog.Find(s.Lib, s.Drum) == null).Select(s => s.Drum).ToList();
        if (unknown.Count > 0)
            Status($"Some drums are not in the EZdrummer 3 core library and were replaced: {string.Join(", ", unknown)}.");
        else
            Status("Read the kit from REAPER. Name it and save.");
        await NewKit(slots);
    }

    async Task UpdateBase()
    {
        var state = await CaptureFromReaper();
        if (state == null) return;
        if (!state.HasEdrumMap &&
            MessageBox.Show(this, "EZdrummer in REAPER has no e-drum map loaded. Use its settings anyway?", Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
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

    async Task Ping()
    {
        var reply = await AskReaper("Checking REAPER...", TimeSpan.FromSeconds(3), "PING");
        if (reply is { Ok: true } r) Status(r.Message);
    }

    Task AdjustPadLevels()
    {
        using var form = new PadLevelsForm(Profile, async () =>
        {
            store.WriteKitFiles(); // each kit's per-note levels include the pad levels
            var reply = await ReaperBridge.WaitForReply(ReaperBridge.Send("PADLEVELS"), TimeSpan.FromSeconds(3));
            if (reply is not { Ok: true }) Status(reply == null ? "REAPER did not answer; the levels are saved and apply when REAPER is open." : "REAPER: " + reply.Value.Message);
        });
        form.ShowDialog(this);
        Status("Pad levels saved.");
        return Task.CompletedTask;
    }

    async Task ReconnectMidi()
    {
        var reply = await AskReaper("Resetting REAPER's MIDI devices...", TimeSpan.FromSeconds(5), "MIDIRESET");
        if (reply is { Ok: true } r) Status(r.Message);
    }

    Task OpenGuide()
    {
        string guide = Path.Combine(AppContext.BaseDirectory, "User Guide.html");
        if (File.Exists(guide))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(guide) { UseShellExecute = true });
        else
            Status("The user guide was not found next to the program. Reinstall E-Drum Kit Manager to restore it.");
        return Task.CompletedTask;
    }

    static Version CurrentVersion => typeof(MainForm).Assembly.GetName().Version ?? new Version(0, 0, 0);

    /// <summary>Looks for a newer version. At startup it stays quiet unless there is one the user hasn't turned down.</summary>
    async Task CheckForUpdates(bool startup)
    {
        UpdateInfo? update;
        try
        {
            if (!startup) Status("Checking for updates...");
            update = await Updates.CheckAsync(CurrentVersion);
        }
        catch (Exception ex)
        {
            if (!startup) Status("Couldn't check for updates. Check your internet connection and try again. (" + ex.Message + ")");
            return;
        }
        string current = CurrentVersion.ToString(3);
        if (update == null)
        {
            if (!startup) Status($"You have the latest version, {current}.");
            return;
        }
        string latest = update.Version.ToString(3);
        if (startup && Updates.SkippedVersion == latest) return;
        Status($"Version {latest} of E-Drum Kit Manager is available. You have {current}.");
        var answer = MessageBox.Show(this,
            $"Version {latest} of E-Drum Kit Manager is available. You have version {current}.\n\n" +
            "Download and install it now? E-Drum Kit Manager closes while it updates and opens again afterwards. Your kits and settings are kept.\n\n" +
            "Choose No to skip this version, or Cancel to be asked again next time.",
            "Update available", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information);
        if (answer == DialogResult.No) { Updates.SkippedVersion = latest; Status($"Version {latest} skipped. Choose Check for updates in the Help menu to install it later."); return; }
        if (answer != DialogResult.Yes) return;
        try
        {
            Status($"Downloading version {latest}...");
            string installer = await Updates.DownloadAsync(update);
            Status($"Installing version {latest}. E-Drum Kit Manager will close and open again in a moment.");
            await Task.Delay(1500); // let the screen reader finish the message
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(installer, "/SILENT /SUPPRESSMSGBOXES /NORESTART /RELAUNCH") { UseShellExecute = true });
            Close();
        }
        catch (Exception ex)
        {
            Status("The update couldn't be downloaded: " + ex.Message + " You can also download it from the project's GitHub page.");
        }
    }

    GroovesForm? grooves;

    /// <summary>The groove browser stays open beside the main window; a second Ctrl+G brings it back to the front.</summary>
    Task OpenGrooves()
    {
        if (grooves is { IsDisposed: false }) { grooves.Activate(); return Task.CompletedTask; }
        grooves = new GroovesForm((waiting, request) => AskReaper(waiting, TimeSpan.FromSeconds(10), request), Profile);
        grooves.Show(this);
        return Task.CompletedTask;
    }

    Task ShowAbout()
    {
        var version = typeof(MainForm).Assembly.GetName().Version;
        MessageBox.Show(this,
            $"E-Drum Kit Manager, version {version?.ToString(3)}\n\n" +
            "Builds and switches EZdrummer 3 drum kits in REAPER for electronic drum kits, with screen readers in mind.\n\n" +
            "Not made or supported by Toontrack or Roland. It reads and writes EZdrummer's own settings, which are not publicly documented, so an EZdrummer update could affect it.",
            "About E-Drum Kit Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return Task.CompletedTask;
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
        using var form = new HardwareForm(hardware, ins, outs, LearnKit);
        if (form.ShowDialog(this) != DialogResult.OK) { kitList.Focus(); return; }
        hardware = form.Result;
        hardware.Save();
        hardware.WriteBridgeSettings();
        store.Profile = hardware.ActiveProfile();
        SaveAll();
        stepCheck.Text = "Step through kits &with the module's kit buttons";
        Status($"Hardware set: {Profile.Name}, MIDI in {Describe(hardware.MidiInput)}, MIDI out {Describe(hardware.MidiOutput)}. Load a kit to apply it in REAPER.");
        kitList.Focus();

        static string Describe(string port) => port.Length == 0 ? "any" : port;
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

    void MoveSelected(int delta)
    {
        if (Selected is not { } kit) return;
        store.Move(kit, delta);
        SaveAll();
        RefreshList(kit);
        Status($"{kit.Name} moved to position {store.Kits.IndexOf(kit) + 1} of {store.Kits.Count}.");
    }

    static readonly string BackupDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TD-12 Backups");

    async Task BackupTd12()
    {
        if (!Profile.RolandBackup) { Status($"Backups work with Roland modules that support bulk dump. {Profile.Name} is not set up for it."); return; }
        if (MessageBox.Show(this,
                "Press OK, then on the TD-12:\n\n" +
                "1. Press SETUP.\n2. Press F1 (MIDI).\n3. Press F5 (BULK). The type is normally already set to ALL.\n4. Press F5 (EXECUTE).\n\n" +
                "Kit Manager waits up to 15 minutes and saves the backup when the TD-12 has finished sending.",
                "Back up TD-12 settings", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
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
        using var dialog = new OpenFileDialog
        {
            Title = "Choose a TD-12 backup to restore",
            Filter = "TD-12 backups (*.syx)|*.syx",
            InitialDirectory = Directory.Exists(BackupDir) ? BackupDir : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var (count, bad) = Sysex.Check(File.ReadAllBytes(dialog.FileName));
        if (count == 0 || bad > 0)
        {
            Status($"That file can't be restored: {count} messages, {bad} failed their check.");
            return;
        }
        if (MessageBox.Show(this,
                $"This replaces the settings on the TD-12 with the backup \"{Path.GetFileName(dialog.FileName)}\" ({count} messages). " +
                "Whatever is in the backup (kits, triggers, setup) is overwritten on the module.\n\nRestore now?",
                "Restore TD-12 backup", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        int seconds = (int)Math.Ceiling(count * 0.06) + 5;
        string id = ReaperBridge.Send("SYSEXSEND", dialog.FileName, hardware.MidiOutput);
        var reply = await AskReaperWait($"Sending the backup to the TD-12. This takes about {seconds} seconds; don't touch the module.", id, TimeSpan.FromSeconds(seconds + 30));
        if (reply is { Ok: true } r) Status(r.Message + ". Restore finished.");
    }

    /// <summary>Like AskReaper, for a request that was already sent.</summary>
    async Task<(bool Ok, string Message)?> AskReaperWait(string waiting, string id, TimeSpan timeout)
    {
        busy = true;
        reaperButtons.ForEach(b => b.Enabled = false);
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
            reaperButtons.ForEach(b => b.Enabled = true);
        }
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
