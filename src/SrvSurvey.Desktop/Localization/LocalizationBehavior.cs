using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using System.Runtime.CompilerServices;

namespace SrvSurvey.Desktop.Localization;

public static class LocalizationBehavior
{
    private static readonly ConditionalWeakTable<AvaloniaObject, TranslationState>
        States = new();

    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<AvaloniaObject, AvaloniaObject, bool>(
            "Enabled",
            defaultValue: false);

    static LocalizationBehavior()
    {
        EnabledProperty.Changed.AddClassHandler<AvaloniaObject>(
            (_, eventArgs) =>
            {
                if (eventArgs.Sender is not AvaloniaObject target)
                {
                    return;
                }

                if (eventArgs.NewValue is true)
                {
                    EnableTranslation(target);
                }
                else
                {
                    DisableTranslation(target);
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
        EnableTranslation(target);
    }

    private static void EnableTranslation(AvaloniaObject target)
    {
        var state = States.GetValue(target, _ => new TranslationState());
        if (target is TextBlock textBlock)
        {
            WatchStringProperty(
                textBlock,
                TextBlock.TextProperty,
                state);
        }

        if (target is HeaderedContentControl headered)
        {
            WatchObjectProperty(
                headered,
                HeaderedContentControl.HeaderProperty,
                state);
        }

        if (target is ContentControl contentControl)
        {
            WatchObjectProperty(
                contentControl,
                ContentControl.ContentProperty,
                state);
        }

        if (target is TextBox textBox)
        {
            WatchStringProperty(
                textBox,
                TextBox.PlaceholderTextProperty,
                state);
        }

        if (target is Window window)
        {
            WatchStringProperty(window, Window.TitleProperty, state);
        }
    }

    private static void DisableTranslation(AvaloniaObject target)
    {
        if (!States.TryGetValue(target, out var state))
        {
            return;
        }

        state.Dispose();
        States.Remove(target);
    }

    private static void WatchStringProperty(
        AvaloniaObject target,
        StyledProperty<string?> property,
        TranslationState state)
    {
        if (!state.WatchedProperties.Add(property))
        {
            return;
        }

        state.Subscriptions.Add(target.GetObservable(property).Subscribe(
            new PropertyObserver<string?>(value => TranslateStringProperty(
                target,
                property,
                value,
                state))));
    }

    private static void WatchObjectProperty(
        AvaloniaObject target,
        StyledProperty<object?> property,
        TranslationState state)
    {
        if (!state.WatchedProperties.Add(property))
        {
            return;
        }

        state.Subscriptions.Add(target.GetObservable(property).Subscribe(
            new PropertyObserver<object?>(value => TranslateObjectProperty(
                target,
                property,
                value,
                state))));
    }

    private static void TranslateStringProperty(
        AvaloniaObject target,
        StyledProperty<string?> property,
        string? current,
        TranslationState state)
    {
        if (state.IsApplying)
        {
            return;
        }

        var translated = LocalizationCatalog.Translate(current);
        if (!string.Equals(current, translated, StringComparison.Ordinal))
        {
            state.IsApplying = true;
            try
            {
                target.SetCurrentValue(property, translated);
            }
            finally
            {
                state.IsApplying = false;
            }
        }
    }

    private static void TranslateObjectProperty(
        AvaloniaObject target,
        StyledProperty<object?> property,
        object? value,
        TranslationState state)
    {
        if (state.IsApplying || value is not string current)
        {
            return;
        }

        var translated = LocalizationCatalog.Translate(current);
        if (!string.Equals(current, translated, StringComparison.Ordinal))
        {
            state.IsApplying = true;
            try
            {
                target.SetCurrentValue(property, translated);
            }
            finally
            {
                state.IsApplying = false;
            }
        }
    }

    private sealed class TranslationState : IDisposable
    {
        public HashSet<AvaloniaProperty> WatchedProperties { get; } = [];

        public List<IDisposable> Subscriptions { get; } = [];

        public bool IsApplying { get; set; }

        public void Dispose()
        {
            foreach (var subscription in Subscriptions)
            {
                subscription.Dispose();
            }

            Subscriptions.Clear();
            WatchedProperties.Clear();
        }
    }

    private sealed class PropertyObserver<T>(Action<T> onNext) : IObserver<T>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(T value)
        {
            onNext(value);
        }
    }
}
