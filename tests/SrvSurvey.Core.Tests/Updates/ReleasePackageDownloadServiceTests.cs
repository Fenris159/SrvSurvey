using System.Net;
using System.Security.Cryptography;
using SrvSurvey.Core.Updates;

namespace SrvSurvey.Core.Tests.Updates;

public sealed class ReleasePackageDownloadServiceTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-release-download-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task DownloadAsyncVerifiesThenAtomicallyActivatesPackage()
    {
        var bytes = Enumerable.Range(0, 300_000)
            .Select(value => (byte)(value % 251))
            .ToArray();
        var handler = new StubHandler(_ => Response(bytes));
        var progress = new List<ReleasePackageDownloadProgress>();
        var package = CreatePackage(bytes);
        var service = new ReleasePackageDownloadService(new HttpClient(handler));

        var result = await service.DownloadAsync(
            new Version(2, 0, 95, 23),
            package,
            temporaryDirectory,
            new CallbackProgress<ReleasePackageDownloadProgress>(progress.Add));

        Assert.True(result.Downloaded);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(result.ArchivePath));
        Assert.Equal(package.Sha256, result.Sha256);
        Assert.Equal(package.Size, result.Size);
        Assert.Equal(package.DownloadUri, handler.RequestUri);
        Assert.True(handler.NoCache);
        Assert.Contains("SrvSurvey-Avalonia/1.0", handler.UserAgent);
        Assert.DoesNotContain(
            Directory.GetFiles(Path.GetDirectoryName(result.ArchivePath)!),
            path => path.EndsWith(".partial", StringComparison.Ordinal));
        Assert.Equal(package.Size, progress[^1].DownloadedBytes);
    }

    [Fact]
    public async Task DownloadAsyncReusesOnlyVerifiedCachedPackage()
    {
        byte[] bytes = [1, 2, 3, 4, 5];
        var package = CreatePackage(bytes);
        var archivePath = GetArchivePath(package);
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        await File.WriteAllBytesAsync(archivePath, bytes);
        var handler = new StubHandler(_ =>
            throw new InvalidOperationException("Network should not be used."));
        var service = new ReleasePackageDownloadService(new HttpClient(handler));

        var result = await service.DownloadAsync(
            new Version(2, 0, 95, 23),
            package,
            temporaryDirectory);

        Assert.False(result.Downloaded);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(archivePath));
        Assert.Null(handler.RequestUri);
    }

    [Fact]
    public async Task FailedReplacementPreservesExistingCacheByteForByte()
    {
        var expected = new byte[] { 1, 2, 3, 4 };
        var existing = new byte[] { 9, 8, 7, 6 };
        var downloaded = new byte[] { 4, 3, 2, 1 };
        var package = CreatePackage(expected);
        var archivePath = GetArchivePath(package);
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        await File.WriteAllBytesAsync(archivePath, existing);
        var service = new ReleasePackageDownloadService(
            new HttpClient(new StubHandler(_ => Response(downloaded))));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.DownloadAsync(
                new Version(2, 0, 95, 23),
                package,
                temporaryDirectory));

        Assert.Equal(existing, await File.ReadAllBytesAsync(archivePath));
        Assert.DoesNotContain(
            Directory.GetFiles(Path.GetDirectoryName(archivePath)!),
            path => path.EndsWith(".partial", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DownloadAsyncRejectsTruncatedResponse()
    {
        var expected = new byte[] { 1, 2, 3, 4, 5 };
        var response = new byte[] { 1, 2, 3, 4 };
        var package = CreatePackage(expected);
        var content = new ByteArrayContent(response);
        content.Headers.ContentLength = null;
        var service = new ReleasePackageDownloadService(
            new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content,
            })));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.DownloadAsync(
                new Version(2, 0, 95, 23),
                package,
                temporaryDirectory));

        Assert.False(File.Exists(GetArchivePath(package)));
    }

    [Fact]
    public async Task DownloadAsyncRejectsMetadataBeforeNetworkOrDiskMutation()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var package = CreatePackage(bytes) with
        {
            ArchiveName = "../escape.zip",
        };
        var handler = new StubHandler(_ => Response(bytes));
        var service = new ReleasePackageDownloadService(new HttpClient(handler));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.DownloadAsync(
                new Version(2, 0, 95, 23),
                package,
                temporaryDirectory));

        Assert.Null(handler.RequestUri);
        Assert.False(Directory.Exists(temporaryDirectory));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private string GetArchivePath(CrossPlatformReleasePackage package)
    {
        return Path.Combine(
            temporaryDirectory,
            "updates",
            "packages",
            "2.0.95.23",
            package.RuntimeIdentifier,
            package.ArchiveName);
    }

    private static CrossPlatformReleasePackage CreatePackage(byte[] bytes)
    {
        return new CrossPlatformReleasePackage(
            "win-x64",
            "SrvSurvey-Avalonia-2.0.95.23-win-x64.zip",
            "zip",
            bytes.LongLength,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            new Uri("https://downloads.example.test/package.zip"));
    }

    private static HttpResponseMessage Response(byte[] bytes)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes),
        };
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> response)
        : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        public bool NoCache { get; private set; }

        public string UserAgent { get; private set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            NoCache = request.Headers.CacheControl?.NoCache == true;
            UserAgent = request.Headers.UserAgent.ToString();
            return Task.FromResult(response(request));
        }
    }

    private sealed class CallbackProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
