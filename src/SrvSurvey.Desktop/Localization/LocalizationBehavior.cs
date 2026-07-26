using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;

namespace SrvSurvey.Desktop.Localization;

public static class LocalizationBehavior
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<AvaloniaObject, AvaloniaObject, bool>(
            "Enabled",
            defaultValue: false);

    static LocalizationBehavior()
    {
        EnabledProperty.Changed.AddClassHandler<AvaloniaObject>(
            (_, eventArgs) =>
            {
                if (eventArgs.NewValue is true
                    && eventArgs.Sender is AvaloniaObject target)
                {
                    TranslateLiteralProperties(target);
                }
            });
    }

    public static void SetEnabled(AvaloniaObject target, bool value)
    {
        target.SetValue(EnabledProperty, value);
    }

    public static bool GetEnabled(AvaloniaObject target)
    {
        return target.GetValue(EnabledProperty);
    }

    internal static void TranslateLiteralProperties(AvaloniaObject target)
    {
        if (target is TextBlock textBlock)
        {
            TranslateStringProperty(textBlock, TextBlock.TextProperty);
        }

        if (target is HeaderedContentControl headered)
        {
            TranslateObjectProperty(headered, HeaderedContentControl.HeaderProperty);
        }

        if (target is ContentControl contentControl)
        {
            TranslateObjectProperty(contentControl, ContentControl.ContentProperty);
        }

        if (target is TextBox textBox)
        {
            TranslateStringProperty(textBox, TextBox.PlaceholderTextProperty);
        }

        if (target is Window window)
        {
            TranslateStringProperty(window, Window.TitleProperty);
        }
    }

    private static void TranslateStringProperty(
        AvaloniaObject target,
        StyledProperty<string?> property)
    {
        if (BindingOperations.GetBindingExpressionBase(target, property) is not null)
        {
            return;
        }

        var current = target.GetValue(property);
        var translated = LocalizationCatalog.Translate(current);
        if (!string.Equals(current, translated, StringComparison.Ordinal))
        {
            target.SetCurrentValue(property, translated);
        }
    }

    private static void TranslateObjectProperty(
        AvaloniaObject target,
        StyledProperty<object?> property)
    {
        if (BindingOperations.GetBindingExpressionBase(target, property) is not null
            || target.GetValue(property) is not string current)
        {
            return;
        }

        var translated = LocalizationCatalog.Translate(current);
        if (!string.Equals(current, translated, StringComparison.Ordinal))
        {
            target.SetCurrentValue(property, translated);
        }
    }
}
