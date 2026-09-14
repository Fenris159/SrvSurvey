using Avalonia;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform.Overlay;

public sealed class FssTuningScreenCaptureTests
{
    [Fact]
    public void CaptureUsesRightHalfAndPreservesFullGameBoundsForPortalCalibration()
    {
        var expected = new CapturedPixelBuffer(1, 1, [3, 2, 1, 255]);
        using var capture = new RecordingCapture(expected);
        var gameBounds = new PixelRect(100, 200, 800, 600);

        CapturedPixelBuffer actual = FssTuningScreenCapture.Capture(capture, gameBounds);

        Assert.Same(expected, actual);
        Assert.Equal(new PixelRect(500, 200, 400, 300), capture.Bounds);
        Assert.Equal(gameBounds, capture.SourceBounds);
    }

    [Fact]
    public void CaptureRejectsMissingCaptureAndInvalidGameBounds()
    {
        Assert.Throws<ArgumentNullException>(() =>
            FssTuningScreenCapture.Capture(null!, new PixelRect(0, 0, 800, 600))
        );

        using var capture = new RecordingCapture(new CapturedPixelBuffer(1, 1, [3, 2, 1, 255]));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FssTuningScreenCapture.Capture(capture, new PixelRect(0, 0, 1, 600))
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FssTuningScreenCapture.Capture(capture, new PixelRect(0, 0, 800, 1))
        );
    }

    private sealed class RecordingCapture(CapturedPixelBuffer result) : IGameScreenCapture
    {
        public bool IsAvailable => true;

        public string? UnavailableReason => null;

        public PixelRect Bounds { get; private set; }

        public PixelRect SourceBounds { get; private set; }

        public CapturedPixelBuffer Capture(PixelRect bounds) => throw new InvalidOperationException();

        public CapturedPixelBuffer Capture(PixelRect bounds, PixelRect sourceBounds)
        {
            Bounds = bounds;
            SourceBounds = sourceBounds;
            return result;
        }

        public void Dispose() { }
    }
}
