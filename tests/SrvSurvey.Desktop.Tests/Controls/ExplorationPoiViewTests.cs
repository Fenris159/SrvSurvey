using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Controls;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Controls;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class ExplorationPoiViewTests
{
    /// <summary>Verifies browser fills available width and aligns headers with rows without overlapping scrollbars.</summary>
    [AvaloniaFact]
    public async Task BrowserFillsAvailableWidthAndAlignsHeadersWithRowsWithoutOverlappingScrollbars()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SrvSurvey-poi-view-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var model = new ExplorationPoiViewModel(
            new PreviewClient(),
            new ExplorationPoiSettingsStore(Path.Combine(directory, "ui.json"))
        );
        model.UpdateContext("Sol", 1, new(0, 0, 0), null, []);
        model.SetExternalEnabled(true);
        await model.PendingLoad;
        var view = new ExplorationPoiView { DataContext = model };
        var window = new Window
        {
            Width = 940,
            Height = 1020,
            Content = view,
        };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            SystemNameEntry system = view.FindControl<SystemNameEntry>("PoiSystemSearch")!;
            SystemNameEntry reference = view.FindControl<SystemNameEntry>("PoiReferenceSystem")!;
            TextBox attraction = view.FindControl<TextBox>("PoiAttractionSearch")!;
            Assert.Equal("Sol", reference.Text);
            Assert.False(attraction.AcceptsReturn);
            Assert.InRange(attraction.Bounds.Height, 20, 48);
            Assert.InRange(system.FindControl<TextBox>("InputBox")!.Bounds.Height, 20, 48);
            Assert.InRange(reference.FindControl<TextBox>("InputBox")!.Bounds.Height, 20, 48);
            model.AttractionQuery = "Attraction";
            Assert.False(system.IsEffectivelyEnabled);
            model.AttractionQuery = "";
            model.SystemQuery = "Sol";
            Assert.False(attraction.IsEffectivelyEnabled);
            model.SystemQuery = "";
            ScrollViewer scroller = view.FindControl<ScrollViewer>("PoiResultsScroller")!;
            Border table = view.FindControl<Border>("PoiResultsTable")!;
            Assert.True(scroller.Extent.Height > scroller.Viewport.Height);
            foreach (double width in new[] { 700d, 940d, 1200d })
            {
                window.Width = width;
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.Equal(view.Bounds.Width, view.FindControl<Border>("PoiPanel")!.Bounds.Width);
                AssertColumnsAlign(view);
            }
            ScrollBar vertical = Assert.Single(
                scroller.GetVisualDescendants().OfType<ScrollBar>(),
                bar =>
                    bar.Orientation == Orientation.Vertical
                    && ReferenceEquals(bar.GetVisualAncestors().OfType<ScrollViewer>().First(), scroller)
            );
            Point tableEnd =
                table.TranslatePoint(new(table.Bounds.Width, 0), scroller)
                ?? throw new InvalidOperationException("The table must share the scroll viewer's visual tree.");
            Point scrollbarStart =
                vertical.TranslatePoint(default, scroller)
                ?? throw new InvalidOperationException("The scrollbar must share the scroll viewer's visual tree.");
            double tableRight = tableEnd.X;
            double scrollbarLeft = scrollbarStart.X;
            Assert.True(tableRight <= scrollbarLeft, "The scrollbar must stay outside the result rows.");
            ListBox rows = view.FindControl<ListBox>("PoiResults")!;
            rows.SelectedIndex = 3;
            Assert.Equal(model.Items[3], model.Selected);
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            string? output = Environment.GetEnvironmentVariable("SRVSURVEY_POI_BROWSER_RENDER_OUTPUT");
            if (!string.IsNullOrWhiteSpace(output))
            {
                frame.Save(output, PngBitmapEncoderOptions.Default);
            }
        }
        finally
        {
            window.Close();
            Directory.Delete(directory, true);
        }
    }

    /// <summary>Verifies assert columns align.</summary>
    private static void AssertColumnsAlign(ExplorationPoiView view)
    {
        foreach (
            (string column, bool alignRight) in new[]
            {
                ("Name", false),
                ("Category", false),
                ("Distance", true),
                ("Score", true),
            }
        )
        {
            TextBlock header = view.FindControl<TextBlock>("Poi" + column + "Header")!;
            TextBlock cell = view.GetVisualDescendants()
                .OfType<TextBlock>()
                .First(text => text.Name == "Poi" + column + "Cell");
            Point headerPoint =
                header.TranslatePoint(new(alignRight ? header.Bounds.Width : 0, 0), view)
                ?? throw new InvalidOperationException("Missing header visual.");
            Point cellPoint =
                cell.TranslatePoint(new(alignRight ? cell.Bounds.Width : 0, 0), view)
                ?? throw new InvalidOperationException("Missing cell visual.");
            Assert.InRange(Math.Abs(headerPoint.X - cellPoint.X), 0, 1);
        }
    }

    /// <summary>Verifies poi overlay controls belong to exploration settings with the standard visibility shortcut.</summary>
    [AvaloniaFact]
    public void PoiOverlayControlsBelongToExplorationSettingsWithTheStandardVisibilityShortcut()
    {
        using MainWindowViewModel model = MainWindowViewModelTestBuilder.Create(null);
        var global = new OverlaySettingsView(OverlaySettingsCategory.Global) { DataContext = model };
        var exploration = new OverlaySettingsView(OverlaySettingsCategory.Exploration) { DataContext = model };
        Assert.False(global.FindControl<Border>("ExplorationPoiCard")!.IsVisible);
        Assert.True(exploration.FindControl<Border>("ExplorationPoiCard")!.IsVisible);
        ItemsControl panels = exploration.FindControl<ItemsControl>("PanelVisibilityItems")!;
        OverlayPanelVisibilityEntryViewModel poi = Assert.Single(
            panels.ItemsSource!.Cast<OverlayPanelVisibilityEntryViewModel>(),
            panel => panel.PlotterName == "PlotExplorationPois"
        );
        Assert.Empty(poi.Shortcut.Chord);
        Assert.Equal("PlotExplorationPois", poi.Shortcut.Definition.OverlayPlotterName);
        model.OverlayPanelVisibility.Toggle(poi.PlotterName);
        Assert.False(poi.IsEnabled);
        model.OverlayPanelVisibility.Toggle(poi.PlotterName);
        Assert.True(poi.IsEnabled);
    }

    private sealed class PreviewClient : IExplorationPoiClient
    {
        /// <summary>Provides get async for the isolated test fixture.</summary>
        public Task<ExplorationPoiLoadResult> GetAsync(
            bool forceRefresh = false,
            CancellationToken cancellationToken = default
        ) =>
            Task.FromResult(
                new ExplorationPoiLoadResult(
                    new ExplorationPoiCatalog(
                        Enumerable
                            .Range(1, 20)
                            .Select(id => new ExplorationPoi(
                                id,
                                "GEC",
                                "Attraction " + id,
                                "Sol",
                                1,
                                new GalacticCoordinate(id, 0, 0),
                                "Stellar Features",
                                "Historical",
                                "Community synopsis of this attraction.",
                                "Full description and visiting information.",
                                6.8,
                                4,
                                25,
                                false
                            ))
                    ),
                    DateTimeOffset.UtcNow,
                    true
                )
            );
    }
}
