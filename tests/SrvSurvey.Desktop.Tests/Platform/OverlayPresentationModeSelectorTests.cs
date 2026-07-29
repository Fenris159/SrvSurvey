using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class OverlayPresentationModeSelectorTests
{
    [Theory]
    [InlineData(OverlayHostKind.Windows)]
    [InlineData(OverlayHostKind.LinuxX11)]
    [InlineData(OverlayHostKind.LinuxXWayland)]
    public void OrdinaryDesktopKeepsExistingMultipleWindowBehavior(
        OverlayHostKind host)
    {
        var decision = Select(host);

        Assert.Equal(OverlayPresentationMode.MultipleWindows, decision.Mode);
    }

    [Theory]
    [InlineData(OverlayHostKind.LinuxX11)]
    [InlineData(OverlayHostKind.LinuxXWayland)]
    public void GamescopeSelectsCombinedWindowForX11CompatibleHosts(
        OverlayHostKind host)
    {
        var decision = Select(
            host,
            gamescopeWaylandDisplay: "gamescope-0");

        Assert.Equal(OverlayPresentationMode.CombinedWindow, decision.Mode);
        Assert.Contains("Gamescope", decision.Reason);
    }

    [Fact]
    public void WindowsOnlyUsesCombinedWindowWhenExplicitlyRequested()
    {
        var decision = Select(
            OverlayHostKind.Windows,
            hostOverride: "combined");

        Assert.Equal(OverlayPresentationMode.CombinedWindow, decision.Mode);
    }

    [Fact]
    public void MultipleWindowOverrideWinsInsideGamescope()
    {
        var decision = Select(
            OverlayHostKind.LinuxXWayland,
            hostOverride: "separate",
            gamescopeWaylandDisplay: "gamescope-0");

        Assert.Equal(OverlayPresentationMode.MultipleWindows, decision.Mode);
    }

    [Fact]
    public void PureWaylandFailsClosedToExistingUnavailablePath()
    {
        var decision = Select(
            OverlayHostKind.LinuxWayland,
            hostOverride: "combined");

        Assert.Equal(OverlayPresentationMode.MultipleWindows, decision.Mode);
        Assert.Contains("does not expose", decision.Reason);
    }

    private static OverlayPresentationDecision Select(
        OverlayHostKind host,
        string? hostOverride = null,
        string? gamescopeWaylandDisplay = null)
    {
        return OverlayPresentationModeSelector.Select(
            OverlayPlatformCapabilities.ForHost(host),
            hostOverride,
            gamescopeWaylandDisplay,
            gamescopeDisplay: null,
            currentDesktop: null);
    }
}
