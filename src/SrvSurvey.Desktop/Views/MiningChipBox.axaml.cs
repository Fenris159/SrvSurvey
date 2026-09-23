using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Views;

public sealed partial class MiningChipBox : UserControl
{
    private MiningChipBoxViewModel? subscribed;
    private TopLevel? openTopLevel;

    public MiningChipBox() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        DetachOutsidePointerHandler();
        openTopLevel = TopLevel.GetTopLevel(this);
        openTopLevel?.AddHandler(PointerPressedEvent, CloseWhenPressedOutside, RoutingStrategies.Tunnel, true);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DetachOutsidePointerHandler();
        base.OnDetachedFromVisualTree(e);
    }

    private void DetachOutsidePointerHandler()
    {
        openTopLevel?.RemoveHandler(PointerPressedEvent, CloseWhenPressedOutside);
        openTopLevel = null;
    }

    private void CloseWhenPressedOutside(object? sender, PointerPressedEventArgs e)
    {
        if (Model is not { Open: true })
        {
            return;
        }

        if (e.Source is Visual source && (ReferenceEquals(source, this) || this.IsVisualAncestorOf(source)))
        {
            return;
        }

        Model.Open = false;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        subscribed?.PropertyChanged -= Model_PropertyChanged;
        subscribed = Model;
        subscribed?.PropertyChanged += Model_PropertyChanged;

        RebuildChips();
    }

    private void Model_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MiningChipBoxViewModel.Tags))
        {
            RebuildChips();
        }
    }

    private void RebuildChips()
    {
        TextBox input = QueryInput;
        FieldWrap.Children.Clear();
        if (Model is { } model)
        {
            foreach (MiningChipTag tag in model.Tags)
            {
                FieldWrap.Children.Add(CreateChip(tag));
            }
        }

        FieldWrap.Children.Add(input);
    }

    private Border CreateChip(MiningChipTag tag)
    {
        var label = new TextBlock { Text = tag.Label, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        label[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("RavenAccentBrush");
        var remove = new Button
        {
            Content = "×",
            Tag = tag.Name,
            Classes = { "chip-remove" },
        };
        remove.Click += Remove_Click;
        var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };
        row.Children.Add(label);
        row.Children.Add(remove);
        var chip = new Border
        {
            Child = row,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 2, 6, 2),
            Padding = new Thickness(8, 2, 8, 2),
        };
        chip[!Border.BackgroundProperty] = new DynamicResourceExtension("RavenAccentMutedBrush");
        chip[!Border.BorderBrushProperty] = new DynamicResourceExtension("RavenAccentBrush");
        ToolTip.SetTip(chip, tag.Name);
        return chip;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double cap = availableSize.Width;
        if (!double.IsFinite(cap) || cap > 520)
        {
            cap = 520;
        }

        Size desired = base.MeasureOverride(new Size(Math.Max(0, cap), availableSize.Height));
        double width = double.IsFinite(availableSize.Width)
            ? Math.Min(cap, availableSize.Width)
            : Math.Min(cap, desired.Width);
        return new Size(Math.Max(0, width), desired.Height);
    }

    private MiningChipBoxViewModel? Model => DataContext as MiningChipBoxViewModel;

    private void Query_GotFocus(object? sender, RoutedEventArgs e)
    {
        if (Model is { } model)
        {
            model.Open = true;
        }
    }

    private void Add_Click(object? sender, RoutedEventArgs e)
    {
        if (Model is { } model && sender is Button { Tag: string value })
        {
            model.Add(value);
        }
    }

    private void Remove_Click(object? sender, RoutedEventArgs e)
    {
        if (Model is { } model && sender is Button { Tag: string value })
        {
            model.Remove(value);
        }
    }

    private void Field_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Button)
        {
            return;
        }

        QueryInput.Focus();
        if (Model is { } model)
        {
            model.Open = true;
        }
    }

    private void Box_LostFocus(object? sender, RoutedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsKeyboardFocusWithin && Model is { } model)
            {
                model.Open = false;
            }
        });
    }
}
