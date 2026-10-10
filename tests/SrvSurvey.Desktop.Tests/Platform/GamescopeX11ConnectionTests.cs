using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using SrvSurvey.Desktop;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed partial class GamescopeX11ConnectionTests
{
    [Fact]
    public void DecodesPackedNativeLongValuesAndIgnoresUpstreamTrailingGarbage()
    {
        Assert.Equal(":7", GamescopeX11Connection.DecodeDisplay([BitConverter.ToUInt32([58, 55, 0, 0]), 99]));
        Assert.Equal(":42", GamescopeX11Connection.DecodeDisplay([BitConverter.ToUInt32([58, 52, 50, 0]), 99]));
        Assert.Equal(
            ":42.0",
            GamescopeX11Connection.DecodeDisplay([
                BitConverter.ToUInt32([58, 52, 50, 46]),
                BitConverter.ToUInt32([48, 0, 99, 99]),
            ])
        );
        Assert.Null(GamescopeX11Connection.DecodeDisplay([]));
        Assert.Null(GamescopeX11Connection.DecodeDisplay([0]));
        Assert.Null(GamescopeX11Connection.DecodeDisplay([0x41414141]));
        Assert.Null(GamescopeX11Connection.DecodeDisplay([0x003078]));
        Assert.Null(GamescopeX11Connection.TryOpen("remote:0"));
        Assert.Null(GamescopeX11Connection.TryOpen(":999999"));
    }

    [Fact]
    public void NativeAndManagedEnvironmentsAgreeBeforeXlibInitialization()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }
        string variable = "SRVSURVEY_NATIVE_ENV_TEST_" + Guid.NewGuid().ToString("N");
        Assert.True(GamescopeOverlaySession.SetProcessEnvironment(variable, ":42"));
        Assert.Equal(":42", Environment.GetEnvironmentVariable(variable));
        Assert.Equal(":42", Marshal.PtrToStringUTF8(GetNativeEnvironment(variable)));
        Assert.False(GamescopeOverlaySession.SetProcessEnvironment("invalid=name", ":43"));
    }

    [AvaloniaFact]
    public async Task VerifiesCompositorIdentityPropertyAbiCanvasBoundsAndPassiveRoleOnRealX11()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/Xvfb"))
        {
            return;
        }
        using Process server =
            Process.Start(
                new ProcessStartInfo("/usr/bin/Xvfb")
                {
                    ArgumentList = { "-displayfd", "1", "-screen", "0", "1280x800x24", "-nolisten", "tcp" },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }
            ) ?? throw new InvalidOperationException("Could not start isolated X11.");
        nint display = nint.Zero;
        try
        {
            string? number = await server.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
            string name = ":" + number;
            nint pointer = Marshal.StringToCoTaskMemUTF8(name);
            try
            {
                display = X11Native.XOpenDisplay(pointer);
            }
            finally
            {
                Marshal.FreeCoTaskMem(pointer);
            }
            Assert.NotEqual(nint.Zero, display);
            X11OverlayPlatformService.RegisterErrorHandledDisplay(display);
            nuint root = X11Native.XDefaultRootWindow(display);
            nuint window = XCreateSimpleWindow(display, root, 0, 0, 1280, 800, 0, 0, 0);
            using var connection = GamescopeX11Connection.TryOpen(name);
            Assert.NotNull(connection);
            Assert.Null(connection.ReadIdentity());
            Assert.False(connection.PrepareExternalOverlay(window));
            SetNumbers(display, root, "GAMESCOPE_XWAYLAND_SERVER_ID", [0]);
            Assert.Null(connection.ReadIdentity());
            SetNumbers(display, root, "_NET_SUPPORTING_WM_CHECK", [(uint)window], 33);
            Assert.Null(connection.ReadIdentity());
            SetNumbers(display, window, "_NET_SUPPORTING_WM_CHECK", [(uint)root], 33);
            Assert.Null(connection.ReadIdentity());
            SetNumbers(display, window, "_NET_SUPPORTING_WM_CHECK", [(uint)window], 33);
            _ = XStoreName(display, window, "other-wm");
            _ = X11Native.XSync(display, 0);
            Assert.Null(connection.ReadIdentity());
            _ = XStoreName(display, window, "steamcompmgr");
            _ = X11Native.XSync(display, 0);
            Assert.Equal(new GamescopeDisplayIdentity(0, null), connection.ReadIdentity());
            SetNumbers(display, root, "GAMESCOPE_PID", [321]);
            Assert.Equal(new GamescopeDisplayIdentity(0, 321), connection.ReadIdentity());
            Assert.False(connection.PrepareExternalOverlay(0));
            SetNumbers(display, root, "GAMESCOPE_XWAYLAND_SERVER_ID", [1]);
            Assert.False(connection.PrepareExternalOverlay(window));
            SetNumbers(display, root, "GAMESCOPE_XWAYLAND_SERVER_ID", [0]);
            Assert.True(connection.PrepareExternalOverlay(window));
            Assert.Equal([1u], connection.ReadNumbers("GAMESCOPE_EXTERNAL_OVERLAY", window));
            Assert.Empty(connection.ReadNumbers("missing-property"));
            SetNumbers(display, root, "GAMESCOPE_PID", [1, 2]);
            Assert.Null(connection.ReadIdentity()?.ProcessId);
            SetNumbers(display, root, "GAMESCOPE_PID", [321], 31); // Wrong type.
            Assert.Empty(connection.ReadNumbers("GAMESCOPE_PID"));
            SetNumbers(display, root, "GAMESCOPE_PID", [321], 6, 16); // Wrong format.
            Assert.Empty(connection.ReadNumbers("GAMESCOPE_PID"));
            SetNumbers(display, root, "GAMESCOPE_PID", Enumerable.Repeat(1u, 65).ToArray());
            Assert.Empty(connection.ReadNumbers("GAMESCOPE_PID"));
            SetNumbers(display, root, "GAMESCOPE_FOCUS_DISPLAY", [BitConverter.ToUInt32([58, 55, 0, 0]), 99]);
            SetNumbers(display, root, "GAMESCOPE_FOCUSED_WINDOW", [(uint)window]);
            SetNumbers(display, root, "GAMESCOPE_FOCUSED_APP", [359320]);
            SetNumbers(display, root, "GAMESCOPE_FOCUSED_APP_GFX", [359320]);
            SetNumbers(display, root, "GAMESCOPE_NEW_SCALING_SCALER", [2]);
            GamescopeFocusSnapshot focus = connection.ReadFocus();
            Assert.Equal(":7", focus.Display);
            Assert.Equal(window, focus.Window);
            Assert.Equal(359320u, focus.InputApp);
            Assert.Equal(359320u, focus.GraphicsApp);
            Assert.Equal(2u, focus.ScalingMode);
            Assert.Equal(new PixelRect(0, 0, 1280, 800), focus.OutputBounds);
            using IOverlayPlatformService? platform = X11OverlayPlatformService.TryCreate(
                OverlayHostKind.LinuxXWayland,
                new(name, new(0, null))
            );
            Assert.NotNull(platform);
            Assert.True(platform.Capabilities.UsesGamescopeExternalOverlay);
            var canvas = new CombinedOverlayWindow();
            Assert.False(platform.SetInteractive(canvas, true).IsPrepared);
            ICombinedOverlayNativeService combined = Assert.IsAssignableFrom<ICombinedOverlayNativeService>(platform);
            Assert.False(combined.SetInteractiveRegions(canvas, [new PixelRect(0, 0, 64, 64)]).IsPrepared);
            var editor = new Window();
            Assert.False(platform.Capabilities.SupportsLiveOverlayInteraction);
            Assert.False(platform.SetInteractive(editor, false).IsPrepared);
            Assert.False(combined.SetInteractiveRegions(canvas, []).IsPrepared);
            using IGameWindowTracker? tracker = GamescopeExternalGameWindowTracker.TryCreate(new(name, new(0, null)));
            Assert.NotNull(tracker);
            Assert.Same(GameWindowSnapshot.Unavailable, tracker.GetSnapshot());
            connection.Dispose();
            connection.Dispose();
            Assert.Equal(default, connection.ReadBounds());
            Assert.Null(connection.ReadIdentity());
            Assert.False(connection.PrepareExternalOverlay(window));
        }
        finally
        {
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

    private static void SetNumbers(
        nint display,
        nuint window,
        string name,
        uint[] values,
        nuint type = 6,
        int format = 32
    )
    {
        nint buffer = Marshal.AllocHGlobal(values.Length * nint.Size);
        try
        {
            for (int i = 0; i < values.Length; i++)
            {
                Marshal.WriteIntPtr(buffer, i * nint.Size, unchecked((nint)values[i]));
            }
            _ = X11Native.XChangeProperty(
                display,
                window,
                X11Native.XInternAtom(display, name, 0),
                type,
                format,
                X11Native.PropertyReplace,
                buffer,
                values.Length
            );
            _ = X11Native.XSync(display, 0);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [LibraryImport("libX11.so.6", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int XStoreName(nint display, nuint window, string name);

    [LibraryImport("libc", EntryPoint = "getenv", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint GetNativeEnvironment(string name);

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
