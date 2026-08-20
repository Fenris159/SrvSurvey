using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class HostedOverlayWindowTests
{
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

    private sealed class RecordingOverlayPlatform : IOverlayPlatformService
    {
        public OverlayPlatformCapabilities Capabilities { get; } =
            OverlayPlatformCapabilities.ForHost(OverlayHostKind.Windows);

        public List<Window> PreparedWindows { get; } = [];

        public OverlayPreparationResult PreparationResult { get; init; } =
            new(true, true, "Prepared");

        public int DisposeCalls { get; private set; }

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

        public void Start()
        {
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
