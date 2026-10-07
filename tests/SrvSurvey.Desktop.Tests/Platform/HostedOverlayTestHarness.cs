using Avalonia;
using Avalonia.Controls;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

/// <summary>An adapter-backed presentation session whose platforms, trackers, and timers are recorded.</summary>
internal sealed class HostedOverlayTestHarness : IDisposable
{
    private readonly List<ManualTimer> timers = [];

    public HostedOverlayTestHarness(
        OverlayWindowRegistry? registry = null,
        Func<IGameWindowTracker>? createTracker = null,
        LegacyOverlayLayout? overlayLayout = null
    )
    {
        Session = OverlayPresentationSession.CreateForAdapters(
            new OverlayPresentationDecision(OverlayPresentationMode.MultipleWindows, "Hosted overlay test session"),
            new OverlayPresentationSessionDependencies(
                CreatePlatform,
                createTracker ?? CreateTracker,
                CreateTimer,
                overlayLayout ?? LegacyOverlayLayout.Empty,
                WindowRegistry: registry
            )
        );
    }

    public static GameWindowSnapshot AvailableGameWindow { get; } =
        new(
            NativeHandle: (nint)1,
            ProcessId: 42,
            ClientBounds: new PixelRect(0, 0, 1920, 1080),
            IsVisible: true,
            IsForeground: true
        );

    public OverlayPresentationSession Session { get; }

    public GameWindowSnapshot GameWindow { get; set; } = AvailableGameWindow;

    public Func<Window, OverlayPreparationResult> Prepare { get; set; } =
        _ => new OverlayPreparationResult(IsPrepared: true, IsClickThrough: true, Status: "Prepared");

    public Func<Window, bool, OverlayInteractionResult> SetInteractive { get; set; } =
        (_, interactive) => new OverlayInteractionResult(IsPrepared: true, IsInteractive: interactive, "Prepared");

    public List<Window> PreparedWindows { get; } = [];

    public List<Window> InteractiveWindows { get; } = [];

    public List<TimeSpan> PollIntervals { get; } = [];

    public int PlatformsCreated { get; private set; }

    public int PlatformsDisposed { get; private set; }

    public int TrackersCreated { get; private set; }

    public int TrackersDisposed { get; private set; }

    public int RunningTimers => timers.Count(timer => timer.IsStarted);

    public void Tick()
    {
        foreach (ManualTimer timer in timers.ToArray())
        {
            timer.Pulse();
        }
    }

    public void Dispose()
    {
        Session.Dispose();
    }

    private RecordingPlatform CreatePlatform()
    {
        PlatformsCreated++;
        return new RecordingPlatform(this);
    }

    private RecordingTracker CreateTracker()
    {
        TrackersCreated++;
        return new RecordingTracker(this);
    }

    private ManualTimer CreateTimer(TimeSpan interval)
    {
        var timer = new ManualTimer();
        timers.Add(timer);
        PollIntervals.Add(interval);
        return timer;
    }

    private sealed class RecordingPlatform(HostedOverlayTestHarness harness) : IOverlayPlatformService
    {
        public OverlayPlatformCapabilities Capabilities { get; } =
            OverlayPlatformCapabilities.ForHost(OverlayHostKind.Windows);

        public OverlayPreparationResult PreparePassiveWindow(Window window)
        {
            harness.PreparedWindows.Add(window);
            return harness.Prepare(window);
        }

        public OverlayInteractionResult SetInteractive(Window window, bool interactive)
        {
            if (interactive)
            {
                harness.InteractiveWindows.Add(window);
            }

            return harness.SetInteractive(window, interactive);
        }

        public void Dispose()
        {
            harness.PlatformsDisposed++;
        }
    }

    private sealed class RecordingTracker(HostedOverlayTestHarness harness) : IGameWindowTracker
    {
        public GameWindowSnapshot GetSnapshot() => harness.GameWindow;

        public void Dispose()
        {
            harness.TrackersDisposed++;
        }
    }

    private sealed class ManualTimer : IHostedOverlayTimer
    {
        public event EventHandler? Tick;

        public bool IsStarted { get; private set; }

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
            if (IsStarted)
            {
                Tick?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
