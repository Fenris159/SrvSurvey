using System.Buffers.Binary;
using System.IO.Compression;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using SkiaSharp;
using SrvSurvey.Core.Discord;
using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class DiscordCarrierImageProcessingTests
{
    [Fact]
    public void OversizedDecodedImagesAreRejectedBeforePixelAllocation()
    {
        var original = new DiscordCarrierImage(Png(8192, 4096), "carrier.png", "image/png");
        Assert.InRange(original.Bytes.Length, 1, 8 * 1024 * 1024);
        Assert.Throws<InvalidDataException>(() =>
            DiscordCarrierImageProcessor.Crop(
                original,
                new(),
                _ => throw new InvalidOperationException("Pixel allocation began before validating image dimensions."),
                image => image.Encode(SKEncodedImageFormat.Png, 100)
            )
        );
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExcessiveImageDimensionsAreRejectedByPreviewAndExport(bool preview)
    {
        var original = new DiscordCarrierImage(Png(16385, 1), "carrier.png", "image/png");
        if (preview)
        {
            Assert.Throws<InvalidDataException>(() =>
            {
                using Bitmap image = DiscordCarrierImageProcessor.CreatePreview(original);
            });
        }
        else
        {
            Assert.Throws<InvalidDataException>(() => DiscordCarrierImageProcessor.Crop(original, new()));
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void FailedSourceOrOutputAllocationReturnsAnImageError(int failedAllocation)
    {
        var original = new DiscordCarrierImage(Png(2, 2), "carrier.png", "image/png");
        int allocation = 0;
        Assert.Throws<InvalidDataException>(() =>
            DiscordCarrierImageProcessor.Crop(
                original,
                new(),
                info => ++allocation == failedAllocation ? null : new SKBitmap(info),
                image => image.Encode(SKEncodedImageFormat.Png, 100)
            )
        );
    }

    [Fact]
    public void FailedPngEncodingReturnsAnImageError()
    {
        var original = new DiscordCarrierImage(Png(2, 2), "carrier.png", "image/png");
        Assert.Throws<InvalidDataException>(() =>
            DiscordCarrierImageProcessor.Crop(original, new(), info => new SKBitmap(info), _ => null)
        );
    }

    [Theory]
    [InlineData("Unable to create a new SKBitmap instance.", typeof(InvalidDataException))]
    [InlineData("Unrelated failure", typeof(InvalidOperationException))]
    public void OnlyIdentifiedNativeAllocationFailuresBecomeImageErrors(string message, Type expected)
    {
        var original = new DiscordCarrierImage(Png(2, 2), "carrier.png", "image/png");
        Assert.Throws(
            expected,
            () =>
                DiscordCarrierImageProcessor.Crop(
                    original,
                    new(),
                    _ => throw new InvalidOperationException(message),
                    image => image.Encode(SKEncodedImageFormat.Png, 100)
                )
        );
    }

    private static byte[] Png(int width, int height)
    {
        using var result = new MemoryStream();
        result.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        Span<byte> header = stackalloc byte[13];
        header.Clear();
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
        header[8] = 8;
        header[9] = 6;
        WriteChunk(result, "IHDR"u8, header);
        using var pixels = new MemoryStream();
        using (var compressor = new ZLibStream(pixels, CompressionLevel.Fastest, leaveOpen: true))
        {
            byte[] row = new byte[1 + width * 4];
            for (int index = 0; index < height; index++)
            {
                compressor.Write(row);
            }
        }
        WriteChunk(result, "IDAT"u8, pixels.ToArray());
        WriteChunk(result, "IEND"u8, []);
        return result.ToArray();
    }

    private static void WriteChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        stream.Write(number);
        stream.Write(type);
        stream.Write(data);
        uint checksum = UpdateChecksum(UpdateChecksum(uint.MaxValue, type), data);
        BinaryPrimitives.WriteUInt32BigEndian(number, ~checksum);
        stream.Write(number);
    }

    private static uint UpdateChecksum(uint checksum, ReadOnlySpan<byte> data)
    {
        foreach (byte value in data)
        {
            checksum ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                checksum = (checksum >> 1) ^ ((checksum & 1) == 0 ? 0 : 0xEDB88320u);
            }
        }
        return checksum;
    }
}
