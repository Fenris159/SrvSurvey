using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using SrvSurvey.Desktop.Behaviors;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class ComboBoxFitContentBehaviorTests
{
    [AvaloniaFact]
    public void EnabledComboBoxSizesToLongestItemInsteadOfSelectedItem()
    {
        var comboBox = new ComboBox
        {
            ItemsSource = new[] { "A", "Medium label", "ZZZZZZZZZZZZZZZZZZZZZZZZZZ" },
            SelectedIndex = 0,
        };
        ComboBoxFitContentBehavior.SetEnabled(comboBox, true);

        using Host host = Show(comboBox);

        Assert.True(ComboBoxFitContentBehavior.GetEnabled(comboBox));
        Assert.True(comboBox.MinWidth > MeasureTextWidth("A") + 48);
        Assert.True(comboBox.MinWidth >= MeasureTextWidth("ZZZZZZZZZZZZZZZZZZZZZZZZZZ") + 48);
    }

    [AvaloniaFact]
    public void ItemTemplateIsUsedWhenMeasuringContentWidth()
    {
        var comboBox = new ComboBox
        {
            ItemsSource = new[] { "short", "also-short" },
            SelectedIndex = 0,
            ItemTemplate = new FuncDataTemplate<string>(
                (value, _) =>
                    new TextBlock
                    {
                        Text = $"Prefix · {value} · with a much longer rendered label",
                        TextWrapping = TextWrapping.NoWrap,
                        TextTrimming = TextTrimming.None,
                    }
            ),
        };
        ComboBoxFitContentBehavior.SetEnabled(comboBox, true);

        using Host host = Show(comboBox);

        Assert.True(
            comboBox.MinWidth > MeasureTextWidth("Prefix · also-short · with a much longer rendered label") + 40
        );
    }

    [AvaloniaFact]
    public void ItemsSourceChangesRecalculateMinimumWidth()
    {
        var items = new ObservableCollection<string> { "tiny" };
        var comboBox = new ComboBox { ItemsSource = items, SelectedIndex = 0 };
        ComboBoxFitContentBehavior.SetEnabled(comboBox, true);

        using Host host = Show(comboBox);
        double initialWidth = comboBox.MinWidth;

        items.Add("a substantially longer colonization build option label");
        Assert.NotNull(host.Window.CaptureRenderedFrame());

        Assert.True(comboBox.MinWidth > initialWidth);
    }

    [AvaloniaFact]
    public void DisablingDetachesAndLeavesControlReusable()
    {
        var comboBox = new ComboBox { ItemsSource = new[] { "Orbital", "Surface" }, SelectedIndex = 0 };
        ComboBoxFitContentBehavior.SetEnabled(comboBox, true);

        using Host host = Show(comboBox);
        Assert.True(comboBox.MinWidth > 0);

        ComboBoxFitContentBehavior.SetEnabled(comboBox, false);
        Assert.False(ComboBoxFitContentBehavior.GetEnabled(comboBox));

        comboBox.ItemsSource = new[] { "Plan", "Build", "Complete", "Demolish" };
        ComboBoxFitContentBehavior.SetEnabled(comboBox, true);
        Assert.NotNull(host.Window.CaptureRenderedFrame());

        Assert.True(comboBox.MinWidth >= MeasureTextWidth("Demolish") + 48);
    }

    [AvaloniaFact]
    public void EmptyItemsLeaveMinimumWidthUnset()
    {
        var comboBox = new ComboBox { ItemsSource = Array.Empty<string>() };
        double originalMinWidth = comboBox.MinWidth;
        ComboBoxFitContentBehavior.SetEnabled(comboBox, true);

        using Host host = Show(comboBox);

        Assert.Equal(originalMinWidth, comboBox.MinWidth);
    }

    private static double MeasureTextWidth(string text)
    {
        var block = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.None,
        };
        block.Measure(Size.Infinity);
        return block.DesiredSize.Width;
    }

    private static Host Show(Control content)
    {
        var window = new Window
        {
            Width = 480,
            Height = 240,
            Content = content,
        };
        window.Show();
        Assert.NotNull(window.CaptureRenderedFrame());
        return new Host(window);
    }

    private sealed class Host(Window window) : IDisposable
    {
        public Window Window { get; } = window;

        public void Dispose()
        {
            Window.Close();
        }
    }
}
