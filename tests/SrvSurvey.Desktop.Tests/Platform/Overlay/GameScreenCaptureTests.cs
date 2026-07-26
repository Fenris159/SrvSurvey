using System.Runtime.InteropServices;
using Avalonia;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform.Overlay;

public sealed class GameScreenCaptureTests
{
    [Fact]
    public void CapturedBufferReadsBgraPixelsAsRgb()
    {
        var buffer = new CapturedPixelBuffer(
            2,
            1,
            [51, 34, 17, 255, 102, 85, 68, 255]);

        Assert.Equal(new FssRgbPixel(17, 34, 51), buffer.GetPixel(0, 0));
        Assert.Equal(new FssRgbPixel(68, 85, 102), buffer.GetPixel(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.GetPixel(2, 0));
    }

    [Fact]
    public void X11DecoderReadsLittleEndian32BitPixels()
    {
        var buffer = DecodeX11(
            [51, 34, 17, 0],
            bitsPerPixel: 32,
            byteOrder: 0);

        Assert.Equal(new FssRgbPixel(17, 34, 51), buffer.GetPixel(0, 0));
    }

    [Fact]
    public void X11DecoderReadsBigEndian24BitPixels()
    {
        var buffer = DecodeX11(
            [17, 34, 51, 0],
            bitsPerPixel: 24,
            byteOrder: 1,
            stride: 4);

        Assert.Equal(new FssRgbPixel(17, 34, 51), buffer.GetPixel(0, 0));
    }

    [Fact]
    public void UnavailableCaptureReportsItsCapabilityFailure()
    {
        using var capture = new UnavailableGameScreenCapture("Wayland capture unavailable.");

        Assert.False(capture.IsAvailable);
        Assert.Contains("Wayland", capture.UnavailableReason);
        Assert.Throws<NotSupportedException>(
            () => capture.Capture(new PixelRect(0, 0, 1, 1)));
    }

    [Fact]
    public void DiagnosticWriterCreatesAPortablePng()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "SrvSurvey-fss-diagnostic-" + Guid.NewGuid().ToString("N"));
        try
        {
            var buffer = new CapturedPixelBuffer(
                1,
                1,
                [51, 34, 17, 255]);

            var path = FssTuningDiagnosticWriter.Save(directory, buffer, 42);

            Assert.StartsWith(directory, path);
            Assert.Equal(
                new byte[] { 137, 80, 78, 71 },
                File.ReadAllBytes(path)[..4]);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static CapturedPixelBuffer DecodeX11(
        byte[] bytes,
        int bitsPerPixel,
        int byteOrder,
        int? stride = null)
    {
        var pointer = Marshal.AllocHGlobal(bytes.Length);
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
                });
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }
}
