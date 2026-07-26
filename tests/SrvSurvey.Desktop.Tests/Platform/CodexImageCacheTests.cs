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

    [Fact]
    public async Task PrefersLocalFloraAndReusesLegacyJpgCacheAcrossUrlExtensions()
    {
        var cacheDirectory = Path.Combine(temporaryDirectory, "cache");
        var floraDirectory = Path.Combine(temporaryDirectory, "flora");
        Directory.CreateDirectory(cacheDirectory);
        Directory.CreateDirectory(floraDirectory);
        var legacyCache = Path.Combine(cacheDirectory, "2310101.jpg");
        var localImage = Path.Combine(
            floraDirectory,
            "aleoida-arcus-yellow.png");
        await File.WriteAllBytesAsync(legacyCache, [1, 2, 3]);
        await File.WriteAllBytesAsync(localImage, [4, 5, 6]);
        var requests = 0;
        using var client = new HttpClient(new StubHandler(_ =>
        {
            requests++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([7, 8, 9]),
            };
        }));
        using var cache = new CodexImageCache(
            () => new CodexImageLocations(cacheDirectory, floraDirectory),
            client);

        var local = await cache.GetAsync(
            2310101,
            "https://example.test/reference.png",
            "aleoida-arcus-yellow");
        File.Delete(localImage);
        var imported = await cache.GetAsync(
            2310101,
            "https://example.test/reference.png",
            "aleoida-arcus-yellow");

        Assert.True(local.IsSuccess);
        Assert.True(local.IsLocal);
        Assert.Equal(localImage, local.Path);
        Assert.True(imported.IsSuccess);
        Assert.True(imported.IsFromCache);
        Assert.False(imported.IsLocal);
        Assert.Equal(legacyCache, imported.Path);
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task PreDownloadReportsDownloadedCachedLocalAndUnavailableImages()
    {
        var cacheDirectory = Path.Combine(temporaryDirectory, "cache");
        var floraDirectory = Path.Combine(temporaryDirectory, "flora");
        Directory.CreateDirectory(cacheDirectory);
        Directory.CreateDirectory(floraDirectory);
        await File.WriteAllBytesAsync(
            Path.Combine(cacheDirectory, "2.jpg"),
            [2]);
        await File.WriteAllBytesAsync(
            Path.Combine(floraDirectory, "local-three.png"),
            [3]);
        using var client = new HttpClient(new StubHandler(request =>
            request.RequestUri!.AbsolutePath.Contains("missing")
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([1]),
                }));
        using var cache = new CodexImageCache(
            () => new CodexImageLocations(cacheDirectory, floraDirectory),
            client);

        var result = await cache.PreDownloadAsync(
        [
            new CodexImageRequest(1, "https://example.test/one.png"),
            new CodexImageRequest(2, "https://example.test/two.png"),
            new CodexImageRequest(
                3,
                "https://example.test/three.png",
                "local-three"),
            new CodexImageRequest(4, "https://example.test/missing.png"),
            new CodexImageRequest(1, "https://example.test/duplicate.png"),
        ]);

        Assert.Equal(
            new CodexImagePreDownloadResult(4, 1, 1, 1, 1),
            result);
        Assert.Equal([1], await File.ReadAllBytesAsync(
            Path.Combine(cacheDirectory, "1.png")));
        Assert.False(File.Exists(Path.Combine(cacheDirectory, "4.png")));
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
