using Avalonia;

namespace SrvSurvey.Desktop.Platform.Overlay;

public static class StreamOverlayProjection
{
    public static StreamOverlayFrame? Create(
        PixelRect gameClientBounds,
        PixelPoint overlayPosition,
        PixelSize overlaySize,
        double streamScaling)
    {
        if (gameClientBounds.Width <= 0
            || gameClientBounds.Height <= 0
            || overlaySize.Width <= 0
            || overlaySize.Height <= 0
            || !double.IsFinite(streamScaling)
            || streamScaling <= 0)
        {
            return null;
        }

        var relativeX = overlayPosition.X - gameClientBounds.X;
        var relativeY = overlayPosition.Y - gameClientBounds.Y;
        if (relativeX < 0 || relativeY < 0)
        {
            return null;
        }

        return new StreamOverlayFrame(
            relativeX / streamScaling,
            relativeY / streamScaling,
            overlaySize.Width / streamScaling,
            overlaySize.Height / streamScaling);
    }
}

public sealed record StreamOverlayFrame(
    double Left,
    double Top,
    double Width,
    double Height);
