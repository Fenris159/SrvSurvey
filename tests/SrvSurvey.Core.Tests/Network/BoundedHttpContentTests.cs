using System.Net;
using System.Text;
using SrvSurvey.Core.Network;

namespace SrvSurvey.Core.Tests.Network;

public sealed class BoundedHttpContentTests
{
    [Fact]
    public async Task ReadBytesAsyncRejectsDeclaredOversizedContent()
    {
        using var content = new ByteArrayContent(new byte[17]);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            BoundedHttpContent.ReadBytesAsync(
                content,
                16,
                "Test response"));

        Assert.Contains("Test response", exception.Message);
        Assert.Contains("16-byte", exception.Message);
    }

    [Fact]
    public async Task ReadBytesAsyncRejectsChunkedOversizedContent()
    {
        using var content = new UnknownLengthContent(new byte[17]);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            BoundedHttpContent.ReadBytesAsync(
                content,
                16,
                "Chunked response"));
    }

    [Fact]
    public async Task JsonAndTextReadersPreserveValidContent()
    {
        using var jsonContent = new StringContent(
            "{\"value\":42}",
            Encoding.UTF8,
            "application/json");
        using var document = await BoundedHttpContent.ReadJsonDocumentAsync(
            jsonContent,
            1_024,
            "JSON response");
        using var textContent = new StringContent(
            "raven",
            Encoding.Unicode,
            "text/plain");

        var text = await BoundedHttpContent.ReadStringAsync(
            textContent,
            1_024,
            "Text response");

        Assert.Equal(42, document.RootElement.GetProperty("value").GetInt32());
        Assert.Equal("raven", text);
    }

    [Fact]
    public async Task ReadStringPrefixAsyncTruncatesWithoutBufferingTheRemainder()
    {
        using var content = new UnknownLengthContent(
            Encoding.UTF8.GetBytes("0123456789"));

        var text = await BoundedHttpContent.ReadStringPrefixAsync(
            content,
            5);

        Assert.Equal("01234...", text);
    }

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context)
        {
            return stream.WriteAsync(bytes).AsTask();
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
