using AppKit;
using CoreGraphics;
using Foundation;

namespace TD12KitManager.Mac;

/// <summary>Choose the drum module profile and the MIDI ports it is connected to. Shown as a sheet.</summary>
sealed class HardwareSheet
{
    const string AnyInput = "Any MIDI input";
    const string NoOutput = "None";
    const string NotConnected = " (not connected)";

    readonly ChoiceBox<string> profileChoice = new("Drum module", 360);
    readonly ChoiceBox<string> inChoice = new("MIDI input from the module", 360);
    readonly ChoiceBox<string> outChoice = new("MIDI output to the module, for backups", 360);
    readonly NSTextField profileInfo;
    readonly Func<TimeSpan, Task<(int Note, int Channel)?>> learn;
    readonly NSWindow window;
    NSWindow? parent;
    Action<HardwareSettings?>? finished;

    public HardwareSheet(HardwareSettings current, List<string> inputs, List<string> outputs,
        Func<TimeSpan, Task<(int Note, int Channel)?>> learn)
    {
        this.learn = learn;
        window = new NSWindow(new CGRect(0, 0, 640, 440), NSWindowStyle.Titled, NSBackingStore.Buffered, false) { Title = "Hardware setup" };
        window.ReleaseWhenClosed(false);
        Announcer.Label(window, "Hardware setup");

        NSTextField Text(string s, CGRect frame, bool wrap = false)
        {
            var t = new NSTextField(frame)
            {
                StringValue = s, Editable = false, Bordered = false, DrawsBackground = false, Selectable = true,
            };
            if (wrap) t.PreferredMaxLayoutWidth = frame.Width;
            return t;
        }

        FillProfiles(current.Profile);
        profileChoice.Activated += (_, _) => ShowProfile();

        inChoice.Add(AnyInput, AnyInput);
        foreach (var i in inputs) inChoice.Add(i, i);
        Select(inChoice, current.MidiInput, AnyInput);

        outChoice.Add(NoOutput, NoOutput);
        foreach (var o in outputs) outChoice.Add(o, o);
        Select(outChoice, current.MidiOutput, NoOutput);

        profileInfo = Text("", new CGRect(190, 300, 430, 64), wrap: true);
        Announcer.Label(profileInfo, "About this drum module");

        var learnButton = new NSButton(new CGRect(190, 262, 255, 28)) { Title = "Learn a new module (hit each pad)...", BezelStyle = NSBezelStyle.Rounded };
        learnButton.Activated += (_, _) => LearnNew();
        var editButton = new NSButton(new CGRect(450, 262, 170, 28)) { Title = "Edit pad notes...", BezelStyle = NSBezelStyle.Rounded };
        Announcer.Help(editButton, "Change the MIDI note each pad of the chosen drum module sends.");
        editButton.Activated += (_, _) => EditNotes();

        var content = window.ContentView!;
        void Place(NSView v, double x, double y) { v.TranslatesAutoresizingMaskIntoConstraints = true; v.SetFrameOrigin(new CGPoint(x, y)); content.AddSubview(v); }
        content.AddSubview(Text("Drum module:", new CGRect(20, 394, 160, 20)));
        Place(profileChoice, 190, 390);
        content.AddSubview(profileInfo);
        content.AddSubview(learnButton);
        content.AddSubview(editButton);
        content.AddSubview(Text("MIDI input from the module:", new CGRect(20, 222, 170, 20)));
        Place(inChoice, 190, 218);
        content.AddSubview(Text("MIDI output to the module (backups):", new CGRect(20, 182, 170, 36)));
        Place(outChoice, 190, 178);
        if (inputs.Count == 0)
        {
            var note = Text("REAPER did not list any MIDI ports. Open REAPER, and turn on your MIDI interface in REAPER's Preferences, MIDI Devices.",
                new CGRect(190, 110, 430, 50), wrap: true);
            Announcer.Label(note, "No MIDI ports found");
            content.AddSubview(note);
        }

        var save = new NSButton(new CGRect(410, 20, 100, 30)) { Title = "Save", BezelStyle = NSBezelStyle.Rounded, KeyEquivalent = "\r" };
        var cancel = new NSButton(new CGRect(520, 20, 100, 30)) { Title = "Cancel", BezelStyle = NSBezelStyle.Rounded, KeyEquivalent = "\u001b" };
        save.Activated += (_, _) => Finish(new HardwareSettings
        {
            Profile = profileChoice.Chosen ?? current.Profile,
            MidiInput = Port(inChoice, AnyInput),
            MidiOutput = Port(outChoice, NoOutput),
        });
        cancel.Activated += (_, _) => Finish(null);
        content.AddSubview(save);
        content.AddSubview(cancel);

        window.InitialFirstResponder = profileChoice;
        window.AutorecalculatesKeyViewLoop = true;
        window.RecalculateKeyViewLoop();
        ShowProfile();
    }

    public void Show(NSWindow parentWindow, Action<HardwareSettings?> done)
    {
        parent = parentWindow;
        finished = done;
        parentWindow.BeginSheet(window, _ => { });
        window.MakeFirstResponder(profileChoice);
    }

    void Finish(HardwareSettings? result)
    {
        parent?.EndSheet(window);
        window.OrderOut(null);
        var done = finished;
        finished = null;
        done?.Invoke(result);
    }

    /// <summary>The saved port name: empty for "any"/"none", and without the "not connected" note.</summary>
    static string Port(ChoiceBox<string> box, string none)
    {
        string s = box.Chosen ?? none;
        if (s == none) return "";
        return s.EndsWith(NotConnected, StringComparison.Ordinal) ? s[..^NotConnected.Length] : s;
    }

    void FillProfiles(string selected)
    {
        profileChoice.RemoveAllItems();
        profileChoice.Values.Clear();
        foreach (var p in HardwareSettings.AllProfiles()) profileChoice.Add(p.Name, p.Name);
        int index = profileChoice.Values.IndexOf(selected);
        profileChoice.SelectItem(index >= 0 ? index : 0);
    }

    void ShowProfile()
    {
        var p = HardwareSettings.AllProfiles().FirstOrDefault(x => x.Name == profileChoice.Chosen);
        if (p == null) return;
        string pads = string.Join(", ", p.Pads().Select(x => x.Name));
        string text = $"{p.Pads().Count} pads: {pads}. Drum channel {(p.DrumChannel == 0 ? "any" : p.DrumChannel)}, {p.KitCount} kits." +
            (p.WriteMap ? " Kit Manager writes the EZdrummer map for it." : " Uses the e-drum map already set up in EZdrummer.");
        profileInfo.StringValue = text;
        Announcer.Help(profileChoice, text);
    }

    /// <summary>Selects the item whose name contains the saved port name (saved names may be partial, like "USB MIDI").</summary>
    static void Select(ChoiceBox<string> box, string saved, string none)
    {
        if (saved.Length == 0) { box.SelectItem(box.Values.IndexOf(none)); return; }
        for (int i = 0; i < box.Values.Count; i++)
            if (box.Values[i].Contains(saved, StringComparison.OrdinalIgnoreCase)) { box.SelectItem(i); return; }
        string missing = saved + NotConnected;
        box.Add(missing, missing);
        box.SelectItem(box.Values.Count - 1);
    }

    void EditNotes()
    {
        var chosen = HardwareSettings.AllProfiles().FirstOrDefault(x => x.Name == profileChoice.Chosen);
        if (chosen == null) return;
        var sheet = new PadNotesSheet(chosen, learn);
        sheet.Show(window, edited =>
        {
            if (edited != null)
            {
                HardwareSettings.SaveProfile(edited);
                FillProfiles(edited.Name);
                ShowProfile();
                Announcer.Say(window, $"Pad notes for {edited.Name} saved. Press Save to use them.");
            }
            window.MakeFirstResponder(profileChoice);
        });
    }

    void LearnNew()
    {
        var wizard = new LearnSheet(learn);
        wizard.Show(window, profile =>
        {
            if (profile == null) return;
            HardwareSettings.SaveProfile(profile);
            FillProfiles(profile.Name);
            ShowProfile();
            window.MakeFirstResponder(profileChoice);
            Announcer.Say(window, $"{profile.Name} learned and added to the list.");
        });
    }
}
