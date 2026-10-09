using Avalonia.Headless.XUnit;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Platform;

/// <summary>Covers each passive overlay's visibility rule and feature hooks; the lifecycle is covered by <see cref="HostedOverlayWindowTests"/>.</summary>
[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class PassiveOverlayCoordinatorCharacterizationTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-passive-overlay-characterization-{Guid.NewGuid():N}"
    );

    [AvaloniaFact]
    public async Task GroundTargetSuppressionClosesAndReopensItsWindow()
    {
        GroundTargetViewModel groundTarget = await CreateGroundTargetAsync();
        using var overlays = new HostedOverlayTestHarness();
        using var coordinator = new GroundTargetOverlayCoordinator(groundTarget, overlays.Session);
        int visibilityChanges = 0;
        coordinator.VisibilityChanged += (_, _) => visibilityChanges++;

        Assert.True(coordinator.IsVisible);
        Assert.IsType<GroundTargetOverlayWindow>(Assert.Single(overlays.PreparedWindows));

        coordinator.SetSuppressed(true);

        Assert.False(coordinator.IsVisible);

        coordinator.SetSuppressed(false);

        Assert.True(coordinator.IsVisible);
        Assert.Equal(2, overlays.PreparedWindows.Count);
        Assert.Equal(2, visibilityChanges);

        coordinator.Dispose();

        AssertLeasesReleased(overlays, expected: 1);
    }

    [AvaloniaFact]
    public void StationInfoSuppressionClosesReopensAndReportsVisibility()
    {
        using StationInfoViewModel stationInfo = CreateStationInfo();
        using var overlays = new HostedOverlayTestHarness();
        using var coordinator = new StationInfoOverlayCoordinator(stationInfo, overlays.Session);
        int visibilityChanges = 0;
        coordinator.VisibilityChanged += (_, _) => visibilityChanges++;

        Assert.True(coordinator.IsVisible);
        Assert.IsType<StationInfoOverlayWindow>(Assert.Single(overlays.PreparedWindows));

        coordinator.SetSuppressed(true);
        coordinator.SetSuppressed(false);

        Assert.True(coordinator.IsVisible);
        Assert.Equal(2, overlays.PreparedWindows.Count);
        Assert.Equal(2, visibilityChanges);

        coordinator.Dispose();

        AssertLeasesReleased(overlays, expected: 1);
    }

    [AvaloniaFact]
    public void OverlaysKeepTheirLegacyPollingCadence()
    {
        using var overlays = new HostedOverlayTestHarness();
        using var notification = new NotificationOverlayCoordinator(CreateNotification(), overlays.Session);
        using var pulse = new PulseOverlayCoordinator(CreatePulse(), overlays.Session);
        using JumpInfoViewModel jumpInfo = CreateJumpInfo();
        using var jump = new JumpInfoOverlayCoordinator(jumpInfo, overlays.Session);

        Assert.Equal(
            [TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(250)],
            overlays.PollIntervals
        );
    }

    [AvaloniaFact]
    public void PulseFollowsItsEnabledPreferenceAcrossPolls()
    {
        PulseOverlayViewModel pulse = CreatePulse();
        pulse.Enabled = false;
        using var overlays = new HostedOverlayTestHarness();
        using var coordinator = new PulseOverlayCoordinator(pulse, overlays.Session);

        Assert.False(coordinator.IsVisible);

        pulse.Enabled = true;
        pulse.InstallEditorPreview();
        overlays.Tick();

        Assert.True(coordinator.IsVisible);
        Assert.IsType<PulseOverlayWindow>(Assert.Single(overlays.PreparedWindows));

        pulse.Enabled = false;

        Assert.False(coordinator.IsVisible);
    }

    [AvaloniaFact]
    public void JumpInfoPresentsWhileForcedAndAdvancesOnEachPoll()
    {
        using JumpInfoViewModel jumpInfo = CreateJumpInfo();
        using var overlays = new HostedOverlayTestHarness();
        using var coordinator = new JumpInfoOverlayCoordinator(jumpInfo, overlays.Session);
        int visibilityChanges = 0;
        coordinator.VisibilityChanged += (_, _) => visibilityChanges++;

        Assert.False(coordinator.IsVisible);

        Assert.True(jumpInfo.ToggleForcedVisibility());
        overlays.Tick();

        Assert.True(coordinator.IsVisible);
        Assert.IsType<JumpInfoOverlayWindow>(Assert.Single(overlays.PreparedWindows));

        coordinator.SetSuppressed(true);

        Assert.False(coordinator.IsVisible);
        Assert.True(coordinator.IsSuppressed);

        coordinator.SetSuppressed(false);

        Assert.True(coordinator.IsVisible);

        Assert.True(jumpInfo.ToggleForcedVisibility());

        Assert.False(coordinator.IsVisible);
        Assert.Equal(4, visibilityChanges);
        Assert.Equal(2, overlays.PreparedWindows.Count);
    }

    [AvaloniaFact]
    public void PulseRecoversFromPreparationFailureAfterHideAndShow()
    {
        using var overlays = new HostedOverlayTestHarness
        {
            Prepare = _ => new OverlayPreparationResult(false, false, "Transient preparation failure"),
        };
        using var coordinator = new PulseOverlayCoordinator(CreatePulse(), overlays.Session);
        Assert.False(coordinator.IsVisible);
        Assert.Single(overlays.PreparedWindows);

        overlays.Prepare = _ => new OverlayPreparationResult(true, true, "Prepared");
        overlays.Tick();
        Assert.False(coordinator.IsVisible);
        Assert.Single(overlays.PreparedWindows);

        coordinator.SetSuppressed(true);
        coordinator.SetSuppressed(false);

        Assert.True(coordinator.IsVisible);
        Assert.Equal(2, overlays.PreparedWindows.Count);
    }

    [AvaloniaFact]
    public void StreamRetriesPreparationOnItsNextPoll()
    {
        using var overlays = new HostedOverlayTestHarness
        {
            Prepare = _ => new OverlayPreparationResult(false, false, "Transient preparation failure"),
        };
        var stream = new StreamOverlayViewModel(
            new StreamOverlaySettingsStore(Path.Combine(temporaryDirectory, "stream", "settings.json"))
        )
        {
            Enabled = true,
        };
        using var coordinator = new StreamOverlayCoordinator(stream, overlays.Session);
        Assert.Single(overlays.PreparedWindows);
        Assert.False(overlays.PreparedWindows[0].IsVisible);
        Assert.Equal("Transient preparation failure", stream.StatusMessage);
        Assert.Single(overlays.Diagnostics);

        overlays.Tick();
        Assert.Equal("Transient preparation failure", stream.StatusMessage);
        Assert.Single(overlays.Diagnostics);
        Assert.Equal(2, overlays.PreparedWindows.Count);
        Assert.All(overlays.PreparedWindows, window => Assert.False(window.IsVisible));

        overlays.Prepare = _ => new OverlayPreparationResult(true, true, "Prepared");
        overlays.Tick();

        Assert.Equal(3, overlays.PreparedWindows.Count);
        Assert.True(overlays.PreparedWindows[2].IsVisible);
        Assert.Contains("Compositing", stream.StatusMessage);
        overlays.Prepare = _ => new OverlayPreparationResult(false, false, "Failed again");
        stream.Enabled = false;
        stream.Enabled = true;
        Assert.Equal(2, overlays.Diagnostics.Count);
        overlays.Tick();
        Assert.Equal(2, overlays.Diagnostics.Count);
        stream.Enabled = false;
        stream.Enabled = true;
        Assert.Equal(3, overlays.Diagnostics.Count);
    }

    [AvaloniaFact]
    public void HumanSiteMapCommandsRequireTheVisiblePanel()
    {
        var humanSite = new HumanSiteViewModel();
        humanSite.InstallEditorPreview(
            new HumanSiteEditorPreview
            {
                SiteName = "Raven Outpost",
                TemplateText = "Military M2 · threat 2",
                GeometryStatus = "Settlement map aligned",
                FactionText = "Blue Fortune Corp · Anarchy",
                DockingStatusText = "Docking granted · pad 02",
                DistanceText = "186 m from origin",
                ApproachDistanceText = "1.8 km approach distance",
                CommanderPositionText = "x +42.0 m · y -18.0 m · 164°",
                ThreatLevelText = "Threat level 2 · full shield",
            }
        );
        using var overlays = new HostedOverlayTestHarness();
        using var coordinator = new HumanSiteOverlayCoordinator(humanSite, overlays.Session);
        int visibilityChanges = 0;
        coordinator.VisibilityChanged += (_, _) => visibilityChanges++;

        Assert.True(coordinator.IsVisible);
        Assert.True(coordinator.AdjustZoom(zoomIn: true));
        Assert.True(coordinator.ResetZoom());
        Assert.True(coordinator.ToggleHuge());
        Assert.True(humanSite.IsHuge);
        Assert.True(coordinator.IsVisible);

        coordinator.SetSuppressed(true);

        Assert.False(coordinator.IsVisible);
        Assert.False(coordinator.AdjustZoom(zoomIn: true));
        Assert.False(coordinator.ResetZoom());
        Assert.False(coordinator.ToggleHuge());
        Assert.Equal(1, visibilityChanges);
    }

    [AvaloniaFact]
    public void CombatHostsOnePanelPerActivityAndReleasesEveryLease()
    {
        var combat = new CombatViewModel(
            new CombatSettingsStore(Path.Combine(temporaryDirectory, "combat", "settings.json")),
            new CommanderProfileStore(Path.Combine(temporaryDirectory, "combat-profile"))
        );
        using var overlays = new HostedOverlayTestHarness();
        var coordinator = new CombatOverlayCoordinator(combat, overlays.Session);

        Assert.False(coordinator.IsVisible);
        Assert.Equal(2, overlays.PlatformsCreated);

        coordinator.Dispose();

        AssertLeasesReleased(overlays, expected: 2);
    }

    [AvaloniaFact]
    public void SystemSurveyShowsSurfacePanelsAndGatesSurfaceZoom()
    {
        SurfaceSurveyViewModel surface = CreateSurfaceSurvey(out SystemSurveyViewModel survey);
        using var overlays = new HostedOverlayTestHarness(new OverlayWindowRegistry());
        var coordinator = new SystemSurveyOverlayCoordinator(
            survey,
            surface,
            overlays.Session,
            new SystemSurveyOverlayCoordinatorOptions
            {
                GameScreenCapture = new UnavailableScreenCapture(),
                ExobiologyCatalog = ExobiologyReferenceCatalog.LoadEmbedded(),
            }
        );
        int visibilityChanges = 0;
        coordinator.VisibilityChanged += (_, _) => visibilityChanges++;

        Assert.False(coordinator.IsVisible);
        Assert.False(coordinator.AdjustSurfaceZoom(zoomIn: true));

        surface.InstallEditorPreview("Raven 1 a", "HEADING 074°", "No history", [], []);

        Assert.True(coordinator.IsSurfaceVisible);
        Assert.True(coordinator.IsMiniTrackVisible);
        Assert.True(coordinator.IsVisible);
        Assert.True(coordinator.AdjustSurfaceZoom(zoomIn: true));
        Assert.True(coordinator.ResetSurfaceZoom());
        Assert.Contains(overlays.PreparedWindows, window => window is SurfaceSurveyOverlayWindow);
        Assert.Contains(overlays.PreparedWindows, window => window is MiniTrackOverlayWindow);

        coordinator.SetSuppressed(true);

        Assert.False(coordinator.IsVisible);
        Assert.True(coordinator.IsSuppressed);
        Assert.Equal(4, visibilityChanges);

        coordinator.Dispose();

        AssertLeasesReleased(overlays, expected: 10);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            try
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
            catch (IOException)
            {
                // Cleanup must not mask the characterization assertion result.
            }
            catch (UnauthorizedAccessException)
            {
                // Cleanup must not mask the characterization assertion result.
            }
        }
    }

    private static void AssertLeasesReleased(HostedOverlayTestHarness overlays, int expected)
    {
        Assert.Equal(expected, overlays.PlatformsCreated);
        Assert.Equal(expected, overlays.PlatformsDisposed);
        Assert.Equal(expected, overlays.TrackersCreated);
        Assert.Equal(expected, overlays.TrackersDisposed);
        Assert.Equal(0, overlays.RunningTimers);
    }

    private async Task<GroundTargetViewModel> CreateGroundTargetAsync()
    {
        var store = new GroundTargetSettingsStore(temporaryDirectory);
        await store.SaveAsync(new GroundTargetSnapshot(true, new SurfaceCoordinate(0, 1)));
        var viewModel = new GroundTargetViewModel(store);
        viewModel.UpdateStatus(
            new SrvSurvey.Core.Journal.EliteStatus
            {
                Flags = SrvSurvey.Core.Journal.StatusFlags.HasLatLong | SrvSurvey.Core.Journal.StatusFlags.InMainShip,
                Latitude = 0,
                Longitude = 0,
                PlanetRadius = 1_000,
            }
        );
        Assert.True(viewModel.ShouldShow);
        return viewModel;
    }

    private static StationInfoViewModel CreateStationInfo()
    {
        var viewModel = new StationInfoViewModel(new EmptySystemSummaryClient());
        viewModel.InstallEditorPreview(
            new StationInfoEditorPreview
            {
                StationName = "Raven Port",
                StationType = "Planetary Port",
                LargestPad = "Largest pad: Large",
                PrimaryEconomy = "Primary economy: High Tech",
                Faction = "Cooperative · Democracy",
                Updated = "Spansh data updated today",
                IsQuestTagged = false,
                Economies = [],
                Services = [],
                Prohibited = [],
            }
        );
        Assert.True(viewModel.ShouldShow);
        return viewModel;
    }

    private NotificationViewModel CreateNotification()
    {
        return new NotificationViewModel(
            new NotificationSettingsStore(Path.Combine(temporaryDirectory, "notification", "settings.json"))
        );
    }

    private PulseOverlayViewModel CreatePulse()
    {
        return new PulseOverlayViewModel(
            new PulseOverlaySettingsStore(Path.Combine(temporaryDirectory, "pulse", "settings.json"))
        );
    }

    private JumpInfoViewModel CreateJumpInfo()
    {
        var jumpInfo = new JumpInfoViewModel(
            new EmptySystemSummaryClient(),
            new JumpInfoSettingsStore(Path.Combine(temporaryDirectory, "jump-info", "settings.json"))
        );
        jumpInfo.InstallEditorPreview(
            new JumpInfoRoutePlan(
                new JumpTarget("Raven", 99, "K"),
                JumpInfoRouteSource.FollowedRoute,
                TargetLegIndex: 0,
                Legs: [new JumpInfoRouteLeg("Sol", "Raven", 42.1, true, false)],
                TargetPosition: new GalacticCoordinate(100, 20, -40)
            ),
            EmptySystemSummaryClient.CreateSummary("Raven", 99),
            []
        );
        return jumpInfo;
    }

    private SurfaceSurveyViewModel CreateSurfaceSurvey(out SystemSurveyViewModel survey)
    {
        string root = Path.Combine(temporaryDirectory, "surface");
        survey = new SystemSurveyViewModel(new SystemSurveySettingsStore(Path.Combine(root, "settings.json")));
        var store = new SystemSurfaceStore(root);
        return new SurfaceSurveyViewModel(
            survey,
            store,
            new SurfaceSurveyJournalTracker(store, ExobiologyReferenceCatalog.LoadEmbedded())
        );
    }

    private sealed class UnavailableScreenCapture : IGameScreenCapture
    {
        public bool IsAvailable => false;

        public string? UnavailableReason => "Unavailable in tests";

        public CapturedPixelBuffer Capture(Avalonia.PixelRect bounds) => throw new NotSupportedException();

        public void Dispose() { }
    }

    private sealed class EmptySystemSummaryClient : ISystemSummaryClient
    {
        public static SystemSummary CreateSummary(string systemName, long systemAddress)
        {
            return new SystemSummary(
                systemName,
                systemAddress,
                null,
                null,
                null,
                0,
                0,
                null,
                null,
                null,
                null,
                new SystemPoiSummary(0, 0, 0, 0, 0, 0, 0),
                []
            );
        }

        public Task<SystemSummaryLoadResult> GetAsync(
            string systemName,
            long systemAddress,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(new SystemSummaryLoadResult(CreateSummary(systemName, systemAddress), []));
        }
    }
}
