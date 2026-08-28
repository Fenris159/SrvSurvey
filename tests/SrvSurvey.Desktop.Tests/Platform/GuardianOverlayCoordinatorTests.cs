using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Platform;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class GuardianOverlayCoordinatorTests
{
    [AvaloniaFact]
    public async Task SuccessfulZoomPreparationRegistersAnInteractiveChild()
    {
        var root = CreateTemporaryDirectory();
        var existingWindows = OverlayWindowRegistry.Shared.Snapshot()
            .Select(registration => registration.Window)
            .ToHashSet();
        try
        {
            using var guardian = await CreateLiveGuardianAsync(root);
            var platform = new FakeOverlayPlatform(
                zoomClickThrough: true,
                zoomInteractive: true);
            using var coordinator = new GuardianOverlayCoordinator(
                guardian,
                platform,
                new FakeGameWindowTracker(AvailableGameWindow));

            var guardianRegistrations = OverlayWindowRegistry.Shared.Snapshot()
                .Where(registration =>
                    !existingWindows.Contains(registration.Window)
                    && registration.PlotterName == "PlotGuardians")
                .ToArray();

            Assert.Contains(
                platform.PreparedWindows,
                window => window is GuardianZoomOverlayWindow);
            Assert.Contains(
                platform.InteractiveWindows,
                window => window is GuardianZoomOverlayWindow);
            Assert.Contains(
                guardianRegistrations,
                registration =>
                    registration.Window is GuardianOverlayWindow);
            Assert.Contains(
                guardianRegistrations,
                registration =>
                    registration.Window is GuardianZoomOverlayWindow);
            Assert.Single(
                guardianRegistrations,
                registration => registration.ParticipatesInPlacement);
            Assert.Contains(
                guardianRegistrations,
                registration =>
                    registration.Window is GuardianOverlayWindow
                    && registration.ParticipatesInPlacement);
            Assert.Contains(
                guardianRegistrations,
                registration =>
                    registration.Window is GuardianZoomOverlayWindow
                    && !registration.ParticipatesInPlacement);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [AvaloniaTheory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task ZoomPreparationFailureIsLatchedWithoutSuppressingSite(
        bool zoomClickThrough,
        bool zoomInteractive)
    {
        var root = CreateTemporaryDirectory();
        try
        {
            using var guardian = await CreateLiveGuardianAsync(root);
            var platform = new FakeOverlayPlatform(
                zoomClickThrough,
                zoomInteractive);
            using var coordinator = new GuardianOverlayCoordinator(
                guardian,
                platform,
                new FakeGameWindowTracker(AvailableGameWindow));

            Assert.True(coordinator.IsLiveSiteVisible);
            Assert.Equal(1, platform.ZoomPreparationCount);

            coordinator.SetSuppressed(true);
            coordinator.SetSuppressed(false);

            Assert.True(coordinator.IsLiveSiteVisible);
            Assert.Equal(1, platform.ZoomPreparationCount);
            Assert.DoesNotContain(
                OverlayWindowRegistry.Shared.Snapshot(),
                registration =>
                    registration.Window is GuardianZoomOverlayWindow);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static GameWindowSnapshot AvailableGameWindow { get; } = new(
        NativeHandle: (nint)1,
        ProcessId: 42,
        ClientBounds: new PixelRect(0, 0, 1920, 1080),
        IsVisible: true,
        IsForeground: true);

    private static async Task<GuardianViewModel> CreateLiveGuardianAsync(
        string root)
    {
        var guardian = new GuardianViewModel(root);
        await guardian.LoadProfileAsync("F123", isOdyssey: true);
        await guardian.ApplyJournalEventsAsync(
        [
            Parse(
                """{"event":"Location","StarSystem":"Synuefe XR-H d11-102","SystemAddress":3515254557027}"""),
            Parse(
                """{"event":"ApproachSettlement","Name":"$Ancient:#index=1;","Name_Localised":"Ancient Ruins (1)","SystemAddress":3515254557027,"BodyID":13,"BodyName":"Synuefe XR-H d11-102 1 b","Latitude":-46.576923,"Longitude":133.985107}"""),
        ],
        "Test Commander");
        guardian.UpdateStatus(new EliteStatus
        {
            Flags = StatusFlags.HasLatLong | StatusFlags.InSrv,
            Latitude = -46.576923,
            Longitude = 133.985107,
            PlanetRadius = 1_000_000,
        });
        Assert.True(guardian.ShouldShowLiveSiteOverlay);
        return guardian;
    }

    private static JournalEventEnvelope Parse(string json)
    {
        var success = JournalEventEnvelope.TryParse(
            json,
            out var journalEvent,
            out var error);
        Assert.True(success, error);
        return Assert.IsType<JournalEventEnvelope>(journalEvent);
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "SrvSurvey.GuardianOverlayCoordinatorTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class FakeOverlayPlatform(
        bool zoomClickThrough,
        bool zoomInteractive) : IOverlayPlatformService
    {
        public OverlayPlatformCapabilities Capabilities { get; } =
            OverlayPlatformCapabilities.ForHost(OverlayHostKind.Windows);

        public List<Window> PreparedWindows { get; } = [];

        public List<Window> InteractiveWindows { get; } = [];

        public int ZoomPreparationCount { get; private set; }

        public OverlayPreparationResult PreparePassiveWindow(Window window)
        {
            PreparedWindows.Add(window);
            if (window is not GuardianZoomOverlayWindow)
            {
                return new OverlayPreparationResult(true, true, "Prepared");
            }

            ZoomPreparationCount++;
            return new OverlayPreparationResult(
                IsPrepared: zoomClickThrough,
                IsClickThrough: zoomClickThrough,
                Status: zoomClickThrough ? "Prepared" : "Unavailable");
        }

        public OverlayInteractionResult SetInteractive(
            Window window,
            bool interactive)
        {
            if (interactive)
            {
                InteractiveWindows.Add(window);
            }

            var succeeded = window is not GuardianZoomOverlayWindow
                || zoomInteractive;
            return new OverlayInteractionResult(
                IsPrepared: succeeded,
                IsInteractive: interactive && succeeded,
                Status: succeeded ? "Prepared" : "Unavailable");
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeGameWindowTracker(GameWindowSnapshot snapshot)
        : IGameWindowTracker
    {
        public GameWindowSnapshot GetSnapshot() => snapshot;

        public void Dispose()
        {
        }
    }
}
