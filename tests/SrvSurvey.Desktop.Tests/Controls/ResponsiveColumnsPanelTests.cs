using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using SrvSurvey.Desktop.Controls;

namespace SrvSurvey.Desktop.Tests.Controls;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class ResponsiveColumnsPanelTests
{
    [AvaloniaTheory]
    [InlineData(500, 1)]
    [InlineData(800, 2)]
    [InlineData(1200, 3)]
    public void SectionsWrapAndTheLastRowUsesTheAvailableWidth(double width, int columns)
    {
        var panel = new ResponsiveColumnsPanel
        {
            MinimumColumnWidth = 300,
            MaximumColumns = 3,
            Spacing = 20,
        };
        for (int index = 0; index < 4; index++)
        {
            panel.Children.Add(new Border { Height = 30 + index * 10, VerticalAlignment = VerticalAlignment.Top });
        }
        panel.Measure(new Size(width, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, width, panel.DesiredSize.Height));
        Assert.Equal(0, panel.Children[0].Bounds.Y);
        Assert.Equal(columns == 1 ? 50 : 0, panel.Children[1].Bounds.Y);
        Assert.True(panel.Children[^1].Bounds.Bottom <= panel.Bounds.Height);
        Assert.Equal(width, panel.Children[^1].Bounds.Right, 3);
        if (columns == 1)
        {
            Assert.Equal(240, panel.DesiredSize.Height);
        }
    }

    [AvaloniaFact]
    public void HiddenFieldsDoNotReserveAColumnAndVisibilityChangesReflowTheRow()
    {
        var panel = new ResponsiveColumnsPanel { MaximumColumns = 4, MinimumColumnWidth = 100 };
        var first = new Border { Height = 20 };
        var optional = new Border { Height = 20, IsVisible = false };
        panel.Children.Add(first);
        panel.Children.Add(optional);
        panel.Measure(new Size(600, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, 600, panel.DesiredSize.Height));
        Assert.Equal(600, first.Bounds.Width);
        optional.IsVisible = true;
        panel.Measure(new Size(600, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, 600, panel.DesiredSize.Height));
        Assert.Equal(290, first.Bounds.Width);
        Assert.Equal(310, optional.Bounds.X);
        panel.MaximumColumns = 1;
        panel.Measure(new Size(600, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, 600, panel.DesiredSize.Height));
        Assert.Equal(40, optional.Bounds.Y);
    }

    [AvaloniaFact]
    public void EmptyAndUnboundedLayoutsHaveFiniteNaturalSizes()
    {
        var panel = new ResponsiveColumnsPanel();
        panel.Measure(Size.Infinity);
        panel.Arrange(new Rect(0, 0, 0, 0));
        Assert.Equal(default, panel.DesiredSize);
        panel.Children.Add(new Border { Width = 120, Height = 30 });
        panel.Children.Add(new Border { Width = 100, Height = 20 });
        panel.Measure(Size.Infinity);
        Assert.Equal(new Size(240, 30), panel.DesiredSize);
        Assert.Throws<ArgumentException>(() => panel.MaximumColumns = 0);
        Assert.Throws<ArgumentException>(() => panel.MinimumColumnWidth = 0);
        Assert.Throws<ArgumentException>(() => panel.MinimumColumnWidth = double.NaN);
        Assert.Throws<ArgumentException>(() => panel.Spacing = -1);
        Assert.Throws<ArgumentException>(() => panel.Spacing = double.PositiveInfinity);
    }
}
