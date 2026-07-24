using Avalonia;

namespace SrvSurvey.Desktop.Platform.Overlay;

public static class OverlayWindowPlacement
{
    public static PixelPoint TopRight(
        PixelRect hostBounds,
        PixelSize overlaySize,
        int margin = 20)
    {
        Validate(hostBounds, overlaySize, margin);
        return new PixelPoint(
            Math.Max(
                hostBounds.X + margin,
                hostBounds.Right - overlaySize.Width - margin),
            hostBounds.Y + margin);
    }

    public static PixelPoint BottomRight(
        PixelRect hostBounds,
        PixelSize overlaySize,
        int margin = 20)
    {
        Validate(hostBounds, overlaySize, margin);
        return new PixelPoint(
            Math.Max(
                hostBounds.X + margin,
                hostBounds.Right - overlaySize.Width - margin),
            Math.Max(
                hostBounds.Y + margin,
                hostBounds.Bottom - overlaySize.Height - margin));
    }

    private static void Validate(
        PixelRect hostBounds,
        PixelSize overlaySize,
        int margin)
    {
        if (hostBounds.Width <= 0 || hostBounds.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(hostBounds),
                "Host bounds must have a positive size.");
        }

        if (overlaySize.Width <= 0 || overlaySize.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(overlaySize),
                "Overlay size must have a positive size.");
        }

        if (margin < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(margin));
        }

    }
}
