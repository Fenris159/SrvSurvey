namespace SrvSurvey.Desktop.Tests;

public sealed class StartupOptionsTests
{
    [Theory]
    [InlineData("--frontier-id", "F123")]
    [InlineData("-fid", "f456")]
    public void ReadsFrontierIdValue(string option, string value)
    {
        Assert.Equal(
            value.ToUpperInvariant(),
            StartupOptions.GetFrontierId([option, value]));
    }

    [Fact]
    public void ReadsInlineFrontierIdValue()
    {
        Assert.Equal(
            "F123",
            StartupOptions.GetFrontierId(["--frontier-id=F123"]));
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
    public void ReadsDiagnosticReplayManifest(
        string option,
        string? separateValue)
    {
        var arguments = separateValue is null
            ? new[] { option }
            : new[] { option, separateValue };

        Assert.Equal(
            "C:\\replays\\session.json",
            StartupOptions.GetDiagnosticReplayManifest(arguments));
    }

    [Fact]
    public void DiagnosticReplayIsDistinctFromJournalDirectoryOverride()
    {
        var arguments = new[]
        {
            "--journal-directory",
            "C:\\journals",
        };

        Assert.Null(StartupOptions.GetDiagnosticReplayManifest(arguments));
    }
}
