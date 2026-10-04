using System.Diagnostics;
using System.Runtime.InteropServices;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed partial class X11OverlayWindowManagementTests
{
    /// <summary>Checks real X11 attributes, raising order, hidden state, and focus on an isolated display.</summary>
    [Fact]
    public async Task BypassIsAppliedBeforeMappingAndRaisingPreservesFocusAndVisibility()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/Xvfb"))
        {
            return;
        }

        using Process server =
            Process.Start(
                new ProcessStartInfo("/usr/bin/Xvfb")
                {
                    ArgumentList = { "-displayfd", "1", "-screen", "0", "640x480x24", "-nolisten", "tcp" },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }
            ) ?? throw new InvalidOperationException("Could not start the isolated X11 display.");
        nint display = nint.Zero;
        nint name = nint.Zero;
        try
        {
            string? number = await server.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(number);
            name = Marshal.StringToCoTaskMemUTF8(":" + number);
            X11OverlayPlatformService.EnsureErrorHandlerInstalled();
            display = X11Native.XOpenDisplay(name);
            Assert.NotEqual(nint.Zero, display);
            X11OverlayPlatformService.RegisterErrorHandledDisplay(display);
            nuint root = X11Native.XDefaultRootWindow(display);
            nuint live = XCreateSimpleWindow(display, root, 10, 10, 100, 60, 0, 0, 0);
            nuint managed = XCreateSimpleWindow(display, root, 10, 10, 100, 60, 0, 0, 0);
            Assert.True(X11OverlayWindowManagement.TryEnable(X11Native.OverlayWindowOperations, display, live));
            Assert.False(X11OverlayWindowManagement.TryEnable(X11Native.OverlayWindowOperations, display, 0));
            X11OverlayWindowManagement.Raise(X11Native.OverlayWindowOperations, display, 0);
            X11OverlayWindowManagement.Raise(X11Native.OverlayWindowOperations, display, live);
            Assert.Equal(0, ReadAttributes(display, live).MapState);
            _ = X11Native.XMapRaised(display, live);
            _ = X11Native.XMapRaised(display, managed);
            Assert.Equal(0, ReadAttributes(display, managed).OverrideRedirect);
            Assert.False(X11OverlayWindowManagement.TryEnable(X11Native.OverlayWindowOperations, display, managed));
            _ = X11Native.XGetInputFocus(display, out nuint initialFocus, out _);
            Assert.Equal(managed, TopWindow(display, root));
            X11OverlayWindowManagement.Raise(X11Native.OverlayWindowOperations, display, managed);
            X11OverlayWindowManagement.Raise(X11Native.OverlayWindowOperations, display, live);
            Assert.Equal(live, TopWindow(display, root));
            _ = X11Native.XGetInputFocus(display, out nuint finalFocus, out _);
            Assert.Equal(initialFocus, finalFocus);
            X11OverlayWindowManagement.Raise(X11Native.OverlayWindowOperations, display, live, activate: true);
            _ = X11Native.XGetInputFocus(display, out nuint editorFocus, out _);
            Assert.Equal(live, editorFocus);
            X11OverlayWindowManagement.Raise(X11Native.OverlayWindowOperations, display, managed, activate: true);
            _ = X11Native.XGetInputFocus(display, out nuint preservedFocus, out _);
            Assert.Equal(editorFocus, preservedFocus);
            Assert.Equal(1, ReadAttributes(display, live).OverrideRedirect);
            _ = X11Native.XUnmapWindow(display, live);
            X11OverlayWindowManagement.Raise(X11Native.OverlayWindowOperations, display, live);
            Assert.Equal(0, ReadAttributes(display, live).MapState);
        }
        finally
        {
            Marshal.FreeCoTaskMem(name);
            if (display != nint.Zero)
            {
                _ = X11Native.XCloseDisplay(display);
                X11OverlayPlatformService.UnregisterErrorHandledDisplay(display);
            }
            if (!server.HasExited)
            {
                server.Kill();
                await server.WaitForExitAsync();
            }
        }
    }

    /// <summary>Reads a native window's attributes after completing preceding requests.</summary>
    private static X11Native.XWindowAttributes ReadAttributes(nint display, nuint window)
    {
        Assert.NotEqual(0, X11Native.XGetWindowAttributes(display, window, out X11Native.XWindowAttributes attributes));
        return attributes;
    }

    /// <summary>Reads the top child in X11's bottom-to-top stacking order.</summary>
    private static nuint TopWindow(nint display, nuint root)
    {
        Assert.NotEqual(0, X11Native.XQueryTree(display, root, out _, out _, out nint children, out uint count));
        try
        {
            Assert.True(count > 0);
            return unchecked((nuint)Marshal.ReadIntPtr(children, checked((int)(count - 1) * nint.Size)));
        }
        finally
        {
            _ = X11Native.XFree(children);
        }
    }

    /// <summary>Creates an unmapped native window for isolated X11 contract tests.</summary>
    [LibraryImport("libX11.so.6")]
    private static partial nuint XCreateSimpleWindow(
        nint display,
        nuint parent,
        int x,
        int y,
        uint width,
        uint height,
        uint borderWidth,
        nuint border,
        nuint background
    );
}
