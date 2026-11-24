using Avalonia;
using SrvSurvey.Core.Updates;
using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests;

public sealed class ProgramTests
{
    [Fact]
    public void StartupVersionIncludesPrereleaseIdentifier()
    {
        string expected = ReleaseVersion.FromAssembly(typeof(Program).Assembly).ToString();

        Assert.Equal(expected, Program.GetStartupVersion(typeof(Program).Assembly));
        Assert.Contains("-rc.", expected, StringComparison.Ordinal);
    }

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
    public void LinuxX11OptionsEmbedPopupsWithDrawnDecorations()
    {
        X11PlatformOptions options = Program.CreateX11Options(useSoftwareRendering: false);

        Assert.False(options.UseDBusMenu);
        Assert.True(options.OverlayPopups);
#pragma warning disable AVALONIA_X11_FORCE_CSD // Verify the reserved Raven-themed X11 chrome configuration.
        Assert.True(options.ForceDrawnDecorations);
#pragma warning restore AVALONIA_X11_FORCE_CSD
        Assert.Equal([X11RenderingMode.Glx, X11RenderingMode.Software], options.RenderingMode);
    }

    [Theory]
    [InlineData("[('xkb', 'us')]", false)]
    [InlineData("[('xkb', 'us'), ('xkb', 'de+nodeadkeys')]", false)]
    [InlineData("@a(ss) []", false)]
    [InlineData("[('xkb', 'us'), ('ibus', 'mozc-jp')]", true)]
    [InlineData("[('unknown', 'custom')]", true)]
    [InlineData("[('xkb', 'us')", true)]
    [InlineData(null, true)]
    public void GnomeXWaylandUsesInputMethodsOnlyWhenRequiredOrUnknown(string? sources, bool enableIme)
    {
        bool useNativeTextInput = LinuxTextInput.UseNativeTextInput(
            OverlayHostKind.LinuxXWayland,
            name => GnomeInputEnvironment(name, null, null),
            () => sources
        );

        Assert.Equal(enableIme, Program.CreateX11Options(false, useNativeTextInput).EnableIme);
    }

    [Theory]
    [InlineData("AVALONIA_IM_MODULE", "ibus")]
    [InlineData("GTK_IM_MODULE", "fcitx")]
    [InlineData("QT_IM_MODULE", "fcitx5")]
    [InlineData("XMODIFIERS", "@im=fcitx")]
    [InlineData("XDG_CURRENT_DESKTOP", "KDE")]
    public void NativeTextInputLeavesOtherDesktopsAndExplicitInputMethodsToAvalonia(string variable, string value)
    {
        bool useNativeTextInput = LinuxTextInput.UseNativeTextInput(
            OverlayHostKind.LinuxXWayland,
            name => GnomeInputEnvironment(name, variable, value),
            () => throw new InvalidOperationException("GNOME sources should be irrelevant to this configuration.")
        );

        Assert.True(Program.CreateX11Options(false, useNativeTextInput).EnableIme);
    }

    [Fact]
    public void NativeXorgKeepsTheDefaultInputMethodSelection()
    {
        bool useNativeTextInput = LinuxTextInput.UseNativeTextInput(
            OverlayHostKind.LinuxX11,
            name => GnomeInputEnvironment(name, null, null),
            () => "[('xkb', 'us')]"
        );

        Assert.True(Program.CreateX11Options(false, useNativeTextInput).EnableIme);
    }

    [Fact]
    public void FailedOrMissingDesktopProbeKeepsTheDefaultInputMethod()
    {
        if (OperatingSystem.IsLinux())
        {
            Assert.Null(LinuxTextInput.ReadGnomeInputSources("/usr/bin/false"));
            Assert.Null(LinuxTextInput.ReadGnomeInputSources("/dev/null/gsettings"));
        }
    }

    private static string? GnomeInputEnvironment(string name, string? variable, string? value) =>
        name == variable
            ? value
            : name switch
            {
                "XDG_CURRENT_DESKTOP" => "ubuntu:GNOME",
                "QT_IM_MODULE" => "ibus",
                "XMODIFIERS" => "@im=ibus",
                _ => null,
            };
}
