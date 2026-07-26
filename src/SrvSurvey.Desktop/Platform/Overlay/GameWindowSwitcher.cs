using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SrvSurvey.Desktop.Platform.Overlay;

public interface IGameWindowSwitcher : IDisposable
{
    int GetAvailableWindowCount();

    bool TryActivateCurrent();

    bool TryActivateNext();
}

public static class GameWindowSwitcher
{
    public static IGameWindowSwitcher CreateCurrent()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsGameWindowSwitcher();
        }

        if (OverlayPlatformCapabilities.DetectCurrent().Host
            == OverlayHostKind.LinuxX11)
        {
            return X11GameWindowSwitcher.TryCreate()
                ?? new UnavailableGameWindowSwitcher();
        }

        return new UnavailableGameWindowSwitcher();
    }
}

internal static class GameWindowCycle
{
    public static nint SelectCurrent(
        IReadOnlyList<nint> windows,
        nint activeWindow,
        nint previousWindow)
    {
        if (windows.Count == 0)
        {
            return nint.Zero;
        }

        if (IndexOf(windows, activeWindow) >= 0)
        {
            return activeWindow;
        }

        return IndexOf(windows, previousWindow) >= 0
            ? previousWindow
            : windows[0];
    }

    public static nint SelectNext(
        IReadOnlyList<nint> windows,
        nint activeWindow,
        nint previousWindow)
    {
        if (windows.Count == 0)
        {
            return nint.Zero;
        }

        var currentIndex = IndexOf(windows, activeWindow);
        if (currentIndex < 0)
        {
            currentIndex = IndexOf(windows, previousWindow);
        }

        return windows[(currentIndex + 1) % windows.Count];
    }

    private static int IndexOf(IReadOnlyList<nint> windows, nint value)
    {
        if (value == nint.Zero)
        {
            return -1;
        }

        for (var index = 0; index < windows.Count; index++)
        {
            if (windows[index] == value)
            {
                return index;
            }
        }

        return -1;
    }
}

internal sealed class UnavailableGameWindowSwitcher : IGameWindowSwitcher
{
    public int GetAvailableWindowCount() => 0;

    public bool TryActivateCurrent() => false;

    public bool TryActivateNext() => false;

    public void Dispose()
    {
    }
}

[SupportedOSPlatform("windows")]
internal sealed partial class WindowsGameWindowSwitcher : IGameWindowSwitcher
{
    private nint previousWindow;

    public int GetAvailableWindowCount()
    {
        try
        {
            return GetCandidateWindows().Length;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or NotSupportedException
                or Win32Exception)
        {
            return 0;
        }
    }

    public bool TryActivateCurrent()
    {
        return TryActivate(activateNext: false);
    }

    public bool TryActivateNext()
    {
        return TryActivate(activateNext: true);
    }

    private bool TryActivate(bool activateNext)
    {
        try
        {
            var handles = GetCandidateWindows();
            var target = activateNext
                ? GameWindowCycle.SelectNext(
                    handles,
                    GetForegroundWindow(),
                    previousWindow)
                : GameWindowCycle.SelectCurrent(
                    handles,
                    GetForegroundWindow(),
                    previousWindow);
            if (target == nint.Zero)
            {
                return false;
            }

            if (IsIconic(target))
            {
                _ = ShowWindow(target, 9);
            }

            if (!SetForegroundWindow(target))
            {
                return false;
            }

            previousWindow = target;
            return true;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or NotSupportedException
                or Win32Exception)
        {
            return false;
        }
    }

    private static nint[] GetCandidateWindows()
    {
        using var currentProcess = Process.GetCurrentProcess();
        var currentSession = currentProcess.SessionId;
        var windows = new List<(int ProcessId, nint Handle)>();
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
                        windows.Add((process.Id, process.MainWindowHandle));
                    }
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException
                        or NotSupportedException
                        or Win32Exception)
                {
                    // Elite can exit while its process details are read.
                }
            }
        }

        return windows
            .OrderBy(window => window.ProcessId)
            .Select(window => window.Handle)
            .ToArray();
    }

    public void Dispose()
    {
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint window, int command);
}

internal sealed class X11GameWindowSwitcher : IGameWindowSwitcher
{
    private const int RevertToParent = 2;
    private const int PropertyReadLength = 16_384;
    private nint display;
    private readonly nuint rootWindow;
    private readonly nuint activeWindowAtom;
    private readonly nuint clientListAtom;
    private readonly nuint clientListStackingAtom;
    private nuint previousWindow;

    private X11GameWindowSwitcher(nint display)
    {
        this.display = display;
        rootWindow = X11Native.XDefaultRootWindow(display);
        activeWindowAtom = GetAtom("_NET_ACTIVE_WINDOW");
        clientListAtom = GetAtom("_NET_CLIENT_LIST");
        clientListStackingAtom = GetAtom("_NET_CLIENT_LIST_STACKING");
    }

    public static IGameWindowSwitcher? TryCreate()
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        var display = nint.Zero;
        try
        {
            display = X11Native.XOpenDisplay(nint.Zero);
            return display == nint.Zero
                ? null
                : new X11GameWindowSwitcher(display);
        }
        catch (Exception exception) when (
            exception is DllNotFoundException
                or EntryPointNotFoundException
                or BadImageFormatException)
        {
            if (display != nint.Zero)
            {
                _ = X11Native.XCloseDisplay(display);
            }

            return null;
        }
    }

    public bool TryActivateNext()
    {
        return TryActivate(activateNext: true);
    }

    public bool TryActivateCurrent()
    {
        return TryActivate(activateNext: false);
    }

    private bool TryActivate(bool activateNext)
    {
        if (display == nint.Zero)
        {
            return false;
        }

        var candidates = GetCandidateWindows();
        var target = activateNext
            ? GameWindowCycle.SelectNext(
                candidates,
                unchecked((nint)ReadSingleWindow(activeWindowAtom)),
                unchecked((nint)previousWindow))
            : GameWindowCycle.SelectCurrent(
                candidates,
                unchecked((nint)ReadSingleWindow(activeWindowAtom)),
                unchecked((nint)previousWindow));
        if (target == nint.Zero)
        {
            return false;
        }

        var targetWindow = unchecked((nuint)target);
        _ = X11Native.XMapRaised(display, targetWindow);
        _ = X11Native.XSetInputFocus(
            display,
            targetWindow,
            RevertToParent,
            0);
        _ = X11Native.XFlush(display);
        previousWindow = targetWindow;
        return true;
    }

    public int GetAvailableWindowCount()
    {
        return display == nint.Zero ? 0 : GetCandidateWindows().Length;
    }

    private nint[] GetCandidateWindows()
    {
        var windows = ReadWindowList(clientListStackingAtom);
        if (windows.Length == 0)
        {
            windows = ReadWindowList(clientListAtom);
        }

        return windows
            .Where(IsEliteWindow)
            .Where(IsViewable)
            .Select(window => unchecked((nint)window))
            .ToArray();
    }

    public void Dispose()
    {
        var currentDisplay = display;
        display = nint.Zero;
        if (currentDisplay != nint.Zero)
        {
            _ = X11Native.XCloseDisplay(currentDisplay);
        }
    }

    private nuint GetAtom(string name)
    {
        return X11Native.XInternAtom(display, name, onlyIfExists: 0);
    }

    private bool IsViewable(nuint window)
    {
        return X11Native.XGetWindowAttributes(
                display,
                window,
                out var attributes) != 0
            && attributes.MapState == X11Native.IsViewable;
    }

    private bool IsEliteWindow(nuint window)
    {
        string? resourceName = null;
        string? resourceClass = null;
        if (X11Native.XGetClassHint(display, window, out var classHint) != 0)
        {
            try
            {
                resourceName = ReadNativeString(classHint.ResourceName);
                resourceClass = ReadNativeString(classHint.ResourceClass);
            }
            finally
            {
                Free(classHint.ResourceName);
                Free(classHint.ResourceClass);
            }
        }

        string? title = null;
        if (X11Native.XFetchName(display, window, out var nativeTitle) != 0)
        {
            try
            {
                title = ReadNativeString(nativeTitle);
            }
            finally
            {
                Free(nativeTitle);
            }
        }

        return EliteGameWindowIdentity.MatchesX11(
            resourceName,
            resourceClass,
            title);
    }

    private nuint ReadSingleWindow(nuint atom)
    {
        var values = ReadWindowList(atom, rootWindow);
        return values.Length == 0 ? 0 : values[0];
    }

    private nuint[] ReadWindowList(nuint atom, nuint? window = null)
    {
        if (atom == 0
            || X11Native.XGetWindowProperty(
                display,
                window ?? rootWindow,
                atom,
                nint.Zero,
                (nint)PropertyReadLength,
                delete: 0,
                requestedType: 0,
                out _,
                out var actualFormat,
                out var itemCount,
                out _,
                out var propertyData) != 0
            || propertyData == nint.Zero)
        {
            return [];
        }

        try
        {
            if (actualFormat != 32 || itemCount == 0 || itemCount > int.MaxValue)
            {
                return [];
            }

            var values = new nuint[(int)itemCount];
            for (var index = 0; index < values.Length; index++)
            {
                values[index] = unchecked((nuint)Marshal.ReadIntPtr(
                    propertyData,
                    index * nint.Size));
            }

            return values;
        }
        finally
        {
            Free(propertyData);
        }
    }

    private static string? ReadNativeString(nint value)
    {
        return value == nint.Zero ? null : Marshal.PtrToStringUTF8(value);
    }

    private static void Free(nint value)
    {
        if (value != nint.Zero)
        {
            _ = X11Native.XFree(value);
        }
    }
}
