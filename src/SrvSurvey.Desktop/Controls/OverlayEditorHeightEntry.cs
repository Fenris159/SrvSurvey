using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;

namespace SrvSurvey.Desktop.Controls;

/// <summary>
/// Applies a typed editor height only on Enter and discards unfinished edits on focus loss.
/// </summary>
public sealed class OverlayEditorHeightEntry : TextBox
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<
        OverlayEditorHeightEntry,
        double
    >(nameof(Value), defaultBindingMode: BindingMode.TwoWay);

    private bool editing;

    static OverlayEditorHeightEntry()
    {
        ValueProperty.Changed.AddClassHandler<OverlayEditorHeightEntry>((entry, _) => entry.ShowValue());
    }

    public OverlayEditorHeightEntry()
    {
        GotFocus += (_, _) => editing = true;
        LostFocus += (_, _) =>
        {
            editing = false;
            ShowValue();
        };
        ShowValue();
    }

    protected override Type StyleKeyOverride => typeof(TextBox);

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (
                (
                    double.TryParse(Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value)
                    || double.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                ) && double.IsFinite(value)
            )
            {
                SetCurrentValue(ValueProperty, Math.Clamp(Math.Round(value, 1), -100, 100));
            }

            RefreshText();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            RefreshText();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private void RefreshText()
    {
        editing = false;
        ShowValue();
        editing = IsFocused;
    }

    private void ShowValue()
    {
        if (!editing)
        {
            Text = Value.ToString("0.0", CultureInfo.CurrentCulture);
        }
    }
}
