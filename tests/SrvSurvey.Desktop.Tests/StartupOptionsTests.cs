namespace SrvSurvey.Desktop.Tests;

public sealed class StartupOptionsTests
{
    /// <summary>An automatic instance's replacement must retain its commander without converting startup preference into a command-line override.</summary>
    [Fact]
    public void RestartRetainsAnAutomaticCommanderAndAllowsTheExistingCompanionInstance()
    {
        string[] arguments = StartupOptions.ForRestart(["--journal-directory", "journals"], "F123");

        Assert.Contains("--instance-frontier-id", arguments);
        Assert.Contains("F123", arguments);
        Assert.Equal("F123", StartupOptions.GetInstanceFrontierId(arguments));
        Assert.Null(StartupOptions.GetFrontierId(arguments));
        Assert.Equal("journals", StartupOptions.GetJournalDirectory(arguments));
        Assert.True(StartupOptions.AllowsConcurrentInstance(arguments));
    }

    /// <summary>Replacing an instance marker retains explicit launch options and avoids duplicate concurrent-instance authorization.</summary>
    [Theory]
    [InlineData("--instance-frontier-id", "F456")]
    [InlineData("--instance-frontier-id=F456", null)]
    public void RestartReplacesTheRetainedAssignmentWithoutChangingTheExplicitOverride(string option, string? value)
    {
        string[] original =
        [
            "--frontier-id",
            "F123",
            StartupOptions.MultiCommanderInstanceOption,
            option,
            .. value is null ? Array.Empty<string>() : new[] { value },
        ];
        string[] restarted = StartupOptions.ForRestart(original, "f123");

        Assert.Equal("F123", StartupOptions.GetFrontierId(restarted));
        Assert.Equal("F123", StartupOptions.GetInstanceFrontierId(restarted));
        Assert.Single(restarted, argument => argument == StartupOptions.MultiCommanderInstanceOption);
        Assert.DoesNotContain("F456", restarted);
        Assert.Contains(option, original);
        string[] changedPreference = StartupOptions.ForRestart(restarted, "F123", preserveCommander: false);
        Assert.Null(StartupOptions.GetInstanceFrontierId(changedPreference));
        Assert.Equal("F123", StartupOptions.GetFrontierId(changedPreference));
    }

    /// <summary>Unknown identities and replay sessions must not be redirected to a personal commander.</summary>
    [Theory]
    [InlineData(null, false)]
    [InlineData("../other", false)]
    [InlineData("F123", true)]
    public void RestartDoesNotAddAnAssignmentWithoutAValidNormalCommander(string? frontierId, bool replay)
    {
        string[] original = replay ? ["--diagnostic-replay", "session.json"] : [];

        Assert.Equal(original, StartupOptions.ForRestart(original, frontierId));
    }

    [Theory]
    [InlineData("--multi-commander-instance")]
    [InlineData("--MULTI-COMMANDER-INSTANCE")]
    [InlineData("--diagnostic-replay=session.json")]
    public void ExplicitLaunchModesAllowConcurrentInstances(string argument)
    {
        Assert.True(StartupOptions.AllowsConcurrentInstance([argument]));
    }

    [Theory]
    [InlineData()]
    [InlineData("--frontier-id", "F123")]
    [InlineData("--journal-directory", "journals")]
    public void OrdinaryLaunchDoesNotAllowConcurrentInstances(params string[] arguments)
    {
        Assert.False(StartupOptions.AllowsConcurrentInstance(arguments));
    }

    [Theory]
    [InlineData("--frontier-id", "F123")]
    [InlineData("-fid", "f456")]
    public void ReadsFrontierIdValue(string option, string value)
    {
        Assert.Equal(value.ToUpperInvariant(), StartupOptions.GetFrontierId([option, value]));
    }

    [Fact]
    public void ReadsInlineFrontierIdValue()
    {
        Assert.Equal("F123", StartupOptions.GetFrontierId(["--frontier-id=F123"]));
    }

    [Theory]
    [InlineData("--frontier-id")]
    [InlineData("--frontier-id=../profile")]
    [InlineData("--frontier-id=Commander")]
    public void RejectsMissingOrUnsafeFrontierId(string argument)
    {
        Assert.Null(StartupOptions.GetFrontierId([argument]));
    }

    [Theory]
    [InlineData("--diagnostic-replay", "C:\\replays\\session.json")]
    [InlineData("--diagnostic-replay=C:\\replays\\session.json", null)]
    public void ReadsDiagnosticReplayManifest(string option, string? separateValue)
    {
        string[] arguments = separateValue is null ? new[] { option } : new[] { option, separateValue };

        Assert.Equal("C:\\replays\\session.json", StartupOptions.GetDiagnosticReplayManifest(arguments));
    }

    [Fact]
    public void DiagnosticReplayIsDistinctFromJournalDirectoryOverride()
    {
        string[] arguments = new[] { "--journal-directory", "C:\\journals" };

        Assert.Null(StartupOptions.GetDiagnosticReplayManifest(arguments));
    }

    [Theory]
    [InlineData("--diagnostic-replay", "diagnostic replay")]
    [InlineData("--diagnostic-replay=C:\\replays\\session.json", "diagnostic replay")]
    [InlineData("--journal-directory", "normal startup")]
    public void StartupFailureMessageIdentifiesTheRequestedMode(string argument, string expectedMode)
    {
        string message = Program.GetStartupFailureMessage([argument], new InvalidDataException("test failure"));

        Assert.Equal($"SrvSurvey {expectedMode} could not start: test failure", message);
    }
}
