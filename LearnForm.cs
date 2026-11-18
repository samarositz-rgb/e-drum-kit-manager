using System.Windows.Forms.Automation;

namespace TD12KitManager;

/// <summary>
/// "Learn my kit": asks the player to hit each pad zone in turn and records the note the module sends,
/// creating a module profile. Every prompt and result is announced for screen readers.
/// </summary>
public sealed class LearnForm : Form
{
    readonly TextBox nameBox = new() { Width = 300, AccessibleName = "Module name, for example Alesis Strike Pro" };
    readonly NumberBox kitCount = new() { Minimum = 1, Maximum = 999, Value = 100, Width = 70, AccessibleName = "Number of kits on the module" };
    readonly Label prompt = new() { AutoSize = true, MaximumSize = new Size(520, 0), Margin = new Padding(3, 10, 3, 10) };
    readonly Button start = new() { Text = "&Start learning", AutoSize = true };
    readonly Button skip = new() { Text = "S&kip this one", AutoSize = true, Enabled = false };
    readonly Button cancel = new() { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
    readonly Func<TimeSpan, Task<(int Note, int Channel)?>> learn;
    TaskCompletionSource? skipPressed;
    bool closing;

    public ModuleProfile? Result { get; private set; }

    public LearnForm(Func<TimeSpan, Task<(int Note, int Channel)?>> learn)
    {
        this.learn = learn;
        Text = "Learn my kit";
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
            Text = "Kit Manager asks you to hit each part of your kit in turn, and records the note your module sends. " +
                   "REAPER must be open. Hit firmly and once. Press Skip for anything your kit doesn't have.",
            AutoSize = true, MaximumSize = new Size(520, 0), Margin = new Padding(3, 3, 3, 10),
        };
        grid.Controls.Add(intro);
        grid.SetColumnSpan(intro, 2);
        grid.Controls.Add(new Label { Text = "Module &name:", AutoSize = true, Margin = new Padding(3, 7, 3, 3) });
        grid.Controls.Add(nameBox);
        grid.Controls.Add(new Label { Text = "Number of &kits on the module:", AutoSize = true, Margin = new Padding(3, 7, 3, 3) });
        grid.Controls.Add(kitCount);
        grid.Controls.Add(prompt);
        grid.SetColumnSpan(prompt, 2);
        var buttons = new RowPanel { AutoSize = true };
        buttons.Controls.AddRange([start, skip, cancel]);
        grid.Controls.Add(buttons);
        grid.SetColumnSpan(buttons, 2);
        Controls.Add(grid);
        CancelButton = cancel;
        AcceptButton = start;

        start.Click += async (_, _) => await Run();
        skip.Click += (_, _) => skipPressed?.TrySetResult();
        FormClosing += (_, _) => { closing = true; skipPressed?.TrySetResult(); };
    }

    void Say(string text)
    {
        prompt.Text = text;
        Speech.Say(prompt, text, interrupt: true);
    }

    async Task Run()
    {
        string name = nameBox.Text.Trim();
        if (name.Length == 0) { Say("Please type the module's name first."); nameBox.Focus(); return; }
        start.Enabled = nameBox.Enabled = kitCount.Enabled = false;
        skip.Enabled = true;
        skip.Focus();

        var notes = new Dictionary<string, int>();
        var padOfNote = new Dictionary<int, string>();
        int channel = 0;
        foreach (var zone in ModuleProfile.Zones)
        {
            string ask = $"Hit {zone.Prompt}.";
            while (!closing)
            {
                Say(ask);
                skipPressed = new TaskCompletionSource();
                var hit = learn(TimeSpan.FromSeconds(30));
                if (await Task.WhenAny(hit, skipPressed.Task) == skipPressed.Task) break; // skipped
                var result = await hit;
                if (result == null) { ask = $"Nothing arrived. Hit {zone.Prompt} again, or press Skip."; continue; }
                var (note, ch) = result.Value;
                if (channel == 0) channel = ch;
                notes[zone.Id] = note;
                string clash = padOfNote.TryGetValue(note, out var other) && other != zone.Pad
                    ? $" That is the same note as {other}, so they will play the same sound." : "";
                padOfNote.TryAdd(note, zone.Pad);
                Say($"Got note {note}.{clash}");
                await Task.Delay(clash.Length > 0 ? 2500 : 900);
                break;
            }
            if (closing) return;
        }

        skip.Enabled = false;
        if (!notes.ContainsKey("kick") && !notes.ContainsKey("snare"))
        {
            Say("No kick or snare was learned, so no profile was made. Press Cancel, check that REAPER is open, and try again.");
            return;
        }
        Result = new ModuleProfile
        {
            Name = name,
            DrumChannel = channel,
            KitCount = (int)kitCount.Value,
            RolandBackup = name.Contains("Roland TD", StringComparison.OrdinalIgnoreCase),
            WriteMap = true,
            Notes = notes,
        };
        Say($"Done: {notes.Count} parts learned for {name}, on MIDI channel {channel}. Saving.");
        await Task.Delay(1500);
        DialogResult = DialogResult.OK;
    }
}
