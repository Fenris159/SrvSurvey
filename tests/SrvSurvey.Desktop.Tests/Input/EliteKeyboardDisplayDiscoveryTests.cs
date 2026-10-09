using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Input;

public sealed class EliteKeyboardDisplayDiscoveryTests
{
    /// <summary>Uses focused clients before the overlay monitor, and declines ambiguous unmapped displays.</summary>
    [Fact]
    public void MultipleClientsFollowFocusThenOverlayMonitor()
    {
        string root = Path.Combine(Path.GetTempPath(), "srv-game-discovery-" + Guid.NewGuid());
        try
        {
            WriteProcess(root, "self", "1000", "SrvSurvey", "SrvSurvey", ":0");
            WriteProcess(root, "10", "1000", "EliteDangerous6", "/game/EliteDangerous64.exe", ":2");
            WriteProcess(root, "20", "1000", "EliteDangerous6", "/game/EliteDangerous64.exe", ":3");
            var left = new Avalonia.PixelRect(-1920, -100, 1920, 1080);
            var right = new Avalonia.PixelRect(0, 0, 2560, 1440);
            GameWindowSnapshot[] windows = [new(1, 10, left, true, true), new(2, 20, right, true, false)];
            var context = new KeyboardDisplayContext(right, windows, []);
            Assert.Equal(10, EliteKeyboardDisplayDiscovery.Read(root, context: context)?.ProcessId);
            Assert.Equal(
                20,
                EliteKeyboardDisplayDiscovery
                    .Read(
                        root,
                        context: context with
                        {
                            Windows = [windows[0] with { IsForeground = false }, windows[1]],
                        }
                    )
                    ?.ProcessId
            );
            Assert.Null(EliteKeyboardDisplayDiscovery.Read(root));
            Assert.Null(
                EliteKeyboardDisplayDiscovery.Read(
                    root,
                    context: context with
                    {
                        OverlayMonitor = new Avalonia.PixelRect(10000, 0, 100, 100),
                        Windows = [],
                    }
                )
            );
            Assert.Null(
                EliteKeyboardDisplayDiscovery.Read(
                    root,
                    context: context with
                    {
                        Windows = [windows[0] with { ClientBounds = right, IsForeground = false }, windows[1]],
                    }
                )
            );
            Assert.Equal(
                20,
                EliteKeyboardDisplayDiscovery
                    .Read(
                        root,
                        context: context with
                        {
                            Windows = [],
                            Bridges = [new(456, ":3.0", right), new(123, ":2", left)],
                        }
                    )
                    ?.ProcessId
            );
            File.WriteAllText(Path.Combine(root, "10", "environ"), "DISPLAY=:3.0\0");
            Assert.NotNull(EliteKeyboardDisplayDiscovery.Read(root));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>Maps a nested client through a gamescope ancestor, tolerating inaccessible and cyclic process trees.</summary>
    [Fact]
    public void MonitorFallbackMapsGamescopeParentWithoutGuessingFromNestedCoordinates()
    {
        string root = Path.Combine(Path.GetTempPath(), "srv-game-parent-" + Guid.NewGuid());
        try
        {
            WriteProcess(root, "self", "1000", "SrvSurvey", "SrvSurvey", ":0");
            WriteProcess(root, "10", "1000", "EliteDangerous6", "/game/EliteDangerous64.exe", ":2");
            WriteProcess(root, "20", "1000", "EliteDangerous6", "/game/EliteDangerous64.exe", ":3");
            WriteProcess(root, "30", "1000", "gamescope", "gamescope", ":0");
            File.AppendAllText(Path.Combine(root, "20", "status"), "PPid:\t30\n");
            var monitor = new Avalonia.PixelRect(1920, 0, 2560, 1440);
            var context = new KeyboardDisplayContext(monitor, [new(1, 30, monitor, true, false)], []);
            Assert.Equal(20, EliteKeyboardDisplayDiscovery.Read(root, context: context)?.ProcessId);
            File.WriteAllText(Path.Combine(root, "30", "comm"), "launcher");
            File.AppendAllText(Path.Combine(root, "30", "status"), "PPid:\t20\n");
            Assert.Null(EliteKeyboardDisplayDiscovery.Read(root, context: context));
            File.WriteAllText(Path.Combine(root, "20", "status"), "Uid:\t1000\nPPid:\t404\n");
            Assert.Null(EliteKeyboardDisplayDiscovery.Read(root, context: context));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>Rejects remote hosts, malformed names, and shell-like display strings.</summary>
    [Theory]
    [InlineData(":0", true)]
    [InlineData(":42.1", true)]
    [InlineData(":", false)]
    [InlineData(":1.", false)]
    [InlineData("host:0", false)]
    [InlineData(":1.2.3", false)]
    [InlineData(":a", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ValidatesDiscoveredDisplay(string? value, bool valid) =>
        Assert.Equal(valid, EliteKeyboardDisplayDiscovery.IsLocalDisplay(value));

    /// <summary>Discovers only a same-user Elite executable and tolerates a disappearing process.</summary>
    [Fact]
    public void FindsEliteWithoutBridgeAndIgnoresOtherUsersAndMisleadingArguments()
    {
        string root = Path.Combine(Path.GetTempPath(), "srv-proc-" + Guid.NewGuid());
        try
        {
            WriteProcess(root, "self", "1000", "SrvSurvey", "SrvSurvey", ":0");
            WriteProcess(root, "1", "2000", "EliteDangerous6", "/game/EliteDangerous64.exe", ":1");
            WriteProcess(root, "2", "1000", "other", "/game/EliteDangerous64.exe", ":2");
            WriteProcess(root, "3", "1000", "EliteDangerous6", "/game/not-elite.exe", ":3");
            WriteProcess(root, "4", "1000", "EliteDangerous6", "C:\\game\\EliteDangerous64.exe", "remote:4");
            WriteProcess(root, "5", "1000", "EliteDangerous64", "/game/EliteDangerous64.exe", ":5.0");
            Assert.Equal(new EliteKeyboardDisplay(5, ":5.0"), EliteKeyboardDisplayDiscovery.Read(root));
            Assert.NotNull(EliteKeyboardDisplayDiscovery.Read(root, requireDisplay: false));
            File.Delete(Path.Combine(root, "5", "environ"));
            Assert.Null(EliteKeyboardDisplayDiscovery.Read(root));
            File.Delete(Path.Combine(root, "self", "status"));
            Assert.Null(EliteKeyboardDisplayDiscovery.Read(root));
            Assert.Null(EliteKeyboardDisplayDiscovery.Read(root + "/missing"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>Creates minimal process metadata without using any actual user's environment.</summary>
    private static void WriteProcess(
        string root,
        string pid,
        string owner,
        string comm,
        string executable,
        string display
    )
    {
        string folder = Path.Combine(root, pid);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "status"), $"Name:\t{comm}\nUid:\t{owner}\t{owner}\t{owner}\t{owner}\n");
        File.WriteAllText(Path.Combine(folder, "comm"), comm + "\n");
        File.WriteAllText(Path.Combine(folder, "cmdline"), executable + "\0");
        File.WriteAllText(Path.Combine(folder, "environ"), "DISPLAY=" + display + "\0");
    }
}
