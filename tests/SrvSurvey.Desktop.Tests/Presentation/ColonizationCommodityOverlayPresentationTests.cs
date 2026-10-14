using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
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
    /// <summary>Large sums and signed deltas grow shared columns while preserving readable gaps in both hosts.</summary>
    [AvaloniaTheory]
    [InlineData(10164, 0, true, false, 0)]
    [InlineData(123456, 23456, false, false, 0)]
    [InlineData(123456, 23456, true, false, 100)]
    [InlineData(1234567, 2000000, true, false, 0)]
    [InlineData(123456, 23456, false, true, 0)]
    [InlineData(123456, 23456, true, true, 100)]
    public void LargeNumericValuesKeepReadableColumnGaps(
        int needed,
        int carrier,
        bool delta,
        bool combined,
        int valueScale
    )
    {
        const string plotter = "PlotBuildCommodities";
        var preview = new OverlayPositionPreviewWindow(OverlayLayoutCatalog.GetRequired(plotter));
        var model = (ColonizationCommodityOverlayViewModel)preview.RuntimePresentation!.DataContext!;
        ColonizationOverlayPreferences preferences = ColonizationOverlayPreferences.Default with
        {
            ShowFleetCarrierDelta = delta,
            InlineFleetCarrierCargo = combined,
            CollapseCoveredGroups = false,
            HideRowBadges = true,
        };
        preview.ConfigureColonizationPreferences(preferences);
        ColonizationCommodityPlan plan = CreatePlan("Large cargo requirements");
        model.Apply(
            plan with
            {
                Rows = [plan.Rows[0] with { Needed = needed, OnFleetCarriers = carrier, InShip = 1173 }],
            },
            null
        );
        OverlayTypographyScale typography = OverlayTypographyScale.Default.WithPercent(
            OverlayTypographyRole.Value,
            valueScale
        );
        int panelScale = OverlayScaleCatalog.GetIndex(50);
        var layout = new LegacyOverlayLayout(
            new Dictionary<string, LegacyOverlayPlacement>
            {
                [plotter] = OverlayLayoutCatalog.GetRequired(plotter).DefaultPlacement with
                {
                    TypographyScale = typography,
                    ScaleIndex = panelScale,
                },
            },
            null,
            null
        );
        var live = new ColonizationCommodityOverlayWindow(model);
        try
        {
            preview.SetRenderScaling(1.5);
            live.SetRenderScaling(1.5);
            preview.ConfigureScale(layout.ScaleIndex, panelScale, 1.5);
            preview.ConfigureTypography(typography);
            preview.ConfigureSize(null);
            OverlayThemeResources.Apply(preview);
            OverlayThemeResources.Apply(live, layout, plotter, new OverlayWindowRegistry());
            preview.Show();
            live.Show();
            Dispatcher.UIThread.RunJobs();
            preview.UpdateLayout();
            live.UpdateLayout();
            foreach (
                Control presentation in new Control[]
                {
                    preview.RuntimePresentation,
                    live.GetVisualDescendants().OfType<ColonizationCommodityOverlayPresentation>().Single(),
                }
            )
            {
                Border row = Assert.Single(
                    presentation.GetVisualDescendants().OfType<Border>(),
                    border => border.Classes.Contains("commodity-row")
                );
                Grid grid = Assert.IsType<Grid>(row.Child);
                TextBlock[] cells = grid
                    .Children.OfType<TextBlock>()
                    .Where(text => text.IsEffectivelyVisible && Grid.GetColumn(text) > 0)
                    .OrderBy(Grid.GetColumn)
                    .ToArray();
                Assert.Equal(combined ? 2 : 3, cells.Length);
                Assert.All(
                    cells,
                    cell => Assert.True(cell.TextLayout.WidthIncludingTrailingWhitespace <= cell.Bounds.Width + 0.01)
                );
                Rect[] rectangles = cells
                    .Select(cell => new Rect(cell.Bounds.Size).TransformToAABB(cell.TransformToVisual(grid)!.Value))
                    .ToArray();
                foreach (double gap in rectangles.Zip(rectangles.Skip(1), (left, right) => right.Left - left.Right))
                {
                    Assert.InRange(gap, 7.99, double.PositiveInfinity);
                }
                Assert.All(
                    cells,
                    cell =>
                    {
                        Rect bounds = new Rect(cell.Bounds.Size).TransformToAABB(
                            cell.TransformToVisual(presentation)!.Value
                        );
                        Assert.InRange(bounds.Left, 0, presentation.Bounds.Width);
                        Assert.True(bounds.Right <= presentation.Bounds.Width + 1);
                    }
                );
                Grid headers = Assert.Single(
                    presentation.GetVisualDescendants().OfType<Grid>(),
                    item => item.Name == "CommodityColumnHeaders"
                );
                Assert.Equal(
                    headers.ColumnDefinitions.Select(column => column.ActualWidth),
                    grid.ColumnDefinitions.Select(column => column.ActualWidth)
                );
                if (combined)
                {
                    Assert.Equal(0, grid.ColumnDefinitions[3].ActualWidth);
                }
            }
        }
        finally
        {
            live.Close();
            preview.Close();
        }
    }

    /// <summary>Short live commodity lists retain the editor's designed column footprint at each body font scale.</summary>
    [AvaloniaTheory]
    [InlineData(0, false)]
    [InlineData(100, false)]
    [InlineData(0, true)]
    public void ShortListsKeepTheEditorsCommodityColumnLayout(int bodyScale, bool combinedCargo)
    {
        OverlayLayoutDefinition definition = OverlayLayoutCatalog.GetRequired("PlotBuildCommodities");
        var preview = new OverlayPositionPreviewWindow(definition);
        var model = new ColonizationCommodityOverlayViewModel();
        ColonizationOverlayPreferences preferences = ColonizationOverlayPreferences.Default with
        {
            HideRowBadges = true,
            InlineFleetCarrierCargo = combinedCargo,
        };
        model.ApplyPreferences(preferences);
        model.Apply(CreatePlan("IMPROBABLY COLD A [III42] (NOVA)"), null);
        var live = new ColonizationCommodityOverlayWindow(model);
        OverlayTypographyScale typography = OverlayTypographyScale.Default.WithPercent(
            OverlayTypographyRole.Body,
            bodyScale
        );
        var layout = new LegacyOverlayLayout(
            new Dictionary<string, LegacyOverlayPlacement>
            {
                [definition.Name] = definition.DefaultPlacement with { TypographyScale = typography },
            },
            null,
            null
        );
        try
        {
            preview.ConfigureColonizationPreferences(preferences);
            preview.ConfigureTypography(typography);
            preview.ConfigureSize(null);
            OverlayThemeResources.Apply(preview);
            OverlayThemeResources.Apply(live, layout, definition.Name, new OverlayWindowRegistry());
            live.Show();
            preview.Show();
            Dispatcher.UIThread.RunJobs();
            live.UpdateLayout();
            preview.UpdateLayout();
            Grid actual = Assert.Single(
                live.GetVisualDescendants().OfType<Grid>(),
                grid => grid.Name == "CommodityColumnHeaders"
            );
            Grid expected = Assert.Single(
                preview.GetVisualDescendants().OfType<Grid>(),
                grid => grid.Name == "CommodityColumnHeaders"
            );
            Assert.InRange(
                Math.Abs(actual.ColumnDefinitions[0].ActualWidth - expected.ColumnDefinitions[0].ActualWidth),
                0,
                1
            );
        }
        finally
        {
            live.Close();
            preview.Close();
        }
    }

    /// <summary>Removes badge space from the live panel and restores it without hiding completion or loading highlights.</summary>
    [AvaloniaFact]
    public void HidingRowBadgesUpdatesPresentationAndReleasesTheirWidth()
    {
        ColonizationCommodityPlan plan = CreatePlan("Test project");
        var viewModel = new ColonizationCommodityOverlayViewModel();
        ColonizationOverlayPreferences preferences = ColonizationOverlayPreferences.Default with
        {
            HighlightAlmostCoveredFleetCarrierLoads = true,
            InlineFleetCarrierCargo = true,
        };
        viewModel.ApplyPreferences(preferences);
        viewModel.Apply(
            plan with
            {
                Rows =
                [
                    plan.Rows[0] with
                    {
                        DisplayName = "Ceramic composites",
                        IsAvailableAtCurrentMarket = true,
                        IsAssignedToCommander = true,
                        CanCompleteFleetCarrierLoad = true,
                    },
                ],
            },
            null
        );
        var window = new ColonizationCommodityOverlayWindow(viewModel);
        try
        {
            OverlayThemeResources.Apply(window);
            window.Show();
            window.UpdateLayout();
            double badgeWidth = window.Bounds.Width;
            Assert.Contains(VisibleTexts(), text => text.Text == "LOAD");
            Assert.Contains(VisibleTexts(), text => text.Text == "PIN");

            viewModel.ApplyPreferences(preferences with { HideRowBadges = true });
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.DoesNotContain(VisibleTexts(), text => text.Text is "LOAD" or "PIN");
            Assert.Contains(VisibleTexts(), text => text.Text == "✓");
            Assert.Contains(
                window.GetVisualDescendants().OfType<Border>(),
                row => row.Classes.Contains("carrier-load")
            );
            Assert.True(window.Bounds.Width < badgeWidth);

            viewModel.ApplyPreferences(preferences);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Contains(VisibleTexts(), text => text.Text == "LOAD");
            Assert.Contains(VisibleTexts(), text => text.Text == "PIN");
            Assert.Equal(badgeWidth, window.Bounds.Width, 3);
        }
        finally
        {
            window.Close();
        }

        IEnumerable<TextBlock> VisibleTexts() =>
            window.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible);
    }

    /// <summary>Scales larger completion glyphs with Icons and market guidance with Detail without changing row text or height.</summary>
    [AvaloniaTheory]
    [InlineData(80, false, "MARKET")]
    [InlineData(80, true, "LOAD")]
    [InlineData(100, false, "READY")]
    public void CompletionCheckmarkAndRowBadgesUseIndependentScaling(int carrierCargo, bool canLoad, string badgeText)
    {
        ColonizationCommodityPlan plan = CreatePlan("Test project");
        var viewModel = new ColonizationCommodityOverlayViewModel();
        viewModel.ApplyPreferences(
            ColonizationOverlayPreferences.Default with
            {
                HighlightAlmostCoveredFleetCarrierLoads = true,
                InlineFleetCarrierCargo = true,
            }
        );
        viewModel.Apply(
            plan with
            {
                Rows =
                [
                    plan.Rows[0] with
                    {
                        OnFleetCarriers = carrierCargo,
                        CanCompleteFleetCarrierLoad = canLoad,
                        IsAvailableAtCurrentMarket = true,
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
            window.UpdateLayout();
            TextBlock checkmark = Assert.Single(
                presentation.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "✓"
            );
            Viewbox checkmarkBox = Assert.Single(checkmark.GetVisualAncestors().OfType<Viewbox>());
            TextBlock badge = Assert.Single(
                presentation.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == badgeText
            );
            TextBlock commodity = Assert.Single(
                presentation.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Name == "CommodityNameText"
            );
            double checkmarkHeight = checkmarkBox.Height;
            double badgeSize = badge.FontSize;
            double commoditySize = commodity.FontSize;
            Border row = Assert.Single(
                presentation.GetVisualDescendants().OfType<Border>(),
                border => border.Classes.Contains("commodity-row")
            );
            double rowHeight = row.Bounds.Height;
            Assert.Equal(16.5, checkmarkHeight, 3);

            OverlayTypographyResources.Apply(presentation, new(0, 0, 0, 0, 0, 0, 100));
            window.UpdateLayout();
            Assert.Equal(badgeSize, badge.FontSize);
            Assert.Equal(checkmarkHeight * 2, checkmarkBox.Height, 3);
            Assert.InRange(checkmarkBox.Bounds.Height, checkmarkBox.Height - 1, checkmarkBox.Height + 1);
            Assert.Equal(commoditySize, commodity.FontSize);
            Assert.Equal(rowHeight, row.Bounds.Height, 3);

            OverlayTypographyResources.Apply(presentation, new(0, 0, 0, 0, 100, 0));
            window.UpdateLayout();
            Assert.Equal(badgeSize * 2, badge.FontSize);
            Assert.Equal(checkmarkHeight, checkmarkBox.Height, 3);
            Assert.Equal(commoditySize, commodity.FontSize);
            Assert.Equal(rowHeight, row.Bounds.Height, 3);

            OverlayTypographyResources.Apply(presentation, new(0, 0, 0, 0, 0, 100));
            window.UpdateLayout();
            Assert.Equal(badgeSize, badge.FontSize);
            Assert.Equal(checkmarkHeight, checkmarkBox.Height, 3);
        }
        finally
        {
            window.Close();
        }
    }

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

    /// <summary>Keeps row height driven by text while completion glyphs and visible badges scale independently.</summary>
    [AvaloniaTheory]
    [InlineData(0, 0)]
    [InlineData(100, 0)]
    [InlineData(0, 100)]
    [InlineData(-50, 100)]
    [InlineData(0, 100, true)]
    [InlineData(-50, 100, true)]
    public void CompletionCheckmarkDoesNotIncreaseRowHeight(int bodyScale, int iconScale, bool showBadges = false)
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
                        IsAvailableAtCurrentMarket = showBadges,
                    },
                    plan.Rows[0] with
                    {
                        OnFleetCarriers = 80,
                        InShip = 0,
                        IsAvailableAtCurrentMarket = showBadges,
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
            OverlayTypographyResources.Apply(presentation, new(0, 0, 0, bodyScale, 0, 0));
            window.UpdateLayout();
            Border[] rows = presentation
                .GetVisualDescendants()
                .OfType<Border>()
                .Where(border => border.Classes.Contains("commodity-row"))
                .ToArray();
            Assert.Equal(2, rows.Length);
            Assert.Equal(rows[1].Bounds.Height, rows[0].Bounds.Height, 3);
            double textHeight = rows[0].Bounds.Height;
            OverlayTypographyResources.Apply(
                presentation,
                new(0, 0, 0, bodyScale, showBadges ? iconScale : 0, 0, iconScale)
            );
            window.UpdateLayout();
            Assert.All(rows, row => Assert.Equal(textHeight, row.Bounds.Height, 3));
            if (iconScale == 100 && bodyScale <= 0)
            {
                TextBlock checkmark = Assert.Single(
                    rows[0].GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text == "✓"
                );
                Viewbox glyph = Assert.Single(checkmark.GetVisualAncestors().OfType<Viewbox>());
                double glyphTop = glyph.TranslatePoint(default, rows[0])!.Value.Y;
                Assert.True(glyphTop < 0);
                Assert.True(glyphTop + glyph.Bounds.Height > rows[0].Bounds.Height);
                OverlayTypographyResources.Apply(
                    presentation,
                    new(0, 0, 100, 100, showBadges ? iconScale : 0, 0, iconScale)
                );
                window.UpdateLayout();
                Assert.All(rows, row => Assert.True(row.Bounds.Height > textHeight));
            }
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

    /// <summary>Scales project lists and collapsed-category summaries with Caption independently of row badge Detail sizing.</summary>
    [AvaloniaFact]
    public void ProjectNamesAndCollapsedSummariesUseCaptionScaling()
    {
        ColonizationCommodityPlan plan = CreatePlan("Two projects");
        var viewModel = new ColonizationCommodityOverlayViewModel();
        viewModel.Apply(
            plan with
            {
                ProjectNames = ["First project", "Second project"],
                FleetCarriers = [new ColonizationFleetCarrier { MarketId = 1, Name = "ABC-123" }],
                Rows = [plan.Rows[0] with { OnFleetCarriers = 100, InShip = 0 }],
            },
            null
        );
        var presentation = new ColonizationCommodityOverlayPresentation { DataContext = viewModel };
        var window = new Window { Content = presentation, SizeToContent = SizeToContent.WidthAndHeight };
        try
        {
            OverlayThemeResources.Apply(window);
            window.Show();
            window.UpdateLayout();
            TextBlock[] captions = presentation
                .GetVisualDescendants()
                .OfType<TextBlock>()
                .Where(text =>
                    text.Text is "› First project" or "› Second project" or "1 commodities covered by linked FCs"
                )
                .ToArray();
            Assert.Equal(3, captions.Length);
            double[] baselineSizes = captions.Select(text => text.FontSize).ToArray();

            OverlayTypographyResources.Apply(presentation, new(0, 0, 0, 0, 100, 0));
            window.UpdateLayout();
            Assert.Equal(baselineSizes, captions.Select(text => text.FontSize).ToArray());

            OverlayTypographyResources.Apply(presentation, new(0, 0, 0, 0, 0, 100));
            window.UpdateLayout();
            Assert.Equal(
                baselineSizes.Select(size => size * 2).ToArray(),
                captions.Select(text => text.FontSize).ToArray()
            );
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
