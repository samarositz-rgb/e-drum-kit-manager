using AppKit;
using CoreGraphics;
using Foundation;

namespace TD12KitManager.Mac;

/// <summary>
/// Edit the MIDI note each pad zone of a module profile sends. Type a note, press Up or Down to change it, or press
/// "Hit pad" and strike the pad to read the note from the module. -1 means the module has no such zone.
/// Shown as a sheet; the result is the edited profile, or null on Cancel.
/// </summary>
sealed class PadNotesSheet
{
    const int RowHeight = 34;

    static readonly Dictionary<string, string> ShortNames = new()
    {
        ["kick"] = "Kick", ["snare"] = "Snare head", ["snareRim"] = "Snare rimshot", ["snareXstick"] = "Snare cross-stick",
        ["tom1"] = "Tom 1 head", ["tom1Rim"] = "Tom 1 rim", ["tom2"] = "Tom 2 head", ["tom2Rim"] = "Tom 2 rim",
        ["tom3"] = "Tom 3 head", ["tom3Rim"] = "Tom 3 rim", ["tom4"] = "Tom 4 head", ["tom4Rim"] = "Tom 4 rim",
        ["hhBowClosed"] = "Hi-hat bow, closed", ["hhBowOpen"] = "Hi-hat bow, open",
        ["hhEdgeClosed"] = "Hi-hat edge, closed", ["hhEdgeOpen"] = "Hi-hat edge, open", ["hhPedal"] = "Hi-hat pedal",
        ["crash1"] = "Crash 1 bow", ["crash1Edge"] = "Crash 1 edge", ["crash2"] = "Crash 2 bow", ["crash2Edge"] = "Crash 2 edge",
        ["ride"] = "Ride bow", ["rideEdge"] = "Ride edge", ["rideBell"] = "Ride bell",
        ["aux1"] = "Aux 1 head", ["aux1Rim"] = "Aux 1 rim", ["aux2"] = "Aux 2 head", ["aux2Rim"] = "Aux 2 rim",
    };

    readonly ModuleProfile profile;
    readonly Dictionary<string, int> original;
    readonly Func<TimeSpan, Task<(int Note, int Channel)?>> learn;
    readonly Dictionary<string, NumberField> fields = [];
    readonly NSWindow window;
    readonly NSTextField status;
    NSWindow? parent;
    Action<ModuleProfile?>? finished;
    bool listening;

    public PadNotesSheet(ModuleProfile profile, Func<TimeSpan, Task<(int Note, int Channel)?>> learn)
    {
        this.profile = profile;
        this.learn = learn;
        original = new Dictionary<string, int>(profile.Notes);
        window = new NSWindow(new CGRect(0, 0, 560, 560), NSWindowStyle.Titled, NSBackingStore.Buffered, false)
            { Title = $"Pad notes for {profile.Name}" };
        window.ReleaseWhenClosed(false);
        Announcer.Label(window, $"Pad notes for {profile.Name}");

        var intro = Label("Each part of the kit and the MIDI note your module sends for it. Type a note from 0 to 127, or press Up or Down. " +
                          "Press Hit pad, then strike that part of the kit, to read the note from the module. Press Play note to hear the sound a note makes, without touching the kit. Use minus 1 when your module has no such part.",
            new CGRect(20, 482, 520, 62), wrap: true);
        Announcer.Label(intro, "About pad notes");

        var doc = new NSView(new CGRect(0, 0, 500, ModuleProfile.Zones.Length * RowHeight));
        for (int i = 0; i < ModuleProfile.Zones.Length; i++)
        {
            var zone = ModuleProfile.Zones[i];
            string name = ShortNames.GetValueOrDefault(zone.Id, zone.Id);
            double y = doc.Frame.Height - (i + 1) * RowHeight + 4;
            var label = Label(name + ":", new CGRect(8, y + 2, 180, 20));
            var field = new NumberField($"{name} note. Minus 1 means not used", -1, 127, 1, profile.Notes.TryGetValue(zone.Id, out int n) ? n : -1, 70);
            field.TranslatesAutoresizingMaskIntoConstraints = true;
            field.Frame = new CGRect(196, y + 1, 70, 24);
            var hit = new NSButton(new CGRect(276, y - 1, 110, 28)) { Title = "Hit pad", BezelStyle = NSBezelStyle.Rounded };
            Announcer.Label(hit, $"Hit pad to read the note for {name}");
            string zoneId = zone.Id;
            hit.Activated += async (_, _) => await Listen(zoneId, name, hit);
            var play = new NSButton(new CGRect(390, y - 1, 100, 28)) { Title = "Play note", BezelStyle = NSBezelStyle.Rounded };
            Announcer.Label(play, $"Play the note for {name} through REAPER");
            play.Activated += (_, _) => PlayNote(name, fields[zoneId].Value);
            fields[zone.Id] = field;
            foreach (var v in new NSView[] { label, field, hit, play }) doc.AddSubview(v);
        }

        var scroll = new NSScrollView(new CGRect(20, 100, 520, 372))
        {
            HasVerticalScroller = true, BorderType = NSBorderType.BezelBorder, DocumentView = doc,
        };
        scroll.AccessibilityLabel = "Pad notes list";

        status = Label("", new CGRect(20, 62, 520, 30), wrap: true);
        Announcer.Label(status, "Pad notes status");

        var reset = new NSButton(new CGRect(20, 20, 140, 30)) { Title = "Undo my changes", BezelStyle = NSBezelStyle.Rounded };
        var save = new NSButton(new CGRect(320, 20, 100, 30)) { Title = "Save", BezelStyle = NSBezelStyle.Rounded, KeyEquivalent = "\r" };
        var cancel = new NSButton(new CGRect(430, 20, 100, 30)) { Title = "Cancel", BezelStyle = NSBezelStyle.Rounded, KeyEquivalent = "\u001b" };
        reset.Activated += (_, _) =>
        {
            foreach (var (id, f) in fields) f.Value = original.TryGetValue(id, out int v) ? v : -1;
            Say("Notes put back as they were when this window opened.");
        };
        save.Activated += (_, _) => Save();
        cancel.Activated += (_, _) => Finish(null);

        var content = window.ContentView!;
        foreach (var v in new NSView[] { intro, scroll, status, reset, save, cancel }) content.AddSubview(v);
        window.InitialFirstResponder = fields["kick"];
        window.AutorecalculatesKeyViewLoop = true;
        window.RecalculateKeyViewLoop();
    }

    static NSTextField Label(string s, CGRect frame, bool wrap = false)
    {
        var t = new NSTextField(frame) { StringValue = s, Editable = false, Bordered = false, DrawsBackground = false, Selectable = true };
        if (wrap) t.PreferredMaxLayoutWidth = frame.Width;
        return t;
    }

    public void Show(NSWindow parentWindow, Action<ModuleProfile?> done)
    {
        parent = parentWindow;
        finished = done;
        parentWindow.BeginSheet(window, _ => { });
        window.MakeFirstResponder(fields["kick"]);
    }

    void Finish(ModuleProfile? result)
    {
        parent?.EndSheet(window);
        window.OrderOut(null);
        var done = finished;
        finished = null;
        done?.Invoke(result);
    }

    void Say(string text)
    {
        status.StringValue = text;
        Announcer.Say(window, text);
    }

    /// <summary>Plays a note into REAPER the way a pad hit would arrive, so it passes through the map and sounds like the real pad.</summary>
    void PlayNote(string name, int note)
    {
        if (note < 0) { Say($"{name} has no note. Type a note from 0 to 127 first."); return; }
        int channel = profile.DrumChannel is >= 1 and <= 16 ? profile.DrumChannel : 10;
        try
        {
            Directory.CreateDirectory(KitStore.BridgeDir);
            string script = Path.Combine(KitStore.BridgeDir, "playnote.lua");
            File.WriteAllText(script,
                $"local ch, note = {channel}, {note}\n" +
                "reaper.StuffMIDIMessage(0, 0x90 + ch - 1, note, 100)\n" +
                "local t = reaper.time_precise()\n" +
                "local function off()\n" +
                "  if reaper.time_precise() - t > 0.35 then reaper.StuffMIDIMessage(0, 0x80 + ch - 1, note, 0)\n" +
                "  else reaper.defer(off) end\n" +
                "end\n" +
                "off()\n");
            // -g keeps REAPER in the background, so VoiceOver stays in Kit Manager.
            using var open = System.Diagnostics.Process.Start("/usr/bin/open", ["-g", "-a", "REAPER", script]);
            Say($"Playing note {note} for {name}.");
        }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception)
        {
            Say("Could not play the note. Check that REAPER is open.");
        }
    }

    async Task Listen(string zoneId, string name, NSButton button)
    {
        if (listening) return;
        listening = true;
        button.Enabled = false;
        Say($"Hit {name} now.");
        try
        {
            var result = await learn(TimeSpan.FromSeconds(15));
            if (result == null) { Say($"Nothing arrived for {name}. Check that REAPER is open, then try again."); return; }
            fields[zoneId].Value = result.Value.Note;
            string clash = fields.Where(f => f.Key != zoneId && f.Value.Value == result.Value.Note).Select(f => ShortNames.GetValueOrDefault(f.Key, f.Key)).FirstOrDefault() is { } other
                ? $" That is also the note for {other}, so they will play the same sound." : "";
            Say($"{name} set to note {result.Value.Note}.{clash}");
        }
        finally
        {
            listening = false;
            button.Enabled = true;
            window.MakeFirstResponder(button);
        }
    }

    void Save()
    {
        foreach (var f in fields.Values) f.Commit();
        var notes = new Dictionary<string, int>();
        foreach (var (id, f) in fields) if (f.Value >= 0) notes[id] = f.Value;
        if (!notes.ContainsKey("kick") && !notes.ContainsKey("snare"))
        {
            Say("The kick and snare both have no note, so nothing would play. Give at least one of them a note.");
            return;
        }
        Finish(new ModuleProfile
        {
            Name = profile.Name,
            DrumChannel = profile.DrumChannel,
            KitCount = profile.KitCount,
            RolandBackup = profile.RolandBackup,
            WriteMap = true, // the edited notes must reach EZdrummer's e-drum map
            Notes = notes,
        });
    }
}
