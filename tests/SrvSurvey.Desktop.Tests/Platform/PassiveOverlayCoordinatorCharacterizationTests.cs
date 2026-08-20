using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Platform;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class PassiveOverlayCoordinatorCharacterizationTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-passive-overlay-characterization-{Guid.NewGuid():N}");

    [AvaloniaFact]
    public async Task GroundTargetSuppressionClosesAndReopensItsWindow()
    {
        var groundTarget = await CreateGroundTargetAsync();
        var platform = new RecordingOverlayPlatform();
        var tracker = new RecordingGameWindowTracker(AvailableGameWindow);
        using var coordinator = new GroundTargetOverlayCoordinator(
            groundTarget,
            platform,
            tracker);
        var visibilityChanges = 0;
        coordinator.VisibilityChanged += (_, _) => visibilityChanges++;

        Assert.True(coordinator.IsVisible);
        Assert.Single(platform.PreparedWindows);
        Assert.IsType<GroundTargetOverlayWindow>(platform.PreparedWindows[0]);

        coordinator.SetSuppressed(true);

        Assert.False(coordinator.IsVisible);

        coordinator.SetSuppressed(false);

        Assert.True(coordinator.IsVisible);
        Assert.Equal(2, platform.PreparedWindows.Count);
        Assert.Equal(2, visibilityChanges);

        coordinator.Dispose();

        Assert.True(platform.IsDisposed);
        Assert.True(tracker.IsDisposed);
    }

    [AvaloniaFact]
    public void StationInfoSuppressionClosesReopensAndReportsVisibility()
    {
        using var stationInfo = CreateStationInfo();
        var platform = new RecordingOverlayPlatform();
        var tracker = new RecordingGameWindowTracker(AvailableGameWindow);
        using var coordinator = new StationInfoOverlayCoordinator(
            stationInfo,
            platform,
            tracker);
        var visibilityChanges = 0;
        coordinator.VisibilityChanged += (_, _) => visibilityChanges++;

        Assert.True(coordinator.IsVisible);
        Assert.Single(platform.PreparedWindows);
        Assert.IsType<StationInfoOverlayWindow>(platform.PreparedWindows[0]);

        coordinator.SetSuppressed(true);
        coordinator.SetSuppressed(false);

        Assert.True(coordinator.IsVisible);
        Assert.Equal(2, platform.PreparedWindows.Count);
        Assert.Equal(2, visibilityChanges);

        coordinator.Dispose();

        Assert.True(platform.IsDisposed);
        Assert.True(tracker.IsDisposed);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private async Task<GroundTargetViewModel> CreateGroundTargetAsync()
    {
        var store = new GroundTargetSettingsStore(temporaryDirectory);
        await store.SaveAsync(new GroundTargetSnapshot(
            true,
            new SurfaceCoordinate(0, 1)));
        var viewModel = new GroundTargetViewModel(store);
        viewModel.UpdateStatus(new SrvSurvey.Core.Journal.EliteStatus
        {
            Flags = SrvSurvey.Core.Journal.StatusFlags.HasLatLong
                | SrvSurvey.Core.Journal.StatusFlags.InMainShip,
            Latitude = 0,
            Longitude = 0,
            PlanetRadius = 1_000,
        });
        Assert.True(viewModel.ShouldShow);
        return viewModel;
    }

    private static StationInfoViewModel CreateStationInfo()
    {
        var viewModel = new StationInfoViewModel(new EmptySystemSummaryClient());
        viewModel.InstallEditorPreview(new StationInfoEditorPreview
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
        });
        Assert.True(viewModel.ShouldShow);
        return viewModel;
    }

    private static GameWindowSnapshot AvailableGameWindow { get; } = new(
        NativeHandle: (nint)1,
        ProcessId: 42,
        ClientBounds: new PixelRect(0, 0, 1920, 1080),
        IsVisible: true,
        IsForeground: true);

    private sealed class RecordingOverlayPlatform : IOverlayPlatformService
    {
        public OverlayPlatformCapabilities Capabilities { get; } =
            OverlayPlatformCapabilities.ForHost(OverlayHostKind.Windows);

        public List<Window> PreparedWindows { get; } = [];

        public bool IsDisposed { get; private set; }

        public OverlayPreparationResult PreparePassiveWindow(Window window)
        {
            PreparedWindows.Add(window);
            return new OverlayPreparationResult(
                IsPrepared: true,
                IsClickThrough: true,
                Status: "Prepared");
        }

        public OverlayInteractionResult SetInteractive(
            Window window,
            bool interactive)
        {
            return new OverlayInteractionResult(
                IsPrepared: true,
                IsInteractive: interactive,
                Status: "Prepared");
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private sealed class RecordingGameWindowTracker(GameWindowSnapshot snapshot)
        : IGameWindowTracker
    {
        public bool IsDisposed { get; private set; }

        public GameWindowSnapshot GetSnapshot() => snapshot;

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private sealed class EmptySystemSummaryClient : ISystemSummaryClient
    {
        public Task<SystemSummaryLoadResult> GetAsync(
            string systemName,
            long systemAddress,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new SystemSummaryLoadResult(
                new SystemSummary(
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
                    []),
                []));
        }
    }
}
