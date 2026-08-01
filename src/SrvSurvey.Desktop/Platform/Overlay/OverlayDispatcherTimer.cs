using System.Diagnostics;
using Avalonia.Threading;

namespace SrvSurvey.Desktop.Platform.Overlay;

/// <summary>
/// Preserves the existing per-overlay timer contract while sharing one UI
/// dispatcher wake-up across every overlay coordinator.
/// </summary>
internal sealed class OverlayDispatcherTimer
{
    private TimeSpan nextTick;
    private bool isEnabled;

    public TimeSpan Interval { get; init; }

    public event EventHandler? Tick;

    public void Start()
    {
        OverlayDispatcherTimerScheduler.Start(this);
    }

    public void Stop()
    {
        OverlayDispatcherTimerScheduler.Stop(this);
    }

    internal void Arm(TimeSpan now)
    {
        if (Interval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "An overlay timer requires a positive interval.");
        }

        isEnabled = true;
        nextTick = now + Interval;
    }

    internal void Disarm()
    {
        isEnabled = false;
    }

    internal bool Pulse(TimeSpan now)
    {
        if (!isEnabled || now < nextTick)
        {
            return false;
        }

        // Match DispatcherTimer behavior after a delayed UI frame: issue one
        // callback and schedule the next interval without a catch-up burst.
        nextTick = now + Interval;
        Tick?.Invoke(this, EventArgs.Empty);
        return true;
    }
}

internal static class OverlayDispatcherTimerScheduler
{
    private static readonly object Gate = new();
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly HashSet<OverlayDispatcherTimer> Timers = [];
    private static readonly DispatcherTimer DispatcherTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(50),
    };

    static OverlayDispatcherTimerScheduler()
    {
        DispatcherTimer.Tick += OnTick;
    }

    public static void Start(OverlayDispatcherTimer timer)
    {
        ArgumentNullException.ThrowIfNull(timer);

        lock (Gate)
        {
            timer.Arm(Clock.Elapsed);
            if (!Timers.Add(timer) || Timers.Count != 1)
            {
                return;
            }

            DispatcherTimer.Start();
        }
    }

    public static void Stop(OverlayDispatcherTimer timer)
    {
        ArgumentNullException.ThrowIfNull(timer);

        lock (Gate)
        {
            timer.Disarm();
            if (!Timers.Remove(timer) || Timers.Count != 0)
            {
                return;
            }

            DispatcherTimer.Stop();
        }
    }

    private static void OnTick(object? sender, EventArgs eventArgs)
    {
        OverlayDispatcherTimer[] timers;
        var now = Clock.Elapsed;
        lock (Gate)
        {
            timers = [.. Timers];
        }

        foreach (var timer in timers)
        {
            timer.Pulse(now);
        }
    }
}
