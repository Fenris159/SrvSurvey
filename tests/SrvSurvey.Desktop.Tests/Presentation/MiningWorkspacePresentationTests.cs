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
            JournalEventEnvelope.TryParse("""{"event":"LoadGame","FID":"MiningPreview","Commander":"Preview","Ship":"python"}""", out var load, out _);
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
            model.MiningWorkspace.Restore(new MiningStore(directory).Export(data));
            model.Bookmarks.AddMiningLocation(new GalacticBookmark { System = "Delkar", Body = "Delkar 7 A Ring", Minerals = "Platinum", ResourceExtractionSites = "High", Rating = 4 });
            window.Show();
            foreach (var theme in RavenThemeCatalog.All)
            {
                themes.Select(theme.Key);
                foreach (var tab in new[] { 0, 1, 2, 3, 5, 6, 7, 8 })
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
        }
        finally
        {
            window.Close(); themes.Select(original);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
