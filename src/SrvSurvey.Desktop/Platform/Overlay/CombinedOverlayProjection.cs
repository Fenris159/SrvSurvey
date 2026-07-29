using Avalonia;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed record CombinedOverlayProjection(
    double Left,
    double Top,
    PixelRect InputRegion)
{
    public static CombinedOverlayProjection? Create(
        PixelRect hostBounds,
        PixelPoint overlayPosition,
        Size overlaySize,
        double scaling)
    {
        if (hostBounds.Width <= 0
            || hostBounds.Height <= 0
            || !double.IsFinite(overlaySize.Width)
            || !double.IsFinite(overlaySize.Height)
            || overlaySize.Width <= 0
            || overlaySize.Height <= 0
            || !double.IsFinite(scaling)
            || scaling <= 0)
        {
            return null;
        }

        var left = (overlayPosition.X - hostBounds.X) / scaling;
        var top = (overlayPosition.Y - hostBounds.Y) / scaling;
        var pixelLeft = (int)Math.Floor(left * scaling);
        var pixelTop = (int)Math.Floor(top * scaling);
        var pixelRight = (int)Math.Ceiling(
            (left + overlaySize.Width) * scaling);
        var pixelBottom = (int)Math.Ceiling(
            (top + overlaySize.Height) * scaling);
        var clippedLeft = Math.Clamp(pixelLeft, 0, hostBounds.Width);
        var clippedTop = Math.Clamp(pixelTop, 0, hostBounds.Height);
        var clippedRight = Math.Clamp(pixelRight, 0, hostBounds.Width);
        var clippedBottom = Math.Clamp(pixelBottom, 0, hostBounds.Height);
        if (clippedRight <= clippedLeft || clippedBottom <= clippedTop)
        {
            return null;
        }

        return new CombinedOverlayProjection(
            left,
            top,
            new PixelRect(
                clippedLeft,
                clippedTop,
                clippedRight - clippedLeft,
                clippedBottom - clippedTop));
    }
}
