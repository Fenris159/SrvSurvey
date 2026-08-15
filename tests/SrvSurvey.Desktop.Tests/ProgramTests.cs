namespace SrvSurvey.Desktop.Tests;

public sealed class ProgramTests
{
    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("software")]
    public void SoftwareRenderingRecognizesExplicitOptIn(string value)
    {
        Assert.True(Program.IsSoftwareRenderingRequested(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("hardware")]
    public void SoftwareRenderingRemainsOffByDefault(string? value)
    {
        Assert.False(Program.IsSoftwareRenderingRequested(value));
    }
}
