using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class EliteGameProcessDetectorTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-elite-proc-tests-{Guid.NewGuid():N}"
    );

    [Fact]
    public void LinuxDetectorMatchesTruncatedWineProtonAndNativeEliteProcesses()
    {
        string proc = CreateProcRoot("match-truncated");
        WriteProcess(proc, "100", "EliteDangerous6", "/game/EliteDangerous64.exe\0");
        Assert.True(new EliteGameProcessDetector(() => true, proc).IsRunning());

        string flatWineProc = CreateProcRoot("match-flat-wine");
        WriteProcess(
            flatWineProc,
            "101",
            "EliteDangerous6",
            @"C:\Program Files (x86)\Steam\steamapps\common\Elite Dangerous\Products\elite-dangerous-odyssey-64\EliteDangerous64.exe ServerToken 12345 /vr"
                + "\0"
        );
        Assert.True(new EliteGameProcessDetector(() => true, flatWineProc).IsRunning());

        string quotedWineProc = CreateProcRoot("match-quoted-wine");
        WriteProcess(
            quotedWineProc,
            "102",
            "wine64-preloader",
            "\"C:\\Program Files (x86)\\Steam\\EliteDangerous64.exe\" /autorun /vr\0"
        );
        Assert.True(new EliteGameProcessDetector(() => true, quotedWineProc).IsRunning());

        string multiArgPreloader = CreateProcRoot("match-preloader-args");
        WriteProcess(
            multiArgPreloader,
            "103",
            "wine-preloader",
            "/opt/proton/bin/wine64\0Z:\\games\\EliteDangerous64.exe\0/vr\0"
        );
        Assert.True(new EliteGameProcessDetector(() => true, multiArgPreloader).IsRunning());

        string untruncatedProc = CreateProcRoot("match-untruncated");
        WriteProcess(untruncatedProc, "104", "EliteDangerous64", "/opt/ed/EliteDangerous64\0");
        Assert.True(new EliteGameProcessDetector(() => true, untruncatedProc).IsRunning());

        string exeCommProc = CreateProcRoot("match-exe-comm");
        WriteProcess(exeCommProc, "105", "EliteDangerous64.exe", "/opt/ed/EliteDangerous64.exe\0");
        Assert.True(new EliteGameProcessDetector(() => true, exeCommProc).IsRunning());
    }

    [Fact]
    public void LinuxDetectorIgnoresLaunchersZombiesUnrelatedProcessesAndMissingProcRoot()
    {
        string proc = CreateProcRoot("non-matching");
        Directory.CreateDirectory(Path.Combine(proc, "self"));
        Directory.CreateDirectory(Path.Combine(proc, "99")); // missing comm/cmdline
        WriteProcess(proc, "100", "EDLaunch.exe", @"C:\Steam\EDLaunch.exe" + "\0");
        WriteProcess(proc, "101", "EliteDangerous6", ""); // zombie process has empty cmdline
        WriteProcess(proc, "102", "bash", "bash\0-c\0echo EliteDangerous64.exe\0");
        WriteProcess(proc, "103", "EliteDangerous6", @"C:\game\not-elite.exe --target EliteDangerous64.exe" + "\0");
        WriteProcess(proc, "104", "EliteDangerous6", "/game/FakeEliteDangerous64.exe\0");
        WriteProcess(proc, "105", "wine64-preloader", "\"C:\\game\\EDLaunch.exe\" EliteDangerous64.exe\0");
        WriteProcess(proc, "106", "wine64", "\"\"\0   \0/game/not-elite.exex\0");

        Assert.False(new EliteGameProcessDetector(() => true, proc).IsRunning());
        Assert.False(new EliteGameProcessDetector(() => true, Path.Combine(temporaryDirectory, "missing")).IsRunning());
        Assert.False(EliteGameProcessDetector.IsRunningOnLinux(""));
        Assert.False(EliteGameProcessDetector.ContainsEliteExecutable(""));
        Assert.False(EliteGameProcessDetector.ContainsEliteExecutable("\"unterminated"));
    }

    [Fact]
    public void NonLinuxDetectorDelegatesToNamedProcessLookup()
    {
        string? requested = null;
        var present = new EliteGameProcessDetector(
            () => false,
            "/unused",
            name =>
            {
                requested = name;
                return true;
            }
        );
        var absent = new EliteGameProcessDetector(() => false, "/unused", _ => false);

        Assert.True(present.IsRunning());
        Assert.Equal("EliteDangerous64", requested);
        Assert.False(absent.IsRunning());
    }

    [Fact]
    public void LinuxDetectorCachesWithinTtlUsesLastPidFastPathAndFallsBackOnExit()
    {
        string proc = CreateProcRoot("cache-and-fast-path");
        WriteProcess(proc, "200", "EliteDangerous6", "/game/EliteDangerous64.exe\0");
        var clock = new AdjustableTimeProvider();
        var detector = new EliteGameProcessDetector(
            () => true,
            proc,
            timeProvider: clock,
            cacheDuration: TimeSpan.FromSeconds(1)
        );

        Assert.True(detector.IsRunning());

        // Within the TTL window, the cached result is returned without rescanning /proc.
        Directory.Delete(Path.Combine(proc, "200"), recursive: true);
        clock.Advance(TimeSpan.FromMilliseconds(500));
        Assert.True(detector.IsRunning());

        // Past the TTL window, the missing PID triggers a fresh check and reports false.
        clock.Advance(TimeSpan.FromMilliseconds(600));
        Assert.False(detector.IsRunning());

        // Re-create Elite at PID 300, detect it, confirm repeated checks after TTL reuse the PID fast path,
        // then replace PID 300 with PID 301 to verify fallback scan.
        WriteProcess(proc, "300", "EliteDangerous6", "/game/EliteDangerous64.exe\0");
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.True(detector.IsRunning());

        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.True(detector.IsRunning());

        Directory.Delete(Path.Combine(proc, "300"), recursive: true);
        WriteProcess(proc, "301", "EliteDangerous6", "/game/EliteDangerous64.exe\0");
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.True(detector.IsRunning());

        // Zero cache duration bypasses caching immediately.
        var uncached = new EliteGameProcessDetector(
            () => true,
            proc,
            timeProvider: clock,
            cacheDuration: TimeSpan.Zero
        );
        Assert.True(uncached.IsRunning());
        Directory.Delete(Path.Combine(proc, "301"), recursive: true);
        Assert.False(uncached.IsRunning());
    }

    private sealed class AdjustableTimeProvider : TimeProvider
    {
        private long ticks = TimeSpan.TicksPerSecond;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => ticks;

        public void Advance(TimeSpan delta) => ticks += delta.Ticks;
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private string CreateProcRoot(string name)
    {
        string path = Path.Combine(temporaryDirectory, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteProcess(string procRoot, string pid, string comm, string cmdline)
    {
        string dir = Path.Combine(procRoot, pid);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "comm"), comm + "\n");
        File.WriteAllText(Path.Combine(dir, "cmdline"), cmdline);
    }
}
