using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;

namespace SrvSurvey.Desktop.Platform.Overlay;

public interface IGameWindowTracker : IDisposable
{
    GameWindowSnapshot GetSnapshot();
}

public static class GameWindowTracker
{
    public static IGameWindowTracker CreateCurrent()
    {
        return SharedGameWindowTrackerPool.Acquire(CreatePlatformTracker);
    }

    private static IGameWindowTracker CreatePlatformTracker()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsGameWindowTracker();
        }

        if (OverlayPlatformCapabilities.DetectCurrent()
            .UsesX11Compatibility)
        {
            return X11GameWindowTracker.TryCreate()
                ?? new UnavailableGameWindowTracker();
        }

        return new UnavailableGameWindowTracker();
    }
}

internal static class SharedGameWindowTrackerPool
{
    private static readonly object Gate = new();
    private static CachedGameWindowTracker? tracker;
    private static int leaseCount;

    public static IGameWindowTracker Acquire(
        Func<IGameWindowTracker> trackerFactory)
    {
        ArgumentNullException.ThrowIfNull(trackerFactory);

        lock (Gate)
        {
            tracker ??= new CachedGameWindowTracker(trackerFactory());
            leaseCount++;
            return new SharedGameWindowTrackerLease(tracker, Release);
        }
    }

    private static void Release()
    {
        CachedGameWindowTracker? releasedTracker = null;
        lock (Gate)
        {
            if (leaseCount == 0)
            {
                return;
            }

            leaseCount--;
            if (leaseCount == 0)
            {
                releasedTracker = tracker;
                tracker = null;
            }
        }

        releasedTracker?.Dispose();
    }
}

internal sealed class SharedGameWindowTrackerLease(
    CachedGameWindowTracker tracker,
    Action release) : IGameWindowTracker
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "CA2213:Disposable fields should be disposed",
        Justification = "The shared pool owns the tracker; this lease releases only its reference count.")]
    private CachedGameWindowTracker? tracker = tracker;
    private Action? release = release;

    public GameWindowSnapshot GetSnapshot()
    {
        return Volatile.Read(ref tracker)?.GetSnapshot()
            ?? GameWindowSnapshot.Unavailable;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref tracker, null) is null)
        {
            return;
        }

        Interlocked.Exchange(ref release, null)?.Invoke();
    }
}

internal sealed class CachedGameWindowTracker : IGameWindowTracker
{
    internal static readonly TimeSpan DefaultFreshness =
        TimeSpan.FromMilliseconds(40);

    private readonly object gate = new();
    private readonly IGameWindowTracker inner;
    private readonly Func<long> timestampProvider;
    private readonly long freshnessTimestampTicks;
    private GameWindowSnapshot snapshot = GameWindowSnapshot.Unavailable;
    private long sampledAt;
    private bool hasSnapshot;
    private bool disposed;

    public CachedGameWindowTracker(
        IGameWindowTracker inner,
        TimeSpan? freshness = null,
        Func<long>? timestampProvider = null)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.timestampProvider = timestampProvider ?? Stopwatch.GetTimestamp;
        var effectiveFreshness = freshness ?? DefaultFreshness;
        if (effectiveFreshness < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(freshness));
        }

        freshnessTimestampTicks = checked((long)Math.Ceiling(
            effectiveFreshness.TotalSeconds * Stopwatch.Frequency));
    }

    public GameWindowSnapshot GetSnapshot()
    {
        lock (gate)
        {
            if (disposed)
            {
                return GameWindowSnapshot.Unavailable;
            }

            var now = timestampProvider();
            if (hasSnapshot
                && now >= sampledAt
                && now - sampledAt <= freshnessTimestampTicks)
            {
                return snapshot;
            }

            snapshot = inner.GetSnapshot();
            sampledAt = now;
            hasSnapshot = true;
            return snapshot;
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            inner.Dispose();
        }
    }
}

public sealed record GameWindowSnapshot(
    nint NativeHandle,
    int? ProcessId,
    PixelRect ClientBounds,
    bool IsVisible,
    bool IsForeground)
{
    public bool IsAvailable => NativeHandle != nint.Zero
        && ClientBounds.Width > 0
        && ClientBounds.Height > 0;

    public static GameWindowSnapshot Unavailable { get; } = new(
        nint.Zero,
        null,
        default,
        IsVisible: false,
        IsForeground: false);
}

internal sealed class UnavailableGameWindowTracker : IGameWindowTracker
{
    public GameWindowSnapshot GetSnapshot()
    {
        return GameWindowSnapshot.Unavailable;
    }

    public void Dispose()
    {
    }
}

[SupportedOSPlatform("windows")]
internal sealed partial class WindowsGameWindowTracker : IGameWindowTracker
{
    private nint windowHandle;
    private nint inspectedForeground;
    private bool inspectedForegroundIsElite;

    public GameWindowSnapshot GetSnapshot()
    {
        try
        {
            var foreground = GetForegroundWindow();
            if (foreground != nint.Zero
                && foreground != windowHandle
                && foreground != inspectedForeground)
            {
                inspectedForeground = foreground;
                inspectedForegroundIsElite = IsEliteWindow(foreground);
            }

            if (foreground != nint.Zero
                && (foreground == windowHandle
                    || (foreground == inspectedForeground
                        && inspectedForegroundIsElite)))
            {
                windowHandle = foreground;
            }

            if (windowHandle == nint.Zero || !IsWindow(windowHandle))
            {
                windowHandle = FindGameWindow(foreground);
            }

            if (windowHandle == nint.Zero
                || !TryGetProcessId(windowHandle, out var processId)
                || !GetClientRect(windowHandle, out var clientRect)
                || !ClientToScreen(windowHandle, ref clientRect.TopLeft))
            {
                windowHandle = nint.Zero;
                return GameWindowSnapshot.Unavailable;
            }

            var width = clientRect.Right - clientRect.Left;
            var height = clientRect.Bottom - clientRect.Top;
            if (width <= 0 || height <= 0)
            {
                return new GameWindowSnapshot(
                    windowHandle,
                    processId,
                    default,
                    IsVisible: false,
                    IsForeground: foreground == windowHandle);
            }

            return new GameWindowSnapshot(
                windowHandle,
                processId,
                new PixelRect(
                    clientRect.TopLeft.X,
                    clientRect.TopLeft.Y,
                    width,
                    height),
                IsVisibleWindow(windowHandle) && !IsIconic(windowHandle),
                foreground == windowHandle);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or InvalidOperationException
                or NotSupportedException
                or Win32Exception)
        {
            windowHandle = nint.Zero;
            return GameWindowSnapshot.Unavailable;
        }
    }

    public void Dispose()
    {
    }

    private static nint FindGameWindow(nint foreground)
    {
        using var currentProcess = Process.GetCurrentProcess();
        var currentSession = currentProcess.SessionId;
        var firstWindow = nint.Zero;
        foreach (var process in Process.GetProcessesByName(
                     EliteGameWindowIdentity.WindowsProcessName))
        {
            using (process)
            {
                try
                {
                    if (process.SessionId == currentSession
                        && process.MainWindowHandle != nint.Zero)
                    {
                        if (process.MainWindowHandle == foreground)
                        {
                            return process.MainWindowHandle;
                        }

                        if (firstWindow == nint.Zero)
                        {
                            firstWindow = process.MainWindowHandle;
                        }
                    }
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException
                        or NotSupportedException
                        or Win32Exception)
                {
                    // The process can exit while its window is being inspected.
                }
            }
        }

        return firstWindow;
    }

    private static bool IsEliteWindow(nint handle)
    {
        if (!TryGetProcessId(handle, out var processId))
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            return string.Equals(
                process.ProcessName,
                EliteGameWindowIdentity.WindowsProcessName,
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or InvalidOperationException
                or NotSupportedException)
        {
            return false;
        }
    }

    private static bool TryGetProcessId(nint handle, out int processId)
    {
        _ = GetWindowThreadProcessId(handle, out var nativeProcessId);
        processId = unchecked((int)nativeProcessId);
        return nativeProcessId != 0;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindow(nint window);

    [LibraryImport("user32.dll", EntryPoint = "IsWindowVisible")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsVisibleWindow(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(nint window);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(
        nint window,
        out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(
        nint window,
        out NativeRect rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ClientToScreen(
        nint window,
        ref NativePoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public NativePoint TopLeft;
        public int Right;
        public int Bottom;

        public int Left => TopLeft.X;

        public int Top => TopLeft.Y;
    }
}
