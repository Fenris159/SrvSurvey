using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class ColonizationCommodityOverlayPresentationTests
{
    /// <summary>Routes column labels through Title scaling while leaving project headers and numeric values independent.</summary>
    [AvaloniaFact]
    public void ColumnLabelsUseTitleScalingInsteadOfCaptionScaling()
    {
        var viewModel = new ColonizationCommodityOverlayViewModel();
        viewModel.Apply(CreatePlan("Test project"), null);
        var presentation = new ColonizationCommodityOverlayPresentation { DataContext = viewModel };
        var window = new Window { Content = presentation, SizeToContent = SizeToContent.WidthAndHeight };
        try
        {
            OverlayThemeResources.Apply(window);
            window.Show();
            window.UpdateLayout();
            TextBlock header = presentation.FindControl<TextBlock>("ProjectHeaderText")!;
            Grid columns = presentation.FindControl<Grid>("CommodityColumnHeaders")!;
            TextBlock[] labels = columns.Children.OfType<TextBlock>().ToArray();
            Assert.Equal(4, labels.Length);
            double labelFontSize = labels[0].FontSize;
            double headerFontSize = header.FontSize;
            Border row = Assert.Single(
                presentation.GetVisualDescendants().OfType<Border>(),
                border => border.Classes.Contains("commodity-row")
            );
            TextBlock need = Assert.Single(
                Assert.IsType<Grid>(row.Child).Children.OfType<TextBlock>(),
                cell => Grid.GetColumn(cell) == 1
            );
            double valueFontSize = need.FontSize;

            OverlayTypographyResources.Apply(presentation, new(0, 0, 0, 0, 0, 100));
            Assert.All(labels, label => Assert.Equal(labelFontSize, label.FontSize));
            OverlayTypographyResources.Apply(presentation, new(50, 100, 0, 0, 0, 0));
            Assert.All(labels, label => Assert.Equal(labelFontSize * 2, label.FontSize));
            Assert.Equal(headerFontSize * 1.5, header.FontSize);
            Assert.Equal(valueFontSize, need.FontSize);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Renders a split project title and wraps long names vertically without widening the content-sized panel.</summary>
    [AvaloniaFact]
    public void ProjectHeaderWrapsWithinCommodityWidth()
    {
        const string title = "BLEIA EOHN SX-F C12 - REFINERY HUB (FC_LOADING)";
        var viewModel = new ColonizationCommodityOverlayViewModel();
        viewModel.Apply(CreatePlan(title), null);
        var presentation = new ColonizationCommodityOverlayPresentation { DataContext = viewModel };
        var window = new Window { Content = presentation, SizeToContent = SizeToContent.WidthAndHeight };
        try
        {
            OverlayThemeResources.Apply(window);
            window.Show();
            window.UpdateLayout();
            TextBlock header = presentation.FindControl<TextBlock>("ProjectHeaderText")!;
            Grid columns = presentation.FindControl<Grid>("CommodityColumnHeaders")!;
            Assert.Equal("BLEIA EOHN SX-F C12 -\nREFINERY HUB (FC_LOADING)", header.Text);
            Assert.Equal(TextWrapping.Wrap, header.TextWrapping);
            Assert.Equal(2, header.TextLayout.TextLines.Count);
            Assert.True(header.Bounds.Width <= columns.Bounds.Width);
            double width = presentation.Bounds.Width;
            double height = header.Bounds.Height;

            string longTitle = "BLEIA EOHN SX-F C12 - " + string.Join(" ", Enumerable.Repeat("REFINERY HUB", 12));
            viewModel.Apply(CreatePlan(longTitle), null);
            window.UpdateLayout();
            Assert.True(presentation.Bounds.Width <= width + 0.1);
            Assert.True(header.Bounds.Height > height);
            Assert.True(header.Bounds.Width <= columns.Bounds.Width);

            viewModel.Apply(CreatePlan("2 projects") with { ProjectNames = [longTitle, longTitle] }, null);
            window.UpdateLayout();
            Assert.True(presentation.Bounds.Width <= width + 0.1);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Supplies a single commodity so header layout can be measured independently of project-name length.</summary>
    private static ColonizationCommodityPlan CreatePlan(string title) =>
        new()
        {
            Title = title,
            ProjectNames = [title],
            FleetCarriers = [],
            Rows =
            [
                new()
                {
                    Commodity = "steel",
                    DisplayName = "Steel",
                    Category = "Metals",
                    Needed = 100,
                    OnFleetCarriers = 80,
                    InShip = 20,
                },
            ],
        };
}
