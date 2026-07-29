using Avalonia;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class X11OverlayWindowManagerPolicyTests
{
    [Fact]
    public void AdvertisedKdeOnScreenDisplayAtomEnablesKdePolicy()
    {
        var mode = X11OverlayWindowManagerPolicy.Select(
            kdeOnScreenDisplayAtom: 42,
            [4, 17, 42, 93]);

        Assert.Equal(X11OverlayStackingMode.KdeOnScreenDisplay, mode);
    }

    [Theory]
    [InlineData(0, new uint[] { 42 })]
    [InlineData(42, new uint[] { 4, 17, 93 })]
    public void MissingKdeCapabilityKeepsStandardTopmostPolicy(
        uint kdeOnScreenDisplayAtom,
        uint[] supportedAtoms)
    {
        var mode = X11OverlayWindowManagerPolicy.Select(
            kdeOnScreenDisplayAtom,
            supportedAtoms.Select(atom => (nuint)atom).ToArray());

        Assert.Equal(X11OverlayStackingMode.StandardTopmost, mode);
    }

    [Fact]
    public void KdePolicyWritesOsdTypeWithNormalFallback()
    {
        var windowTypes = X11OverlayWindowManagerPolicy.CreateWindowTypes(
            X11OverlayStackingMode.KdeOnScreenDisplay,
            kdeOnScreenDisplayAtom: 42,
            normalWindowAtom: 17);

        Assert.Equal([(nuint)42, (nuint)17], windowTypes);
    }

    [Fact]
    public void StandardPolicyDoesNotReplaceAvaloniaWindowType()
    {
        var windowTypes = X11OverlayWindowManagerPolicy.CreateWindowTypes(
            X11OverlayStackingMode.StandardTopmost,
            kdeOnScreenDisplayAtom: 42,
            normalWindowAtom: 17);

        Assert.Empty(windowTypes);
    }

    [Theory]
    [InlineData(0, 17)]
    [InlineData(42, 0)]
    public void IncompleteKdeAtomPairDoesNotReplaceAvaloniaWindowType(
        uint kdeOnScreenDisplayAtom,
        uint normalWindowAtom)
    {
        var windowTypes = X11OverlayWindowManagerPolicy.CreateWindowTypes(
            X11OverlayStackingMode.KdeOnScreenDisplay,
            kdeOnScreenDisplayAtom,
            normalWindowAtom);

        Assert.Empty(windowTypes);
    }

    [Fact]
    public void ManagedDragUsesScreenPixelDelta()
    {
        var position = ManagedOverlayWindowDragSession.CalculatePosition(
            initialWindowPosition: new PixelPoint(100, 200),
            initialPointerPosition: new PixelPoint(125, 240),
            currentPointerPosition: new PixelPoint(165, 225));

        Assert.Equal(new PixelPoint(140, 185), position);
    }
}
