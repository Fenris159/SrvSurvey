using Avalonia;

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

    [Fact]
    public void LinuxSoftwareRenderingUsesTheX11FramebufferRenderer()
    {
        X11PlatformOptions options = Program.CreateX11SoftwareRenderingOptions();

        Assert.Equal([X11RenderingMode.Software], options.RenderingMode);
    }
}
