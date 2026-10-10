using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class OverlayPresentationModeSelectorTests
{
    [Fact]
    public void VerifiedExternalSlotRequiresOnePassiveHostEvenWithSeparateOverride()
    {
        OverlayPlatformCapabilities capabilities = OverlayPlatformCapabilities.ForHost(
            OverlayHostKind.LinuxXWayland
        ) with
        {
            UsesGamescopeExternalOverlay = true,
        };
        OverlayPresentationDecision decision = OverlayPresentationModeSelector.Select(
            capabilities,
            "separate",
            null,
            null,
            null
        );
        Assert.Equal(OverlayPresentationMode.CombinedWindow, decision.Mode);
        Assert.False(capabilities.SupportsLiveOverlayInteraction);
        Assert.Contains("performance HUD", capabilities.StatusText);
        Assert.Contains("failed", (capabilities with { SupportsClickThrough = false }).StatusText);
        Assert.Contains("failed", (capabilities with { SupportsTopmost = false }).StatusText);
        Assert.Contains("failed", (capabilities with { SupportsGameWindowTracking = false }).StatusText);
    }

    [Theory]
    [InlineData(OverlayHostKind.Windows)]
    [InlineData(OverlayHostKind.LinuxX11)]
    [InlineData(OverlayHostKind.LinuxXWayland)]
    public void OrdinaryDesktopKeepsExistingMultipleWindowBehavior(OverlayHostKind host)
    {
        OverlayPresentationDecision decision = Select(host);

        Assert.Equal(OverlayPresentationMode.MultipleWindows, decision.Mode);
    }

    [Theory]
    [InlineData(OverlayHostKind.LinuxX11)]
    [InlineData(OverlayHostKind.LinuxXWayland)]
    public void GamescopeSelectsCombinedWindowForX11CompatibleHosts(OverlayHostKind host)
    {
        OverlayPresentationDecision decision = Select(host, gamescopeWaylandDisplay: "gamescope-0");

        Assert.Equal(OverlayPresentationMode.CombinedWindow, decision.Mode);
        Assert.Contains("Gamescope", decision.Reason);
    }

    [Fact]
    public void WindowsOnlyUsesCombinedWindowWhenExplicitlyRequested()
    {
        OverlayPresentationDecision decision = Select(OverlayHostKind.Windows, hostOverride: "combined");

        Assert.Equal(OverlayPresentationMode.CombinedWindow, decision.Mode);
    }

    [Fact]
    public void MultipleWindowOverrideWinsInsideGamescope()
    {
        OverlayPresentationDecision decision = Select(
            OverlayHostKind.LinuxXWayland,
            hostOverride: "separate",
            gamescopeWaylandDisplay: "gamescope-0"
        );

        Assert.Equal(OverlayPresentationMode.MultipleWindows, decision.Mode);
    }

    [Fact]
    public void PureWaylandFailsClosedToExistingUnavailablePath()
    {
        OverlayPresentationDecision decision = Select(OverlayHostKind.LinuxWayland, hostOverride: "combined");

        Assert.Equal(OverlayPresentationMode.MultipleWindows, decision.Mode);
        Assert.Contains("does not expose", decision.Reason);
    }

    private static OverlayPresentationDecision Select(
        OverlayHostKind host,
        string? hostOverride = null,
        string? gamescopeWaylandDisplay = null
    )
    {
        return OverlayPresentationModeSelector.Select(
            OverlayPlatformCapabilities.ForHost(host),
            hostOverride,
            gamescopeWaylandDisplay,
            gamescopeDisplay: null,
            currentDesktop: null
        );
    }
}
