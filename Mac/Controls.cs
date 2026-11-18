using AppKit;
using CoreGraphics;
using Foundation;
using ObjCRuntime;

namespace TD12KitManager.Mac;

/// <summary>Speaks a message through VoiceOver, like the Windows app's screen reader notifications.</summary>
static class Announcer
{
    public static void Say(NSObject element, string text)
    {
        NSObject[] values = [new NSString(text), NSNumber.FromInt32((int)NSAccessibilityPriorityLevel.High)];
        NSObject[] keys = [NSAccessibilityNotificationUserInfoKeys.AnnouncementKey, NSAccessibilityNotificationUserInfoKeys.PriorityKey];
        NSAccessibility.PostNotification(element, NSApplication.AnnouncementRequestedNotification, NSDictionary.FromObjectsAndKeys(values, keys));
    }

    /// <summary>Sets the VoiceOver label through the Objective-C property (some AppKit bindings expose it read-only).</summary>
    public static void Label(NSObject control, string text) =>
        control.SetValueForKey(new NSString(text), new NSString("accessibilityLabel"));

    public static void Help(NSObject control, string text) =>
        control.SetValueForKey(new NSString(text), new NSString("accessibilityHelp"));
}

/// <summary>A whole-number text field that behaves like the Windows spin box: type a value, or press Up and Down.</summary>
sealed class NumberField : NSTextField
{
    sealed class Handler(NumberField field) : NSTextFieldDelegate
    {
        public override bool DoCommandBySelector(NSControl control, NSTextView textView, Selector commandSelector)
        {
            switch (commandSelector.Name)
            {
                case "moveUp:": field.Nudge(1); return true;
                case "moveDown:": field.Nudge(-1); return true;
            }
            return false;
        }

        public override void EditingEnded(NSNotification notification) => field.Commit();
    }

    readonly int min, max, step;
    readonly Handler handler;
    int current;
    bool ready;

    /// <summary>Raised when the number changes, whether typed, nudged with the arrow keys or set from code.</summary>
    public event Action? ValueChanged;

    public NumberField(string label, int min, int max, int step, int initial, int width = 70)
        : base(new CGRect(0, 0, width, 22))
    {
        this.min = min; this.max = max; this.step = step;
        handler = new Handler(this);
        Delegate = handler;
        TranslatesAutoresizingMaskIntoConstraints = false;
        WidthAnchor.ConstraintEqualTo(width).Active = true;
        Alignment = NSTextAlignment.Right;
        Announcer.Label(this, label);
        Announcer.Help(this, $"Type a number from {min} to {max}, or press Up or Down arrow to change it.");
        Value = initial;
        ready = true;
    }

    public int Value
    {
        get => current;
        set
        {
            int clamped = Math.Clamp(value, min, max);
            bool changed = clamped != current;
            current = clamped;
            StringValue = current.ToString();
            if (changed && ready) ValueChanged?.Invoke();
        }
    }

    public void Commit() => Value = int.TryParse(StringValue.Trim(), out int v) ? v : current;

    void Nudge(int direction)
    {
        Commit();
        Value = current + direction * step;
    }
}

/// <summary>A pop-up list that keeps its own items, so duplicate names cannot collapse into one entry.</summary>
sealed class ChoiceBox<T> : NSPopUpButton
{
    public List<T> Values { get; } = [];

    public ChoiceBox(string label, int width) : base(new CGRect(0, 0, width, 26), false)
    {
        TranslatesAutoresizingMaskIntoConstraints = false;
        WidthAnchor.ConstraintEqualTo(width).Active = true;
        Announcer.Label(this, label);
    }

    public void Add(T item, string title)
    {
        Values.Add(item);
        Menu!.AddItem(new NSMenuItem(title));
    }

    public void Insert(int index, T item, string title)
    {
        Values.Insert(index, item);
        Menu!.InsertItem(new NSMenuItem(title), index);
    }

    public T? Chosen => IndexOfSelectedItem >= 0 && IndexOfSelectedItem < Values.Count ? Values[(int)IndexOfSelectedItem] : default;
}
