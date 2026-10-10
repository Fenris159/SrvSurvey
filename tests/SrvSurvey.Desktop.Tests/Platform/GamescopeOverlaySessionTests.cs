using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class GamescopeOverlaySessionTests
{
    [Fact]
    public void StartupRoutesBeforeRenderingAndReportsPassiveAndFailureStates()
    {
        var messages = new List<string>();
        string? routed = null;
        var session = new GamescopeOverlaySession(":42", new(0, 100));
        (GamescopeOverlaySession? Session, string? Error) startup = GamescopeOverlaySession.Initialize(
            messages.Add,
            ":43",
            () => (session, null),
            display =>
            {
                routed = display;
                return true;
            }
        );
        Assert.Same(session, startup.Session);
        Assert.Null(startup.Error);
        Assert.Equal(":42", routed);
        Assert.Contains(messages, message => message.Contains("inherited display: :43", StringComparison.Ordinal));
        Assert.Contains(messages, message => message.Contains("live pointer interaction", StringComparison.Ordinal));
        startup = GamescopeOverlaySession.Initialize(messages.Add, ":43", () => (session, null), _ => false);
        Assert.Null(startup.Session);
        Assert.Contains("unavailable", startup.Error);
        routed = null;
        startup = GamescopeOverlaySession.Initialize(
            messages.Add,
            ":43",
            () => (null, "ambiguous"),
            display =>
            {
                routed = display;
                return true;
            }
        );
        Assert.Null(startup.Session);
        Assert.Equal("ambiguous", startup.Error);
        Assert.Null(routed);
        messages.Clear();
        startup = GamescopeOverlaySession.Initialize(
            messages.Add,
            ":0",
            () => (null, null),
            _ => throw new InvalidOperationException()
        );
        Assert.Null(startup.Session);
        Assert.Null(startup.Error);
        Assert.Empty(messages);
    }

    [Fact]
    public void MissingNativeLibrariesLeaveTheOrdinaryDesktopPathAvailable()
    {
        Exception[] failures =
        [
            new DllNotFoundException(),
            new EntryPointNotFoundException(),
            new BadImageFormatException(),
        ];
        foreach (Exception failure in failures)
        {
            var messages = new List<string>();
            (GamescopeOverlaySession? Session, string? Error) startup = GamescopeOverlaySession.Initialize(
                messages.Add,
                ":0",
                () => throw failure,
                _ => true
            );
            Assert.Null(startup.Session);
            Assert.Null(startup.Error);
            Assert.Contains("probing is unavailable", Assert.Single(messages));
        }
    }

    [Fact]
    public void OrdinaryDesktopRetainsItsDisplayAndPrimaryNeedsNoSteamLauncher()
    {
        (GamescopeOverlaySession? Session, string? Error) ordinary = GamescopeOverlaySession.Resolve(
            ":42",
            [":43"],
            _ => null
        );
        Assert.Null(ordinary.Session);
        Assert.Null(ordinary.Error);
        (GamescopeOverlaySession? Session, string? Error) primary = GamescopeOverlaySession.Resolve(
            ":42",
            [],
            _ => new(0, 100)
        );
        Assert.Equal(":42", primary.Session?.Display);
        Assert.Null(primary.Error);
    }

    [Fact]
    public void GameServerRoutesToSameCompositorPrimaryWithoutGuessingDisplayZero()
    {
        var identities = new Dictionary<string, GamescopeDisplayIdentity>
        {
            [":43"] = new(1, 100),
            [":42"] = new(0, 100),
            [":0"] = new(0, 200),
        };
        (GamescopeOverlaySession? Session, string? Error) result = GamescopeOverlaySession.Resolve(
            ":43",
            [":0", ":42", ":42", ":missing"],
            name => name is null ? null : identities.GetValueOrDefault(name)
        );
        Assert.Equal(":42", result.Session?.Display);
        Assert.Null(result.Error);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void MissingOrAmbiguousPrimaryFailsClosed(bool includeFirst, bool includeSecond)
    {
        string[] candidates = includeFirst ? [":7"] : [];
        if (includeSecond)
        {
            candidates = [":7", ":8"];
        }
        uint otherPid = includeSecond ? 100u : 200u;
        (GamescopeOverlaySession? Session, string? Error) result = GamescopeOverlaySession.Resolve(
            ":6",
            candidates,
            name => name == ":6" ? new(1, 100) : new(0, otherPid)
        );
        Assert.Null(result.Session);
        Assert.Contains("unavailable", result.Error);
    }

    [Fact]
    public void OlderGamescopeWithoutPidUsesOnlyUniqueVerifiedPrimary()
    {
        (GamescopeOverlaySession? Session, string? Error) result = GamescopeOverlaySession.Resolve(
            ":6",
            [":7"],
            name => name == ":6" ? new(1, null) : new(0, null)
        );
        Assert.Equal(":7", result.Session?.Display);
    }

    [Fact]
    public void SteamCandidatesRequireSameOwnerExecutableAndLocalDisplay()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            WriteProcess(root, "self", "1000", "SrvSurvey", "SrvSurvey", ":0");
            WriteProcess(root, "10", "1000", "steam", "/steam/ubuntu12_32/steam", ":42");
            WriteProcess(root, "20", "1001", "steam", "/steam/steam", ":43");
            WriteProcess(root, "30", "1000", "steam", "/other/helper", ":44");
            WriteProcess(root, "40", "1000", "steam", "/steam/steam", "remote:0");
            WriteProcess(root, "50", "1000", "other", "/steam/steam", ":45");
            Directory.CreateDirectory(Path.Combine(root, "60"));
            Assert.Equal([":42"], GamescopeOverlaySession.ReadSteamDisplays(root));
            File.WriteAllText(Path.Combine(root, "self", "status"), "missing uid");
            Assert.Empty(GamescopeOverlaySession.ReadSteamDisplays(root));
            Assert.Empty(GamescopeOverlaySession.ReadSteamDisplays(Path.Combine(root, "missing")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    internal static void WriteProcess(
        string root,
        string pid,
        string uid,
        string comm,
        string executable,
        string display,
        int parent = 1
    )
    {
        string directory = Path.Combine(root, pid);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "status"), $"Uid:\t{uid}\nPPid:\t{parent}\n");
        File.WriteAllText(Path.Combine(directory, "comm"), comm + "\n");
        File.WriteAllText(Path.Combine(directory, "cmdline"), executable + "\0");
        File.WriteAllText(Path.Combine(directory, "environ"), "DISPLAY=" + display + "\0");
    }
}
