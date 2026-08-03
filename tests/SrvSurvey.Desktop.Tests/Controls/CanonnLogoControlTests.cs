using System.Security.Cryptography;
using SrvSurvey.Desktop.Controls;

namespace SrvSurvey.Desktop.Tests.Controls;

public sealed class CanonnLogoControlTests
{
    [Fact]
    public void EmbeddedArtworkIsByteForByteLegacyCanonnLogo()
    {
        var bytes = CanonnLogoControl.GetOriginalPngBytes();

        Assert.Equal(1193, bytes.Length);
        Assert.Equal(
            "f20b153c6a3beb2299d79a9b1e7d79f5509b6d82e072baf74e5282247833e65d",
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }

    [Fact]
    public void EmbeddedArtworkRetainsNativeSixteenPixelPngDimensions()
    {
        var bytes = CanonnLogoControl.GetOriginalPngBytes();

        Assert.Equal(16u, ReadBigEndianUInt32(bytes, 16));
        Assert.Equal(16u, ReadBigEndianUInt32(bytes, 20));
    }

    private static uint ReadBigEndianUInt32(byte[] bytes, int offset) =>
        (uint)bytes[offset] << 24
        | (uint)bytes[offset + 1] << 16
        | (uint)bytes[offset + 2] << 8
        | bytes[offset + 3];
}
