using System.ComponentModel;
using System.Globalization;


namespace TD12KitManager;

/// <summary>
/// A number field: type a value, or press Up and Down (Page Up and Page Down for bigger steps).
/// Used instead of NumericUpDown, whose spin box and inner edit both carry the name, so JAWS reads it twice.
/// </summary>
public sealed class NumberBox : TextBox
{
    decimal value;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public decimal Minimum { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public decimal Maximum { get; set; } = 100;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public decimal Increment { get; set; } = 1;
    public event EventHandler? ValueChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public decimal Value
    {
        get => value;
        set
        {
            var v = Math.Clamp(value, Minimum, Maximum);
            string text = v.ToString(CultureInfo.CurrentCulture);
            if (Text != text) Text = text; // OnTextChanged sets the value
            else SetValue(v);
        }
    }

    public NumberBox() => Text = "0";

    void SetValue(decimal v)
    {
        if (v == value) return;
        value = v;
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        if (decimal.TryParse(Text, NumberStyles.Integer | NumberStyles.AllowDecimalPoint, CultureInfo.CurrentCulture, out var v)
            && v >= Minimum && v <= Maximum)
            SetValue(v);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        decimal step = e.KeyCode switch
        {
            Keys.Up => Increment, Keys.Down => -Increment,
            Keys.PageUp => Increment * 10, Keys.PageDown => -Increment * 10,
            _ => 0,
        };
        if (step != 0 && !e.Alt && !e.Control)
        {
            e.Handled = e.SuppressKeyPress = true;
            Value = value + step;
            SelectAll();
            // A program change to an edit's text isn't spoken by screen readers, so say the new value once.
            Speech.Say(this, Text, interrupt: true);
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar) && !(e.KeyChar == '-' && Minimum < 0)) e.Handled = true;
        base.OnKeyPress(e);
    }

    protected override void OnEnter(EventArgs e)
    {
        base.OnEnter(e);
        BeginInvoke(SelectAll);
    }

    protected override void OnLeave(EventArgs e)
    {
        base.OnLeave(e);
        Text = value.ToString(CultureInfo.CurrentCulture); // drop anything half typed or out of range
    }
}

/// <summary>
/// A row of controls with no name of its own. A plain FlowLayoutPanel takes the name of the label before it,
/// so JAWS says that label again when focus moves into the row.
/// </summary>
public class RowPanel : FlowLayoutPanel
{
    protected override AccessibleObject CreateAccessibilityInstance() => new Unnamed(this);

    sealed class Unnamed(Control owner) : ControlAccessibleObject(owner)
    {
        public override string? Name { get => null; set { } }
    }
}
