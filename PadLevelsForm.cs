namespace TD12KitManager;

/// <summary>
/// Volume per pad (percentage of hit strength), for evening out weak or older pads.
/// The pads come from the drum module profile. Changes are sent to REAPER as you go.
/// </summary>
public sealed class PadLevelsForm : Form
{
    readonly List<(string Pad, NumberBox Box)> boxes = [];
    readonly System.Windows.Forms.Timer sendDelay = new() { Interval = 300 };
    readonly Func<Task> apply;

    /// <param name="apply">Regenerates the kit files and asks REAPER to apply the levels.</param>
    public PadLevelsForm(ModuleProfile profile, Func<Task> apply)
    {
        this.apply = apply;
        Text = "Pad levels";
        AutoScaleMode = AutoScaleMode.Font;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Padding = new Padding(10) };
        var intro = new Label
        {
            Text = $"Percent of hit strength for each pad of your {profile.Name}. 100 is unchanged; raise a quiet pad, lower a loud one.\nChanges take effect in REAPER as you go, so play the pad while you adjust.",
            AutoSize = true, MaximumSize = new Size(520, 0), Margin = new Padding(3, 3, 3, 10),
        };
        grid.Controls.Add(intro);
        grid.SetColumnSpan(intro, 2);

        var levels = PadLevels.Load();
        foreach (var pad in profile.Pads())
        {
            string label = pad.Name == "Crash 1" ? "Crash 1 (usually next to the hi-hat)" : pad.Name;
            grid.Controls.Add(new Label { Text = label + ":", AutoSize = true, Margin = new Padding(3, 7, 3, 3) });
            var box = new NumberBox
            {
                Minimum = 25, Maximum = 300, Increment = 5, Width = 80,
                Value = Math.Clamp(levels.GetValueOrDefault(pad.Name, 100), 25, 300),
                AccessibleName = label + " level, percent",
            };
            box.ValueChanged += (_, _) => { Save(); sendDelay.Stop(); sendDelay.Start(); };
            boxes.Add((pad.Name, box));
            grid.Controls.Add(box);
        }
        sendDelay.Tick += async (_, _) => { sendDelay.Stop(); await this.apply(); };

        var reset = new Button { Text = "&Reset all to 100", AutoSize = true };
        reset.Click += (_, _) => { foreach (var (_, b) in boxes) b.Value = 100; };
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.OK };
        var buttons = new RowPanel { AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
        buttons.Controls.AddRange([reset, close]);
        grid.Controls.Add(new Label { AutoSize = true });
        grid.Controls.Add(buttons);
        Controls.Add(grid);
        AcceptButton = close;
        CancelButton = close;
    }

    void Save()
    {
        var levels = PadLevels.Load();
        foreach (var (pad, box) in boxes) levels[pad] = (int)box.Value;
        PadLevels.Save(levels);
    }
}
