using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.Tests.Platform;
using SrvSurvey.Desktop.Tests.ViewModels;
using SrvSurvey.Desktop.Theming;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class SquadronCarrierRoutesPresentationTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"SrvSurvey-sc-routes-ui-{Guid.NewGuid():N}");

    private readonly HttpClient noNetworkClient = new(new NoNetwork());

    [AvaloniaTheory]
    [InlineData(640)]
    [InlineData(1100)]
    public async Task SquadronTabFollowsFleetCarrierTabAndRendersTheSharedLibraryInEveryTheme(int width)
    {
        using MainWindowViewModel main = CreateMain();
        var library = new FollowRouteStore(main.AppDataPaths.DataDirectory, FollowRouteKind.FleetCarrier);
        FollowRouteDocument blank = await library.CreateNewAsync("F1");
        FollowRouteDocument saved = await library.SaveAsAsync(
            blank with
            {
                Hops =
                [
                    new("Sol", 1, null, null, false, false),
                    new("Lave", 2, null, null, false, false),
                    new("Achenar", 3, null, null, false, false),
                ],
            },
            "Colonia resupply loop"
        );
        await library.SaveAsAsync(saved, "Guardian expedition");
        await library.SaveAsAsync(saved, "Construction supply run");
        await main.FleetCarrierRoute.UpdateContextAsync("F1", "Sol", 1, null);
        await main.SquadronCarrierRoute.UpdateContextAsync("F1", "Sol", 1, null);
        await main.FleetCarrierRouteManager.UpdateContextAsync("F1");
        await main.SquadronCarrierRouteManager.UpdateContextAsync("F1");
        await main.SquadronCarrierRoute.LoadSavedRouteAsync("Colonia resupply loop.json", false);
        var view = new TravelView { DataContext = main };
        var window = new Window
        {
            Content = view,
            Width = width,
            Height = 1000,
            Classes = { "app-window" },
        };
        var themes = new RavenThemeService(
            Application.Current!,
            new ThemePreferenceStore(Path.Combine(directory, "themes.json"))
        );
        try
        {
            window.Show();
            TabControl tabs = view.FindControl<TabControl>("TravelModeTabs")!;
            TabItem[] items = tabs.Items.OfType<TabItem>().ToArray();
            int fc = Array.FindIndex(items, item => Equals(item.Header, "FC Routes"));
            int sc = Array.FindIndex(items, item => Equals(item.Header, "SC Routes"));
            Assert.Equal(fc + 1, sc);
            tabs.SelectedItem = items[sc];
            foreach (RavenThemeDefinition theme in RavenThemeCatalog.All)
            {
                themes.Select(theme.Key);
                window.Background = new SolidColorBrush(Color.Parse(theme.WindowColor));
                using WriteableBitmap? frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                CarrierRoutesView carrier = Assert.Single(
                    view.GetVisualDescendants().OfType<CarrierRoutesView>(),
                    control => control.IsEffectivelyVisible
                );
                Assert.Same(main.SquadronCarrierRouteManager, carrier.DataContext);
                Assert.Contains(
                    carrier.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.IsEffectivelyVisible && text.Text == "SC Routes"
                );
                Assert.Contains(
                    carrier.GetVisualDescendants().OfType<Button>(),
                    button => button.IsEffectivelyVisible && Equals(button.Content, "Overlay settings")
                );
                Assert.Equal(3, main.SquadronCarrierRouteManager.Routes.Count);
                Assert.Equal(3, main.FleetCarrierRouteManager.Routes.Count);
                Assert.InRange(carrier.Bounds.Width, 1, width);
                Assert.InRange(carrier.FindControl<TextBlock>("CarrierRouteTitle")!.Bounds.Width, 250, width);
                frame.Save(
                    Path.Combine(Path.GetTempPath(), $"srvsurvey-sc-routes-{width}-{theme.Key}.png"),
                    PngBitmapEncoderOptions.Default
                );
            }
            Assert.Equal(FollowRouteCarrierKind.Personal, main.FleetCarrierRoute.SelectedCarrierKind);
            Assert.Equal(FollowRouteCarrierKind.Squadron, main.SquadronCarrierRoute.SelectedCarrierKind);
            main.SquadronCarrierRoute.SelectedCarrier = main.SquadronCarrierRoute.CarrierOptions[0];
            Assert.Equal(FollowRouteCarrierKind.Squadron, main.SquadronCarrierRoute.SelectedCarrierKind);
        }
        finally
        {
            themes.Select(RavenThemeCatalog.DefaultThemeKey);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task SharedBookmarkRenameFavoriteAndDeletePreserveIndependentCarrierProgress()
    {
        using MainWindowViewModel main = CreateMain();
        var library = new FollowRouteStore(main.AppDataPaths.DataDirectory, FollowRouteKind.FleetCarrier);
        FollowRouteDocument saved = await library.SaveAsAsync(
            (await library.CreateNewAsync("F1")) with
            {
                Hops =
                [
                    new("Sol", 1, null, null, false, false),
                    new("Lave", 2, null, null, false, false),
                    new("Achenar", 3, null, null, false, false),
                ],
            },
            "Shared loop"
        );
        await main.FleetCarrierRoute.UpdateContextAsync("F1", "Sol", 1, null);
        await main.SquadronCarrierRoute.UpdateContextAsync("F1", "Sol", 1, null);
        await main.FleetCarrierRouteManager.UpdateContextAsync("F1");
        await main.SquadronCarrierRouteManager.UpdateContextAsync("F1");
        await main.FleetCarrierRoute.LoadSavedRouteAsync("Shared loop.json", false);
        await main.SquadronCarrierRoute.LoadSavedRouteAsync("Shared loop.json", false);
        main.FleetCarrierRoute.SetProgressThrough(0, true);
        main.SquadronCarrierRoute.SetProgressThrough(1, true);
        await main.FleetCarrierRoute.FlushWorkspaceAsync();
        await main.SquadronCarrierRoute.FlushWorkspaceAsync();
        RouteManagerItemViewModel row = Assert.Single(main.FleetCarrierRouteManager.Routes);
        row.RenameCommand.Execute(null);
        main.FleetCarrierRouteManager.RenameDraft = "Renamed loop";
        await main.FleetCarrierRouteManager.SaveRenameAsync();
        Assert.Equal("Renamed loop", main.SquadronCarrierRoute.RouteName);
        Assert.Equal(0, main.FleetCarrierRoute.CreateSnapshot()!.LastReachedIndex);
        Assert.Equal(1, main.SquadronCarrierRoute.CreateSnapshot()!.LastReachedIndex);
        Assert.Equal(FollowRouteCarrierKind.Squadron, main.SquadronCarrierRoute.CreateSnapshot()!.CarrierKind);
        await main.FleetCarrierRouteManager.ToggleFavoriteAsync(row);
        await main.SquadronCarrierRouteManager.RefreshAsync();
        Assert.True(Assert.Single(main.SquadronCarrierRouteManager.Routes).IsFavorite);
        row.EditNotesCommand.Execute(null);
        main.FleetCarrierRouteManager.NotesDraft = "Shared supply notes";
        await main.FleetCarrierRouteManager.SaveNotesAsync();
        Assert.Equal("Shared supply notes", main.SquadronCarrierRoute.CreateSnapshot()!.Notes);
        var restored = new FollowRouteStore(main.AppDataPaths.DataDirectory, FollowRouteKind.FleetCarrier);
        Assert.Equal(
            0,
            (await restored.ForCarrier(FollowRouteCarrierKind.Personal).LoadAsync("F1"))
                .WorkspaceDraft!
                .LastReachedIndex
        );
        Assert.Equal(
            1,
            (await restored.ForCarrier(FollowRouteCarrierKind.Squadron).LoadAsync("F1"))
                .WorkspaceDraft!
                .LastReachedIndex
        );
        row.IsSelected = true;
        await main.FleetCarrierRouteManager.ConfirmDeleteAsync();
        Assert.False(main.FleetCarrierRoute.HasRoute);
        Assert.False(main.SquadronCarrierRoute.HasRoute);
        Assert.Empty(await restored.ListAsync("F1"));
        await library.SaveAsAsync(saved, "Renamed loop");
        FollowRouteDocument recreated = (
            await restored.ForCarrier(FollowRouteCarrierKind.Squadron).LoadNamedAsync("F1", "Renamed loop.json", false)
        ).Route!;
        Assert.Equal(-1, recreated.LastReachedIndex);
        Assert.False(recreated.AutoCopy);
    }

    [AvaloniaFact]
    public async Task PartialImportRefreshesBothCarrierLibraries()
    {
        using MainWindowViewModel main = CreateMain();
        await main.FleetCarrierRoute.UpdateContextAsync("F1", "Sol", 1, null);
        await main.SquadronCarrierRoute.UpdateContextAsync("F1", "Sol", 1, null);
        await main.FleetCarrierRouteManager.UpdateContextAsync("F1");
        await main.SquadronCarrierRouteManager.UpdateContextAsync("F1");
        var incoming = new FollowRouteStore(Path.Combine(directory, "incoming"), FollowRouteKind.FleetCarrier);
        FollowRouteDocument saved = await incoming.SaveAsAsync(
            (await incoming.CreateNewAsync("F1")) with
            {
                Hops = [new("Sol", 1, null, null, false, false), new("Lave", 2, null, null, false, false)],
            },
            "Imported carrier route"
        );
        await main.FleetCarrierRouteManager.ImportAsync([saved.FilePath, Path.Combine(directory, "missing.json")]);
        Assert.Contains(
            "could not be completed",
            main.FleetCarrierRouteManager.StatusMessage,
            StringComparison.Ordinal
        );
        Assert.Single(main.FleetCarrierRouteManager.Routes);
        Assert.Single(main.FleetCarrierRoute.SavedRoutes);
        Assert.Single(main.SquadronCarrierRoute.SavedRoutes);
    }

    [AvaloniaFact]
    public async Task DefaultSquadronPlacementReservesTheScaledPersonalPanelWidth()
    {
        RouteWorkspaceViewModel fc = CreateCarrier(FollowRouteCarrierKind.Personal);
        RouteWorkspaceViewModel sc = CreateCarrier(FollowRouteCarrierKind.Squadron);
        await fc.UpdateContextAsync("F1", "Sol", 1, null);
        await sc.UpdateContextAsync("F1", "Sol", 1, null);
        var layout = new LegacyOverlayLayout(
            new Dictionary<string, LegacyOverlayPlacement>
            {
                ["PlotFleetCarrierRoute"] = OverlayLayoutCatalog
                    .GetRequired("PlotFleetCarrierRoute")
                    .DefaultPlacement with
                {
                    ScaleIndex = OverlayScaleCatalog.GetIndex(50),
                },
                ["PlotSquadronCarrierRoute"] = OverlayLayoutCatalog
                    .GetRequired("PlotSquadronCarrierRoute")
                    .DefaultPlacement,
            },
            null,
            null
        );
        var registry = new OverlayWindowRegistry();
        using var host = new HostedOverlayTestHarness(registry, overlayLayout: layout);
        using var personal = new FleetCarrierRouteOverlayCoordinator(fc, host.Session);
        using var squadron = new FleetCarrierRouteOverlayCoordinator(sc, host.Session);
        JournalEventEnvelope[] requests =
        [
            Event(
                $$"""{"event":"CarrierJumpRequest","CarrierID":1,"CarrierType":"FleetCarrier","SystemName":"Lave","DepartureTime":"{{DateTimeOffset.UtcNow.AddMinutes(15):O}}"}"""
            ),
            Event(
                $$"""{"event":"CarrierJumpRequest","CarrierID":2,"CarrierType":"SquadronCarrier","SystemName":"Achenar","DepartureTime":"{{DateTimeOffset.UtcNow.AddMinutes(15):O}}"}"""
            ),
        ];
        fc.ApplyFleetCarrierJumpEvents(requests);
        sc.ApplyFleetCarrierJumpEvents(requests);
        host.Tick();
        RegisteredOverlayWindow own = registry
            .Snapshot()
            .Single(window => window.PlotterName == "PlotFleetCarrierRoute");
        RegisteredOverlayWindow other = registry
            .Snapshot()
            .Single(window => window.PlotterName == "PlotSquadronCarrierRoute");
        Assert.True(OverlayWindowMetrics.GetPixelSize(own).Width > OverlayWindowMetrics.GetPixelSize(other).Width);
        Assert.True(
            other.Window.Position.X + OverlayWindowMetrics.GetPixelSize(other).Width + 16 <= own.Window.Position.X
        );
        LegacyOverlayPlacement custom = layout.Placements["PlotSquadronCarrierRoute"] with
        {
            HorizontalOffset = 700,
            VerticalOffset = 220,
            PositionReference = new(1920, 1080),
        };
        layout.SetPlacement("PlotSquadronCarrierRoute", custom);
        host.Tick();
        Assert.Equal(
            layout.GetPosition(
                "PlotSquadronCarrierRoute",
                HostedOverlayTestHarness.AvailableGameWindow.ClientBounds,
                OverlayWindowMetrics.GetPixelSize(other)
            ),
            other.Window.Position
        );
        await fc.FlushWorkspaceAsync();
        await sc.FlushWorkspaceAsync();
    }

    [AvaloniaFact]
    public async Task BothPassiveOverlaysTrackTheirOwnRequestsAndCancellationWithoutRoutes()
    {
        RouteWorkspaceViewModel fc = CreateCarrier(FollowRouteCarrierKind.Personal);
        RouteWorkspaceViewModel sc = CreateCarrier(FollowRouteCarrierKind.Squadron);
        await fc.UpdateContextAsync("F1", "Sol", 1, null);
        await sc.UpdateContextAsync("F1", "Sol", 1, null);
        var registry = new OverlayWindowRegistry();
        using var host = new HostedOverlayTestHarness(registry);
        using var personal = new FleetCarrierRouteOverlayCoordinator(fc, host.Session);
        using var squadron = new FleetCarrierRouteOverlayCoordinator(sc, host.Session);
        JournalEventEnvelope[] requests =
        [
            Event(
                $$"""{"event":"CarrierJumpRequest","CarrierID":1,"CarrierType":"FleetCarrier","SystemName":"Lave","DepartureTime":"{{DateTimeOffset.UtcNow.AddMinutes(15):O}}"}"""
            ),
            Event(
                $$"""{"event":"CarrierJumpRequest","CarrierID":2,"CarrierType":"SquadronCarrier","SystemName":"Achenar","DepartureTime":"{{DateTimeOffset.UtcNow.AddMinutes(15):O}}"}"""
            ),
        ];
        fc.ApplyFleetCarrierJumpEvents(requests);
        sc.ApplyFleetCarrierJumpEvents(requests);
        FleetCarrierRouteOverlayWindow[] visible = host
            .PreparedWindows.OfType<FleetCarrierRouteOverlayWindow>()
            .Where(window => window.IsVisible)
            .ToArray();
        Assert.Equal(2, visible.Length);
        foreach (FleetCarrierRouteOverlayWindow panel in visible)
        {
            using WriteableBitmap? frame = panel.CaptureRenderedFrame();
            Assert.NotNull(frame);
            if (
                Assert
                    .IsType<FleetCarrierRouteOverlayViewModel>(panel.DataContext)
                    .Header.StartsWith("SQUADRON", StringComparison.Ordinal)
            )
            {
                frame.Save(
                    Path.Combine(Path.GetTempPath(), "srvsurvey-sc-route-overlay.png"),
                    PngBitmapEncoderOptions.Default
                );
            }
        }
        Assert.NotEqual(visible[0].Position, visible[1].Position);
        Assert.Equal("Lave", fc.CarrierJumpDestination);
        Assert.Equal("Achenar", sc.CarrierJumpDestination);
        Assert.False(fc.HasRoute);
        Assert.False(sc.HasRoute);
        Assert.Contains(
            visible,
            window =>
                Assert.IsType<FleetCarrierRouteOverlayViewModel>(window.DataContext).Header
                == "SQUADRON CARRIER NEXT JUMP"
        );
        JournalEventEnvelope stats = Event(
            """{"event":"CarrierStats","CarrierID":2,"Callsign":"SQU-002","FuelLevel":700}"""
        );
        fc.ApplyFleetCarrierJumpEvents([stats]);
        sc.ApplyFleetCarrierJumpEvents([stats]);
        JournalEventEnvelope cancel = Event("""{"event":"CarrierJumpCancelled","CarrierID":2}""");
        fc.ApplyFleetCarrierJumpEvents([cancel]);
        sc.ApplyFleetCarrierJumpEvents([cancel]);
        Assert.Equal(FleetCarrierJumpCountdownKind.Scheduled, fc.CarrierJumpKind);
        Assert.Equal(FleetCarrierJumpCountdownKind.CancellationCooldown, sc.CarrierJumpKind);
        sc.ShowCarrierNextJumpInformation = false;
        Assert.Single(host.PreparedWindows, window => window.IsVisible);
        Assert.True(fc.ShowCarrierNextJumpInformation);
        sc.ShowCarrierNextJumpInformation = true;
        registry.SetUserVisibility("PlotSquadronCarrierRoute", false);
        host.Tick();
        FleetCarrierRouteOverlayWindow remaining = Assert.IsType<FleetCarrierRouteOverlayWindow>(
            Assert.Single(host.PreparedWindows, window => window.IsVisible)
        );
        Assert.Equal(
            "FLEET CARRIER NEXT JUMP",
            Assert.IsType<FleetCarrierRouteOverlayViewModel>(remaining.DataContext).Header
        );
        await fc.FlushWorkspaceAsync();
        await sc.FlushWorkspaceAsync();
    }

    [AvaloniaFact]
    public void SquadronSettingsShareTheirMasterShortcutAndPersistIndependentlyOfFleetCarrierSettings()
    {
        using MainWindowViewModel main = CreateMain();
        var view = new OverlaySettingsView(OverlaySettingsCategory.SquadronCarrierRoutes) { DataContext = main };
        Assert.True(view.FindControl<Border>("SquadronCarrierNextJumpCard")!.IsVisible);
        Assert.False(view.FindControl<Border>("CarrierNextJumpCard")!.IsVisible);
        OverlayPanelVisibilityEntryViewModel panel = Assert.Single(
            main.OverlayPanelVisibility.ForCategory(OverlaySettingsCategory.SquadronCarrierRoutes)
        );
        InputBindingViewModel master = main.InputSettings.Bindings.Single(binding =>
            binding.Definition.Action == GlobalInputAction.ToggleSquadronCarrierRouteVisibility
        );
        Assert.Same(master, panel.Shortcut);
        panel.Shortcut.Chord = "CTRL ALT Y";
        Assert.Equal("ALT CTRL Y", master.Chord);
        var inputStore = new GlobalInputSettingsStore(main.AppDataPaths.UiSettingsPath);
        Assert.Equal(master.Chord, inputStore.Load().Bindings[GlobalInputAction.ToggleSquadronCarrierRouteVisibility]);
        master.Chord = "CTRL SHIFT F8";
        Assert.Equal("CTRL SHIFT F8", panel.Shortcut.Chord);
        main.InputSettings.ResetBindingsCommand.Execute(null);
        Assert.Empty(panel.Shortcut.Chord);
        Assert.Empty(inputStore.Load().Bindings[GlobalInputAction.ToggleSquadronCarrierRouteVisibility]);
        main.SquadronCarrierRoute.ShowCarrierNextJumpInformation = false;
        Assert.False(new CarrierNextJumpSettingsStore(main.AppDataPaths.UiSettingsPath, true).Load());
        Assert.True(new CarrierNextJumpSettingsStore(main.AppDataPaths.UiSettingsPath).Load());
        panel.IsEnabled = false;
        Assert.False(
            new OverlayPanelVisibilitySettingsStore(main.AppDataPaths.UiSettingsPath).Load()["PlotSquadronCarrierRoute"]
        );
        Assert.True(
            main.OverlayPanelVisibility.ForCategory(OverlaySettingsCategory.FleetCarrierRoutes).Single().IsEnabled
        );
        panel.IsEnabled = true;
        using FleetCarrierRouteOverlayViewModel preview = Assert.IsType<FleetCarrierRouteOverlayViewModel>(
            OverlayEditorPreviewCatalog.Create("PlotSquadronCarrierRoute", 3)
        );
        Assert.Equal("SQUADRON CARRIER NEXT JUMP", preview.Header);
        Assert.True(preview.IsPassive);
    }

    private MainWindowViewModel CreateMain() =>
        MainWindowViewModelTestBuilder.Create(
            null,
            builder =>
                builder
                    .WithAppDataPaths(
                        new(
                            Path.Combine(directory, "config"),
                            Path.Combine(directory, "data"),
                            Path.Combine(directory, "cache"),
                            []
                        )
                    )
                    .WithExternalNetworkClient(noNetworkClient)
        );

    private RouteWorkspaceViewModel CreateCarrier(FollowRouteCarrierKind kind) =>
        new(
            new FollowRouteService(new FollowRouteStore(directory, FollowRouteKind.FleetCarrier).ForCarrier(kind)),
            new RouteNameImporter(new Resolver()),
            new Routes(),
            new RouteWorkspaceOptions(FollowRouteKind.FleetCarrier, DedicatedCarrier: kind)
        );

    private static JournalEventEnvelope Event(string json)
    {
        Assert.True(JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? entry, out string? error), error);
        return entry!;
    }

    public void Dispose()
    {
        noNetworkClient.Dispose();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    private sealed class Resolver : IStarSystemResolver
    {
        public Task<IReadOnlyList<StarSystemReference>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<StarSystemReference>>([]);
    }

    private sealed class Routes : ISpanshRouteClient
    {
        public Task<IReadOnlyList<FollowRouteHop>> GetRouteAsync(
            SpanshRouteReference route,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<FollowRouteHop>>([]);
    }

    private sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
