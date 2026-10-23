using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class GamescopeScalingStateTests
{
    [Fact]
    public void ReattachedSubscriptionsPreserveObservedHistoryAndRequireFreshCommandsAfterConflicts()
    {
        var state = new GamescopeScalingState(0);
        state.Observe([new(20, 1, ":game")]);
        state.ReconcileSubscription([]);
        Assert.Equal(1u, state.Mode);
        state.ReconcileSubscription([1]);
        Assert.Equal(1u, state.Mode);
        state.ReconcileSubscription([0]);
        Assert.Null(state.Mode);
        state.Observe([new(10, 0, ":primary"), new(20, 1, ":game")]);
        Assert.Null(state.Mode);
        state.ReconcileSubscription([1]);
        Assert.Null(state.Mode);
        state.Observe([new(21, 3, ":primary")]);
        Assert.Equal(3u, state.Mode);
    }

    [Fact]
    public void ReviewRegressionOlderPollsCannotReplaceNewerScalingAndEqualTimeConflictsPersist()
    {
        var state = new GamescopeScalingState(0);
        state.Observe([new(20, 3, ":game")]);
        state.Observe([new(10, 1, ":primary")]);
        Assert.Equal(3u, state.Mode);
        state.Observe([new(20, 1, ":primary")]);
        Assert.Null(state.Mode);
        state.Observe([new(20, 3, ":game")]);
        Assert.Null(state.Mode);
        state.Observe([new(20, 3, ":primary")]);
        Assert.Equal(3u, state.Mode);
        state.Observe([new(21, 4, ":primary")]);
        Assert.Equal(4u, state.Mode);

        var wrapped = new GamescopeScalingState(0);
        wrapped.Observe([new(uint.MaxValue, 1, ":primary")]);
        wrapped.Observe([new(1, 3, ":game")]);
        wrapped.Observe([new(uint.MaxValue, 1, ":primary")]);
        Assert.Equal(3u, wrapped.Mode);
    }

    [Theory]
    [InlineData("auto", 0u)]
    [InlineData("integer", 1u)]
    [InlineData("fit", 2u)]
    [InlineData("fill", 3u)]
    [InlineData("stretch", 4u)]
    public void ReadsStartupScalerWithoutARequestProperty(string scaler, uint mode)
    {
        Assert.Equal(mode, GamescopeScalingState.ParseArguments(["gamescope", "-S", scaler]));
        Assert.Equal(mode, GamescopeScalingState.ParseArguments(["gamescope", "--scaler=" + scaler]));
        Assert.Equal(mode, GamescopeScalingState.ParseArguments(["gamescope", "-S" + scaler]));
        Assert.Equal(
            mode,
            GamescopeScalingState.ParseArguments(["gamescope", "--scaler", scaler, "--", "game", "-S", "invalid"])
        );
    }

    [Fact]
    public void StartupParsingHonorsLastSettingAndRejectsUnsupportedOrIncompleteValues()
    {
        Assert.Equal(0u, GamescopeScalingState.ParseArguments(["gamescope", "-w", "960"]));
        Assert.Equal(4u, GamescopeScalingState.ParseArguments(["gamescope", "-S", "integer", "-S", "stretch"]));
        Assert.Equal(1u, GamescopeScalingState.ParseArguments(["gamescope", "-efSinteger"]));
        Assert.Equal(1u, GamescopeScalingState.ParseArguments(["gamescope", "-efS", "integer"]));
        Assert.Equal(0u, GamescopeScalingState.ParseArguments(["gamescope", "-w960", "-ef"]));
        Assert.Equal(0u, GamescopeScalingState.ParseArguments(["gamescope", "-C", "-Sinteger"]));
        Assert.Null(GamescopeScalingState.ParseArguments(["gamescope", "-S"]));
        Assert.Null(GamescopeScalingState.ParseArguments(["gamescope", "--scaler=wrong"]));
    }

    [Fact]
    public void StartupRequiresTheVerifiedSameUserCompositorProcess()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            GamescopeOverlaySessionTests.WriteProcess(root, "self", "1000", "test", "test", ":0");
            GamescopeOverlaySessionTests.WriteProcess(root, "10", "1000", "gamescope-wl", "/bin/gamescope", ":0");
            string command = Path.Combine(root, "10", "cmdline");
            File.WriteAllText(command, "/bin/gamescope\0-S\0integer\0--\0game\0-S\0stretch\0");
            Assert.Equal(1u, GamescopeScalingState.ReadStartupMode(root, 10));
            File.WriteAllText(Path.Combine(root, "10", "comm"), "other");
            Assert.Null(GamescopeScalingState.ReadStartupMode(root, 10));
            File.WriteAllText(Path.Combine(root, "10", "comm"), "gamescope");
            File.WriteAllText(command, "/custom/gamescope-host-focus\0-Sinteger\0");
            Assert.Equal(1u, GamescopeScalingState.ReadStartupMode(root, 10));
            File.WriteAllText(command, string.Empty);
            Assert.Null(GamescopeScalingState.ReadStartupMode(root, 10));
            File.WriteAllText(Path.Combine(root, "10", "status"), "Uid: 2000\n");
            Assert.Null(GamescopeScalingState.ReadStartupMode(root, 10));
            File.WriteAllText(Path.Combine(root, "self", "status"), "no uid");
            Assert.Null(GamescopeScalingState.ReadStartupMode(root, 10));
            Assert.Null(GamescopeScalingState.ReadStartupMode(root, null));
            Assert.Null(GamescopeScalingState.ReadStartupMode(root, 0));
            Assert.Null(GamescopeScalingState.ReadStartupMode(root, 999));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ConflictingOldCommandsRemainUnknownUntilANewObservedRequest()
    {
        var state = new GamescopeScalingState(1);
        state.Initialize([]);
        Assert.Equal(1u, state.Mode);
        state.Initialize([2, 2]);
        Assert.Equal(2u, state.Mode);
        state.Initialize([1, 0]);
        Assert.Null(state.Mode);
        state.Observe([]);
        Assert.Null(state.Mode);
        state.Observe([new(20, 3), new(10, 0)]);
        Assert.Equal(3u, state.Mode);
        state.Observe([new(30, 1, ":1"), new(30, 0, ":2")]);
        Assert.Null(state.Mode);
        state.Observe([new(30, 1, ":1"), new(30, 0, ":1")]);
        Assert.Equal(0u, state.Mode);
        state.Observe([new(40, 4), new(40, 4)]);
        Assert.Equal(4u, state.Mode);
        state.Initialize([]);
        state.Observe([new(uint.MaxValue, 3), new(1, 1)]);
        Assert.Equal(1u, state.Mode);
    }

    [Fact]
    public void BothCommandPathsMatchGamescopeAndInvalidNewModesAreIgnored()
    {
        Assert.Equal(4u, GamescopeScalingState.DecodeRequest(GamescopeScalingState.ScalerAtom, 4));
        Assert.Null(GamescopeScalingState.DecodeRequest(GamescopeScalingState.ScalerAtom, 5));
        Assert.Equal(1u, GamescopeScalingState.DecodeRequest(GamescopeScalingState.LegacyAtom, 2));
        Assert.Equal(0u, GamescopeScalingState.DecodeRequest(GamescopeScalingState.LegacyAtom, 4));
        Assert.Null(GamescopeScalingState.DecodeRequest("other", 1));
    }
}
