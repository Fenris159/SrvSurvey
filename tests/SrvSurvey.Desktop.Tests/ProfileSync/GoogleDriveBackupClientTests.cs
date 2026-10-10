using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using SrvSurvey.Core.ProfileSync;
using SrvSurvey.Desktop.ProfileSync;
using Xunit;

namespace SrvSurvey.Desktop.Tests.ProfileSync;

public sealed class GoogleDriveBackupClientTests
{
    [Fact]
    public async Task BrowserLinkValidatesPkceAndStatePersistsAuthorizationAndDisconnects()
    {
        using var root = new ProfileSyncTestRoot();
        string? challenge = null;
        using var harness = new DriveHarness(
            root,
            async request =>
            {
                Dictionary<string, string> form = Query(await request.Content!.ReadAsStringAsync());
                Assert.Equal("authorization_code", form["grant_type"]);
                Assert.Equal("test-code", form["code"]);
                string expected = Convert
                    .ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"])))
                    .TrimEnd('=')
                    .Replace('+', '-')
                    .Replace('/', '_');
                Assert.Equal(challenge, expected);
                return Json(Token());
            },
            uri =>
            {
                Dictionary<string, string> query = Query(uri.Query);
                challenge = query["code_challenge"];
                Assert.Equal("S256", query["code_challenge_method"]);
                Assert.Equal("offline", query["access_type"]);
                Assert.EndsWith("/auth/drive.appdata", query["scope"]);
            }
        );
        Assert.True(harness.Drive.IsConfigured);
        Assert.False(harness.Drive.IsLinked);
        await harness.LinkAsync();
        Assert.True(harness.Drive.IsLinked);
        string tokenPath = Path.Combine(root.Store.StateDirectory, "google-authorization.dat");
        Assert.True(File.Exists(tokenPath));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(tokenPath));
        }
        using var reopened = new GoogleDriveBackupClient(root.Store);
        Assert.True(reopened.IsLinked);
        harness.Drive.Disconnect();
        Assert.False(harness.Drive.IsLinked);
        Assert.False(File.Exists(tokenPath));
        harness.Drive.Disconnect();
    }

    [Fact]
    public async Task InvalidStateCannotExchangeAResponseAndCancellationClosesTheListener()
    {
        using var root = new ProfileSyncTestRoot();
        using var invalid = new DriveHarness(
            root,
            _ => throw new InvalidOperationException("Must not exchange"),
            state: "wrong"
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() => invalid.LinkAsync());
        Assert.False(invalid.Drive.IsLinked);
        using var cancellation = new CancellationTokenSource();
        using var client = new GoogleDriveBackupClient(root.Store, openBrowser: _ => cancellation.Cancel());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.LinkAsync(cancellation.Token));
        Assert.False(client.IsLinked);
    }

    [Fact]
    public async Task MissingSetupOrCorruptAuthorizationDoesNotOpenBrowserOrAuthorizeRequests()
    {
        using var root = new ProfileSyncTestRoot();
        await File.WriteAllTextAsync(Path.Combine(root.Store.StateDirectory, "google-authorization.dat"), "broken");
        using var client = new GoogleDriveBackupClient(
            root.Store,
            openBrowser: _ => throw new InvalidOperationException("Do not open")
        );
        Assert.False(client.IsConfigured);
        Assert.False(client.IsLinked);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.LinkAsync(CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RefreshUsesStoredTokenAndMetadataPaginationSkipsInvalidEntries()
    {
        using var root = new ProfileSyncTestRoot();
        int exchanges = 0;
        int pages = 0;
        using var harness = new DriveHarness(
            root,
            async request =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("/token", StringComparison.Ordinal))
                {
                    exchanges++;
                    Dictionary<string, string> form = Query(await request.Content!.ReadAsStringAsync());
                    if (exchanges == 1)
                    {
                        return Json(Token(expires: -1));
                    }
                    Assert.Equal("refresh_token", form["grant_type"]);
                    Assert.Equal("refresh-test", form["refresh_token"]);
                    return Json("{\"access_token\":\"refreshed-test\",\"expires_in\":3600}");
                }
                Assert.Equal("refreshed-test", request.Headers.Authorization!.Parameter);
                Dictionary<string, string> query = Query(request.RequestUri.Query);
                Assert.Equal("appDataFolder", query["spaces"]);
                pages++;
                if (pages == 1)
                {
                    return Json(
                        """{"nextPageToken":"next-page","files":[{"id":"a","name":"backup","createdTime":"2026-10-10T12:00:00Z","appProperties":{"deviceId":"A","deviceName":"Computer A"}},{"id":"invalid-date","createdTime":"bad","appProperties":{"deviceId":"A"}},{"id":"no-device"}]}"""
                    );
                }
                Assert.Equal("next-page", query["pageToken"]);
                return Json("{\"files\":[null,{}]}");
            }
        );
        await harness.LinkAsync();
        DriveBackup backup = Assert.Single(await harness.Drive.ListAsync(CancellationToken.None));
        Assert.Equal("Computer A", backup.DeviceName);
        Assert.Equal(2, exchanges);
        Assert.Equal(2, pages);
    }

    [Fact]
    public async Task DownloadsValidateContentAndBoundStreamSize()
    {
        using var root = new ProfileSyncTestRoot();
        root.Write("data", "bookmarks.json", "[]");
        byte[] body = Encoding.UTF8.GetBytes(" \n" + Encoding.UTF8.GetString(root.Store.Capture().Serialize()) + "\n ");
        using var harness = new DriveHarness(
            root,
            request =>
                Task.FromResult(
                    request.RequestUri!.AbsolutePath.EndsWith("/token", StringComparison.Ordinal)
                        ? Json(Token())
                        : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
                )
        );
        await harness.LinkAsync();
        Assert.Equal(body, await harness.Drive.DownloadBytesAsync("file/id", CancellationToken.None));
        ProfileSnapshot result = await harness.Drive.DownloadAsync("file/id", CancellationToken.None);
        Assert.Single(result.Portable);
        body = Encoding.UTF8.GetBytes("not JSON");
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            harness.Drive.DownloadAsync("bad", CancellationToken.None)
        );
        body = new byte[ProfileSnapshot.MaximumBytes + 1];
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            harness.Drive.DownloadAsync("too-big", CancellationToken.None)
        );
    }

    [Theory]
    [InlineData("\"123456\"", 123456L)]
    [InlineData("\"0\"", 0L)]
    [InlineData("\"-1\"", null)]
    [InlineData("\"invalid\"", null)]
    [InlineData("\"9223372036854775808\"", null)]
    [InlineData("null", null)]
    [InlineData("{}", null)]
    public async Task HistoryReadsGoogleStringSizesWithoutInventingMissingValues(string size, long? expected)
    {
        using var root = new ProfileSyncTestRoot();
        using var harness = new DriveHarness(
            root,
            request =>
                Task.FromResult(
                    request.RequestUri!.AbsolutePath.EndsWith("/token", StringComparison.Ordinal)
                        ? Json(Token())
                        : Json(
                            "{\"files\":[{\"id\":\"a\",\"createdTime\":\"2026-10-10T12:00:00Z\",\"appProperties\":{\"deviceId\":\"A\"},\"size\":"
                                + size
                                + "}]}"
                        )
                )
        );
        await harness.LinkAsync();
        DriveBackup backup = Assert.Single(await harness.Drive.ListAsync(CancellationToken.None));
        Assert.Equal(expected, backup.SizeBytes);
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task DeletionEscapesTheIdAndOnlyIgnoresAlreadyRemovedFiles(HttpStatusCode status)
    {
        using var root = new ProfileSyncTestRoot();
        using var harness = new DriveHarness(
            root,
            request =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("/token", StringComparison.Ordinal))
                {
                    return Task.FromResult(Json(Token()));
                }
                Assert.Equal(HttpMethod.Delete, request.Method);
                Assert.EndsWith("file%2Fid", request.RequestUri.AbsoluteUri);
                Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
                return Task.FromResult(new HttpResponseMessage(status));
            }
        );
        await harness.LinkAsync();
        if (status == HttpStatusCode.Forbidden)
        {
            HttpRequestException error = await Assert.ThrowsAsync<HttpRequestException>(() =>
                harness.Drive.DeleteAsync("file/id", CancellationToken.None)
            );
            Assert.Equal(status, error.StatusCode);
        }
        else
        {
            await harness.Drive.DeleteAsync("file/id", CancellationToken.None);
        }
    }

    [Fact]
    public async Task ResumableUploadIncludesPrivateFolderAndPrunesOnlyThisMachinesOldHistory()
    {
        using var root = new ProfileSyncTestRoot();
        root.Write("data", "bookmarks.json", "[]");
        ProfileSnapshot snapshot = root.Store.Capture();
        int uploads = 0;
        var deleted = new List<string>();
        using var harness = new DriveHarness(
            root,
            async request =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("/token", StringComparison.Ordinal))
                {
                    return Json(Token());
                }
                Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
                if (request.Method == HttpMethod.Post)
                {
                    Assert.Contains("uploadType=resumable", request.RequestUri.Query);
                    JsonNode metadata = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
                    Assert.Equal("appDataFolder", metadata["parents"]![0]!.GetValue<string>());
                    Assert.Equal(snapshot.DeviceId, metadata["appProperties"]!["deviceId"]!.GetValue<string>());
                    HttpResponseMessage session = Json("{}");
                    session.Headers.Location = new Uri("https://www.googleapis.com/upload/session-test");
                    return session;
                }
                if (request.Method == HttpMethod.Put)
                {
                    var payload = ProfileSnapshot.Parse(await request.Content!.ReadAsByteArrayAsync());
                    Assert.Equal(snapshot.DeviceId, payload.DeviceId);
                    uploads++;
                    return Json("{\"id\":\"new\"}");
                }
                if (request.Method == HttpMethod.Delete)
                {
                    deleted.Add(request.RequestUri.AbsolutePath);
                    return Json("{}");
                }
                var files = new JsonArray();
                for (int i = 0; i < 12; i++)
                {
                    files.Add(
                        new JsonObject
                        {
                            ["id"] = "file-" + i,
                            ["createdTime"] = DateTimeOffset.UtcNow.AddMinutes(-i).ToString("O"),
                            ["appProperties"] = new JsonObject { ["deviceId"] = snapshot.DeviceId },
                        }
                    );
                }
                files.Add(
                    new JsonObject
                    {
                        ["id"] = "other",
                        ["createdTime"] = "2025-01-01T00:00:00Z",
                        ["appProperties"] = new JsonObject { ["deviceId"] = "other" },
                    }
                );
                return Json(new JsonObject { ["files"] = files }.ToJsonString());
            }
        );
        await harness.LinkAsync();
        await harness.Drive.UploadAsync(snapshot, CancellationToken.None);
        Assert.Equal(1, uploads);
        Assert.Equal(2, deleted.Count);
        Assert.All(deleted, path => Assert.DoesNotContain("other", path));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("http://www.googleapis.com/upload/session")]
    [InlineData("https://untrusted.example/upload/session")]
    [InlineData("/relative")]
    public async Task UploadNeverSendsBearerToAnUntrustedSession(string location)
    {
        using var root = new ProfileSyncTestRoot();
        using var harness = new DriveHarness(
            root,
            request =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("/token", StringComparison.Ordinal))
                {
                    return Task.FromResult(Json(Token()));
                }
                Assert.Equal(HttpMethod.Post, request.Method);
                HttpResponseMessage response = Json("{}");
                if (location != "missing")
                {
                    response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
                }
                return Task.FromResult(response);
            }
        );
        await harness.LinkAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            harness.Drive.UploadAsync(root.Store.Capture(), CancellationToken.None)
        );
    }

    [Theory]
    [InlineData("[]", "invalid authorization")]
    [InlineData("{}", "renewable access")]
    [InlineData("{\"access_token\":\"access\"}", "renewable access")]
    public async Task InvalidTokenResponsesAreRejected(string response, string message)
    {
        using var root = new ProfileSyncTestRoot();
        using var harness = new DriveHarness(root, _ => Task.FromResult(Json(response)));
        InvalidDataException failure = await Assert.ThrowsAsync<InvalidDataException>(() => harness.LinkAsync());
        Assert.Contains(message, failure.Message);
    }

    [Fact]
    public async Task AuthorizationAndDriveErrorsDoNotExposeResponseSecrets()
    {
        using var root = new ProfileSyncTestRoot();
        bool failToken = true;
        using var harness = new DriveHarness(
            root,
            request =>
                Task.FromResult(
                    request.RequestUri!.AbsolutePath.EndsWith("/token", StringComparison.Ordinal) && !failToken
                        ? Json(Token())
                        : new HttpResponseMessage(HttpStatusCode.Forbidden)
                        {
                            Content = new StringContent("private-response-secret"),
                        }
                )
        );
        InvalidOperationException auth = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.LinkAsync());
        Assert.DoesNotContain("private-response-secret", auth.Message);
        failToken = false;
        await harness.LinkAsync();
        HttpRequestException drive = await Assert.ThrowsAsync<HttpRequestException>(() =>
            harness.Drive.ListAsync(CancellationToken.None)
        );
        Assert.Equal(HttpStatusCode.Forbidden, drive.StatusCode);
        Assert.DoesNotContain("private-response-secret", drive.Message);
    }

    private static string Token(int expires = 3600) =>
        $"{{\"access_token\":\"access-test\",\"refresh_token\":\"refresh-test\",\"expires_in\":{expires}}}";

    private static HttpResponseMessage Json(string value) =>
        new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/json") };

    private static Dictionary<string, string> Query(string value) =>
        value
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => WebUtility.UrlDecode(pair[0]), pair => WebUtility.UrlDecode(pair[1]));

    private sealed class DriveHarness : IDisposable
    {
        private Task? callback;

        internal DriveHarness(
            ProfileSyncTestRoot root,
            Func<HttpRequestMessage, Task<HttpResponseMessage>> handler,
            Action<Uri>? inspect = null,
            string? state = null
        )
        {
            root.Store.SavePreferences(new(ClientId: "test.apps.googleusercontent.com", ClientSecret: "test-client"));
            Drive = new(
                root.Store,
                new HttpClient(new Handler(handler)),
                uri =>
                {
                    inspect?.Invoke(uri);
                    Dictionary<string, string> query = Query(uri.Query);
                    string redirect = query["redirect_uri"];
                    callback = Task.Run(async () =>
                    {
                        using var browser = new HttpClient();
                        using HttpResponseMessage response = await browser.GetAsync(
                            redirect + "?code=test-code&state=" + Uri.EscapeDataString(state ?? query["state"])
                        );
                        Assert.Equal(
                            state is null ? HttpStatusCode.OK : HttpStatusCode.BadRequest,
                            response.StatusCode
                        );
                    });
                }
            );
        }

        internal GoogleDriveBackupClient Drive { get; }

        internal async Task LinkAsync()
        {
            try
            {
                await Drive.LinkAsync(CancellationToken.None);
            }
            finally
            {
                if (callback is not null)
                {
                    await callback;
                }
            }
        }

        public void Dispose() => Drive.Dispose();
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => handler(request);
    }
}
