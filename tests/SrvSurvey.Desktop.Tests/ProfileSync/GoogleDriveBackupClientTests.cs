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
    public async Task StaleRefreshFailurePreservesAnotherInstancesNewAuthorization()
    {
        using var root = new ProfileSyncTestRoot();
        int exchanges = 0;
        using var stale = new DriveHarness(
            root,
            _ =>
                Task.FromResult(
                    ++exchanges == 1
                        ? Json(Token(expires: -1))
                        : new HttpResponseMessage(HttpStatusCode.BadRequest)
                        {
                            Content = new StringContent("{\"error\":\"invalid_grant\"}"),
                        }
                )
        );
        await stale.LinkAsync();
        using var linked = new DriveHarness(
            root,
            _ => Task.FromResult(Json(Token().Replace("access-test", "new-access", StringComparison.Ordinal)))
        );
        await linked.LinkAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => stale.Drive.ListAsync(CancellationToken.None));

        using var reopened = new DriveHarness(
            root,
            request =>
            {
                Assert.Equal("new-access", request.Headers.Authorization!.Parameter);
                return Task.FromResult(Json("{\"files\":[]}"));
            }
        );
        Assert.True(reopened.Drive.IsLinked);
        Assert.Empty(await reopened.Drive.ListAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("bytes=5-9")]
    [InlineData("bytes=0-9999999999")]
    [InlineData("bytes=0-10,20-30")]
    [InlineData("invalid")]
    [InlineData(null)]
    [InlineData("premature-completion")]
    public async Task InvalidUploadAcknowledgementsNeverTriggerHistoryCleanup(string? acknowledgement)
    {
        using var root = new ProfileSyncTestRoot();
        root.Write("data", "bookmarks.json", "[\"" + new string('x', 2 * 1024 * 1024) + "\"]");
        bool cleanedUp = false;
        using var harness = new DriveHarness(
            root,
            request =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("/token", StringComparison.Ordinal))
                {
                    return Task.FromResult(Json(Token()));
                }
                if (request.Method == HttpMethod.Post)
                {
                    HttpResponseMessage session = Json("{}");
                    session.Headers.Location = new Uri("https://www.googleapis.com/upload/session-test");
                    return Task.FromResult(session);
                }
                if (request.Method != HttpMethod.Put)
                {
                    cleanedUp = true;
                    return Task.FromResult(Json("{\"files\":[]}"));
                }
                if (acknowledgement == "premature-completion")
                {
                    return Task.FromResult(Json("{\"id\":\"incomplete\"}"));
                }
                var response = new HttpResponseMessage((HttpStatusCode)308);
                if (acknowledgement is not null)
                {
                    response.Headers.TryAddWithoutValidation("Range", acknowledgement);
                }
                return Task.FromResult(response);
            }
        );
        await harness.LinkAsync();

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            harness.Drive.UploadAsync(root.Store.Capture(), CancellationToken.None)
        );

        Assert.False(cleanedUp);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LostUploadResponseChecksWhetherGoogleAcceptedTheData(bool accepted)
    {
        using var root = new ProfileSyncTestRoot();
        int sends = 0;
        int probes = 0;
        using var harness = new DriveHarness(
            root,
            request =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("/token", StringComparison.Ordinal))
                {
                    return Task.FromResult(Json(Token()));
                }
                if (request.Method == HttpMethod.Post)
                {
                    HttpResponseMessage session = Json("{}");
                    session.Headers.Location = new Uri("https://www.googleapis.com/upload/session-test");
                    return Task.FromResult(session);
                }
                if (request.Method == HttpMethod.Get)
                {
                    return Task.FromResult(Json("{\"files\":[]}"));
                }
                if (request.Content!.Headers.ContentLength == 0)
                {
                    probes++;
                    return Task.FromResult(
                        accepted ? Json("{\"id\":\"complete\"}") : new HttpResponseMessage((HttpStatusCode)308)
                    );
                }
                if (++sends == 1)
                {
                    throw new TaskCanceledException("Simulated request timeout.");
                }
                return Task.FromResult(Json("{\"id\":\"complete\"}"));
            }
        );
        await harness.LinkAsync();

        await harness.Drive.UploadAsync(root.Store.Capture(), CancellationToken.None);

        Assert.Equal(accepted ? 1 : 2, sends);
        Assert.Equal(1, probes);
    }

    [Theory]
    [InlineData(false, 4)]
    [InlineData(true, 1)]
    public async Task UploadRetriesAreBoundedAndCallerCancellationIsImmediate(bool cancel, int expectedRequests)
    {
        using var root = new ProfileSyncTestRoot();
        using var cancellation = new CancellationTokenSource();
        int sends = 0;
        using var harness = new DriveHarness(
            root,
            request =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("/token", StringComparison.Ordinal))
                {
                    return Task.FromResult(Json(Token()));
                }
                if (request.Method == HttpMethod.Post)
                {
                    HttpResponseMessage session = Json("{}");
                    session.Headers.Location = new Uri("https://www.googleapis.com/upload/session-test");
                    return Task.FromResult(session);
                }
                Assert.Equal(HttpMethod.Put, request.Method);
                sends++;
                if (cancel)
                {
                    cancellation.Cancel();
                    throw new OperationCanceledException(cancellation.Token);
                }
                throw new HttpRequestException("Unavailable", null, HttpStatusCode.ServiceUnavailable);
            }
        );
        await harness.LinkAsync();

        if (cancel)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                harness.Drive.UploadAsync(root.Store.Capture(), cancellation.Token)
            );
        }
        else
        {
            await Assert.ThrowsAsync<HttpRequestException>(() =>
                harness.Drive.UploadAsync(root.Store.Capture(), cancellation.Token)
            );
        }
        Assert.Equal(expectedRequests, sends);
    }

    [Theory]
    [InlineData("invalid_grant", true, "Disconnect and link Google Drive again")]
    [InlineData("invalid_client", true, "publisher")]
    public async Task RefreshFailureDistinguishesExpiredConsentFromPublisherSetup(
        string code,
        bool linked,
        string message
    )
    {
        using var root = new ProfileSyncTestRoot();
        int exchanges = 0;
        using var harness = new DriveHarness(
            root,
            _ =>
                Task.FromResult(
                    ++exchanges == 1
                        ? Json(Token(expires: -1))
                        : new HttpResponseMessage(HttpStatusCode.BadRequest)
                        {
                            Content = new StringContent(
                                "{\"error\":\"" + code + "\",\"error_description\":\"private-response-secret\"}"
                            ),
                        }
                )
        );
        await harness.LinkAsync();

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Drive.ListAsync(CancellationToken.None)
        );

        Assert.Contains(message, failure.Message);
        Assert.DoesNotContain("private-response-secret", failure.Message);
        Assert.Equal(linked, harness.Drive.IsLinked);
        Assert.Equal(linked, File.Exists(Path.Combine(root.Store.StateDirectory, "google-authorization.dat")));
    }

    [Fact]
    public async Task InterruptedUploadResumesAfterBytesAlreadyAcceptedWithoutDuplicatingTheBackup()
    {
        using var root = new ProfileSyncTestRoot();
        root.Write("data", "bookmarks.json", "[\"" + new string('x', 2 * 1024 * 1024 + 100) + "\"]");
        ProfileSnapshot snapshot = root.Store.Capture();
        byte[] expected = snapshot.Serialize();
        using var received = new MemoryStream();
        int sessions = 0;
        int probes = 0;
        bool interrupted = false;
        using var harness = new DriveHarness(
            root,
            async request =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("/token", StringComparison.Ordinal))
                {
                    return Json(Token());
                }
                if (request.Method == HttpMethod.Post)
                {
                    sessions++;
                    HttpResponseMessage session = Json("{}");
                    session.Headers.Location = new Uri("https://www.googleapis.com/upload/session-test");
                    return session;
                }
                if (request.Method == HttpMethod.Get)
                {
                    Assert.Equal(expected, received.ToArray());
                    return Json("{\"files\":[]}");
                }
                Assert.Equal(HttpMethod.Put, request.Method);
                byte[] part = await request.Content!.ReadAsByteArrayAsync();
                if (part.Length == 0)
                {
                    probes++;
                    Assert.Equal("bytes */" + expected.Length, request.Content.Headers.ContentRange!.ToString());
                }
                else
                {
                    Assert.InRange(part.Length, 1, 1024 * 1024);
                    Assert.Equal(received.Length, request.Content.Headers.ContentRange!.From);
                    await received.WriteAsync(part);
                    if (!interrupted)
                    {
                        interrupted = true;
                        throw new HttpRequestException("Connection lost after Google accepted the chunk.");
                    }
                }
                if (received.Length == expected.Length)
                {
                    return Json("{\"id\":\"complete\"}");
                }
                var incomplete = new HttpResponseMessage((HttpStatusCode)308);
                incomplete.Headers.Add("Range", "bytes=0-" + (received.Length - 1));
                return incomplete;
            }
        );
        await harness.LinkAsync();

        await harness.Drive.UploadAsync(snapshot, CancellationToken.None);

        Assert.Equal(expected, received.ToArray());
        Assert.Equal(1, sessions);
        Assert.Equal(1, probes);
    }

    /// <summary>Verifies that browser link validates PKCE and state persists authorization and disconnects.</summary>
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

    /// <summary>Verifies that invalid state cannot exchange a response and cancellation closes the listener.</summary>
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

    /// <summary>Verifies that missing setup or corrupt authorization does not open browser or authorize requests.</summary>
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

    /// <summary>Verifies that refresh uses stored token and metadata pagination skips invalid entries.</summary>
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

    /// <summary>Verifies that downloads validate content and bound stream size.</summary>
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

    /// <summary>Verifies that history reads Google string sizes without inventing missing values.</summary>
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

    /// <summary>Verifies that deletion escapes the id and only ignores already removed files.</summary>
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

    /// <summary>Verifies that resumable upload includes private folder and prunes only this machine's old history.</summary>
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

    /// <summary>Verifies that upload never sends bearer to an untrusted session.</summary>
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

    /// <summary>Verifies that invalid token responses are rejected.</summary>
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

    /// <summary>Verifies that authorization and Drive errors do not expose response secrets.</summary>
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

    /// <summary>Builds a test authorization response with a configurable token expiry.</summary>
    private static string Token(int expires = 3600) =>
        $"{{\"access_token\":\"access-test\",\"refresh_token\":\"refresh-test\",\"expires_in\":{expires}}}";

    /// <summary>Creates an HTTP JSON response for the simulated Google endpoints.</summary>
    private static HttpResponseMessage Json(string value) =>
        new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/json") };

    /// <summary>Decodes URL query parameters for OAuth request assertions.</summary>
    private static Dictionary<string, string> Query(string value) =>
        value
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => WebUtility.UrlDecode(pair[0]), pair => WebUtility.UrlDecode(pair[1]));

    private sealed class DriveHarness : IDisposable
    {
        private Task? callback;

        /// <summary>Creates isolated profile storage and a Google client using simulated HTTP and browser callbacks.</summary>
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

        /// <summary>Completes authorization through the test browser and callback listener.</summary>
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

        /// <summary>Releases the Google client and its simulated HTTP resources.</summary>
        public void Dispose() => Drive.Dispose();
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        /// <summary>Routes Google requests to the supplied test handler without contacting the network.</summary>
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => handler(request);
    }
}
