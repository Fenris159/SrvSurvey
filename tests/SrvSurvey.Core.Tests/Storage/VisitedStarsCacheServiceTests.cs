using System.Net;
using System.Net.Http.Headers;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Core.Tests.Storage;

public sealed class VisitedStarsCacheServiceTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-visited-stars-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task SwapCreatesVerifiedBackupAndRestorePreservesIt()
    {
        var gameDirectory = Path.Combine(temporaryDirectory, "game", "123");
        var downloadDirectory = Path.Combine(temporaryDirectory, "downloads");
        Directory.CreateDirectory(gameDirectory);
        var target = Path.Combine(
            gameDirectory,
            VisitedStarsCacheService.CacheFileName);
        byte[] original = [1, 2, 3, 4];
        byte[] replacement = [9, 8, 7, 6, 5];
        await File.WriteAllBytesAsync(target, original);
        var service = CreateService(downloadDirectory, replacement);

        var swapped = await service.SwapAsync("Sol", target);
        var restored = await service.RestoreAsync(target);

        Assert.Equal(replacement, await File.ReadAllBytesAsync(swapped.DownloadPath));
        Assert.Equal(original, await File.ReadAllBytesAsync(swapped.BackupPath));
        Assert.Equal(original, await File.ReadAllBytesAsync(target));
        Assert.True(File.Exists(swapped.BackupPath + ".sha256"));
        Assert.Equal(swapped.BackupPath, restored.BackupPath);
        Assert.Equal(64, swapped.OriginalSha256.Length);
        Assert.Equal(64, swapped.ReplacementSha256.Length);
    }

    [Fact]
    public async Task SwapRejectsUnexpectedResponseWithoutChangingCache()
    {
        var gameDirectory = Path.Combine(temporaryDirectory, "game", "123");
        Directory.CreateDirectory(gameDirectory);
        var target = Path.Combine(
            gameDirectory,
            VisitedStarsCacheService.CacheFileName);
        byte[] original = [1, 2, 3, 4];
        await File.WriteAllBytesAsync(target, original);
        using var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(
            HttpStatusCode.OK)
        {
            Content = new StringContent("not a cache"),
        }));
        var service = new VisitedStarsCacheService(
            client,
            Path.Combine(temporaryDirectory, "downloads"));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.SwapAsync("Sol", target));

        Assert.Equal(original, await File.ReadAllBytesAsync(target));
        Assert.False(File.Exists(VisitedStarsCacheService.GetBackupPath(target)));
    }

    [Fact]
    public async Task SwapRefusesToRunWhileGameIsActive()
    {
        var target = Path.Combine(
            temporaryDirectory,
            VisitedStarsCacheService.CacheFileName);
        Directory.CreateDirectory(temporaryDirectory);
        await File.WriteAllBytesAsync(target, [1]);
        var service = CreateService(
            Path.Combine(temporaryDirectory, "downloads"),
            [2],
            isGameRunning: () => true);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SwapAsync("Sol", target));

        Assert.Contains("Close Elite Dangerous", exception.Message);
        Assert.Equal([1], await File.ReadAllBytesAsync(target));
    }

    [Fact]
    public async Task RestoreRejectsBackupWhoseRecordedHashNoLongerMatches()
    {
        var target = Path.Combine(
            temporaryDirectory,
            VisitedStarsCacheService.CacheFileName);
        Directory.CreateDirectory(temporaryDirectory);
        await File.WriteAllBytesAsync(target, [1, 2, 3]);
        var service = CreateService(
            Path.Combine(temporaryDirectory, "downloads"),
            [4, 5, 6]);
        var swapped = await service.SwapAsync("Sol", target);
        await File.WriteAllBytesAsync(swapped.BackupPath, [0]);

        await Assert.ThrowsAsync<IOException>(() => service.RestoreAsync(target));

        Assert.Equal([4, 5, 6], await File.ReadAllBytesAsync(target));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private static VisitedStarsCacheService CreateService(
        string downloadDirectory,
        byte[] content,
        Func<bool>? isGameRunning = null)
    {
        var client = new HttpClient(new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(
                "https://edgalaxy.net/visitedstars",
                request.RequestUri?.AbsoluteUri);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(
                "application/octet-stream");
            response.Content.Headers.ContentDisposition =
                new ContentDispositionHeaderValue("attachment")
                {
                    FileName = VisitedStarsCacheService.CacheFileName,
                };
            return response;
        }));
        return new VisitedStarsCacheService(
            client,
            downloadDirectory,
            isGameRunning);
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(responseFactory(request));
        }
    }
}
