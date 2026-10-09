using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using SharpHook.Testing;
using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Input;

public sealed partial class GamescopeKeyboardIntegrationTests
{
    /// <summary>Checks a missing display or RECORD extension degrades to the desktop input source.</summary>
    [Fact]
    public async Task UnavailableRecordingReturnsNull()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/Xvfb"))
        {
            return;
        }
        Assert.Null(X11KeyboardRecord.TryCreate(":99999"));
        using Process server = StartServer(disableRecord: true);
        try
        {
            string name = ":" + await server.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Null(X11KeyboardRecord.TryCreate(name));
        }
        finally
        {
            server.Kill();
            await server.WaitForExitAsync();
        }
    }

    /// <summary>Checks closing the game's X server does not close or crash the desktop application.</summary>
    [Fact]
    public async Task NestedServerShutdownIsRecoverable()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/Xvfb"))
        {
            return;
        }
        using Process server = StartServer();
        try
        {
            string name = ":" + await server.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
            using IX11KeyboardRecord? record = X11KeyboardRecord.TryCreate(name);
            Assert.NotNull(record);
            Assert.Empty(record.ReadEvents());
            server.Kill();
            await server.WaitForExitAsync();
            Assert.Empty(record.ReadEvents());
            Assert.True(record.HasFailed);
            record.Dispose();
            Assert.Empty(record.ReadEvents());
        }
        finally
        {
            if (!server.HasExited)
            {
                server.Kill();
                await server.WaitForExitAsync();
            }
        }
    }

    /// <summary>Starts an isolated server with optional extension suppression.</summary>
    private static Process StartServer(bool disableRecord = false)
    {
        var info = new ProcessStartInfo("/usr/bin/Xvfb")
        {
            ArgumentList = { "-displayfd", "1", "-screen", "0", "640x480x24", "-nolisten", "tcp" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (disableRecord)
        {
            info.ArgumentList.Add("-extension");
            info.ArgumentList.Add("RECORD");
        }
        return Process.Start(info) ?? throw new InvalidOperationException("Could not start the nested test display.");
    }

    /// <summary>Exercises a real nested X server while the desktop hook receives no game keys.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NestedGameShortcutReachesDesktopService(bool withBridge)
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
            ) ?? throw new InvalidOperationException("Could not start the nested test display.");
        nint display = nint.Zero;
        try
        {
            string number =
                await server.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5))
                ?? throw new InvalidOperationException("No nested display number.");
            string displayName = ":" + number;
            nint name = Marshal.StringToCoTaskMemUTF8(displayName);
            try
            {
                display = X11Native.XOpenDisplay(name);
            }
            finally
            {
                Marshal.FreeCoTaskMem(name);
            }
            Assert.NotEqual(nint.Zero, display);
            var nestedInput = new GamescopeKeyboardInput(
                () =>
                    withBridge
                        ? new GamescopeGameWindowBridge(
                            Environment.ProcessId,
                            displayName,
                            new PixelRect(0, 0, 640, 480)
                        )
                        : null,
                desktopDisplay: ":desktop-test",
                readDisplay: () => new EliteKeyboardDisplay(Environment.ProcessId, displayName),
                createTracker: _ => new TestGameWindowTracker { Focused = true }
            );
            Assert.True(nestedInput.ReadEvents(true).Reset);
            using var desktopHook = new TestGlobalHook(TestThreadingMode.Simple);
            await using var service = new GlobalKeyboardHookService(
                GlobalInputSettings.Default with
                {
                    KeyboardEnabled = true,
                },
                [nestedInput, new DesktopKeyboardHookSource(OverlayHostKind.LinuxXWayland, () => desktopHook)],
                new TestGameWindowTracker { Focused = withBridge },
                () => false
            );
            var triggered = new TaskCompletionSource<GlobalInputAction>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            service.ActionTriggered += (_, args) => triggered.TrySetResult(args.Action);
            service.Start();
            await Task.Delay(100);
            foreach (uint key in new uint[] { 64, 50, 32 }) // Xvfb's Alt, Shift, O keycodes.
            {
                _ = XTestFakeKeyEvent(display, key, 1, 0);
            }
            foreach (uint key in new uint[] { 32, 50, 64 })
            {
                _ = XTestFakeKeyEvent(display, key, 0, 0);
            }
            _ = X11Native.XFlush(display);
            Assert.True(await CompletesWithinAsync(triggered.Task), "The nested game's shortcut was never received.");
            Assert.Equal(GlobalInputAction.ToggleOverlayInteraction, await triggered.Task);
        }
        finally
        {
            if (display != nint.Zero)
            {
                _ = X11Native.XCloseDisplay(display);
            }
            if (!server.HasExited)
            {
                server.Kill();
                await server.WaitForExitAsync();
            }
        }
    }

    /// <summary>Bounds the event wait so a broken listener fails without hanging the test host.</summary>
    private static async Task<bool> CompletesWithinAsync(Task task) =>
        await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(2))) == task;

    /// <summary>Sends test keys only to the isolated nested X server.</summary>
    [LibraryImport("libXtst.so.6")]
    private static partial int XTestFakeKeyEvent(nint display, uint keycode, int isPress, nuint delay);
}
