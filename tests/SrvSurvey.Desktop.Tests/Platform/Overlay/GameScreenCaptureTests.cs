using System.Runtime.InteropServices;
using Avalonia;
using PipeWire.NET;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform.Overlay;

public sealed class GameScreenCaptureTests
{
    [Fact]
    public void CapturedBufferReadsBgraPixelsAsRgb()
    {
        var buffer = new CapturedPixelBuffer(2, 1, [51, 34, 17, 255, 102, 85, 68, 255]);

        Assert.Equal(new FssRgbPixel(17, 34, 51), buffer.GetPixel(0, 0));
        Assert.Equal(new FssRgbPixel(68, 85, 102), buffer.GetPixel(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.GetPixel(2, 0));
    }

    [Fact]
    public void X11DecoderReadsLittleEndian32BitPixels()
    {
        CapturedPixelBuffer buffer = DecodeX11([51, 34, 17, 0], bitsPerPixel: 32, byteOrder: 0);

        Assert.Equal(new FssRgbPixel(17, 34, 51), buffer.GetPixel(0, 0));
    }

    [Fact]
    public void X11DecoderReadsBigEndian24BitPixels()
    {
        CapturedPixelBuffer buffer = DecodeX11([17, 34, 51, 0], bitsPerPixel: 24, byteOrder: 1, stride: 4);

        Assert.Equal(new FssRgbPixel(17, 34, 51), buffer.GetPixel(0, 0));
    }

    [Fact]
    public void UnavailableCaptureReportsItsCapabilityFailure()
    {
        using var capture = new UnavailableGameScreenCapture("Wayland capture unavailable.");

        Assert.False(capture.IsAvailable);
        Assert.Contains("Wayland", capture.UnavailableReason);
        Assert.Throws<NotSupportedException>(() => capture.Capture(new PixelRect(0, 0, 1, 1)));
    }

    [Fact]
    public void X11CaptureClipsBoundsToTheRootWindow()
    {
        Assert.Equal(
            new PixelRect(0, 10, 30, 40),
            X11GameScreenCapture.ClipToRootWindow(new PixelRect(-20, 10, 50, 40), rootWidth: 100, rootHeight: 80)
        );
        Assert.Equal(
            new PixelRect(80, 60, 20, 20),
            X11GameScreenCapture.ClipToRootWindow(new PixelRect(80, 60, 50, 40), rootWidth: 100, rootHeight: 80)
        );
    }

    [Fact]
    public void X11CaptureRejectsBoundsOutsideTheRootWindow()
    {
        Assert.Throws<InvalidOperationException>(() =>
            X11GameScreenCapture.ClipToRootWindow(new PixelRect(100, 20, 10, 10), rootWidth: 100, rootHeight: 80)
        );
    }

    [Fact]
    public void X11CaptureFailureFallsBackToWaylandPortal()
    {
        var expected = new CapturedPixelBuffer(1, 1, [51, 34, 17, 255]);
        using var capture = new FallbackGameScreenCapture(
            new StubCapture(_ =>
                throw new InvalidOperationException("X11 could not capture the Elite Dangerous window.")
            ),
            new StubCapture(_ => expected)
        );

        CapturedPixelBuffer actual = capture.Capture(new PixelRect(0, 0, 1, 1));

        Assert.Same(expected, actual);
    }

    [Fact]
    public void RepeatedCaptureFailuresBackOffAndRecover()
    {
        var expected = new CapturedPixelBuffer(1, 1, [51, 34, 17, 255]);
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));
        int attempts = 0;
        using var capture = new BackoffGameScreenCapture(
            new StubCapture(_ => ++attempts < 3 ? throw new IOException("capture failed") : expected),
            time
        );
        var bounds = new PixelRect(0, 0, 1, 1);

        Assert.Throws<IOException>(() => capture.Capture(bounds));
        Assert.Throws<ScreenCaptureBackoffException>(() => capture.Capture(bounds));
        Assert.Equal(1, attempts);

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Throws<IOException>(() => capture.Capture(bounds));
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Throws<ScreenCaptureBackoffException>(() => capture.Capture(bounds));
        Assert.Equal(2, attempts);

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Same(expected, capture.Capture(bounds));
        Assert.Same(expected, capture.Capture(bounds));
        Assert.Equal(4, attempts);
    }

    [Fact]
    public void PortalWindowCaptureUsesGameRelativeCalibrationBounds()
    {
        byte[] pixels = Enumerable.Range(0, 4 * 4).SelectMany(index => new byte[] { (byte)index, 0, 0, 255 }).ToArray();
        VideoFrame frame = new(pixels, stride: 16, width: 4, height: 4, PixelFormat.Bgra, sequenceNumber: 1);

        CapturedPixelBuffer capture = PortalFrameCropper.Crop(
            frame,
            new PortalStreamInfo(42, SourceType: 2, Position: null, Size: null),
            new PixelRect(150, 250, 100, 100),
            new PixelRect(100, 200, 200, 200)
        );

        Assert.Equal(2, capture.Width);
        Assert.Equal(2, capture.Height);
        Assert.Equal(new FssRgbPixel(0, 0, 5), capture.GetPixel(0, 0));
        Assert.Equal(new FssRgbPixel(0, 0, 10), capture.GetPixel(1, 1));
    }

    [Fact]
    public void PortalMonitorCaptureUsesPortalPositionAndScaling()
    {
        byte[] pixels = Enumerable.Range(0, 8 * 4).SelectMany(index => new byte[] { (byte)index, 0, 0, 255 }).ToArray();
        VideoFrame frame = new(pixels, stride: 32, width: 8, height: 4, PixelFormat.Bgrx, sequenceNumber: 1);

        CapturedPixelBuffer capture = PortalFrameCropper.Crop(
            frame,
            new PortalStreamInfo(42, SourceType: 1, new PixelPoint(100, 200), new PixelSize(400, 200)),
            new PixelRect(200, 250, 100, 50),
            new PixelRect(200, 250, 100, 50)
        );

        Assert.Equal(2, capture.Width);
        Assert.Equal(1, capture.Height);
        Assert.Equal(new FssRgbPixel(0, 0, 10), capture.GetPixel(0, 0));
        Assert.Equal(new FssRgbPixel(0, 0, 11), capture.GetPixel(1, 0));
    }

    [Fact]
    public void PortalCaptureConvertsRgbaToBgra()
    {
        VideoFrame frame = new([17, 34, 51, 0], stride: 4, width: 1, height: 1, PixelFormat.Rgba, sequenceNumber: 1);

        CapturedPixelBuffer capture = PortalFrameCropper.Crop(
            frame,
            new PortalStreamInfo(42, SourceType: 2, Position: null, Size: null),
            new PixelRect(0, 0, 1, 1),
            new PixelRect(0, 0, 1, 1)
        );

        Assert.Equal(new FssRgbPixel(17, 34, 51), capture.GetPixel(0, 0));
        Assert.Equal((byte)255, capture.BgraPixels.Span[3]);
    }

    [Fact]
    public void PortalStreamMetadataReadsWindowGeometryAndDefaults()
    {
        var properties = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["source_type"] = 1U,
            ["position"] = (100, 200),
            ["size"] = (1920, 1080),
        };
        var results = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["streams"] = new ValueTuple<uint, IDictionary<string, object>>[] { (42U, properties) },
        };

        var stream = PortalStreamInfo.Read(results);

        Assert.Equal(42U, stream.NodeId);
        Assert.Equal(1U, stream.SourceType);
        Assert.Equal(new PixelPoint(100, 200), stream.Position);
        Assert.Equal(new PixelSize(1920, 1080), stream.Size);

        properties.Clear();
        stream = PortalStreamInfo.Read(results);
        Assert.Equal(0U, stream.SourceType);
        Assert.Null(stream.Position);
        Assert.Null(stream.Size);
    }

    [Fact]
    public void PortalStreamMetadataRequiresExactlyOneStream()
    {
        var missing = new Dictionary<string, object>(StringComparer.Ordinal);
        var multiple = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["streams"] = new ValueTuple<uint, IDictionary<string, object>>[]
            {
                (1U, new Dictionary<string, object>()),
                (2U, new Dictionary<string, object>()),
            },
        };

        Assert.Throws<InvalidDataException>(() => PortalStreamInfo.Read(missing));
        Assert.Throws<InvalidDataException>(() => PortalStreamInfo.Read(multiple));
    }

    [Fact]
    public void PortalCaptureRejectsUnreadableUnsupportedAndOutsideFrames()
    {
        var stream = new PortalStreamInfo(42, SourceType: 2, Position: null, Size: null);
        var bounds = new PixelRect(0, 0, 1, 1);

        Assert.Throws<InvalidDataException>(() =>
            PortalFrameCropper.Crop(
                new VideoFrame([], stride: 0, width: 0, height: 0, PixelFormat.Bgra, sequenceNumber: 1),
                stream,
                bounds,
                bounds
            )
        );
        Assert.Throws<InvalidDataException>(() =>
            PortalFrameCropper.Crop(
                new VideoFrame([0, 0, 0, 0], stride: 4, width: 1, height: 1, PixelFormat.Yuv420, sequenceNumber: 1),
                stream,
                bounds,
                bounds
            )
        );
        Assert.Throws<InvalidDataException>(() =>
            PortalFrameCropper.Crop(
                new VideoFrame([0, 0, 0, 0], stride: 4, width: 1, height: 1, PixelFormat.Bgra, sequenceNumber: 1),
                stream,
                new PixelRect(2, 2, 1, 1),
                bounds
            )
        );
    }

    [Fact]
    public void DiagnosticWriterCreatesAPortablePng()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SrvSurvey-fss-diagnostic-" + Guid.NewGuid().ToString("N"));
        try
        {
            var buffer = new CapturedPixelBuffer(1, 1, [51, 34, 17, 255]);

            string path = FssTuningDiagnosticWriter.Save(directory, buffer, 42);

            Assert.StartsWith(directory, path);
            Assert.Equal(new byte[] { 137, 80, 78, 71 }, File.ReadAllBytes(path)[..4]);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static CapturedPixelBuffer DecodeX11(byte[] bytes, int bitsPerPixel, int byteOrder, int? stride = null)
    {
        nint pointer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
            return X11GameScreenCapture.Decode(
                new X11GameScreenCapture.X11ImageMetadata
                {
                    Width = 1,
                    Height = 1,
                    Data = pointer,
                    ByteOrder = byteOrder,
                    BytesPerLine = stride ?? bytes.Length,
                    BitsPerPixel = bitsPerPixel,
                    RedMask = 0x00FF0000,
                    GreenMask = 0x0000FF00,
                    BlueMask = 0x000000FF,
                }
            );
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    private sealed class StubCapture(Func<PixelRect, CapturedPixelBuffer> capture) : IGameScreenCapture
    {
        public bool IsAvailable => true;

        public string? UnavailableReason => null;

        public CapturedPixelBuffer Capture(PixelRect bounds) => capture(bounds);

        public void Dispose() { }
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;

        public void Advance(TimeSpan amount)
        {
            utcNow += amount;
        }
    }
}
