using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SrvSurvey.Desktop.Behaviors;

public static class ListBoxBringIntoViewBehavior
{
    private static readonly ConditionalWeakTable<ListBox, Subscription>
        Subscriptions = new();

    public static readonly AttachedProperty<bool> ContainProperty =
        AvaloniaProperty.RegisterAttached<ListBox, ListBox, bool>(
            "Contain",
            defaultValue: false);

    static ListBoxBringIntoViewBehavior()
    {
        ContainProperty.Changed.AddClassHandler<ListBox>(OnContainChanged);
    }

    public static void SetContain(ListBox target, bool value)
    {
        target.SetValue(ContainProperty, value);
    }

    public static bool GetContain(ListBox target)
    {
        return target.GetValue(ContainProperty);
    }

    private static void OnContainChanged(
        ListBox listBox,
        AvaloniaPropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.NewValue is true)
        {
            Subscriptions.GetValue(
                listBox,
                static target => new Subscription(target));
            return;
        }

        if (Subscriptions.TryGetValue(listBox, out var subscription))
        {
            subscription.Dispose();
            Subscriptions.Remove(listBox);
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly ListBox listBox;

        public Subscription(ListBox listBox)
        {
            this.listBox = listBox;
            listBox.AddHandler(
                Control.RequestBringIntoViewEvent,
                OnRequestBringIntoView,
                RoutingStrategies.Bubble);
        }

        public void Dispose()
        {
            listBox.RemoveHandler(
                Control.RequestBringIntoViewEvent,
                OnRequestBringIntoView);
        }

        private static void OnRequestBringIntoView(
            object? sender,
            RequestBringIntoViewEventArgs eventArgs)
        {
            eventArgs.Handled = true;
        }
    }
}
