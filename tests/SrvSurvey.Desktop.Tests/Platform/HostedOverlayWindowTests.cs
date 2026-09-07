using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Platform;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class HostedOverlayWindowTests
{
    [AvaloniaFact]
    public async Task MiningWarningHostFollowsRangeFocusAndVehicleChanges()
    {
        var root = Path.Combine(Path.GetTempPath(), $"SrvSurvey-mining-warning-{Guid.NewGuid():N}");
        try
        {
            using var mining = new SurfaceMiningViewModel(new SystemSurfaceStore(root));
            var scan = new SystemScanState();
            foreach (var json in new[]
            {
                """{"event":"Location","StarSystem":"Test","SystemAddress":42}""",
                """{"event":"Scan","StarSystem":"Test","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Radius":1000000,"PlanetClass":"Rocky body"}""",
            })
            {
                Assert.True(JournalEventEnvelope.TryParse(json, out var envelope, out _));
                scan.Apply(envelope!);
            }
            var commander = new SurfaceSurveySessionContext("F123", "Test", "Test", 42, null);
            var status = new EliteStatus
            {
                Flags = StatusFlags.InSrv | StatusFlags.HasLatLong,
                BodyName = "Test 1",
                PlanetRadius = 1_000_000
            };
            await mining.ApplyUpdateAsync(commander, scan.CreateSnapshot(), status, "mev_rhino");
            await mining.ToggleRigAsync(1);
            var trackers = new List<RecordingGameWindowTracker>();
            var timers = new List<ManualHostedOverlayTimer>();
            var platform = new RecordingOverlayPlatform();
            var registry = new OverlayWindowRegistry();
            using var session = OverlayPresentationSession.CreateForAdapters(
                new OverlayPresentationDecision(OverlayPresentationMode.MultipleWindows, "Mining warning test"),
                new OverlayPresentationSessionDependencies(
                    () => platform,
                    () => { var tracker = new RecordingGameWindowTracker(AvailableGameWindow); trackers.Add(tracker); return tracker; },
                    _ => { var timer = new ManualHostedOverlayTimer(); timers.Add(timer); return timer; },
                    LegacyOverlayLayout.Empty, WindowRegistry: registry));
            using var coordinator = new SurfaceMiningOverlayCoordinator(mining, session);
            Assert.False(coordinator.IsWarningVisible);
            var far = status with { Latitude = .3 };
            await mining.ApplyUpdateAsync(commander, scan.CreateSnapshot(), far, "mev_rhino");
            Assert.True(coordinator.IsWarningVisible);
            var warning = Assert.Single(platform.PreparedWindows.OfType<MiningWarningOverlayWindow>());
            Assert.True(warning.Topmost);
            Assert.True(registry.TryGetPlotterName(warning, out var name));
            Assert.Equal("PlotMiningWarning", name);
            foreach (var tracker in trackers) tracker.Snapshot = AvailableGameWindow with { IsForeground = false };
            foreach (var timer in timers) timer.Pulse();
            Assert.False(coordinator.IsWarningVisible);
            foreach (var tracker in trackers) tracker.Snapshot = AvailableGameWindow;
            foreach (var timer in timers) timer.Pulse();
            Assert.True(coordinator.IsWarningVisible);
            coordinator.SetSuppressed(true);
            Assert.False(coordinator.IsWarningVisible);
            coordinator.SetSuppressed(false);
            Assert.True(coordinator.IsWarningVisible);
            await mining.ApplyUpdateAsync(commander, scan.CreateSnapshot(), status, "mev_rhino");
            Assert.False(coordinator.IsWarningVisible);
            await mining.ApplyUpdateAsync(commander, scan.CreateSnapshot(), far, "mev_rhino");
            Assert.True(coordinator.IsWarningVisible);
            await mining.ApplyUpdateAsync(commander, scan.CreateSnapshot(), far with
            {
                Flags = StatusFlags.HasLatLong,
                Flags2 = StatusFlags2.OnFoot | StatusFlags2.OnFootOnPlanet,
            }, "mev_rhino", parkedSrvType: "mev_rhino");
            Assert.False(coordinator.IsWarningVisible);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [AvaloniaFact]
    public void DispatcherTimerSupportsItsHostedLifecycle()
    {
        var timer = new DispatcherHostedOverlayTimer(TimeSpan.FromMinutes(1));
        var ticks = 0;
        EventHandler handler = (_, _) => ticks++;

        var exception = Record.Exception(() =>
        {
            timer.Tick += handler;
            timer.Start();
            timer.Stop();
            timer.Tick -= handler;
            timer.Dispose();
        });

        Assert.Null(exception);
        Assert.Equal(0, ticks);
    }

    [AvaloniaFact]
    public void CurrentSessionReportsTheDetectedPresentationDecision()
    {
        var expected = OverlayPresentationModeSelector.DetectCurrent(
            OverlayPlatformCapabilities.DetectCurrent());

        using var session = OverlayPresentationSession.CreateCurrent();

        Assert.Equal(expected, session.Decision);
    }

    [AvaloniaFact]
    public void EligibleIntentCreatesPositionsAndPreparesOneWindow()
    {
        var platform = new RecordingOverlayPlatform();
        var tracker = new RecordingGameWindowTracker(AvailableGameWindow);
        var timer = new ManualHostedOverlayTimer();
        var registry = new OverlayWindowRegistry();
        using var session = OverlayPresentationSession.CreateForAdapters(
            new OverlayPresentationDecision(
                OverlayPresentationMode.MultipleWindows,
                "Test session"),
            new OverlayPresentationSessionDependencies(
                () => platform,
                () => tracker,
                _ => timer,
                LegacyOverlayLayout.Empty,
                WindowRegistry: registry));
        var preparation = default(OverlayPreparationResult);
        var fallbackCalls = 0;
        using var hosted = session.HostPassiveWindow(
            new PassiveOverlayWindowDefinition(
                "PlotTrackTarget",
                _ => new Window
                {
                    Width = 128,
                    Height = 108,
                },
                (gameBounds, windowSize) =>
                {
                    fallbackCalls++;
                    Assert.Equal(AvailableGameWindow.ClientBounds, gameBounds);
                    Assert.True(windowSize.Width > 0);
                    Assert.True(windowSize.Height > 0);
                    return new PixelPoint(25, 30);
                },
                result => preparation = result));

        hosted.Reconcile(wantsWindow: true);

        Assert.True(hosted.IsVisible);
        Assert.Equal(OverlayHostHealth.Healthy, hosted.Health);
        Assert.Single(platform.PreparedWindows);
        Assert.Equal(new PixelPoint(25, 30), platform.PreparedWindows[0].Position);
        Assert.Equal("Prepared", preparation?.Status);
        Assert.Equal(1, fallbackCalls);
        Assert.True(timer.IsStarted);
        Assert.True(registry.TryGetPlotterName(
            platform.PreparedWindows[0],
            out var registeredPlotter));
        Assert.Equal("PlotTrackTarget", registeredPlotter);
    }

    [AvaloniaFact]
    public void WindowFactoryFaultIsLatchedReportedAndNotRetried()
    {
        var platform = new RecordingOverlayPlatform();
        var tracker = new RecordingGameWindowTracker(AvailableGameWindow);
        var diagnostics = new List<OverlayHostDiagnostic>();
        using var session = OverlayPresentationSession.CreateForAdapters(
            new OverlayPresentationDecision(
                OverlayPresentationMode.MultipleWindows,
                "Test session"),
            new OverlayPresentationSessionDependencies(
                () => platform,
                () => tracker,
                _ => new ManualHostedOverlayTimer(),
                LegacyOverlayLayout.Empty,
                diagnostics.Add));
        var factoryCalls = 0;
        using var hosted = session.HostPassiveWindow(
            new PassiveOverlayWindowDefinition(
                "PlotTrackTarget",
                _ =>
                {
                    factoryCalls++;
                    throw new InvalidOperationException("Factory failed");
                },
                (_, _) => new PixelPoint(25, 30)));

        hosted.Reconcile(wantsWindow: true);
        hosted.Reconcile(wantsWindow: true);

        Assert.False(hosted.IsVisible);
        Assert.Equal(OverlayHostHealth.Faulted, hosted.Health);
        Assert.Equal(1, factoryCalls);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("PlotTrackTarget", diagnostic.PlotterName);
        Assert.Equal(OverlayHostPhase.Opening, diagnostic.Phase);
        Assert.Equal(OverlayHostHealth.Faulted, diagnostic.Health);
        Assert.Equal("Factory failed", diagnostic.Status);
        Assert.IsType<InvalidOperationException>(diagnostic.Exception);
    }

    [AvaloniaFact]
    public void NullWindowFactoryResultIsLatchedAsAHostFault()
    {
        var diagnostics = new List<OverlayHostDiagnostic>();
        using var session = OverlayPresentationSession.CreateForAdapters(
            new OverlayPresentationDecision(
                OverlayPresentationMode.MultipleWindows,
                "Test session"),
            new OverlayPresentationSessionDependencies(
                () => new RecordingOverlayPlatform(),
                () => new RecordingGameWindowTracker(AvailableGameWindow),
                _ => new ManualHostedOverlayTimer(),
                LegacyOverlayLayout.Empty,
                diagnostics.Add));
        using var hosted = session.HostPassiveWindow(
            new PassiveOverlayWindowDefinition(
                "PlotTrackTarget",
                _ => null!,
                (_, _) => new PixelPoint(25, 30)));

        hosted.Reconcile(wantsWindow: true);

        Assert.Equal(OverlayHostHealth.Faulted, hosted.Health);
        Assert.False(hosted.IsVisible);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("returned null", diagnostic.Status);
    }

    [AvaloniaFact]
    public void UnsupportedCapabilitiesAreReportedOnceAndStopPolling()
    {
        var capabilities = OverlayPlatformCapabilities.ForHost(
            OverlayHostKind.LinuxWayland);
        var platform = new RecordingOverlayPlatform(capabilities);
        var timer = new ManualHostedOverlayTimer();
        var diagnostics = new List<OverlayHostDiagnostic>();
        using var session = OverlayPresentationSession.CreateForAdapters(
            new OverlayPresentationDecision(
                OverlayPresentationMode.MultipleWindows,
                "Test session"),
            new OverlayPresentationSessionDependencies(
                () => platform,
                () => new RecordingGameWindowTracker(AvailableGameWindow),
                _ => timer,
                LegacyOverlayLayout.Empty,
                diagnostics.Add));
        using var hosted = session.HostPassiveWindow(
            new PassiveOverlayWindowDefinition(
                "PlotTrackTarget",
                _ => new Window { Width = 128, Height = 108 },
                (_, _) => new PixelPoint(25, 30)));

        hosted.Reconcile(wantsWindow: true);
        hosted.Reconcile(wantsWindow: true);
        timer.Pulse();

        Assert.False(hosted.IsVisible);
        Assert.False(timer.IsStarted);
        Assert.Equal(OverlayHostHealth.Unsupported, hosted.Health);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(OverlayHostPhase.Hidden, diagnostic.Phase);
        Assert.Equal(OverlayHostHealth.Unsupported, diagnostic.Health);
        Assert.Equal(capabilities.StatusText, diagnostic.Status);
    }

    [AvaloniaFact]
    public void DiagnosticSinkFailureDoesNotEscapeTheHostedLifecycle()
    {
        using var session = OverlayPresentationSession.CreateForAdapters(
            new OverlayPresentationDecision(
                OverlayPresentationMode.MultipleWindows,
                "Test session"),
            new OverlayPresentationSessionDependencies(
                () => new RecordingOverlayPlatform(),
                () => new RecordingGameWindowTracker(AvailableGameWindow),
                _ => new ManualHostedOverlayTimer(),
                LegacyOverlayLayout.Empty,
                _ => throw new InvalidOperationException(
                    "Diagnostic sink failed")));
        using var hosted = session.HostPassiveWindow(
            new PassiveOverlayWindowDefinition(
                "PlotTrackTarget",
                _ => throw new InvalidOperationException("Factory failed"),
                (_, _) => new PixelPoint(25, 30)));

        var exception = Record.Exception(
            () => hosted.Reconcile(wantsWindow: true));

        Assert.Null(exception);
        Assert.Equal(OverlayHostHealth.Faulted, hosted.Health);
    }

    [Fact]
    public void ConstructionFailureReleasesEarlierDependencyLeases()
    {
        var platform = new RecordingOverlayPlatform();
        using var session = OverlayPresentationSession.CreateForAdapters(
            new OverlayPresentationDecision(
                OverlayPresentationMode.MultipleWindows,
                "Test session"),
            new OverlayPresentationSessionDependencies(
                () => platform,
                () => throw new InvalidOperationException("Tracker failed"),
                _ => new ManualHostedOverlayTimer(),
                LegacyOverlayLayout.Empty));

        var exception = Assert.Throws<InvalidOperationException>(
            () => session.HostPassiveWindow(
                new PassiveOverlayWindowDefinition(
                    "PlotTrackTarget",
                    _ => new Window { Width = 128, Height = 108 },
                    (_, _) => new PixelPoint(25, 30))));

        Assert.Equal("Tracker failed", exception.Message);
        Assert.Equal(1, platform.DisposeCalls);
    }

    [Fact]
    public void NonPositivePollingIntervalIsRejectedBeforeLeaseAcquisition()
    {
        var platformFactoryCalls = 0;
        using var session = OverlayPresentationSession.CreateForAdapters(
            new OverlayPresentationDecision(
                OverlayPresentationMode.MultipleWindows,
                "Test session"),
            new OverlayPresentationSessionDependencies(
                () =>
                {
                    platformFactoryCalls++;
                    return new RecordingOverlayPlatform();
                },
                () => new RecordingGameWindowTracker(AvailableGameWindow),
                _ => new ManualHostedOverlayTimer(),
                LegacyOverlayLayout.Empty));
        var definition = CreateDefinition() with
        {
            PollInterval = TimeSpan.Zero,
        };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => session.HostPassiveWindow(definition));

        Assert.Equal("definition", exception.ParamName);
        Assert.Equal(0, platformFactoryCalls);
    }

    [Fact]
    public void TimerFactoryFailureReleasesPlatformAndTrackerLeases()
    {
        var platform = new RecordingOverlayPlatform();
        var tracker = new RecordingGameWindowTracker(AvailableGameWindow);
        using var session = OverlayPresentationSession.CreateForAdapters(
            new OverlayPresentationDecision(
                OverlayPresentationMode.MultipleWindows,
                "Test session"),
            new OverlayPresentationSessionDependencies(
                () => platform,
                () => tracker,
                _ => throw new InvalidOperationException("Timer failed"),
                LegacyOverlayLayout.Empty));

        var exception = Assert.Throws<InvalidOperationException>(
            () => session.HostPassiveWindow(CreateDefinition()));

        Assert.Equal("Timer failed", exception.Message);
        Assert.Equal(1, platform.DisposeCalls);
        Assert.Equal(1, tracker.DisposeCalls);
    }

    [Fact]
    public void TimerStartFailureReleasesEveryAcquiredLease()
    {
        var platform = new RecordingOverlayPlatform();
        var tracker = new RecordingGameWindowTracker(AvailableGameWindow);
        var timer = new ManualHostedOverlayTimer
        {
            StartException = new InvalidOperationException(
                "Timer start failed"),
        };
        using var session = OverlayPresentationSession.CreateForAdapters(
            new OverlayPresentationDecision(
                OverlayPresentationMode.MultipleWindows,
                "Test session"),
            new OverlayPresentationSessionDependencies(
                () => platform,
                () => tracker,
                _ => timer,
                LegacyOverlayLayout.Empty));

        var exception = Assert.Throws<InvalidOperationException>(
            () => session.HostPassiveWindow(CreateDefinition()));

        Assert.Equal("Timer start failed", exception.Message);
        Assert.Equal(1, platform.DisposeCalls);
        Assert.Equal(1, tracker.DisposeCalls);
        Assert.Equal(1, timer.DisposeCalls);
    }

    [AvaloniaFact]
    public async Task BackgroundReconciliationCreatesWindowOnUiThread()
    {
        var platform = new RecordingOverlayPlatform();
        var tracker = new RecordingGameWindowTracker(AvailableGameWindow);
        using var session = OverlayPresentationSession.CreateForAdapters(
            new OverlayPresentationDecision(
                OverlayPresentationMode.MultipleWindows,
                "Test session"),
            new OverlayPresentationSessionDependencies(
                () => platform,
                () => tracker,
                _ => new ManualHostedOverlayTimer(),
                LegacyOverlayLayout.Empty));
        var uiThread = Environment.CurrentManagedThreadId;
        var factoryThread = 0;
        using var hosted = session.HostPassiveWindow(
            new PassiveOverlayWindowDefinition(
                "PlotTrackTarget",
                _ =>
                {
                    factoryThread = Environment.CurrentManagedThreadId;
                    return new Window { Width = 128, Height = 108 };
                },
                (_, _) => new PixelPoint(25, 30)));

        await Task.Run(() => hosted.Reconcile(wantsWindow: true));
        await Dispatcher.UIThread.InvokeAsync(() => { });

        Assert.True(hosted.IsVisible);
        Assert.Equal(uiThread, factoryThread);
    }

    [AvaloniaFact]
    public void ReentrantIntentChangeRunsOneCoalescedFollowUp()
    {
        using var session = OverlayPresentationSession.CreateForAdapters(
            new OverlayPresentationDecision(
                OverlayPresentationMode.MultipleWindows,
                "Test session"),
            new OverlayPresentationSessionDependencies(
                () => new RecordingOverlayPlatform(),
                () => new RecordingGameWindowTracker(AvailableGameWindow),
                _ => new ManualHostedOverlayTimer(),
                LegacyOverlayLayout.Empty));
        HostedOverlayWindow? hosted = null;
        var visibilityChanges = 0;
        hosted = session.HostPassiveWindow(
            new PassiveOverlayWindowDefinition(
                "PlotTrackTarget",
                _ => new Window { Width = 128, Height = 108 },
                (_, _) => new PixelPoint(25, 30),
                _ => hosted!.Reconcile(wantsWindow: false)));
        hosted.VisibilityChanged += (_, _) => visibilityChanges++;

        hosted.Reconcile(wantsWindow: true);

        Assert.False(hosted.IsVisible);
        Assert.Equal(2, visibilityChanges);
    }

    [AvaloniaFact]
    public void PassivePreparationFailureLatchesWithoutPublishingVisibility()
    {
        var platform = new RecordingOverlayPlatform
        {
            PreparationResult = new OverlayPreparationResult(
                IsPrepared: false,
                IsClickThrough: false,
                Status: "Click-through failed"),
        };
        var diagnostics = new List<OverlayHostDiagnostic>();
        using var session = OverlayPresentationSession.CreateForAdapters(
            new OverlayPresentationDecision(
                OverlayPresentationMode.MultipleWindows,
                "Test session"),
            new OverlayPresentationSessionDependencies(
                () => platform,
                () => new RecordingGameWindowTracker(AvailableGameWindow),
                _ => new ManualHostedOverlayTimer(),
                LegacyOverlayLayout.Empty,
                diagnostics.Add));
        var factoryCalls = 0;
        var visibilityChanges = 0;
        var observedPreparations = new List<OverlayPreparationResult>();
        using var hosted = session.HostPassiveWindow(
            new PassiveOverlayWindowDefinition(
                "PlotTrackTarget",
                _ =>
                {
                    factoryCalls++;
                    return new Window { Width = 128, Height = 108 };
                },
                (_, _) => new PixelPoint(25, 30),
                observedPreparations.Add));
        hosted.VisibilityChanged += (_, _) => visibilityChanges++;

        hosted.Reconcile(wantsWindow: true);
        hosted.Reconcile(wantsWindow: true);

        Assert.False(hosted.IsVisible);
        Assert.Equal(OverlayHostHealth.PassivePreparationFailed, hosted.Health);
        Assert.Equal(1, factoryCalls);
        Assert.Equal(0, visibilityChanges);
        Assert.Single(observedPreparations);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(OverlayHostHealth.PassivePreparationFailed, diagnostic.Health);
        Assert.Equal("Click-through failed", diagnostic.Status);
    }

    [AvaloniaFact]
    public void ExternallyClosedWindowReopensWhileIntentRemainsEligible()
    {
        var timer = new ManualHostedOverlayTimer();
        using var session = OverlayPresentationSession.CreateForAdapters(
            new OverlayPresentationDecision(
                OverlayPresentationMode.MultipleWindows,
                "Test session"),
            new OverlayPresentationSessionDependencies(
                () => new RecordingOverlayPlatform(),
                () => new RecordingGameWindowTracker(AvailableGameWindow),
                _ => timer,
                LegacyOverlayLayout.Empty));
        var windows = new List<Window>();
        using var hosted = session.HostPassiveWindow(
            new PassiveOverlayWindowDefinition(
                "PlotTrackTarget",
                _ =>
                {
                    var window = new Window { Width = 128, Height = 108 };
                    windows.Add(window);
                    return window;
                },
                (_, _) => new PixelPoint(25, 30)));
        var visibilityChanges = 0;
        hosted.VisibilityChanged += (_, _) => visibilityChanges++;

        hosted.Reconcile(wantsWindow: true);
        windows[0].Close();
        timer.Pulse();

        Assert.True(hosted.IsVisible);
        Assert.Equal(2, windows.Count);
        Assert.Equal(3, visibilityChanges);
    }

    [AvaloniaFact]
    public void SessionDisposalReleasesHostedResourcesExactlyOnce()
    {
        var platform = new RecordingOverlayPlatform();
        var tracker = new RecordingGameWindowTracker(AvailableGameWindow);
        var timer = new ManualHostedOverlayTimer();
        var session = OverlayPresentationSession.CreateForAdapters(
            new OverlayPresentationDecision(
                OverlayPresentationMode.MultipleWindows,
                "Test session"),
            new OverlayPresentationSessionDependencies(
                () => platform,
                () => tracker,
                _ => timer,
                LegacyOverlayLayout.Empty));
        var hosted = session.HostPassiveWindow(
            new PassiveOverlayWindowDefinition(
                "PlotTrackTarget",
                _ => new Window { Width = 128, Height = 108 },
                (_, _) => new PixelPoint(25, 30)));

        session.Dispose();
        session.Dispose();
        hosted.Dispose();
        hosted.Reconcile(wantsWindow: true);

        Assert.Equal(OverlayHostHealth.Disposed, hosted.Health);
        Assert.Equal(1, platform.DisposeCalls);
        Assert.Equal(1, tracker.DisposeCalls);
        Assert.Equal(1, timer.DisposeCalls);
    }

    [AvaloniaFact]
    public void SessionDisposalContinuesAfterAHostedWindowFails()
    {
        var platforms = new List<RecordingOverlayPlatform>();
        var trackers = new List<RecordingGameWindowTracker>();
        var timers = new List<ManualHostedOverlayTimer>();
        var session = OverlayPresentationSession.CreateForAdapters(
            new OverlayPresentationDecision(
                OverlayPresentationMode.MultipleWindows,
                "Test session"),
            new OverlayPresentationSessionDependencies(
                () =>
                {
                    var platform = new RecordingOverlayPlatform
                    {
                        DisposeException = new InvalidOperationException(
                            "Platform disposal failed"),
                    };
                    platforms.Add(platform);
                    return platform;
                },
                () =>
                {
                    var tracker = new RecordingGameWindowTracker(
                        AvailableGameWindow);
                    trackers.Add(tracker);
                    return tracker;
                },
                _ =>
                {
                    var timer = new ManualHostedOverlayTimer();
                    timers.Add(timer);
                    return timer;
                },
                LegacyOverlayLayout.Empty));
        _ = session.HostPassiveWindow(CreateDefinition());
        _ = session.HostPassiveWindow(CreateDefinition());

        var exception = Assert.Throws<InvalidOperationException>(
            session.Dispose);

        Assert.Equal("Platform disposal failed", exception.Message);
        Assert.Equal(2, platforms.Sum(platform => platform.DisposeCalls));
        Assert.All(trackers, tracker => Assert.Equal(1, tracker.DisposeCalls));
        Assert.All(timers, timer => Assert.Equal(1, timer.DisposeCalls));
    }

    [AvaloniaFact]
    public async Task OffThreadDisposalFailsBeforeReleasingResources()
    {
        var platform = new RecordingOverlayPlatform();
        var tracker = new RecordingGameWindowTracker(AvailableGameWindow);
        var timer = new ManualHostedOverlayTimer();
        using var session = OverlayPresentationSession.CreateForAdapters(
            new OverlayPresentationDecision(
                OverlayPresentationMode.MultipleWindows,
                "Test session"),
            new OverlayPresentationSessionDependencies(
                () => platform,
                () => tracker,
                _ => timer,
                LegacyOverlayLayout.Empty));
        var hosted = session.HostPassiveWindow(CreateDefinition());

        var exception = await Record.ExceptionAsync(
            () => Task.Run(hosted.Dispose));

        Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(OverlayHostHealth.Healthy, hosted.Health);
        Assert.Equal(0, platform.DisposeCalls);
        Assert.Equal(0, tracker.DisposeCalls);
        Assert.Equal(0, timer.DisposeCalls);

        hosted.Dispose();
    }

    [AvaloniaFact]
    public void PollingClosesAndRestoresWindowAsGameEligibilityChanges()
    {
        var tracker = new RecordingGameWindowTracker(AvailableGameWindow);
        var timer = new ManualHostedOverlayTimer();
        using var session = OverlayPresentationSession.CreateForAdapters(
            new OverlayPresentationDecision(
                OverlayPresentationMode.MultipleWindows,
                "Test session"),
            new OverlayPresentationSessionDependencies(
                () => new RecordingOverlayPlatform(),
                () => tracker,
                _ => timer,
                LegacyOverlayLayout.Empty));
        var factoryCalls = 0;
        using var hosted = session.HostPassiveWindow(
            new PassiveOverlayWindowDefinition(
                "PlotTrackTarget",
                _ =>
                {
                    factoryCalls++;
                    return new Window { Width = 128, Height = 108 };
                },
                (_, _) => new PixelPoint(25, 30)));

        hosted.Reconcile(wantsWindow: true);
        tracker.Snapshot = AvailableGameWindow with { IsForeground = false };
        timer.Pulse();
        Assert.False(hosted.IsVisible);

        tracker.Snapshot = AvailableGameWindow;
        timer.Pulse();

        Assert.True(hosted.IsVisible);
        Assert.Equal(2, factoryCalls);
    }

    private static GameWindowSnapshot AvailableGameWindow { get; } = new(
        NativeHandle: (nint)1,
        ProcessId: 42,
        ClientBounds: new PixelRect(0, 0, 1920, 1080),
        IsVisible: true,
        IsForeground: true);

    private static PassiveOverlayWindowDefinition CreateDefinition()
    {
        return new PassiveOverlayWindowDefinition(
            "PlotTrackTarget",
            _ => new Window { Width = 128, Height = 108 },
            (_, _) => new PixelPoint(25, 30));
    }

    private sealed class RecordingOverlayPlatform : IOverlayPlatformService
    {
        public RecordingOverlayPlatform(
            OverlayPlatformCapabilities? capabilities = null)
        {
            Capabilities = capabilities
                ?? OverlayPlatformCapabilities.ForHost(
                    OverlayHostKind.Windows);
        }

        public OverlayPlatformCapabilities Capabilities { get; }

        public List<Window> PreparedWindows { get; } = [];

        public OverlayPreparationResult PreparationResult { get; init; } =
            new(true, true, "Prepared");

        public int DisposeCalls { get; private set; }

        public Exception? DisposeException { get; init; }

        public OverlayPreparationResult PreparePassiveWindow(Window window)
        {
            PreparedWindows.Add(window);
            return PreparationResult;
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
            DisposeCalls++;
            if (DisposeException is not null)
            {
                throw DisposeException;
            }
        }
    }

    private sealed class RecordingGameWindowTracker : IGameWindowTracker
    {
        public RecordingGameWindowTracker(GameWindowSnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        public int DisposeCalls { get; private set; }

        public GameWindowSnapshot Snapshot { get; set; }

        public GameWindowSnapshot GetSnapshot() => Snapshot;

        public void Dispose()
        {
            DisposeCalls++;
        }
    }

    private sealed class ManualHostedOverlayTimer : IHostedOverlayTimer
    {
        public event EventHandler? Tick;

        public bool IsStarted { get; private set; }

        public int DisposeCalls { get; private set; }

        public Exception? StartException { get; init; }

        public void Start()
        {
            if (StartException is not null)
            {
                throw StartException;
            }

            IsStarted = true;
        }

        public void Stop()
        {
            IsStarted = false;
        }

        public void Pulse()
        {
            Tick?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            DisposeCalls++;
            Stop();
        }
    }
}
