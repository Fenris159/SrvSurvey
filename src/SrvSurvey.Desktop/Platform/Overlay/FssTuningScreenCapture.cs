using Avalonia;

namespace SrvSurvey.Desktop.Platform.Overlay;

internal static class FssTuningScreenCapture
{
    public static CapturedPixelBuffer Capture(IGameScreenCapture capture, PixelRect gameBounds)
    {
        ArgumentNullException.ThrowIfNull(capture);
        int halfWidth = gameBounds.Width / 2;
        int halfHeight = gameBounds.Height / 2;
        if (halfWidth <= 0 || halfHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(gameBounds),
                "The game capture area must have a positive size."
            );
        }

        var captureBounds = new PixelRect(gameBounds.X + halfWidth, gameBounds.Y, halfWidth, halfHeight);
        return capture.Capture(captureBounds, gameBounds);
    }
}
