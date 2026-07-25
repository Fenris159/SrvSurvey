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
}
