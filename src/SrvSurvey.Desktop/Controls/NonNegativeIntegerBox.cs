using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace SrvSurvey.Desktop.Controls;

/// <summary>Whole-tonne entry that lets TextBox own editing and caret movement while rejecting invalid insertions.</summary>
public sealed class NonNegativeIntegerBox : TextBox
{
    protected override Type StyleKeyOverride => typeof(TextBox);

    public NonNegativeIntegerBox() => PastingFromClipboard += PasteFromClipboard;

    protected override void OnTextInput(TextInputEventArgs e)
    {
        string text = Text ?? string.Empty;
        int start = Math.Min(SelectionStart, SelectionEnd);
        int length = Math.Abs(SelectionEnd - SelectionStart);
        string candidate = text.Remove(start, length).Insert(start, e.Text ?? string.Empty);
        if (
            !candidate.All(char.IsAsciiDigit)
            || !int.TryParse(candidate, NumberStyles.None, CultureInfo.InvariantCulture, out _)
        )
        {
            e.Handled = true;
            return;
        }
        base.OnTextInput(e);
    }

    private async void PasteFromClipboard(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        await PasteNumericTextAsync();
    }

    internal async Task PasteNumericTextAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (IsReadOnly || !IsEffectivelyEnabled || topLevel?.Clipboard is not { } clipboard)
        {
            return;
        }
        string? previousText = Text;
        int start = SelectionStart;
        int end = SelectionEnd;
        int caret = CaretIndex;
        string? pasted;
        try
        {
            pasted = await clipboard.TryGetTextAsync();
        }
        catch (Exception exception)
            when (exception
                    is InvalidOperationException
                        or NotSupportedException
                        or COMException
                        or UnauthorizedAccessException
                        or IOException
            )
        {
            // A temporarily unavailable clipboard must leave the field and caret unchanged.
            return;
        }
        if (
            string.IsNullOrEmpty(pasted)
            || Text != previousText
            || SelectionStart != start
            || SelectionEnd != end
            || CaretIndex != caret
            || TopLevel.GetTopLevel(this) != topLevel
            || IsReadOnly
            || !IsEffectivelyEnabled
        )
        {
            return;
        }
        RaiseEvent(new TextInputEventArgs { RoutedEvent = TextInputEvent, Text = pasted });
    }
}
