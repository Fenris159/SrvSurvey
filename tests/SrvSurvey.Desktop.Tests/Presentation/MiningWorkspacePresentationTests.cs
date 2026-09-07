using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
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
    public async Task SearchResultsHaveOneBoundedViewportAndFitNarrowWorkspace()
    {
        using var http = new HttpClient(new SearchRowsHandler());
        using var model = MainWindowViewModelTestBuilder.Create(null, builder => builder.WithExternalNetworkClient(http));
        var view = new Views.MiningView { DataContext = model };
        var window = new Window { Content = view, Width = 660, Height = 920 };
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
                Assert.DoesNotContain(search.GetVisualAncestors(), a => a is ScrollViewer);
                var results = Assert.Single(search.GetVisualDescendants().OfType<ListBox>(), l => l.IsEffectivelyVisible);
                Assert.NotEmpty(results.Items);
                Assert.InRange(results.Bounds.Height, 80, window.Height);
                Assert.DoesNotContain(results.GetVisualAncestors(), a => a is ScrollViewer);
                var scroll = Assert.Single(results.GetVisualDescendants().OfType<ScrollViewer>());
                Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1);
                var output = Environment.GetEnvironmentVariable("SRVSURVEY_MINING_RENDER_OUTPUT");
                if (output is not null) { Directory.CreateDirectory(output); using var stream = File.Create(Path.Combine(output, $"mining-search-{destination}.png")); frame!.Save(stream, PngBitmapEncoderOptions.Default); }
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
}
