using Avalonia;
using Avalonia.Controls;

namespace SrvSurvey.Desktop.Behaviors;

/// <summary>Prevents optional text hints from opening empty tooltip popups.</summary>
internal static class ToolTipContentBehavior
{
    private static int registered;

    public static void Register()
    {
        if (Interlocked.Exchange(ref registered, 1) == 0)
        {
            ToolTip.TipProperty.Changed.AddClassHandler<Control>(OnTipChanged);
        }
    }

    private static void OnTipChanged(Control control, AvaloniaPropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.GetNewValue<object?>() is string text && string.IsNullOrWhiteSpace(text))
        {
            // Preserve optional bindings so their next nonempty value can still provide a hint.
            control.SetCurrentValue(ToolTip.TipProperty, null);
        }
    }
}
