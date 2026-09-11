using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Desktop.Theming;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class MiningWorkspacePresentationTests
{
    [AvaloniaFact]
    public void SurfaceMiningMapPaletteStaysReadableAcrossAllApplicationThemes()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var model = MainWindowViewModelTestBuilder.Create(null, _ => { });
        model.MineMap.SelectedTab = 1;
        var themes = new RavenThemeService(
            Application.Current!,
            new ThemePreferenceStore(Path.Combine(directory, "theme.json")));
        var original = themes.Current.Key;
        var view = new Views.MineMapView { DataContext = model };
        var window = new Window { Content = view, Width = 900, Height = 900 };
        try
        {
            window.Show();
            var application = Application.Current!;

            foreach (var theme in RavenThemeCatalog.All)
            {
                themes.Select(theme.Key);
                themes.ApplyCurrent();
                using var frame = window.CaptureRenderedFrame();
                var text = ColorOf(application.Resources["RavenTextBrush"] as IBrush);
                var grid = ColorOf(application.Resources["RavenMapGridBrush"] as IBrush);
                foreach (var background in new[]
                         {
                             Color.Parse(theme.RaisedSurfaceColor),
                             Color.Parse(theme.WindowColor),
                         })
                {
                    Assert.True(
                        Contrast(background, text) >= 4.5,
                        $"{theme.Key} map text contrast is too low.");
                    Assert.True(
                        Contrast(background, grid) >= 3,
                        $"{theme.Key} map grid contrast is too low.");
                }
                var output = Environment.GetEnvironmentVariable(
                    "SRVSURVEY_MINE_MAP_THEME_RENDER_OUTPUT");
                if (!string.IsNullOrWhiteSpace(output))
                {
                    Directory.CreateDirectory(output);
                    using var stream = File.Create(Path.Combine(
                        output,
                        $"surface-map-{theme.Key}.png"));
                    frame!.Save(stream, PngBitmapEncoderOptions.Default);
                }
            }
        }
        finally
        {
            window.Close();
            themes.Select(original);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [AvaloniaFact]
    public void SurfaceHuntRendersEightSortableColumnsWithoutHorizontalOverflow()
    {
        using var model = MainWindowViewModelTestBuilder.Create(null, _ => { });
        model.MineMap.SelectedTab = 3;
        var view = new Views.MineMapView { DataContext = model };
        var window = new Window { Content = view, Width = 1200, Height = 900 };
        try
        {
            window.Show();
            using var frame = window.CaptureRenderedFrame();
            var header = view.FindControl<Grid>("SurfaceHuntHeader");
            var results = view.FindControl<ScrollViewer>("SurfaceHuntResults");

            Assert.NotNull(header);
            Assert.NotNull(results);
            Assert.Equal(8, header.Children.OfType<Button>().Count());
            Assert.True(results.Extent.Width <= results.Viewport.Width + 1);
            Assert.Equal(37, model.MineMap.SurfaceHuntRows.Count);

            var output = Environment.GetEnvironmentVariable("SRVSURVEY_SURFACE_HUNT_RENDER_OUTPUT");
            if (!string.IsNullOrWhiteSpace(output))
            {
                using var stream = File.Create(output);
                frame!.Save(stream, PngBitmapEncoderOptions.Default);
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(2, "HotspotListHorizontalScroller", "HotspotListResults")]
    [InlineData(3, "SurfaceHuntHorizontalScroller", "SurfaceHuntResults")]
    public void SurfaceReferenceTablesScrollHorizontallyAtNarrowWorkspaceWidths(
        int selectedTab,
        string horizontalScrollerName,
        string resultsName)
    {
        using var model = MainWindowViewModelTestBuilder.Create(null, _ => { });
        model.MineMap.SelectedTab = selectedTab;
        var view = new Views.MineMapView { DataContext = model };
        var window = new Window { Content = view, Width = 620, Height = 900 };
        try
        {
            window.Show();
            using var frame = window.CaptureRenderedFrame();
            var horizontalScroller = view.FindControl<ScrollViewer>(
                horizontalScrollerName);
            var results = view.FindControl<ScrollViewer>(resultsName);

            Assert.NotNull(horizontalScroller);
            Assert.NotNull(results);
            Assert.Equal(
                ScrollBarVisibility.Auto,
                horizontalScroller.HorizontalScrollBarVisibility);
            Assert.Equal(
                ScrollBarVisibility.Disabled,
                horizontalScroller.VerticalScrollBarVisibility);
            Assert.True(
                horizontalScroller.Extent.Width
                > horizontalScroller.Viewport.Width + 1);
            Assert.Equal(
                ScrollBarVisibility.Disabled,
                results.HorizontalScrollBarVisibility);
            Assert.Equal(
                ScrollBarVisibility.Auto,
                results.VerticalScrollBarVisibility);

            var resultsContent = Assert.IsType<Control>(
                results.Content,
                exactMatch: false);
            resultsContent.RaiseEvent(new ScrollGestureEventArgs(
                id: 1,
                new Vector(12.5, 0))
            {
                RoutedEvent = InputElement.ScrollGestureEvent,
            });
            using var scrolledFrame = window.CaptureRenderedFrame();

            Assert.Equal(12.5, horizontalScroller.Offset.X);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SurfaceMiningMapUsesASquareViewportAtNarrowWorkspaceWidths()
    {
        using var model = MainWindowViewModelTestBuilder.Create(null, _ => { });
        model.MineMap.SelectedTab = 1;
        var view = new Views.MineMapView { DataContext = model };
        var window = new Window { Content = view, Width = 900, Height = 900 };
        try
        {
            window.Show();
            using var frame = window.CaptureRenderedFrame();
            var viewport = view.FindControl<Viewbox>("MineSurveyMapViewport");

            Assert.NotNull(viewport);
            Assert.InRange(Math.Abs(viewport.Bounds.Width - viewport.Bounds.Height), 0, 1);
            Assert.InRange(viewport.Bounds.Height, 350, 640);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task SearchContentExpandsAndWheelScrollsTheWorkspace()
    {
        using var http = new HttpClient(new SearchRowsHandler());
        using var model = MainWindowViewModelTestBuilder.Create(null, builder => builder.WithExternalNetworkClient(http));
        var view = new Views.MiningView { DataContext = model };
        var window = new Window { Content = view, Width = 660, Height = 640 };
        try
        {
            model.MiningWorkspace.SelectedTab = 3;
            model.MiningWorkspace.Search.Reference = "Wille";
            model.MiningWorkspace.Search.Source = "Spansh";
            await model.MiningWorkspace.Search.SearchRingsAsync();
            await model.MiningWorkspace.Search.SearchMarketsAsync();
            await model.MiningWorkspace.Search.SearchTradersAsync();
            await model.MiningWorkspace.Search.SearchSystemsAsync();
            window.Show();
            foreach (var destination in Enumerable.Range(0, 4))
            {
                model.MiningWorkspace.Search.Destination = destination;
                using var frame = window.CaptureRenderedFrame();
                var search = view.FindControl<Views.MiningSearchView>("SearchPane")!;
                var prefix = new[] { "Ring", "Market", "Trader", "System" }[destination];
                var expectedHeaders = new[]
                {
                    new[] { "SYSTEM", "RING", "MINERALS", "TYPE", "RESERVE", "POWER", "OVERLAPS", "RES", "DISTANCE", "ARRIVAL" },
                    new[] { "SYSTEM", "STATION", "TYPE", "PAD", "DISTANCE", "ARRIVAL", "PRICE", "DEMAND", "STOCK", "OBSERVED" },
                    new[] { "SYSTEM", "STATION", "TYPE", "PAD", "DISTANCE", "ARRIVAL" },
                    new[] { "SYSTEM", "POWER", "POWER STATE", "SECURITY", "ECONOMY", "GOVERNMENT", "ALLEGIANCE", "FACTION STATE", "POPULATION", "DISTANCE" },
                }[destination];
                var header = search.FindControl<Grid>($"{prefix}ResultsHeader");
                Assert.NotNull(header);
                Assert.Equal(
                    expectedHeaders,
                    header.GetLogicalDescendants().OfType<TextBlock>()
                        .Select(text => text.Text)
                        .Where(text => !string.IsNullOrEmpty(text)
                            && text is not "↑" and not "↓"));
                Assert.All(header.Children.OfType<Button>(), button => Assert.NotNull(button.CommandParameter));
                var firstSort = header.Children.OfType<Button>().First();
                model.MiningWorkspace.Search.SortCommand.Execute(firstSort.CommandParameter);
                using var sortedFrame = window.CaptureRenderedFrame();
                Assert.Contains(
                    firstSort.GetLogicalDescendants().OfType<TextBlock>(),
                    text => text.Text is "↑" or "↓");
                var page = Assert.Single(search.GetVisualAncestors().OfType<ScrollViewer>());
                var results = Assert.Single(search.GetVisualDescendants().OfType<ListBox>(), l => l.IsEffectivelyVisible);
                Assert.NotEmpty(results.Items);
                Assert.DoesNotContain(header.GetVisualAncestors(), ancestor => ReferenceEquals(ancestor, results));
                var rows = results.GetVisualDescendants().OfType<Grid>().Where(grid => grid.Classes.Contains("table-row")).ToArray();
                Assert.NotEmpty(rows);
                Assert.All(rows, row =>
                {
                    Assert.Single(row.RowDefinitions);
                    Assert.InRange(row.Bounds.Height, 20, 48);
                });
                var resultScrollers = results.GetVisualAncestors()
                    .OfType<ScrollViewer>()
                    .ToArray();
                Assert.Contains(page, resultScrollers);
                var horizontal = Assert.Single(
                    resultScrollers,
                    scroller => !ReferenceEquals(scroller, page));
                Assert.Equal(
                    ScrollBarVisibility.Auto,
                    horizontal.HorizontalScrollBarVisibility);
                Assert.Equal(
                    ScrollBarVisibility.Disabled,
                    horizontal.VerticalScrollBarVisibility);
                var inner = Assert.Single(results.GetVisualDescendants().OfType<ScrollViewer>());
                Assert.True(inner.Extent.Height <= inner.Viewport.Height + 1);
                Assert.True(inner.Extent.Width <= inner.Viewport.Width + 1);
                Assert.True(results.Bounds.Height > window.Height);
                var collapsedHeight = search.Bounds.Height;
                var filters = search.GetVisualDescendants().OfType<Expander>().FirstOrDefault(e => e.IsEffectivelyVisible);
                if (filters is not null)
                {
                    filters.IsExpanded = true;
                    using var expanded = window.CaptureRenderedFrame();
                    Assert.True(search.Bounds.Height > collapsedHeight);
                    Assert.True(inner.Extent.Height <= inner.Viewport.Height + 1);
                }
                var top = results.TranslatePoint(default, page)!.Value.Y;
                page.Offset = new Vector(0, page.Offset.Y + top);
                using var positioned = window.CaptureRenderedFrame();
                var wheelPoint = results.TranslatePoint(new Point(30, 30), window)!.Value;
                var oldOffset = page.Offset.Y;
                window.MouseWheel(wheelPoint, new Vector(0, -1));
                using var wheeled = window.CaptureRenderedFrame();
                Assert.True(page.Offset.Y > oldOffset, $"{destination}: wheel over results must scroll the workspace");
                var output = Environment.GetEnvironmentVariable("SRVSURVEY_MINING_RENDER_OUTPUT");
                if (output is not null)
                {
                    Directory.CreateDirectory(output);
                    using (var stream = File.Create(Path.Combine(output, $"mining-search-{destination}.png"))) wheeled!.Save(stream, PngBitmapEncoderOptions.Default);
                }
            }
        }
        finally { window.Close(); }
    }

    private sealed class SearchRowsHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            const string system = "Synuefe NL-N C23-4";
            var rows = Enumerable.Range(1, 24).Select(i => request.RequestUri!.AbsolutePath switch
            {
                "/api/bodies/search" => (object)new { system_name = system, distance = i * 1.2, rings = new[] { new { name = $"{system} {i} A Ring", type = "Metallic", signals = new[] { new { name = "Platinum", count = 2 } } } } },
                "/api/stations/search" => new { system_name = system, name = $"Long station name for material exchange {i}", type = "Coriolis Starport", distance = i * 1.2, distance_to_arrival = 123456, has_large_pad = true },
                "/api/systems/search" => new { name = $"{system} {i}", distance = i * 1.2, controlling_power = "Arissa Lavigny-Duval", power_state = "Fortified", security = "High", primary_economy = "Industrial" },
                _ => new { systemName = system, stationName = $"Long commodity market station name {i}", stationType = "Coriolis Starport", sellPrice = 250000, demand = 500000, updatedAt = DateTimeOffset.UtcNow, maxLandingPadSize = 3, distance = i * 1.2 }
            }).ToArray();
            var payload = request.RequestUri!.Host.Contains("spansh", StringComparison.Ordinal) ? System.Text.Json.JsonSerializer.Serialize(new { results = rows }) : System.Text.Json.JsonSerializer.Serialize(rows);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(payload) });
        }
    }

    [AvaloniaFact]
    public void MiningHeadersShrinkBeforeWrappingAndMovedToolsRemainAvailable()
    {
        using var model = MainWindowViewModelTestBuilder.Create(null, _ => { });
        var mining = new Views.MiningView { DataContext = model };
        var window = new Window { Content = mining, Width = 1200, Height = 800 };
        try
        {
            window.Show(); using var initial = window.CaptureRenderedFrame();
            var tabs = mining.FindControl<TabControl>("MiningTabs")!;
            var items = tabs.Items.OfType<TabItem>().ToArray();
            Assert.Equal(["Session", "Reports", "Missions", "Find", "Bookmarks", "Reference", "Settings"], items.Select(t => ((TextBlock)t.Header!).Text));
            Assert.All(items, t => Assert.Equal(22, ((TextBlock)t.Header!).FontSize));
            window.Width = 700; using var smaller = window.CaptureRenderedFrame();
            Assert.All(items, t => Assert.InRange(((TextBlock)t.Header!).FontSize, 14, 21));
            Assert.True(items.Max(t => t.Bounds.Y) - items.Min(t => t.Bounds.Y) < 2);
            window.Width = 360; using var narrow = window.CaptureRenderedFrame();
            Assert.All(items, t => Assert.Equal(14, ((TextBlock)t.Header!).FontSize));
            Assert.True(items.Max(t => t.Bounds.Y) > items.Min(t => t.Bounds.Y));
            window.Width = 1200; using var expanded = window.CaptureRenderedFrame();
            Assert.All(items, t => Assert.Equal(22, ((TextBlock)t.Header!).FontSize));
            window.Content = new Views.TravelView { DataContext = model }; using var travelFrame = window.CaptureRenderedFrame();
            var travel = ((Views.TravelView)window.Content).FindControl<TabControl>("TravelModeTabs")!;
            Assert.Equal("Distance", travel.Items.OfType<TabItem>().Last().Header);
            model.MiningWorkspace.Status = "Save failed: test status";
            travel.SelectedIndex = 3; using var distanceFrame = window.CaptureRenderedFrame();
            Assert.Contains(((Control)window.Content).GetVisualDescendants().OfType<TextBlock>(), t => t.Text == model.MiningWorkspace.Status && t.IsEffectivelyVisible);
            window.Content = new Views.FiregroupsView { DataContext = model }; using var fireFrame = window.CaptureRenderedFrame();
            Assert.Contains(((Control)window.Content).GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Save"));
            Assert.Contains(((Control)window.Content).GetVisualDescendants().OfType<TextBlock>(), t => t.Text == model.Firegroups.Status && t.IsEffectivelyVisible);
            window.Content = new Views.FleetCarrierWorkspaceView { DataContext = model }; using var fleetFrame = window.CaptureRenderedFrame();
            var fullCarrier = Assert.Single(((Control)window.Content).GetVisualDescendants().OfType<Views.FrontierCarrierTabView>());
            Assert.Same(model.FrontierProfile, fullCarrier.DataContext);
            Assert.Same(model.FleetCarrierWorkspace, fullCarrier.SupplementaryContent!.DataContext);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void WorkspaceTabsRenderAcrossApplicationThemesAndBookmarksUseSharedModel()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        using var model = MainWindowViewModelTestBuilder.Create(null, _ => { });
        var themes = new RavenThemeService(Application.Current!, new ThemePreferenceStore(Path.Combine(directory, "theme.json")));
        var original = themes.Current.Key;
        var window = new MainWindow(model) { Width = 1400, Height = 950 };
        try
        {
            model.SelectedNavigation = model.NavigationItems.Single(n => n.Key == "mining");
            var journal = new JournalSessionState();
            Assert.True(JournalEventEnvelope.TryParse("""{"event":"LoadGame","FID":"MiningPreview","Commander":"Preview","Ship":"python"}""", out var load, out _));
            journal.Apply(load!);
            var ship = new EliteStatus { Flags = StatusFlags.InMainShip };
            model.MiningWorkspace.Apply(new JournalMonitorUpdate(null, [load!], ship, null, null, null, [], true), journal,
                new CargoSnapshot(DateTimeOffset.UtcNow, "Cargo", "Ship", 42, [new CargoItem("platinum", "Platinum", 30, 0), new CargoItem("drones", "Limpets", 12, 0)]), ship);
            var now = DateTimeOffset.UtcNow;
            var session = new MiningSession
            {
                Started = now.AddMinutes(-60),
                Ended = now,
                System = "Delkar",
                Ring = "Delkar 7 A Ring",
                Ship = "Python",
                Notes = "Platinum session near a High RES.",
                Prospects = [new MiningProspect(now.AddMinutes(-55), [new MiningMaterial("Platinum", 42), new MiningMaterial("Osmium", 11)], "", "High")],
                Collections = [new MiningCollection(now.AddMinutes(-1), "platinum", 30, false), new MiningCollection(now.AddMinutes(-1), "iron", 3, true)]
            };
            var data = new MiningCommanderData
            {
                Current = session with { Ended = null },
                History = [session],
                Missions = [new MiningMission { Id = 1, Commodity = "platinum", Required = 50, OnBoard = 30, Destination = "Sol / Abraham Lincoln" }],
                Rings = [new MiningRing { System = "Delkar", Body = "Delkar 7 A Ring", RingType = "Metallic", Reserve = "Pristine", Hotspots = new() { ["Platinum"] = 2 } }]
            };
            model.MiningWorkspace.Restore(MiningStore.Export(data));
            model.Bookmarks.AddMiningLocation(new GalacticBookmark { System = "Delkar", Body = "Delkar 7 A Ring", Minerals = "Platinum", ResourceExtractionSites = "High", Rating = 4 });
            window.Show();
            using (var initialMiningFrame = window.CaptureRenderedFrame())
            {
                var miningView = Assert.Single(window.GetVisualDescendants().OfType<Views.MiningView>(), view => view.IsEffectivelyVisible);
                AssertMiningTable(miningView, "CargoHeader", "CargoRows", ["CARGO", "TONS"]);
                AssertMiningTable(miningView, "MaterialsHeader", "MaterialsRows", ["MINERAL", "FINDS", "QUALITY HITS", "AVERAGE", "BEST", "TONS"]);
                AssertTableHeader(miningView, "ProspectsHeader", ["TIME", "MINERALS", "CORE"]);
                AssertTableHeader(miningView, "EngineeringMaterialsHeader", ["MATERIAL", "GRADE", "COUNT"]);
                AssertTableHeader(miningView, "NoticesHeader", ["TIME", "KIND", "NOTIFICATION"]);

                model.MiningWorkspace.SelectedTab = 1;
                using var reportsFrame = window.CaptureRenderedFrame();
                AssertMiningTable(miningView, "HistoryHeader", "HistoryRows", ["STARTED", "SYSTEM", "RING", "TONS", "TONS / HOUR"]);

                model.MiningWorkspace.SelectedTab = 2;
                using var missionsFrame = window.CaptureRenderedFrame();
                AssertMiningTable(miningView, "MissionsHeader", "MissionRows", ["COMMODITY", "REQUIRED", "DELIVERED", "ON BOARD", "NEEDED", "DESTINATION", "STATUS"]);

                model.MiningWorkspace.SelectedTab = 3;
                miningView.FindControl<ScrollViewer>("SearchPage")!.IsVisible = false;
                miningView.FindControl<Border>("LocalPane")!.IsVisible = true;
                using var localFrame = window.CaptureRenderedFrame();
                AssertMiningTable(miningView, "LocalRingsHeader", "LocalRingRows", ["SYSTEM", "RING", "TYPE", "RESERVE", "HOTSPOTS", "ARRIVAL"]);
            }
            foreach (var theme in RavenThemeCatalog.All)
            {
                themes.Select(theme.Key);
                foreach (var tab in new[] { 0, 1, 2, 3, 4, 5, 6 })
                {
                    model.MiningWorkspace.SelectedTab = tab;
                    using var frame = window.CaptureRenderedFrame();
                    Assert.NotNull(frame);
                    Assert.Contains(window.GetVisualDescendants().OfType<TabControl>(), t => t.IsEffectivelyVisible);
                    var output = Environment.GetEnvironmentVariable("SRVSURVEY_MINING_RENDER_OUTPUT");
                    if (output is not null && theme.Key is "monochrome-dark" or "blue-light")
                    {
                        Directory.CreateDirectory(output!);
                        using var stream = File.Create(Path.Combine(output!, $"mining-{theme.Key}-{tab}.png"));
                        frame.Save(stream, PngBitmapEncoderOptions.Default);
                    }
                }
            }
            model.SelectedNavigation = model.NavigationItems.Single(n => n.Key == "bookmarks");
            using var bookmarksFrame = window.CaptureRenderedFrame();
            var bookmarkView = Assert.Single(window.GetVisualDescendants().OfType<Views.BookmarksView>(), view => view.IsEffectivelyVisible);
            Assert.Same(model.Bookmarks, bookmarkView.DataContext);
            AssertTableHeader(bookmarkView, "BookmarksHeader", ["SYSTEM", "BODY", "RING", "CATEGORY", "DETAILS", "NOTES"]);
            AssertSingleLineRows(bookmarkView.FindControl<ListBox>("BookmarkRows"));
            Assert.True(model.IsNavigationNavigationExpanded);
            var buttons = bookmarkView.GetVisualDescendants().OfType<Button>().ToArray();
            Assert.Contains(buttons, b => Equals(b.Content, "Attach screenshots…"));
            var undo = Assert.Single(buttons, b => Equals(b.Content, "Undo delete"));
            model.Bookmarks.Selected = model.Bookmarks.All[0];
            var deletedId = model.Bookmarks.Selected.Id;
            model.Bookmarks.DeleteCommand.Execute(null);
            Assert.DoesNotContain(model.Bookmarks.All, b => b.Id == deletedId);
            undo.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Contains(model.Bookmarks.All, b => b.Id == deletedId);
        }
        finally
        {
            window.Close(); themes.Select(original);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static void AssertMiningTable(Views.MiningView view, string headerName, string rowsName, string[] expectedHeaders)
    {
        AssertTableHeader(view, headerName, expectedHeaders);
        var header = view.FindControl<Grid>(headerName)!;
        var rows = view.FindControl<ListBox>(rowsName);
        Assert.DoesNotContain(header.GetVisualAncestors(), ancestor => ReferenceEquals(ancestor, rows));
        var horizontal = Assert.Single(
            header.GetVisualAncestors().OfType<ScrollViewer>(),
            scroller => scroller.HorizontalScrollBarVisibility == ScrollBarVisibility.Auto
                && scroller.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled);
        Assert.Contains(
            horizontal,
            rows!.GetVisualAncestors().OfType<ScrollViewer>());
        AssertSingleLineRows(rows);
    }

    private static Color ColorOf(IBrush? brush) =>
        Assert.IsType<SolidColorBrush>(brush).Color;

    private static double Contrast(Color left, Color right)
    {
        var leftLuminance = Luminance(left);
        var rightLuminance = Luminance(right);
        return (Math.Max(leftLuminance, rightLuminance) + 0.05)
            / (Math.Min(leftLuminance, rightLuminance) + 0.05);
    }

    private static double Luminance(Color color) =>
        0.2126 * Linear(color.R)
        + 0.7152 * Linear(color.G)
        + 0.0722 * Linear(color.B);

    private static double Linear(byte channel)
    {
        var value = channel / 255d;
        return value <= 0.04045
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static void AssertTableHeader(Control view, string headerName, string[] expectedHeaders)
    {
        var header = view.FindControl<Grid>(headerName);
        Assert.NotNull(header);
        Assert.Contains("table-header", header.Classes);
        Assert.Equal(
            expectedHeaders,
            header.GetLogicalDescendants().OfType<TextBlock>()
                .Select(text => text.Text)
                .Where(text => !string.IsNullOrEmpty(text)
                    && text is not "↑" and not "↓"));
        Assert.All(header.Children.OfType<Button>(), button => Assert.NotNull(button.CommandParameter));
    }

    private static void AssertSingleLineRows(ListBox? rows)
    {
        Assert.NotNull(rows);
        Assert.Contains("table-rows", rows.Classes);
        var rowGrids = rows.GetVisualDescendants().OfType<Grid>().Where(grid => grid.Classes.Contains("table-row")).ToArray();
        Assert.NotEmpty(rowGrids);
        Assert.All(rowGrids, row =>
        {
            Assert.Single(row.RowDefinitions);
            Assert.InRange(row.Bounds.Height, 20, 48);
        });
    }
}
