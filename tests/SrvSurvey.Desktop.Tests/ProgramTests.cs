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
        X11PlatformOptions options = Program.CreateX11Options(useSoftwareRendering: true);

        Assert.Equal([X11RenderingMode.Software], options.RenderingMode);
    }

    [Fact]
    public void LinuxX11OptionsDisableTheUnusedGlobalMenuExporter()
    {
        X11PlatformOptions options = Program.CreateX11Options(useSoftwareRendering: false);

        Assert.False(options.UseDBusMenu);
        Assert.True(options.OverlayPopups);
#pragma warning disable AVALONIA_X11_FORCE_CSD // Verify the reserved Raven-themed X11 chrome configuration.
        Assert.True(options.ForceDrawnDecorations);
#pragma warning restore AVALONIA_X11_FORCE_CSD
        Assert.Equal([X11RenderingMode.Glx, X11RenderingMode.Software], options.RenderingMode);
    }
}
