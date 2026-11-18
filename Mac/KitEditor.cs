using AppKit;
using CoreGraphics;
using Foundation;

namespace TD12KitManager.Mac;

/// <summary>
/// Create or edit a kit: name, optional module kit number, room switch, room and overhead amounts,
/// a percussion one-shot, and a drum with tune and level for each kit position. Shown as a sheet.
/// </summary>
sealed class KitEditorSheet
{
    sealed record SlotRow(ChoiceBox<Drum> Drum, NumberField Tune, NumberField Level);

    readonly Kit kit;
    readonly Func<Kit, Task> preview;
    readonly Func<string, bool> nameTaken;
    readonly NSWindow window;
    readonly NSTextField nameBox = new(new CGRect(0, 0, 420, 22));
    readonly NumberField moduleKit;
    readonly ChoiceBox<(string Lib, string Name)> roomChoice = new("Switch all drums to room", 300);
    readonly NumberField roomAmount, overheadAmount, oneShotLevel;
    readonly ChoiceBox<string> oneShotChoice = new("Percussion one-shot", 300);
    readonly ChoiceBox<string> oneShotPad = new("Pad that plays the one-shot", 110);
    readonly List<SlotRow> slotRows = [];
    readonly NSButton previewButton;
    Action<bool>? finished;

    public KitEditorSheet(Kit kit, bool isNew, ModuleProfile profile, Func<Kit, Task> preview, Func<string, bool> nameTaken)
    {
        this.kit = kit;
        this.preview = preview;
        this.nameTaken = nameTaken;

        string title = isNew ? "New kit" : $"Edit kit: {kit.Name}";
        window = new NSWindow(new CGRect(0, 0, 880, 700), NSWindowStyle.Titled, NSBackingStore.Buffered, false) { Title = title };
        window.ReleaseWhenClosed(false);
        Announcer.Label(window, title);

        nameBox.TranslatesAutoresizingMaskIntoConstraints = false;
        nameBox.WidthAnchor.ConstraintEqualTo(420).Active = true;
        nameBox.StringValue = kit.Name;
        Announcer.Label(nameBox, "Kit name");

        moduleKit = new NumberField("Module kit number, 0 for none", 0, 999, 1, kit.Td12KitNumber, 80);
        roomAmount = new NumberField("Room amount, percent", 0, 200, 10, Math.Clamp(kit.Room, 0, 200));
        overheadAmount = new NumberField("Overheads amount, percent", 0, 200, 10, Math.Clamp(kit.Overheads, 0, 200));
        oneShotLevel = new NumberField("One-shot level, percent", 25, 200, 5, Math.Clamp(kit.OneShotLevel, 25, 200), 60);

        var rows = new List<NSView[]>();
        void Row(string label, NSView control) => rows.Add([Caption(label), control]);

        Row("Kit name:", nameBox);
        Row("Module kit number (0 = none):", moduleKit);

        foreach (var room in Catalog.Rooms) roomChoice.Add(room, room.Name);
        var mainRoom = kit.Slots.GroupBy(s => s.Lib).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key;
        roomChoice.SelectItem(Math.Max(0, Array.FindIndex(Catalog.Rooms, r => r.Lib == mainRoom)));
        var switchButton = new NSButton(new CGRect(0, 0, 120, 28)) { Title = "Switch drums", BezelStyle = NSBezelStyle.Rounded };
        switchButton.Activated += (_, _) => SwitchRoom();
        Row("Switch all drums to room:", Line(roomChoice, switchButton));

        Row("Room amount (%, 100 = normal, 0 = dry):", roomAmount);
        Row("Overheads amount (%):", overheadAmount);

        oneShotChoice.Add("", "None");
        foreach (var o in Catalog.OneShots) oneShotChoice.Add(o.Id, o.Name);
        int os = Array.FindIndex(Catalog.OneShots, o => o.Id == kit.OneShot);
        oneShotChoice.SelectItem(os + 1);
        var shotPads = profile.Pads().Where(p => p.CanPlayOneShot).Select(p => p.Name).ToList();
        foreach (var p in shotPads) oneShotPad.Add(p, p);
        if (shotPads.Count == 0)
        {
            oneShotChoice.Enabled = oneShotPad.Enabled = oneShotLevel.Enabled = false;
            Announcer.Label(oneShotChoice, "Percussion one-shot, needs an Aux pad");
        }
        else oneShotPad.SelectItem(shotPads.Contains(kit.OneShotPad) ? shotPads.IndexOf(kit.OneShotPad) : shotPads.Count - 1);
        Row("Percussion one-shot (replaces that pad's drum):",
            Line(oneShotChoice, Caption("on pad:"), oneShotPad, Caption("Level %:"), oneShotLevel));

        foreach (var slot in Catalog.Slots)
        {
            var drumBox = new ChoiceBox<Drum>(slot.Label, 460);
            var drums = Catalog.ForSlot(slot).ToList();
            var current = kit.Slots.ElementAtOrDefault(slot.Index);
            var selected = current == null ? null : Catalog.Find(current.Lib, current.Drum);
            if (selected != null && !drums.Contains(selected)) drums.Insert(0, selected);
            foreach (var d in drums) drumBox.Add(d, d.ToString());
            var pick = selected ?? drums.FirstOrDefault();
            if (pick != null) drumBox.SelectItem(drumBox.Values.IndexOf(pick));

            var tune = new NumberField(slot.Label + " tuning, semitones", -12, 12, 1, Math.Clamp(current?.Tune ?? 0, -12, 12), 55);
            bool played = profile.PadsForSlot(slot.Index).Count > 0;
            var level = new NumberField(slot.Label + (played ? " level, percent" : " level, not played by any pad"),
                25, 200, 5, Math.Clamp(current?.Level ?? 100, 25, 200), 60) { Enabled = played };
            slotRows.Add(new SlotRow(drumBox, tune, level));
            Row(slot.Label + ":", Line(drumBox, Caption("Tune:"), tune, Caption("Level %:"), level));
        }

        var grid = new NSGridView();
        foreach (var r in rows) grid.AddRow(r);
        grid.ColumnSpacing = 10;
        grid.RowSpacing = 8;
        grid.Frame = new CGRect(0, 0, 840, grid.FittingSize.Height);

        var scroll = new NSScrollView(new CGRect(0, 56, 880, 644))
        {
            DocumentView = grid, HasVerticalScroller = true, DrawsBackground = false,
            AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable,
        };
        Announcer.Label(scroll, "Kit settings");
        // Start at the top whichever way the grid is flipped.
        scroll.ContentView.ScrollToPoint(new CGPoint(0, grid.IsFlipped ? 0 : Math.Max(0, grid.Frame.Height - scroll.ContentView.Bounds.Height)));
        scroll.ReflectScrolledClipView(scroll.ContentView);

        previewButton = new NSButton(new CGRect(20, 14, 170, 28)) { Title = "Preview in REAPER", BezelStyle = NSBezelStyle.Rounded };
        var save = new NSButton(new CGRect(640, 14, 100, 28)) { Title = "Save", BezelStyle = NSBezelStyle.Rounded, KeyEquivalent = "\r" };
        var cancel = new NSButton(new CGRect(750, 14, 100, 28)) { Title = "Cancel", BezelStyle = NSBezelStyle.Rounded, KeyEquivalent = "\u001b" };
        previewButton.Activated += async (_, _) =>
        {
            previewButton.Enabled = false;
            try { await this.preview(Snapshot()); }
            finally { previewButton.Enabled = true; }
        };
        save.Activated += (_, _) => OnSave();
        cancel.Activated += (_, _) => Finish(false);

        var content = window.ContentView!;
        content.AddSubview(scroll);
        content.AddSubview(previewButton);
        content.AddSubview(save);
        content.AddSubview(cancel);
        window.InitialFirstResponder = nameBox;
        // Tab order follows the layout: name, module kit number, room, ... then the buttons.
        window.AutorecalculatesKeyViewLoop = true;
        window.RecalculateKeyViewLoop();
    }

    static NSTextField Caption(string text) => new(new CGRect(0, 0, 10, 18))
    {
        StringValue = text, Editable = false, Bordered = false, DrawsBackground = false, Selectable = false,
    };

    static NSStackView Line(params NSView[] views)
    {
        var line = NSStackView.FromViews(views);
        line.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
        line.Spacing = 8;
        return line;
    }

    /// <summary>Shows the editor over <paramref name="parent"/>; <paramref name="done"/> gets true when the kit was saved.</summary>
    public void Show(NSWindow parent, Action<bool> done)
    {
        finished = done;
        parent.BeginSheet(window, _ => { });
        window.MakeFirstResponder(nameBox);
        parentWindow = parent;
    }

    NSWindow? parentWindow;

    void Finish(bool saved)
    {
        parentWindow?.EndSheet(window);
        window.OrderOut(null);
        var done = finished;
        finished = null;
        done?.Invoke(saved);
    }

    void SwitchRoom()
    {
        var room = Catalog.Rooms[Math.Max(0, (int)roomChoice.IndexOfSelectedItem)];
        var slots = Snapshot().Slots;
        var missing = Catalog.SwitchRoom(slots, room.Lib);
        for (int i = 0; i < slotRows.Count; i++)
        {
            var box = slotRows[i].Drum;
            var drum = Catalog.Find(slots[i].Lib, slots[i].Drum);
            if (drum == null) continue;
            int index = box.Values.IndexOf(drum);
            if (index < 0) { box.Insert(0, drum, drum.ToString()); index = 0; }
            box.SelectItem(index);
        }
        string roomName = room.Name.Split(':')[0];
        Announcer.Say(window, missing.Count == 0
            ? $"All drums switched to {roomName}."
            : $"Switched to {roomName}. {roomName} has no drum for {string.Join(", ", missing)}, so those stay the same.");
    }

    Kit Snapshot()
    {
        string name = nameBox.StringValue.Trim();
        moduleKit.Commit(); roomAmount.Commit(); overheadAmount.Commit(); oneShotLevel.Commit();
        foreach (var r in slotRows) { r.Tune.Commit(); r.Level.Commit(); }
        return new Kit
        {
            Id = kit.Id,
            Name = name.Length > 0 ? name : "Untitled kit",
            Td12KitNumber = moduleKit.Value,
            Room = roomAmount.Value,
            OneShot = oneShotChoice.IndexOfSelectedItem > 0 ? Catalog.OneShots[(int)oneShotChoice.IndexOfSelectedItem - 1].Id : "",
            OneShotPad = oneShotPad.Chosen ?? "Aux 2",
            OneShotLevel = oneShotLevel.Value,
            Overheads = overheadAmount.Value,
            Slots = slotRows.Select(r => r.Drum.Chosen is { } d
                ? new KitSlot { Lib = d.Lib, Drum = d.Id, Tune = r.Tune.Value, Level = r.Level.Value }
                : new KitSlot()).ToList(),
        };
    }

    void Problem(string message)
    {
        var alert = new NSAlert { MessageText = message, AlertStyle = NSAlertStyle.Informational };
        alert.AddButton("OK");
        alert.BeginSheet(window, _ => window.MakeFirstResponder(nameBox));
    }

    void OnSave()
    {
        string name = nameBox.StringValue.Trim();
        if (name.Length == 0) { Problem("Please enter a kit name."); return; }
        if (nameTaken(name)) { Problem($"There is already a kit called \"{name}\"."); return; }
        var snap = Snapshot();
        kit.Name = snap.Name;
        kit.Td12KitNumber = snap.Td12KitNumber;
        kit.Room = snap.Room;
        kit.OneShot = snap.OneShot;
        kit.OneShotPad = snap.OneShotPad;
        kit.OneShotLevel = snap.OneShotLevel;
        kit.Overheads = snap.Overheads;
        kit.Slots = snap.Slots;
        Finish(true);
    }
}
