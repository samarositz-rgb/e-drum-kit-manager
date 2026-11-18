using AppKit;
using CoreGraphics;
using Foundation;

namespace TD12KitManager.Mac;

/// <summary>
/// "Learn my kit": asks the player to hit each pad zone in turn and records the note the module sends,
/// creating a module profile. Every prompt and result is announced for VoiceOver.
/// </summary>
sealed class LearnSheet
{
    readonly Func<TimeSpan, Task<(int Note, int Channel)?>> learn;
    readonly NSWindow window;
    readonly NSTextField nameBox = new(new CGRect(0, 0, 300, 22));
    readonly NumberField kitCount;
    readonly NSTextField prompt;
    readonly NSButton start, skip, cancel;
    NSWindow? parent;
    TaskCompletionSource? skipPressed;
    bool closing;
    Action<ModuleProfile?>? finished;

    public LearnSheet(Func<TimeSpan, Task<(int Note, int Channel)?>> learn)
    {
        this.learn = learn;
        window = new NSWindow(new CGRect(0, 0, 600, 400), NSWindowStyle.Titled, NSBackingStore.Buffered, false) { Title = "Learn my kit" };
        window.ReleaseWhenClosed(false);
        Announcer.Label(window, "Learn my kit");

        NSTextField Text(string s, CGRect frame, bool wrap = false)
        {
            var t = new NSTextField(frame)
            {
                StringValue = s, Editable = false, Bordered = false, DrawsBackground = false, Selectable = true,
            };
            if (wrap) t.PreferredMaxLayoutWidth = frame.Width;
            return t;
        }

        var intro = Text("Kit Manager asks you to hit each part of your kit in turn, and records the note your module sends. " +
                         "REAPER must be open. Hit firmly and once. Press Skip for anything your kit doesn't have.",
            new CGRect(20, 330, 560, 50), wrap: true);
        Announcer.Label(intro, "About learning your kit");

        var nameLabel = Text("Module name:", new CGRect(20, 290, 160, 20));
        nameBox.Frame = new CGRect(190, 288, 380, 24);
        Announcer.Label(nameBox, "Module name, for example Alesis Strike Pro");
        var countLabel = Text("Number of kits on the module:", new CGRect(20, 254, 200, 20));
        kitCount = new NumberField("Number of kits on the module", 1, 999, 1, 100, 70);
        kitCount.TranslatesAutoresizingMaskIntoConstraints = true;
        kitCount.Frame = new CGRect(230, 252, 70, 24);

        prompt = Text("", new CGRect(20, 120, 560, 110), wrap: true);
        prompt.Font = NSFont.SystemFontOfSize(16);
        Announcer.Label(prompt, "Learn prompt");

        start = new NSButton(new CGRect(20, 20, 150, 30)) { Title = "Start learning", BezelStyle = NSBezelStyle.Rounded, KeyEquivalent = "\r" };
        skip = new NSButton(new CGRect(180, 20, 130, 30)) { Title = "Skip this one", BezelStyle = NSBezelStyle.Rounded, Enabled = false };
        cancel = new NSButton(new CGRect(470, 20, 110, 30)) { Title = "Cancel", BezelStyle = NSBezelStyle.Rounded, KeyEquivalent = "\u001b" };
        start.Activated += async (_, _) => await Run();
        skip.Activated += (_, _) => skipPressed?.TrySetResult();
        cancel.Activated += (_, _) => Finish(null);

        var content = window.ContentView!;
        foreach (var v in new NSView[] { intro, nameLabel, nameBox, countLabel, kitCount, prompt, start, skip, cancel })
            content.AddSubview(v);
        window.InitialFirstResponder = nameBox;
        window.AutorecalculatesKeyViewLoop = true;
        window.RecalculateKeyViewLoop();
    }

    public void Show(NSWindow parentWindow, Action<ModuleProfile?> done)
    {
        parent = parentWindow;
        finished = done;
        parentWindow.BeginSheet(window, _ => { });
        window.MakeFirstResponder(nameBox);
    }

    void Finish(ModuleProfile? result)
    {
        closing = true;
        skipPressed?.TrySetResult();
        parent?.EndSheet(window);
        window.OrderOut(null);
        var done = finished;
        finished = null;
        done?.Invoke(result);
    }

    void Say(string text)
    {
        prompt.StringValue = text;
        Announcer.Say(window, text);
    }

    async Task Run()
    {
        string name = nameBox.StringValue.Trim();
        if (name.Length == 0) { Say("Please type the module's name first."); window.MakeFirstResponder(nameBox); return; }
        kitCount.Commit();
        start.Enabled = nameBox.Enabled = kitCount.Enabled = false;
        skip.Enabled = true;
        window.MakeFirstResponder(skip);

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
        var profile = new ModuleProfile
        {
            Name = name,
            DrumChannel = channel,
            KitCount = kitCount.Value,
            RolandBackup = name.Contains("Roland TD", StringComparison.OrdinalIgnoreCase),
            WriteMap = true,
            Notes = notes,
        };
        Say($"Done: {notes.Count} parts learned for {name}, on MIDI channel {channel}. Saving.");
        await Task.Delay(1500);
        Finish(profile);
    }
}
