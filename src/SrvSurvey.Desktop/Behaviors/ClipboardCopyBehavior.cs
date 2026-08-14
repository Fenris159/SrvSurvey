using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace SrvSurvey.Desktop.Behaviors;

public static class ClipboardCopyBehavior
{
    private static readonly ConditionalWeakTable<Control, object> Attached = new();

    public static readonly AttachedProperty<object?> TextProperty =
        AvaloniaProperty.RegisterAttached<Control, Control, object?>(
            "Text");

    static ClipboardCopyBehavior()
    {
        TextProperty.Changed.AddClassHandler<Control>(OnTextChanged);
    }

    public static void SetText(Control target, object? value)
    {
        target.SetValue(TextProperty, value);
    }

    public static object? GetText(Control target)
    {
        return target.GetValue(TextProperty);
    }

    private static void OnTextChanged(
        Control control,
        AvaloniaPropertyChangedEventArgs eventArgs)
    {
        if (string.IsNullOrWhiteSpace(eventArgs.NewValue?.ToString()))
        {
            ToolTip.SetTip(control, null);
            return;
        }

        ToolTip.SetTip(control, "Click to copy");
        if (Attached.TryGetValue(control, out _))
        {
            return;
        }

        Attached.Add(control, new object());
        if (control is Button button)
        {
            button.Click += CopyText_Click;
        }
        else
        {
            control.Classes.Add("system-copy-link");
            control.PointerPressed += CopyText_PointerPressed;
        }
    }

    private static void CopyText_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (sender is Button button)
        {
            _ = CopyTextAsync(button);
        }
    }

    private static void CopyText_PointerPressed(
        object? sender,
        PointerPressedEventArgs eventArgs)
    {
        if (sender is Control control
            && eventArgs.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            eventArgs.Handled = true;
            _ = CopyTextAsync(control);
        }
    }

    private static async Task CopyTextAsync(Control control)
    {
        if (string.IsNullOrWhiteSpace(GetText(control)?.ToString())
            || TopLevel.GetTopLevel(control)?.Clipboard is not { } clipboard)
        {
            return;
        }

        try
        {
            await clipboard.SetTextAsync(GetText(control)!.ToString()!.Trim());
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or NotSupportedException)
        {
            // Clipboard availability is platform-owned; copying is best effort.
        }
    }
}
