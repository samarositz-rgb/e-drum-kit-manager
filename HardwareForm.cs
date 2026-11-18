namespace TD12KitManager;

/// <summary>Choose the drum module profile and the MIDI ports it is connected to.</summary>
public sealed class HardwareForm : Form
{
    readonly ComboBox profileCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320, AccessibleName = "Drum module" };
    readonly ComboBox inCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320, AccessibleName = "MIDI input from the module" };
    readonly ComboBox outCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320, AccessibleName = "MIDI output to the module" };
    readonly Label profileInfo = new() { AutoSize = true, MaximumSize = new Size(480, 0), Margin = new Padding(3, 3, 3, 8) };
    readonly Func<TimeSpan, Task<(int Note, int Channel)?>> learn;
    const string AnyInput = "Any MIDI input";
    const string NoOutput = "None";

    public HardwareSettings Result { get; private set; }

    public HardwareForm(HardwareSettings current, List<string> inputs, List<string> outputs,
        Func<TimeSpan, Task<(int Note, int Channel)?>> learn)
    {
        this.learn = learn;
        Result = current;
        Text = "Hardware setup";
        AutoScaleMode = AutoScaleMode.Font;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Padding = new Padding(10) };
        void Row(string label, Control c)
        {
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 7, 3, 3) });
            grid.Controls.Add(c);
        }

        FillProfiles(current.Profile);
        profileCombo.SelectedIndexChanged += (_, _) => ShowProfile();
        Row("Drum &module:", profileCombo);
        grid.Controls.Add(new Label { AutoSize = true });
        grid.Controls.Add(profileInfo);

        var learnButton = new Button { Text = "&Learn a new module (hit each pad)...", AutoSize = true };
        learnButton.Click += (_, _) => LearnNew();
        grid.Controls.Add(new Label { AutoSize = true });
        grid.Controls.Add(learnButton);

        inCombo.Items.Add(AnyInput);
        foreach (var i in inputs) inCombo.Items.Add(i);
        Select(inCombo, current.MidiInput, AnyInput);
        Row("MIDI &input from the module:", inCombo);

        outCombo.Items.Add(NoOutput);
        foreach (var o in outputs) outCombo.Items.Add(o);
        Select(outCombo, current.MidiOutput, NoOutput);
        Row("MIDI &output to the module (backups):", outCombo);

        if (inputs.Count == 0)
        {
            var note = new Label
            {
                Text = "REAPER did not list any MIDI ports. Open REAPER, and turn on your MIDI interface in REAPER's Preferences, MIDI Devices.",
                AutoSize = true, MaximumSize = new Size(480, 0),
            };
            grid.Controls.Add(new Label { AutoSize = true });
            grid.Controls.Add(note);
        }

        var ok = new Button { Text = "&Save", AutoSize = true };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) =>
        {
            Result = new HardwareSettings
            {
                Profile = (string)profileCombo.SelectedItem!,
                MidiInput = inCombo.SelectedItem is string i && i != AnyInput ? i : "",
                MidiOutput = outCombo.SelectedItem is string o && o != NoOutput ? o : "",
            };
            DialogResult = DialogResult.OK;
        };
        var buttons = new RowPanel { AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
        buttons.Controls.AddRange([ok, cancel]);
        grid.Controls.Add(new Label { AutoSize = true });
        grid.Controls.Add(buttons);
        Controls.Add(grid);
        AcceptButton = ok;
        CancelButton = cancel;
        ShowProfile();
    }

    void FillProfiles(string selected)
    {
        profileCombo.Items.Clear();
        foreach (var p in HardwareSettings.AllProfiles()) profileCombo.Items.Add(p.Name);
        profileCombo.SelectedItem = profileCombo.Items.Contains(selected) ? selected : profileCombo.Items[0];
    }

    void ShowProfile()
    {
        var p = HardwareSettings.AllProfiles().FirstOrDefault(x => x.Name == (string?)profileCombo.SelectedItem);
        if (p == null) return;
        string pads = string.Join(", ", p.Pads().Select(x => x.Name));
        profileInfo.Text = $"{p.Pads().Count} pads: {pads}. Drum channel {(p.DrumChannel == 0 ? "any" : p.DrumChannel)}, {p.KitCount} kits." +
            (p.WriteMap ? " Kit Manager writes the EZdrummer map for it." : " Uses the e-drum map already set up in EZdrummer.");
        profileCombo.AccessibleDescription = profileInfo.Text;
    }

    /// <summary>Selects the item whose name contains the saved port name (saved names may be partial, like "USB MIDI").</summary>
    static void Select(ComboBox combo, string saved, string none)
    {
        if (saved.Length == 0) { combo.SelectedItem = none; return; }
        foreach (var item in combo.Items)
            if (item is string s && s.Contains(saved, StringComparison.OrdinalIgnoreCase)) { combo.SelectedItem = item; return; }
        combo.Items.Add(saved + " (not connected)");
        combo.SelectedIndex = combo.Items.Count - 1;
    }

    void LearnNew()
    {
        using var wizard = new LearnForm(learn);
        if (wizard.ShowDialog(this) == DialogResult.OK && wizard.Result != null)
        {
            HardwareSettings.SaveProfile(wizard.Result);
            FillProfiles(wizard.Result.Name);
            ShowProfile();
            profileCombo.Focus();
        }
    }
}
