using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class OverlayPlatformCapabilitiesTests
{
    [Fact]
    public void WindowsAdvertisesOnlyImplementedPassiveCapabilities()
    {
        var capabilities = OverlayPlatformCapabilities.ForHost(
            OverlayHostKind.Windows);

        Assert.True(capabilities.SupportsPassiveOverlay);
        Assert.True(capabilities.SupportsClickThrough);
        Assert.False(capabilities.SupportsGameWindowTracking);
        Assert.False(capabilities.SupportsGlobalInput);
        Assert.Contains("pending", capabilities.StatusText);
    }

    [Fact]
    public void X11KeepsUnimplementedInputCapabilitiesVisible()
    {
        var capabilities = OverlayPlatformCapabilities.ForHost(
            OverlayHostKind.LinuxX11);

        Assert.True(capabilities.SupportsPassiveOverlay);
        Assert.False(capabilities.SupportsClickThrough);
        Assert.Contains("click-through", capabilities.StatusText);
    }

    [Fact]
    public void WaylandDoesNotClaimUnverifiedCompositorBehavior()
    {
        var capabilities = OverlayPlatformCapabilities.ForHost(
            OverlayHostKind.LinuxWayland);

        Assert.False(capabilities.SupportsPassiveOverlay);
        Assert.False(capabilities.SupportsClickThrough);
        Assert.False(capabilities.SupportsGameWindowTracking);
        Assert.Contains("compositor", capabilities.StatusText);
    }

    [Fact]
    public void UnknownPlatformsDisableDetachedOverlays()
    {
        var capabilities = OverlayPlatformCapabilities.ForHost(
            OverlayHostKind.Other);

        Assert.False(capabilities.SupportsPassiveOverlay);
        Assert.Contains("unavailable", capabilities.StatusText);
    }
}
