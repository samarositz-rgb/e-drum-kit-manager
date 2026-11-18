using AppKit;
using CoreGraphics;
using Foundation;

namespace TD12KitManager.Mac;

/// <summary>
/// Volume per pad (percentage of hit strength), for evening out weak or older pads. The pads come from the
/// drum module profile. Each change is saved at once and sent to REAPER shortly after. Shown as a sheet.
/// </summary>
sealed class PadLevelsSheet
{
    readonly List<(string Pad, NumberField Box)> boxes = [];
    readonly Func<Task> apply;
    readonly NSWindow window;
    NSWindow? parent;
    NSTimer? sendDelay;
    Action? finished;

    /// <param name="apply">Regenerates the kit files and asks REAPER to apply the levels.</param>
    public PadLevelsSheet(ModuleProfile profile, Func<Task> apply)
    {
        this.apply = apply;
        window = new NSWindow(new CGRect(0, 0, 640, 620), NSWindowStyle.Titled, NSBackingStore.Buffered, false) { Title = "Pad levels" };
        window.ReleaseWhenClosed(false);
        Announcer.Label(window, "Pad levels");

        var intro = new NSTextField(new CGRect(20, 560, 600, 44))
        {
            StringValue = $"Percent of hit strength for each pad of your {profile.Name}. 100 is unchanged; raise a quiet pad, lower a loud one. " +
                          "Changes take effect in REAPER as you go, so play the pad while you adjust.",
            Editable = false, Bordered = false, DrawsBackground = false, Selectable = true,
            AutoresizingMask = NSViewResizingMask.MinYMargin,
        };
        intro.PreferredMaxLayoutWidth = 600;
        Announcer.Label(intro, "About pad levels");

        var levels = PadLevels.Load();
        var rows = new List<NSView[]>();
        foreach (var pad in profile.Pads())
        {
            string label = pad.Name == "Crash 1" ? "Crash 1 (usually next to the hi-hat)" : pad.Name;
            var box = new NumberField(label + " level, percent", 25, 300, 5, levels.GetValueOrDefault(pad.Name, 100), 80);
            box.ValueChanged += () => { Save(); ScheduleApply(); };
            boxes.Add((pad.Name, box));
            rows.Add([new NSTextField(new CGRect(0, 0, 10, 18))
            {
                StringValue = label + ":", Editable = false, Bordered = false, DrawsBackground = false, Selectable = false,
            }, box]);
        }
        var grid = new NSGridView();
        foreach (var r in rows) grid.AddRow(r);
        grid.ColumnSpacing = 10;
        grid.RowSpacing = 8;
        grid.Frame = new CGRect(0, 0, 580, grid.FittingSize.Height);

        var scroll = new NSScrollView(new CGRect(20, 56, 600, 500))
        {
            DocumentView = grid, HasVerticalScroller = true, DrawsBackground = false,
            AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable,
        };
        Announcer.Label(scroll, "Pad levels");
        scroll.ContentView.ScrollToPoint(new CGPoint(0, grid.IsFlipped ? 0 : Math.Max(0, grid.Frame.Height - scroll.ContentView.Bounds.Height)));
        scroll.ReflectScrolledClipView(scroll.ContentView);

        var reset = new NSButton(new CGRect(20, 14, 150, 28)) { Title = "Reset all to 100", BezelStyle = NSBezelStyle.Rounded };
        var close = new NSButton(new CGRect(520, 14, 100, 28)) { Title = "Close", BezelStyle = NSBezelStyle.Rounded, KeyEquivalent = "\r" };
        reset.Activated += (_, _) =>
        {
            foreach (var (_, b) in boxes) b.Value = 100;
            Announcer.Say(window, "All pad levels reset to 100.");
        };
        close.Activated += (_, _) => Finish();

        var content = window.ContentView!;
        content.AddSubview(intro);
        content.AddSubview(scroll);
        content.AddSubview(reset);
        content.AddSubview(close);
        window.InitialFirstResponder = boxes.Count > 0 ? boxes[0].Box : close;
        window.AutorecalculatesKeyViewLoop = true;
        window.RecalculateKeyViewLoop();
    }

    public void Show(NSWindow parentWindow, Action done)
    {
        parent = parentWindow;
        finished = done;
        parentWindow.BeginSheet(window, _ => { });
        if (boxes.Count > 0) window.MakeFirstResponder(boxes[0].Box);
    }

    void Finish()
    {
        sendDelay?.Invalidate();
        parent?.EndSheet(window);
        window.OrderOut(null);
        var done = finished;
        finished = null;
        done?.Invoke();
    }

    void ScheduleApply()
    {
        sendDelay?.Invalidate();
        sendDelay = NSTimer.CreateScheduledTimer(0.3, timer => _ = apply());
    }

    void Save()
    {
        var levels = PadLevels.Load();
        foreach (var (pad, box) in boxes) levels[pad] = box.Value;
        PadLevels.Save(levels);
    }
}
