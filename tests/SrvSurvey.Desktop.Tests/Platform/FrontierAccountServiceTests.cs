using System.Net;
using System.Text;
using SrvSurvey.Core.Frontier;
using SrvSurvey.Desktop.Platform.Frontier;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class FrontierAccountServiceTests
{
    [Fact]
    public async Task CallbackUsesPkceAndStoresTokensWithoutClientSecret()
    {
        var store = new MemoryCredentialStore
        {
            Document = new FrontierCredentialDocument
            {
                PendingAuthorization = new FrontierPendingAuthorization(
                    "expected-state",
                    "verifier-value",
                    DateTimeOffset.UnixEpoch),
            },
        };
        string? tokenBody = null;
        using var service = CreateService(
            store,
            request =>
            {
                tokenBody = request.Content!.ReadAsStringAsync().Result;
                return Json(HttpStatusCode.OK,
                    "{\"access_token\":\"access\",\"refresh_token\":\"refresh\",\"token_type\":\"Bearer\",\"expires_in\":14400}");
            });

        await service.HandleCallbackAsync(new FrontierOAuthCallback(
            "authorization-code",
            "expected-state",
            string.Empty,
            string.Empty));

        Assert.Contains("code_verifier=verifier-value", tokenBody);
        Assert.Contains("client_id=" + FrontierAccountService.ClientId, tokenBody);
        Assert.DoesNotContain("client_secret", tokenBody);
        Assert.Equal("access", store.Document!.AccessToken);
        Assert.Equal("refresh", store.Document.RefreshToken);
        Assert.Null(store.Document.PendingAuthorization);
        Assert.True(store.Document.AuthorizationResult!.Succeeded);
    }

    [Fact]
    public async Task CallbackRejectsMismatchedStateBeforeNetworkRequest()
    {
        var store = new MemoryCredentialStore
        {
            Document = new FrontierCredentialDocument
            {
                PendingAuthorization = new FrontierPendingAuthorization(
                    "expected",
                    "verifier",
                    DateTimeOffset.UnixEpoch),
            },
        };
        var requestCount = 0;
        using var service = CreateService(store, request =>
        {
            requestCount++;
            return Json(HttpStatusCode.OK, "{}");
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.HandleCallbackAsync(new FrontierOAuthCallback(
                "code",
                "wrong",
                string.Empty,
                string.Empty)));

        Assert.Equal(0, requestCount);
        Assert.NotNull(store.Document!.PendingAuthorization);
    }

    [Fact]
    public async Task RefreshFetchesProfileAndCarrierThenEnforcesCooldown()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-frontier-service-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var now = DateTimeOffset.Parse("2026-07-29T12:00:00Z");
            var store = new MemoryCredentialStore
            {
                Document = LinkedCredential(now),
            };
            var requests = new List<string>();
            using var service = CreateService(
                store,
                request =>
                {
                    requests.Add(request.RequestUri!.AbsolutePath);
                    now = now.AddSeconds(1);
                    return request.RequestUri.AbsolutePath == "/profile"
                        ? Json(HttpStatusCode.OK,
                            "{\"commander\":{\"name\":\"Fenris\",\"credits\":42,\"rank\":{}},\"ships\":[]}")
                        : Json(HttpStatusCode.NoContent, string.Empty);
                },
                root,
                () => now);

            var snapshot = await service.RefreshAsync();

            Assert.Equal("Fenris", snapshot.CommanderName);
            Assert.Equal(
                [
                    "/profile",
                    "/fleetcarrier",
                    "/market",
                    "/shipyard",
                    "/communitygoals",
                ],
                requests);
            Assert.NotNull(store.Document!.LastCapiRefreshAt);
            Assert.NotNull(store.Document.LastCapiAttemptAt);
            var cooldown = await Assert.ThrowsAsync<FrontierRefreshCooldownException>(
                () => service.RefreshAsync());
            Assert.True(cooldown.Remaining > TimeSpan.FromSeconds(50));
            Assert.Equal(5, requests.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CarrierAbsenceIsCachedForFifteenMinutes()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-frontier-carrier-cadence-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var now = DateTimeOffset.Parse("2026-07-29T12:00:00Z");
            var store = new MemoryCredentialStore
            {
                Document = LinkedCredential(now),
            };
            var requests = new List<string>();
            using var service = CreateService(
                store,
                request =>
                {
                    requests.Add(request.RequestUri!.AbsolutePath);
                    return request.RequestUri.AbsolutePath == "/profile"
                        ? Json(HttpStatusCode.OK,
                            "{\"commander\":{\"name\":\"Fenris\",\"rank\":{}},\"ships\":[]}")
                        : Json(HttpStatusCode.NoContent, string.Empty);
                },
                root,
                () => now);

            var first = await service.RefreshAsync();
            Assert.Null(first.Carrier);
            Assert.Equal(now, first.CarrierFetchedAt);

            now = now.AddMinutes(2);
            requests.Clear();
            var second = await service.RefreshAsync();

            Assert.Null(second.Carrier);
            Assert.Equal(first.CarrierFetchedAt, second.CarrierFetchedAt);
            Assert.DoesNotContain("/fleetcarrier", requests);
            Assert.Equal(
                ["/profile", "/market", "/shipyard", "/communitygoals"],
                requests);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CarrierEnvelopeMetadataIsCachedWithoutInventingCarrier()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-frontier-carrier-envelope-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var now = DateTimeOffset.Parse("2026-07-29T12:00:00Z");
            var store = new MemoryCredentialStore
            {
                Document = LinkedCredential(now),
            };
            using var service = CreateService(
                store,
                request => request.RequestUri!.AbsolutePath switch
                {
                    "/profile" => Json(HttpStatusCode.OK,
                        "{\"commander\":{\"name\":\"Fenris\",\"rank\":{}},\"ships\":[]}"),
                    "/fleetcarrier" => Json(HttpStatusCode.OK,
                        "{\"reputation\":[{\"majorFaction\":\"federation\",\"score\":91}],\"accountMetadata\":{\"available\":true}}"),
                    _ => Json(HttpStatusCode.NoContent, string.Empty),
                },
                root,
                () => now);

            var snapshot = await service.RefreshAsync();

            Assert.Null(snapshot.Carrier);
            Assert.Equal(91, Assert.Single(snapshot.CommanderReputation!).Score);
            Assert.Contains(snapshot.CarrierEndpointData!, point =>
                point.Path == "fleetcarrier.accountMetadata.available"
                    && point.Value == "Yes");

            var cached = await new FrontierProfileCacheStore(
                Path.Combine(root, "cache.json")).LoadAsync();
            Assert.Null(cached!.Carrier);
            Assert.Equal(91, Assert.Single(cached.CommanderReputation!).Score);
            Assert.Contains(cached.CarrierEndpointData!, point =>
                point.Path == "fleetcarrier.accountMetadata.available");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task NoCarrierResponseRetainsIndependentCommanderReputation()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-frontier-carrier-removed-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var now = DateTimeOffset.Parse("2026-07-29T12:00:00Z");
            const string profile =
                "{\"commander\":{\"name\":\"Fenris\",\"rank\":{}},\"ships\":[]}";
            var cache = new FrontierProfileCacheStore(
                Path.Combine(root, "cache.json"));
            await cache.SaveAsync(FrontierCapiSnapshotParser.Parse(
                profile,
                "{\"name\":{\"callsign\":\"RAV-001\"},\"reputation\":[{\"majorFaction\":\"federation\",\"score\":91}]}",
                now.AddMinutes(-16)));
            var store = new MemoryCredentialStore
            {
                Document = LinkedCredential(now),
            };
            using var service = new FrontierAccountService(
                new HttpClient(new StubHandler(request =>
                    request.RequestUri!.AbsolutePath == "/profile"
                        ? Json(HttpStatusCode.OK, profile)
                        : Json(HttpStatusCode.NoContent, string.Empty))),
                store,
                cache,
                () => now,
                (_, _) => Task.CompletedTask,
                _ => Task.CompletedTask);

            var snapshot = await service.RefreshAsync();

            Assert.Null(snapshot.Carrier);
            Assert.Equal(91, Assert.Single(snapshot.CommanderReputation!).Score);
            Assert.Equal(now.AddMinutes(-16), snapshot.CommanderReputationFetchedAt);
            Assert.Empty(snapshot.CarrierEndpointData!);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MalformedCarrierDoesNotDiscardValidProfile()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-frontier-carrier-invalid-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var now = DateTimeOffset.Parse("2026-07-29T12:00:00Z");
            var store = new MemoryCredentialStore
            {
                Document = LinkedCredential(now),
            };
            using var service = CreateService(
                store,
                request => request.RequestUri!.AbsolutePath switch
                {
                    "/profile" => Json(HttpStatusCode.OK,
                        "{\"commander\":{\"name\":\"Fenris\",\"rank\":{}},\"ships\":[]}"),
                    "/fleetcarrier" => Json(HttpStatusCode.OK, "{not-json"),
                    _ => Json(HttpStatusCode.NoContent, string.Empty),
                },
                root,
                () => now);

            var snapshot = await service.RefreshAsync();

            Assert.Equal("Fenris", snapshot.CommanderName);
            Assert.Null(snapshot.Carrier);
            Assert.Contains("could not be read", snapshot.CarrierError);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FailedRefreshAttemptIsAlsoThrottled()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-frontier-failed-refresh-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var now = DateTimeOffset.Parse("2026-07-29T12:00:00Z");
            var store = new MemoryCredentialStore
            {
                Document = LinkedCredential(now),
            };
            var requestCount = 0;
            using var service = CreateService(
                store,
                _ =>
                {
                    requestCount++;
                    return Json(
                        HttpStatusCode.InternalServerError,
                        "{\"message\":\"try later\"}");
                },
                root,
                () => now);

            await Assert.ThrowsAsync<HttpRequestException>(
                () => service.RefreshAsync());
            Assert.Equal(now, store.Document!.LastCapiAttemptAt);

            await Assert.ThrowsAsync<FrontierRefreshCooldownException>(
                () => service.RefreshAsync());
            Assert.Equal(1, requestCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RefreshLeaseSerializesIndependentServiceInstances()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-frontier-refresh-lease-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var cachePath = Path.Combine(root, "cache.json");
            var firstStore = new FrontierProfileCacheStore(cachePath);
            var secondStore = new FrontierProfileCacheStore(cachePath);
            await using (await firstStore.AcquireRefreshLeaseAsync())
            {
                using var timeout = new CancellationTokenSource(
                    TimeSpan.FromMilliseconds(150));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => secondStore.AcquireRefreshLeaseAsync(timeout.Token));
            }

            await using var secondLease = await secondStore.AcquireRefreshLeaseAsync();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task UnlinkClearsCredentialsAndCachedProfile()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-frontier-unlink-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var store = new MemoryCredentialStore
            {
                Document = LinkedCredential(DateTimeOffset.UtcNow),
            };
            var cachePath = Path.Combine(root, "frontier-profile-cache.json");
            await File.WriteAllTextAsync(cachePath, "{}");
            using var service = new FrontierAccountService(
                new HttpClient(new StubHandler(_ => Json(HttpStatusCode.OK, "{}"))),
                store,
                new FrontierProfileCacheStore(cachePath));

            await service.UnlinkAsync();

            Assert.Null(store.Document);
            Assert.False(File.Exists(cachePath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task WindowsCredentialStoreEncryptsTokensAtRest()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-frontier-credential-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var store = FrontierCredentialStore.CreateCurrent(root);
            var document = LinkedCredential(DateTimeOffset.UtcNow) with
            {
                AccessToken = "plain-access-token",
                RefreshToken = "plain-refresh-token",
            };

            await store.SaveAsync(document);

            var bytes = await File.ReadAllBytesAsync(
                Path.Combine(root, "frontier-auth.dat"));
            var persistedText = Encoding.UTF8.GetString(bytes);
            Assert.DoesNotContain(document.AccessToken, persistedText);
            Assert.DoesNotContain(document.RefreshToken, persistedText);
            Assert.Equal(document.AccessToken, (await store.LoadAsync())!.AccessToken);

            await store.ClearAsync();
            Assert.Null(await store.LoadAsync());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static FrontierAccountService CreateService(
        MemoryCredentialStore store,
        Func<HttpRequestMessage, HttpResponseMessage> response,
        string? root = null,
        Func<DateTimeOffset>? now = null)
    {
        var cachePath = root is null
            ? Path.Combine(
                Path.GetTempPath(),
                $"SrvSurvey-frontier-cache-{Guid.NewGuid():N}.json")
            : Path.Combine(root, "cache.json");
        if (root is not null)
        {
            Directory.CreateDirectory(root);
        }

        return new FrontierAccountService(
            new HttpClient(new StubHandler(response)),
            store,
            new FrontierProfileCacheStore(cachePath),
            now,
            (_, _) => Task.CompletedTask,
            _ => Task.CompletedTask);
    }

    private static FrontierCredentialDocument LinkedCredential(
        DateTimeOffset now)
    {
        return new FrontierCredentialDocument
        {
            AccessToken = "access",
            RefreshToken = "refresh",
            TokenType = "Bearer",
            ExpiresAt = now.AddHours(1),
            AuthorizedAt = now,
        };
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string content)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json"),
        };
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> response)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(response(request));
        }
    }

    private sealed class MemoryCredentialStore : IFrontierCredentialStore
    {
        public FrontierCredentialDocument? Document { get; set; }

        public Task<FrontierCredentialDocument?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Document);
        }

        public Task SaveAsync(
            FrontierCredentialDocument document,
            CancellationToken cancellationToken = default)
        {
            Document = document;
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            Document = null;
            return Task.CompletedTask;
        }
    }
}
