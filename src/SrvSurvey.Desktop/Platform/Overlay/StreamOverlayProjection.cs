using Avalonia;

namespace SrvSurvey.Desktop.Platform.Overlay;

public static class StreamOverlayProjection
{
    /// <summary>Projects a panel into stream coordinates, optionally keeping intersecting panels inside the capture canvas.</summary>
    public static StreamOverlayFrame? Create(
        PixelRect gameClientBounds,
        PixelPoint overlayPosition,
        PixelSize overlaySize,
        double streamScaling,
        bool keepInsideCanvas = false
    )
    {
        if (
            gameClientBounds.Width <= 0
            || gameClientBounds.Height <= 0
            || overlaySize.Width <= 0
            || overlaySize.Height <= 0
            || !double.IsFinite(streamScaling)
            || streamScaling <= 0
        )
        {
            return null;
        }

        if (keepInsideCanvas)
        {
            PixelRect overlap = gameClientBounds.Intersect(new PixelRect(overlayPosition, overlaySize));
            if (overlap.Width <= 0 || overlap.Height <= 0)
            {
                return null;
            }
            overlayPosition = OverlayContentPlacement.ClampToBounds(overlayPosition, overlaySize, gameClientBounds);
        }
        int relativeX = overlayPosition.X - gameClientBounds.X;
        int relativeY = overlayPosition.Y - gameClientBounds.Y;
        if (relativeX < 0 || relativeY < 0)
        {
            return null;
        }

        return new StreamOverlayFrame(
            relativeX / streamScaling,
            relativeY / streamScaling,
            overlaySize.Width / streamScaling,
            overlaySize.Height / streamScaling
        );
    }
}

public sealed record StreamOverlayFrame(double Left, double Top, double Width, double Height);
