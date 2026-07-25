using System.Net;
using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class CodexImageCacheTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-CodexImageCache-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DownloadsAtomicallyThenReusesCachedImage()
    {
        var requestCount = 0;
        using var client = new HttpClient(new StubHandler(_ =>
        {
            requestCount++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3, 4]),
            };
        }));
        using var cache = new CodexImageCache(temporaryDirectory, client);

        var downloaded = await cache.GetAsync(
            2310206,
            "https://example.test/reference.png");
        var cached = await cache.GetAsync(
            2310206,
            "https://example.test/reference.png");

        Assert.True(downloaded.IsSuccess);
        Assert.False(downloaded.IsFromCache);
        Assert.EndsWith("2310206.png", downloaded.Path);
        Assert.Equal([1, 2, 3, 4], await File.ReadAllBytesAsync(downloaded.Path));
        Assert.True(cached.IsSuccess);
        Assert.True(cached.IsFromCache);
        Assert.Equal(1, requestCount);
        Assert.Empty(Directory.GetFiles(temporaryDirectory, "*.tmp"));
    }

    [Fact]
    public async Task RejectsInvalidMissingAndOversizedImagesWithoutPartialFiles()
    {
        using var client = new HttpClient(new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("missing"))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1]),
            };
            response.Content.Headers.ContentLength =
                CodexImageCache.MaximumImageBytes + 1;
            return response;
        }));
        using var cache = new CodexImageCache(temporaryDirectory, client);

        var invalid = await cache.GetAsync(1, "file:///tmp/image.png");
        var missing = await cache.GetAsync(
            2,
            "https://example.test/missing.png");
        var oversized = await cache.GetAsync(
            3,
            "https://example.test/oversized.jpg");

        Assert.False(invalid.IsSuccess);
        Assert.Contains("invalid", invalid.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(missing.IsSuccess);
        Assert.Contains("No reference image", missing.Error);
        Assert.False(oversized.IsSuccess);
        Assert.Contains("30 MB", oversized.Error);
        Assert.Empty(Directory.Exists(temporaryDirectory)
            ? Directory.GetFiles(temporaryDirectory)
            : []);
    }

    [Fact]
    public async Task TimesOutWhenTheRemoteResponseStalls()
    {
        using var client = new HttpClient(new BlockingHandler());
        using var cache = new CodexImageCache(
            temporaryDirectory,
            client,
            TimeSpan.FromMilliseconds(25));

        var result = await cache.GetAsync(
            2310206,
            "https://example.test/stalled.png");

        Assert.False(result.IsSuccess);
        Assert.Contains("timed out", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.Exists(temporaryDirectory)
            ? Directory.GetFiles(temporaryDirectory)
            : []);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(responder(request));
        }
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The cancellation did not stop the request.");
        }
    }
}
