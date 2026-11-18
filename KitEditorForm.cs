namespace TD12KitManager;

/// <summary>Create or edit a kit: a name, an optional TD-12 kit number, and a drum for each kit position.</summary>
public sealed class KitEditorForm : Form
{
    readonly TextBox nameBox = new() { Width = 420, AccessibleName = "Kit name" };
    readonly NumberBox td12Number = new() { Minimum = 0, Maximum = 999, Width = 80, AccessibleName = "Module kit number, 0 for none" };
    readonly ComboBox roomCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300, AccessibleName = "Switch all drums to room" };
    readonly ComboBox[] combos;
    readonly NumberBox[] tunes;
    readonly NumberBox[] levels;
    readonly ComboBox oneShotCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300, AccessibleName = "Percussion one-shot" };
    readonly ComboBox oneShotPad = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90, AccessibleName = "Pad that plays the one-shot" };
    readonly NumberBox oneShotLevel = new() { Minimum = 25, Maximum = 200, Increment = 5, Width = 60, AccessibleName = "One-shot level, percent" };
    readonly NumberBox roomLevel = new() { Minimum = 0, Maximum = 200, Increment = 10, Width = 70, AccessibleName = "Room amount, percent" };
    readonly NumberBox overheadLevel = new() { Minimum = 0, Maximum = 200, Increment = 10, Width = 70, AccessibleName = "Overheads amount, percent" };
    readonly Func<Kit, Task> preview;
    readonly Func<string, bool> nameTaken;

    public Kit Result { get; }

    public KitEditorForm(Kit kit, bool isNew, ModuleProfile profile, Func<Kit, Task> preview, Func<string, bool> nameTaken)
    {
        Result = kit;
        this.preview = preview;
        this.nameTaken = nameTaken;
        Text = isNew ? "New kit" : $"Edit kit: {kit.Name}";
        AccessibleName = Text; // otherwise the & in names like C&C is dropped as a shortcut marker
        AutoScaleMode = AutoScaleMode.Font;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        var grid = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10),
            Dock = DockStyle.Fill,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        void Row(string label, Control c)
        {
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 3, 3) });
            grid.Controls.Add(c);
        }

        nameBox.Text = kit.Name;
        Row("Kit &name:", nameBox);
        td12Number.Value = kit.Td12KitNumber;
        Row("Module &kit number (0 = none):", td12Number);
        foreach (var room in Catalog.Rooms) roomCombo.Items.Add(room.Name);
        var mainRoom = kit.Slots.GroupBy(s => s.Lib).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key;
        roomCombo.SelectedIndex = Math.Max(0, Array.FindIndex(Catalog.Rooms, r => r.Lib == mainRoom));
        var switchButton = new Button { Text = "S&witch drums", AutoSize = true };
        switchButton.Click += (_, _) => SwitchRoom(switchButton);
        var roomRow = new RowPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        roomRow.Controls.AddRange([roomCombo, switchButton]);
        Row("Switch all drums to &room:", roomRow);
        roomLevel.Value = Math.Clamp(kit.Room, 0, 200);
        Row("Room &amount (%, 100 = normal, 0 = dry):", roomLevel);
        overheadLevel.Value = Math.Clamp(kit.Overheads, 0, 200);
        Row("&Overheads amount (%):", overheadLevel);
        oneShotCombo.Items.Add("None");
        foreach (var o in Catalog.OneShots) oneShotCombo.Items.Add(o.Name);
        int os = Array.FindIndex(Catalog.OneShots, o => o.Id == kit.OneShot);
        oneShotCombo.SelectedIndex = os + 1;
        var shotPads = profile.Pads().Where(p => p.CanPlayOneShot).Select(p => p.Name).ToList();
        foreach (var p in shotPads) oneShotPad.Items.Add(p);
        if (shotPads.Count == 0) { oneShotCombo.Enabled = oneShotPad.Enabled = oneShotLevel.Enabled = false; oneShotCombo.AccessibleName = "Percussion one-shot, needs an Aux pad"; }
        else oneShotPad.SelectedItem = shotPads.Contains(kit.OneShotPad) ? kit.OneShotPad : shotPads[^1];
        oneShotLevel.Value = Math.Clamp(kit.OneShotLevel, 25, 200);
        var percRow = new RowPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        percRow.Controls.AddRange([
            oneShotCombo,
            new Label { Text = "on pad:", AutoSize = true, Margin = new Padding(8, 7, 2, 3) }, oneShotPad,
            new Label { Text = "Level %:", AutoSize = true, Margin = new Padding(8, 7, 2, 3) }, oneShotLevel,
        ]);
        Row("Percussion one-s&hot (replaces that pad's drum):", percRow);

        combos = new ComboBox[Catalog.Slots.Length];
        tunes = new NumberBox[Catalog.Slots.Length];
        levels = new NumberBox[Catalog.Slots.Length];
        foreach (var slot in Catalog.Slots)
        {
            var combo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 520,
                AccessibleName = slot.Label,
            };
            var drums = Catalog.ForSlot(slot).ToList();
            var current = kit.Slots.ElementAtOrDefault(slot.Index);
            var selected = current == null ? null : Catalog.Find(current.Lib, current.Drum);
            if (selected != null && !drums.Contains(selected)) drums.Insert(0, selected);
            combo.Items.AddRange([.. drums]);
            combo.SelectedItem = selected ?? drums.FirstOrDefault();
            combos[slot.Index] = combo;
            var tune = new NumberBox
            {
                Minimum = -12, Maximum = 12, Width = 55, Value = Math.Clamp(current?.Tune ?? 0, -12, 12),
                AccessibleName = slot.Label + " tuning, semitones",
            };
            bool played = profile.PadsForSlot(slot.Index).Count > 0;
            var level = new NumberBox
            {
                Minimum = 25, Maximum = 200, Increment = 5, Width = 60, Value = Math.Clamp(current?.Level ?? 100, 25, 200),
                Enabled = played,
                AccessibleName = slot.Label + (played ? " level, percent" : " level, not played by any pad"),
            };
            tunes[slot.Index] = tune;
            levels[slot.Index] = level;
            var line = new RowPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            line.Controls.AddRange([
                combo,
                new Label { Text = "Tune:", AutoSize = true, Margin = new Padding(8, 7, 2, 3) }, tune,
                new Label { Text = "Level %:", AutoSize = true, Margin = new Padding(8, 7, 2, 3) }, level,
            ]);
            Row(slot.Label + ":", line);
        }

        var buttons = new RowPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 10, 0, 0) };
        var previewButton = new Button { Text = "&Preview in REAPER", AutoSize = true };
        var save = new Button { Text = "&Save", AutoSize = true };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        previewButton.Click += async (_, _) =>
        {
            previewButton.Enabled = false;
            try { await preview(Snapshot()); }
            finally { previewButton.Enabled = true; }
        };
        save.Click += (_, _) => OnSave();
        buttons.Controls.AddRange([previewButton, save, cancel]);
        grid.Controls.Add(new Label { AutoSize = true });
        grid.Controls.Add(buttons);

        Controls.Add(grid);
        AcceptButton = save;
        CancelButton = cancel;
    }

    void SwitchRoom(Control announcer)
    {
        var room = Catalog.Rooms[Math.Max(0, roomCombo.SelectedIndex)];
        var slots = Snapshot().Slots;
        var missing = Catalog.SwitchRoom(slots, room.Lib);
        for (int i = 0; i < combos.Length; i++)
        {
            var drum = Catalog.Find(slots[i].Lib, slots[i].Drum);
            if (drum != null && !combos[i].Items.Contains(drum)) combos[i].Items.Insert(0, drum);
            combos[i].SelectedItem = drum;
        }
        string roomName = room.Name.Split(':')[0];
        string message = missing.Count == 0
            ? $"All drums switched to {roomName}."
            : $"Switched to {roomName}. {roomName} has no drum for {string.Join(", ", missing)}, so those stay the same.";
        Speech.Say(announcer, message);
    }

    Kit Snapshot() => new()
    {
        Id = Result.Id,
        Name = nameBox.Text.Trim().Length > 0 ? nameBox.Text.Trim() : "Untitled kit",
        Td12KitNumber = (int)td12Number.Value,
        Room = (int)roomLevel.Value,
        OneShot = oneShotCombo.SelectedIndex > 0 ? Catalog.OneShots[oneShotCombo.SelectedIndex - 1].Id : "",
        OneShotPad = (string?)oneShotPad.SelectedItem ?? "Aux 2",
        OneShotLevel = (int)oneShotLevel.Value,
        Overheads = (int)overheadLevel.Value,
        Slots = combos.Select((c, i) => c.SelectedItem is Drum d
            ? new KitSlot { Lib = d.Lib, Drum = d.Id, Tune = (int)tunes[i].Value, Level = (int)levels[i].Value }
            : new KitSlot()).ToList(),
    };

    void OnSave()
    {
        string name = nameBox.Text.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show(this, "Please enter a kit name.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            nameBox.Focus();
            return;
        }
        if (nameTaken(name))
        {
            MessageBox.Show(this, $"There is already a kit called \"{name}\".", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            nameBox.Focus();
            return;
        }
        var snap = Snapshot();
        Result.Name = snap.Name;
        Result.Td12KitNumber = snap.Td12KitNumber;
        Result.Room = snap.Room;
        Result.OneShot = snap.OneShot;
        Result.OneShotPad = snap.OneShotPad;
        Result.OneShotLevel = snap.OneShotLevel;
        Result.Overheads = snap.Overheads;
        Result.Slots = snap.Slots;
        DialogResult = DialogResult.OK;
    }
}
