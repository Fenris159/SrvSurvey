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
        if (OperatingSystem.IsWindows())
        {
            return new WindowsGameWindowTracker();
        }

        if (OverlayPlatformCapabilities.DetectCurrent().Host
            == OverlayHostKind.LinuxX11)
        {
            return X11GameWindowTracker.TryCreate()
                ?? new UnavailableGameWindowTracker();
        }

        return new UnavailableGameWindowTracker();
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
