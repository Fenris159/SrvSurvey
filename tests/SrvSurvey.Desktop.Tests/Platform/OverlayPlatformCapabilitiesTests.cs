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
        Assert.True(capabilities.SupportsGameWindowTracking);
        Assert.True(capabilities.SupportsGlobalInput);
        Assert.Contains("global keyboard input", capabilities.StatusText);
    }

    [Fact]
    public void X11AdvertisesImplementedPassiveCapabilities()
    {
        var capabilities = OverlayPlatformCapabilities.ForHost(
            OverlayHostKind.LinuxX11);

        Assert.True(capabilities.SupportsPassiveOverlay);
        Assert.True(capabilities.SupportsClickThrough);
        Assert.True(capabilities.SupportsGameWindowTracking);
        Assert.True(capabilities.SupportsGlobalInput);
        Assert.Contains("global keyboard input", capabilities.StatusText);
    }

    [Fact]
    public void XWaylandAdvertisesTheX11CompatibilityCapabilities()
    {
        var capabilities = OverlayPlatformCapabilities.ForHost(
            OverlayHostKind.LinuxXWayland);

        Assert.True(capabilities.UsesX11Compatibility);
        Assert.True(capabilities.SupportsPassiveOverlay);
        Assert.True(capabilities.SupportsClickThrough);
        Assert.True(capabilities.SupportsGameWindowTracking);
        Assert.True(capabilities.SupportsGlobalInput);
        Assert.Contains("XWayland", capabilities.StatusText);
    }

    [Theory]
    [InlineData("wayland", ":0", "wayland-0", OverlayHostKind.LinuxXWayland)]
    [InlineData("wayland", ":1", null, OverlayHostKind.LinuxXWayland)]
    [InlineData(null, ":2", "wayland-1", OverlayHostKind.LinuxXWayland)]
    [InlineData("x11", ":0", null, OverlayHostKind.LinuxX11)]
    [InlineData("wayland", null, "wayland-0", OverlayHostKind.LinuxWayland)]
    [InlineData(null, null, null, OverlayHostKind.Other)]
    public void LinuxDetectionDistinguishesXWaylandFromNativeWayland(
        string? sessionType,
        string? display,
        string? waylandDisplay,
        OverlayHostKind expected)
    {
        Assert.Equal(
            expected,
            OverlayPlatformCapabilities.DetectLinuxHost(
                sessionType,
                display,
                waylandDisplay));
    }

    [Fact]
    public void WaylandDoesNotClaimUnverifiedCompositorBehavior()
    {
        var capabilities = OverlayPlatformCapabilities.ForHost(
            OverlayHostKind.LinuxWayland);

        Assert.False(capabilities.SupportsPassiveOverlay);
        Assert.False(capabilities.UsesX11Compatibility);
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
