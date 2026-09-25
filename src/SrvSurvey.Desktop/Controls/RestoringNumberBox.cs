using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;

namespace SrvSurvey.Desktop.Controls;

/// <summary>
/// Keeps the last committed number while the text is cleared, and restores that number when focus leaves.
/// </summary>
public sealed class RestoringNumberBox : TextBox
{
    public static readonly StyledProperty<decimal> NumberProperty = AvaloniaProperty.Register<
        RestoringNumberBox,
        decimal
    >(nameof(Number), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<decimal> MinimumProperty = AvaloniaProperty.Register<
        RestoringNumberBox,
        decimal
    >(nameof(Minimum));

    public static readonly StyledProperty<decimal> MaximumProperty = AvaloniaProperty.Register<
        RestoringNumberBox,
        decimal
    >(nameof(Maximum), 1_000_000_000);

    private bool editing;

    static RestoringNumberBox()
    {
        NumberProperty.Changed.AddClassHandler<RestoringNumberBox>((box, _) => box.ShowCommittedNumber());
    }

    protected override Type StyleKeyOverride => typeof(TextBox);

    public RestoringNumberBox()
    {
        GotFocus += (_, _) => editing = true;
        LostFocus += (_, _) => Commit();
    }

    public decimal Number
    {
        get => GetValue(NumberProperty);
        set => SetValue(NumberProperty, value);
    }

    public decimal Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public decimal Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ShowCommittedNumber();
    }

    private void Commit()
    {
        editing = false;
        if (
            decimal.TryParse(Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out decimal parsed)
            || decimal.TryParse(Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
        )
        {
            Number = Math.Clamp(parsed, Minimum, Maximum);
        }

        ShowCommittedNumber();
    }

    private void ShowCommittedNumber()
    {
        if (editing)
        {
            return;
        }

        Text = Number.ToString("0", CultureInfo.CurrentCulture);
    }
}
