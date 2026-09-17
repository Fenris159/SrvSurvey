using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace SrvSurvey.Desktop.Behaviors;

/// <summary>
/// Sizes a <see cref="ComboBox"/> so its closed width and dropdown can show the
/// longest item without clipping, instead of only measuring the selected value.
/// </summary>
public static class ComboBoxFitContentBehavior
{
    private const double DropdownChromeWidth = 48;

    private static readonly ConditionalWeakTable<ComboBox, Subscription> Subscriptions = [];

    public static readonly AttachedProperty<bool> EnabledProperty = AvaloniaProperty.RegisterAttached<
        ComboBox,
        ComboBox,
        bool
    >("Enabled", defaultValue: false);

    static ComboBoxFitContentBehavior()
    {
        EnabledProperty.Changed.AddClassHandler<ComboBox>(OnEnabledChanged);
    }

    public static void SetEnabled(ComboBox target, bool value)
    {
        target.SetValue(EnabledProperty, value);
    }

    public static bool GetEnabled(ComboBox target)
    {
        return target.GetValue(EnabledProperty);
    }

    private static void OnEnabledChanged(ComboBox comboBox, AvaloniaPropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.GetNewValue<bool>())
        {
            if (Subscriptions.TryGetValue(comboBox, out _))
            {
                UpdateMinWidth(comboBox);
                return;
            }

            var subscription = new Subscription(comboBox);
            Subscriptions.Add(comboBox, subscription);
            subscription.Attach();
            UpdateMinWidth(comboBox);
            return;
        }

        if (Subscriptions.TryGetValue(comboBox, out Subscription? existing))
        {
            existing.Detach();
            Subscriptions.Remove(comboBox);
        }
    }

    private static void UpdateMinWidth(ComboBox comboBox)
    {
        if (!comboBox.IsAttachedToVisualTree())
        {
            return;
        }

        double maxContentWidth = 0;
        IDataTemplate? template = comboBox.ItemTemplate ?? comboBox.SelectionBoxItemTemplate;

        foreach (object? item in comboBox.Items)
        {
            Control visual = CreateMeasurementVisual(item, template);
            visual.Measure(Size.Infinity);
            maxContentWidth = Math.Max(maxContentWidth, visual.DesiredSize.Width);
        }

        if (maxContentWidth <= 0)
        {
            return;
        }

        comboBox.MinWidth = Math.Ceiling(maxContentWidth + DropdownChromeWidth);
    }

    private static Control CreateMeasurementVisual(object? item, IDataTemplate? template)
    {
        if (template?.Build(item) is Control built)
        {
            return built;
        }

        return new TextBlock
        {
            Text = item?.ToString() ?? string.Empty,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.None,
        };
    }

    private sealed class Subscription(ComboBox comboBox)
    {
        private INotifyCollectionChanged? _collection;

        public void Attach()
        {
            comboBox.AttachedToVisualTree += OnAttachedToVisualTree;
            comboBox.PropertyChanged += OnPropertyChanged;
            HookCollection(comboBox.Items);
        }

        public void Detach()
        {
            comboBox.AttachedToVisualTree -= OnAttachedToVisualTree;
            comboBox.PropertyChanged -= OnPropertyChanged;
            UnhookCollection();
        }

        private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            UpdateMinWidth(comboBox);
        }

        private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (
                e.Property == ItemsControl.ItemsSourceProperty
                || e.Property == ItemsControl.ItemTemplateProperty
                || e.Property == ComboBox.SelectionBoxItemTemplateProperty
                || e.Property == ComboBox.SelectedItemProperty
            )
            {
                HookCollection(comboBox.Items);
                UpdateMinWidth(comboBox);
            }
        }

        private void HookCollection(ItemCollection items)
        {
            UnhookCollection();
            if (items is INotifyCollectionChanged notify)
            {
                _collection = notify;
                _collection.CollectionChanged += OnCollectionChanged;
            }
        }

        private void UnhookCollection()
        {
            if (_collection is null)
            {
                return;
            }

            _collection.CollectionChanged -= OnCollectionChanged;
            _collection = null;
        }

        private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateMinWidth(comboBox);
        }
    }
}
