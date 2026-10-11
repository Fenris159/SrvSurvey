using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class ColonizationCommodityOverlayPresentationTests
{
    /// <summary>Keeps FC and HAVE delta labels on the same baseline as the other column titles at every Title scale.</summary>
    [AvaloniaTheory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 100)]
    [InlineData(true, 100)]
    public void DeltaHeadersShareTheOtherColumnBaselines(bool combined, int titleScale)
    {
        var viewModel = new ColonizationCommodityOverlayViewModel();
        viewModel.Apply(CreatePlan("Test project"), null);
        viewModel.ApplyPreferences(
            ColonizationOverlayPreferences.Default with
            {
                ShowFleetCarrierDelta = true,
                InlineFleetCarrierCargo = combined,
            }
        );
        var presentation = new ColonizationCommodityOverlayPresentation { DataContext = viewModel };
        var window = new Window { Content = presentation, SizeToContent = SizeToContent.WidthAndHeight };
        try
        {
            OverlayThemeResources.Apply(window);
            window.Show();
            OverlayTypographyResources.Apply(presentation, new(0, titleScale, 0, 0, 0, 0));
            window.UpdateLayout();
            Grid columns = presentation.FindControl<Grid>("CommodityColumnHeaders")!;
            TextBlock[] labels = columns
                .GetVisualDescendants()
                .OfType<TextBlock>()
                .Where(label => label.Classes.Contains("commodity-column-label") && label.IsEffectivelyVisible)
                .ToArray();
            Assert.Equal(combined ? 3 : 4, labels.Length);
            double baseline = labels[0]
                .TranslatePoint(new Point(0, labels[0].TextLayout.TextLines[0].Baseline), columns)!
                .Value.Y;
            Assert.All(
                labels,
                label =>
                    Assert.Equal(
                        baseline,
                        label.TranslatePoint(new Point(0, label.TextLayout.TextLines[0].Baseline), columns)!.Value.Y,
                        3
                    )
            );
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Measures identical commodity rows with and without the completed checkmark, including independent icon scaling.</summary>
    [AvaloniaTheory]
    [InlineData(0, 0)]
    [InlineData(100, 0)]
    [InlineData(0, 100)]
    [InlineData(-50, 100)]
    public void CompletionCheckmarkDoesNotIncreaseRowHeight(int bodyScale, int iconScale)
    {
        ColonizationCommodityPlan plan = CreatePlan("Test project");
        var viewModel = new ColonizationCommodityOverlayViewModel();
        viewModel.Apply(
            plan with
            {
                Rows =
                [
                    plan.Rows[0] with
                    {
                        OnFleetCarriers = 100,
                        InShip = 0,
                    },
                    plan.Rows[0] with
                    {
                        OnFleetCarriers = 80,
                        InShip = 0,
                    },
                ],
            },
            null
        );
        var presentation = new ColonizationCommodityOverlayPresentation { DataContext = viewModel };
        var window = new Window { Content = presentation, SizeToContent = SizeToContent.WidthAndHeight };
        try
        {
            OverlayThemeResources.Apply(window);
            window.Show();
            OverlayTypographyResources.Apply(presentation, new(0, 0, 0, bodyScale, 0, 0, iconScale));
            window.UpdateLayout();
            Border[] rows = presentation
                .GetVisualDescendants()
                .OfType<Border>()
                .Where(border => border.Classes.Contains("commodity-row"))
                .ToArray();
            Assert.Equal(2, rows.Length);
            Assert.Equal(rows[1].Bounds.Height, rows[0].Bounds.Height, 3);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Releases the width of hidden cargo columns and restores it when those columns are shown again.</summary>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void HiddenCargoColumnsReleaseTheirPanelWidth(bool delta, bool warning)
    {
        var viewModel = new ColonizationCommodityOverlayViewModel();
        viewModel.Apply(CreatePlan("Build") with { IsDockedAtUntrackedFleetCarrier = warning }, null);
        ColonizationOverlayPreferences preferences = ColonizationOverlayPreferences.Default with
        {
            ShowFleetCarrierDelta = delta,
        };
        viewModel.ApplyPreferences(preferences);
        var window = new ColonizationCommodityOverlayWindow(viewModel);
        ColonizationCommodityOverlayPresentation presentation = Assert.IsType<ColonizationCommodityOverlayPresentation>(
            Assert.IsType<Border>(window.Content).Child
        );
        try
        {
            OverlayThemeResources.Apply(window);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            double fullWidth = presentation.Bounds.Width;
            double fullWindowWidth = window.Bounds.Width;
            Grid columns = presentation.FindControl<Grid>("CommodityColumnHeaders")!;
            viewModel.ApplyPreferences(preferences with { InlineFleetCarrierCargo = true });
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Equal(0, columns.ColumnDefinitions[3].ActualWidth);
            Assert.True(
                presentation.Bounds.Width < fullWidth,
                $"Combined width {presentation.Bounds.Width} should be less than separate width {fullWidth}."
            );
            Assert.True(window.Bounds.Width < fullWindowWidth);
            viewModel.ApplyPreferences(preferences with { ShowFleetCarrierCargo = false });
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Equal(0, columns.ColumnDefinitions[2].ActualWidth);
            Assert.True(presentation.Bounds.Width < fullWidth);
            Assert.True(window.Bounds.Width < fullWindowWidth);
            viewModel.ApplyPreferences(preferences);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Equal(fullWidth, presentation.Bounds.Width, 3);
            Assert.Equal(fullWindowWidth, window.Bounds.Width, 3);
        }
        finally
        {
            window.Close();
        }
    }

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
            TextBlock[] labels = columns
                .GetVisualDescendants()
                .OfType<TextBlock>()
                .Where(label => label.Classes.Contains("commodity-column-label"))
                .ToArray();
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
