using System.Net;
using System.Text;
using SrvSurvey.Core.Frontier;
using SrvSurvey.Desktop.Platform.Frontier;
using SrvSurvey.Desktop.Platform.Inara;

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
                    DateTimeOffset.UnixEpoch,
                    "F123",
                    "Fenris"),
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
        var account = store.Document!.Accounts["F123"];
        Assert.Equal("access", account.AccessToken);
        Assert.Equal("refresh", account.RefreshToken);
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
                    DateTimeOffset.UnixEpoch,
                    "F123",
                    "Fenris"),
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
            var credential = store.Document!.Accounts["F123"];
            Assert.NotNull(credential.LastCapiRefreshAt);
            Assert.NotNull(credential.LastCapiAttemptAt);
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
    public async Task RefreshEnrichesCommunityGoalsWithGenericInaraReadData()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-frontier-inara-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var now = DateTimeOffset.Parse("2026-07-31T12:00:00Z");
            var store = new MemoryCredentialStore
            {
                Document = LinkedCredential(now),
            };
            var inara = new StubInaraCommunityGoalClient(new(
                [new InaraCommunityGoalSnapshot(
                    "Deliver medicines",
                    "Expanded global briefing",
                    "Deliver Basic Medicines",
                    "Credits",
                    "Sol",
                    "Galileo",
                    now.AddDays(2),
                    false,
                    2,
                    5,
                    1_234,
                    5_000,
                    now.AddMinutes(-1),
                    "https://inara.cz/elite/communitygoals/6/")],
                now,
                false,
                string.Empty));
            using var service = CreateService(
                store,
                request => request.RequestUri!.AbsolutePath switch
                {
                    "/profile" => Json(HttpStatusCode.OK,
                        "{\"commander\":{\"name\":\"Fenris\",\"rank\":{}},\"ships\":[]}"),
                    "/communitygoals" => Json(HttpStatusCode.OK,
                        "{\"goals\":[{\"id\":6,\"title\":\"Deliver medicines\",\"systemName\":\"Sol\",\"marketName\":\"Galileo\",\"expiry\":\"2026-08-02T12:00:00Z\",\"currentTotal\":0}]}"),
                    _ => Json(HttpStatusCode.NoContent, string.Empty),
                },
                root,
                () => now,
                inara);

            var snapshot = await service.RefreshAsync();

            var goal = Assert.Single(snapshot.CommunityGoals!);
            Assert.Equal("Expanded global briefing", goal.Description);
            Assert.Equal("Tier 2 / 5", goal.TierReached);
            Assert.Equal(1_234, goal.Contributors);
            Assert.Equal(now, snapshot.InaraCommunityGoalsFetchedAt);
            Assert.Equal(1, inara.RequestCount);
            var cached = await new FrontierProfileCacheStore(
                Path.Combine(root, "cache.json")).LoadAsync();
            Assert.Equal("Tier 2 / 5", Assert.Single(cached!.CommunityGoals!).TierReached);
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
            service.SetActiveCommander("F123", "Fenris");

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
            Assert.Equal(
                now,
                store.Document!.LastCapiAttemptAt);

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
    public async Task SwitchingCommanderUsesIndependentCredentialsAndCaches()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-frontier-multi-account-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var now = DateTimeOffset.Parse("2026-07-30T12:00:00Z");
            var store = new MemoryCredentialStore
            {
                Document = new FrontierCredentialDocument
                {
                    Accounts = new Dictionary<string, FrontierAccountCredential>
                    {
                        ["F123"] = ScopedCredential("access-a", now),
                        ["F456"] = ScopedCredential("access-b", now),
                    },
                },
            };
            var requestedTokens = new List<string>();
            using var service = new FrontierAccountService(
                new HttpClient(new StubHandler(request =>
                {
                    var token = request.Headers.Authorization?.Parameter ?? string.Empty;
                    requestedTokens.Add(token);
                    now = now.AddSeconds(1);
                    if (request.RequestUri!.AbsolutePath != "/profile")
                    {
                        return Json(HttpStatusCode.NoContent, string.Empty);
                    }

                    return token == "access-a"
                        ? Json(HttpStatusCode.OK,
                            "{\"commander\":{\"id\":739749,\"name\":\"Fenris\",\"credits\":100,\"rank\":{}},\"ships\":[]}")
                        : Json(HttpStatusCode.OK,
                            "{\"commander\":{\"id\":831234,\"name\":\"Second\",\"credits\":200,\"rank\":{}},\"ships\":[]}");
                })),
                store,
                frontierId => new FrontierProfileCacheStore(Path.Combine(
                    root,
                    frontierId + ".json")),
                utcNow: () => now,
                openBrowser: (_, _) => Task.CompletedTask,
                registerProtocol: _ => Task.CompletedTask);

            service.SetActiveCommander("F123", "Fenris");
            var first = await service.RefreshAsync();
            service.SetActiveCommander("F456", "Second");
            var second = await service.RefreshAsync();

            Assert.Equal("Fenris", first.CommanderName);
            Assert.Equal(100, first.Credits);
            Assert.Equal("Second", second.CommanderName);
            Assert.Equal(200, second.Credits);
            Assert.Contains("access-a", requestedTokens);
            Assert.Contains("access-b", requestedTokens);

            service.SetActiveCommander("F123", "Fenris");
            var firstState = await service.GetStateAsync();
            service.SetActiveCommander("F456", "Second");
            var secondState = await service.GetStateAsync();
            Assert.Equal("Fenris", firstState.Snapshot!.CommanderName);
            Assert.Equal("Second", secondState.Snapshot!.CommanderName);
            Assert.True(File.Exists(Path.Combine(root, "F123.json")));
            Assert.True(File.Exists(Path.Combine(root, "F456.json")));

            var linkedCommanders = await service.GetLinkedCommandersAsync();
            Assert.Collection(
                linkedCommanders,
                commander =>
                {
                    Assert.Equal("F123", commander.FrontierId);
                    Assert.Equal("Fenris", commander.CommanderName);
                },
                commander =>
                {
                    Assert.Equal("F456", commander.FrontierId);
                    Assert.Equal("Second", commander.CommanderName);
                });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task JournalFidAndCapiCommanderIdAreIndependentIdentifiers()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-frontier-id-domains-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var now = DateTimeOffset.Parse("2026-07-30T12:00:00Z");
            var store = new MemoryCredentialStore
            {
                Document = new FrontierCredentialDocument
                {
                    Accounts = new Dictionary<string, FrontierAccountCredential>
                    {
                        ["F472567"] = ScopedCredential("access", now),
                    },
                },
            };
            using var service = new FrontierAccountService(
                new HttpClient(new StubHandler(request =>
                    request.RequestUri!.AbsolutePath == "/profile"
                        ? Json(HttpStatusCode.OK,
                            "{\"commander\":{\"id\":739749,\"name\":\"Fenris Nihilus\",\"rank\":{}},\"ships\":[]}")
                        : Json(HttpStatusCode.NoContent, string.Empty))),
                store,
                frontierId => new FrontierProfileCacheStore(Path.Combine(
                    root,
                    frontierId + ".json")),
                utcNow: () => now,
                openBrowser: (_, _) => Task.CompletedTask,
                registerProtocol: _ => Task.CompletedTask);
            service.SetActiveCommander("F472567", "Fenris Nihilus");

            var snapshot = await service.RefreshAsync();

            Assert.Equal(739749, snapshot.CommanderId);
            Assert.True(store.Document!.Accounts.ContainsKey("F472567"));
            Assert.False(store.Document.Accounts.ContainsKey("F739749"));
            Assert.True(File.Exists(Path.Combine(root, "F472567.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MiskeyedCapiIdAliasIsRemovedWithoutMergingAnotherCommander()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-frontier-capi-alias-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var now = DateTimeOffset.Parse("2026-07-30T12:00:00Z");
            var store = new MemoryCredentialStore
            {
                Document = new FrontierCredentialDocument
                {
                    Accounts = new Dictionary<string, FrontierAccountCredential>
                    {
                        ["F472567"] = ScopedCredential("correct", now),
                        ["F739749"] = ScopedCredential("alias", now),
                        ["F831234"] = ScopedCredential("second", now),
                    },
                },
            };
            var activeCache = new FrontierProfileCacheStore(Path.Combine(
                root,
                "F472567.json"));
            var aliasCache = new FrontierProfileCacheStore(Path.Combine(
                root,
                "F739749.json"));
            var secondCache = new FrontierProfileCacheStore(Path.Combine(
                root,
                "F831234.json"));
            await activeCache.SaveAsync(FrontierCapiSnapshotParser.Parse(
                "{\"commander\":{\"id\":739749,\"name\":\"Fenris Nihilus\",\"rank\":{}},\"ships\":[]}",
                null,
                now.AddMinutes(-5)));
            await aliasCache.SaveAsync(FrontierCapiSnapshotParser.Parse(
                "{\"commander\":{\"id\":739749,\"name\":\"Fenris Nihilus\",\"credits\":42,\"rank\":{}},\"ships\":[]}",
                null,
                now));
            await secondCache.SaveAsync(FrontierCapiSnapshotParser.Parse(
                "{\"commander\":{\"id\":831234,\"name\":\"Second\",\"rank\":{}},\"ships\":[]}",
                null,
                now));
            using var service = new FrontierAccountService(
                new HttpClient(new StubHandler(_ => Json(HttpStatusCode.OK, "{}"))),
                store,
                frontierId => new FrontierProfileCacheStore(Path.Combine(
                    root,
                    frontierId + ".json")),
                utcNow: () => now,
                openBrowser: (_, _) => Task.CompletedTask,
                registerProtocol: _ => Task.CompletedTask);
            service.SetActiveCommander("F472567", "Fenris Nihilus");

            var linked = await service.GetLinkedCommandersAsync();

            Assert.Collection(
                linked,
                commander =>
                {
                    Assert.Equal("F472567", commander.FrontierId);
                    Assert.Equal("Fenris Nihilus", commander.CommanderName);
                },
                commander =>
                {
                    Assert.Equal("F831234", commander.FrontierId);
                    Assert.Equal("Second", commander.CommanderName);
                });
            Assert.Equal(
                "correct",
                store.Document!.Accounts["F472567"].AccessToken);
            Assert.False(store.Document.Accounts.ContainsKey("F739749"));
            Assert.True(store.Document.Accounts.ContainsKey("F831234"));
            Assert.False(File.Exists(Path.Combine(root, "F739749.json")));
            Assert.Equal(42, (await activeCache.LoadAsync())!.Credits);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BlankCommanderNameDoesNotMatchOrMigrateMiskeyedAlias()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-frontier-blank-name-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var now = DateTimeOffset.Parse("2026-07-30T12:00:00Z");
            var snapshot = FrontierCapiSnapshotParser.Parse(
                "{\"commander\":{\"id\":739749,\"name\":\"Fenris Nihilus\",\"rank\":{}},\"ships\":[]}",
                null,
                now);
            var identity = FrontierCommanderIdentity.Create("F472567", null)!;
            Assert.False(identity.Matches(snapshot));

            var store = new MemoryCredentialStore
            {
                Document = new FrontierCredentialDocument
                {
                    Accounts = new Dictionary<string, FrontierAccountCredential>
                    {
                        ["F739749"] = ScopedCredential("alias", now),
                    },
                },
            };
            await new FrontierProfileCacheStore(Path.Combine(root, "F739749.json"))
                .SaveAsync(snapshot);
            using var service = new FrontierAccountService(
                new HttpClient(new StubHandler(_ => Json(HttpStatusCode.OK, "{}"))),
                store,
                frontierId => new FrontierProfileCacheStore(Path.Combine(
                    root,
                    frontierId + ".json")),
                utcNow: () => now,
                openBrowser: (_, _) => Task.CompletedTask,
                registerProtocol: _ => Task.CompletedTask);
            service.SetActiveCommander("F472567", null);

            var linked = await service.GetLinkedCommandersAsync();

            var commander = Assert.Single(linked);
            Assert.Equal("F739749", commander.FrontierId);
            Assert.True(store.Document!.Accounts.ContainsKey("F739749"));
            Assert.False(store.Document.Accounts.ContainsKey("F472567"));
            Assert.True(File.Exists(Path.Combine(root, "F739749.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BlankCommanderNameDoesNotDeleteScopedCache()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-frontier-blank-name-cache-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var now = DateTimeOffset.Parse("2026-07-30T12:00:00Z");
            var store = new MemoryCredentialStore
            {
                Document = new FrontierCredentialDocument
                {
                    Accounts = new Dictionary<string, FrontierAccountCredential>
                    {
                        ["F472567"] = ScopedCredential("correct", now),
                    },
                },
            };
            var cachePath = Path.Combine(root, "F472567.json");
            await new FrontierProfileCacheStore(cachePath).SaveAsync(
                FrontierCapiSnapshotParser.Parse(
                    "{\"commander\":{\"id\":739749,\"name\":\"Fenris Nihilus\",\"rank\":{}},\"ships\":[]}",
                    null,
                    now));
            using var service = new FrontierAccountService(
                new HttpClient(new StubHandler(_ => Json(HttpStatusCode.OK, "{}"))),
                store,
                frontierId => new FrontierProfileCacheStore(Path.Combine(
                    root,
                    frontierId + ".json")),
                utcNow: () => now,
                openBrowser: (_, _) => Task.CompletedTask,
                registerProtocol: _ => Task.CompletedTask);
            service.SetActiveCommander("F472567", null);

            var state = await service.GetStateAsync();

            Assert.True(state.IsLinked);
            Assert.Null(state.Snapshot);
            Assert.True(File.Exists(cachePath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MiskeyedCapiIdCredentialMovesToJournalFidWhenItIsTheOnlyCopy()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-frontier-capi-alias-move-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var now = DateTimeOffset.Parse("2026-07-30T12:00:00Z");
            var store = new MemoryCredentialStore
            {
                Document = new FrontierCredentialDocument
                {
                    Accounts = new Dictionary<string, FrontierAccountCredential>
                    {
                        ["F739749"] = ScopedCredential("alias", now),
                    },
                },
            };
            await new FrontierProfileCacheStore(Path.Combine(root, "F739749.json"))
                .SaveAsync(FrontierCapiSnapshotParser.Parse(
                    "{\"commander\":{\"id\":739749,\"name\":\"Fenris Nihilus\",\"rank\":{}},\"ships\":[]}",
                    null,
                    now));
            using var service = new FrontierAccountService(
                new HttpClient(new StubHandler(_ => Json(HttpStatusCode.OK, "{}"))),
                store,
                frontierId => new FrontierProfileCacheStore(Path.Combine(
                    root,
                    frontierId + ".json")),
                utcNow: () => now,
                openBrowser: (_, _) => Task.CompletedTask,
                registerProtocol: _ => Task.CompletedTask);
            service.SetActiveCommander("F472567", "Fenris Nihilus");

            var state = await service.GetStateAsync();

            Assert.True(state.IsLinked);
            Assert.Equal("Fenris Nihilus", state.Snapshot!.CommanderName);
            Assert.Equal(
                "alias",
                store.Document!.Accounts["F472567"].AccessToken);
            Assert.False(store.Document.Accounts.ContainsKey("F739749"));
            Assert.True(File.Exists(Path.Combine(root, "F472567.json")));
            Assert.False(File.Exists(Path.Combine(root, "F739749.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MismatchedOAuthIsRejectedWithoutReplacingAnotherCommander()
    {
        var now = DateTimeOffset.Parse("2026-07-30T12:00:00Z");
        var store = new MemoryCredentialStore
        {
            Document = new FrontierCredentialDocument
            {
                Accounts = new Dictionary<string, FrontierAccountCredential>
                {
                    ["F123"] = ScopedCredential("existing-a", now),
                },
                PendingAuthorization = new FrontierPendingAuthorization(
                    "state-b",
                    "verifier-b",
                    now,
                    "F456",
                    "Second"),
            },
        };
        using var service = CreateService(
            store,
            request => request.RequestUri!.AbsolutePath == "/token"
                ? Json(HttpStatusCode.OK,
                    "{\"access_token\":\"wrong-account\",\"refresh_token\":\"wrong-refresh\",\"token_type\":\"Bearer\",\"expires_in\":14400}")
                : Json(HttpStatusCode.OK,
                    "{\"commander\":{\"id\":123,\"name\":\"Fenris\",\"rank\":{}},\"ships\":[]}"),
            now: () => now);

        await service.HandleCallbackAsync(new FrontierOAuthCallback(
            "code-b",
            "state-b",
            string.Empty,
            string.Empty));
        service.SetActiveCommander("F456", "Second");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RefreshAsync());

        Assert.Contains("active journal", error.Message);
        Assert.True(store.Document!.Accounts.ContainsKey("F123"));
        Assert.False(store.Document.Accounts.ContainsKey("F456"));
        Assert.Equal("existing-a", store.Document.Accounts["F123"].AccessToken);
    }

    [Fact]
    public async Task UnlinkRemovesOnlyTheActiveCommander()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new MemoryCredentialStore
        {
            Document = new FrontierCredentialDocument
            {
                Accounts = new Dictionary<string, FrontierAccountCredential>
                {
                    ["F123"] = ScopedCredential("access-a", now),
                    ["F456"] = ScopedCredential("access-b", now),
                },
            },
        };
        using var service = CreateService(
            store,
            _ => Json(HttpStatusCode.OK, "{}"));
        service.SetActiveCommander("F456", "Second");

        await service.UnlinkAsync();

        Assert.True(store.Document!.Accounts.ContainsKey("F123"));
        Assert.False(store.Document.Accounts.ContainsKey("F456"));
    }

    [Fact]
    public async Task LegacySingleAccountAuthorizationMigratesToVerifiedFid()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-frontier-legacy-migration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var now = DateTimeOffset.Parse("2026-07-30T12:00:00Z");
            var legacyCache = new FrontierProfileCacheStore(Path.Combine(
                root,
                "frontier-profile-cache.json"));
            var snapshot = FrontierCapiSnapshotParser.Parse(
                "{\"commander\":{\"id\":739749,\"name\":\"Fenris\",\"rank\":{}},\"ships\":[]}",
                null,
                now);
            await legacyCache.SaveAsync(snapshot);
            var store = new MemoryCredentialStore
            {
                Document = LinkedCredential(now),
            };
            using var service = new FrontierAccountService(
                new HttpClient(new StubHandler(_ => Json(HttpStatusCode.OK, "{}"))),
                store,
                frontierId => new FrontierProfileCacheStore(Path.Combine(
                    root,
                    "frontier-profile-cache",
                    frontierId + ".json")),
                legacyCache,
                () => now,
                (_, _) => Task.CompletedTask,
                _ => Task.CompletedTask);
            service.SetActiveCommander("F123", "Fenris");

            var state = await service.GetStateAsync();

            Assert.True(state.IsLinked);
            Assert.Equal("Fenris", state.Snapshot!.CommanderName);
            Assert.False(store.Document!.IsLinked);
            Assert.True(store.Document.Accounts.ContainsKey("F123"));
            Assert.False(File.Exists(Path.Combine(root, "frontier-profile-cache.json")));
            Assert.True(File.Exists(Path.Combine(
                root,
                "frontier-profile-cache",
                "F123.json")));
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
            service.SetActiveCommander("F123", "Fenris");

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
            var account = ScopedCredential(
                "plain-access-token",
                DateTimeOffset.UtcNow) with
            {
                RefreshToken = "plain-refresh-token",
            };
            var document = new FrontierCredentialDocument
            {
                Accounts = new Dictionary<string, FrontierAccountCredential>
                {
                    ["F123"] = account,
                },
            };

            await store.SaveAsync(document);

            var bytes = await File.ReadAllBytesAsync(
                Path.Combine(root, "frontier-auth.dat"));
            var persistedText = Encoding.UTF8.GetString(bytes);
            Assert.DoesNotContain(account.AccessToken, persistedText);
            Assert.DoesNotContain(account.RefreshToken, persistedText);
            Assert.Equal(
                account.AccessToken,
                (await store.LoadAsync())!.Accounts["F123"].AccessToken);

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
        Func<DateTimeOffset>? now = null,
        IInaraCommunityGoalClient? inaraCommunityGoals = null)
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

        var service = new FrontierAccountService(
            new HttpClient(new StubHandler(response)),
            store,
            new FrontierProfileCacheStore(cachePath),
            now,
            (_, _) => Task.CompletedTask,
            _ => Task.CompletedTask,
            inaraCommunityGoals);
        service.SetActiveCommander("F123", "Fenris");
        return service;
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

    private static FrontierAccountCredential ScopedCredential(
        string accessToken,
        DateTimeOffset now) =>
        new()
        {
            AccessToken = accessToken,
            RefreshToken = accessToken + "-refresh",
            TokenType = "Bearer",
            ExpiresAt = now.AddHours(1),
            AuthorizedAt = now,
        };

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

    private sealed class StubInaraCommunityGoalClient(
        InaraCommunityGoalsResult result) : IInaraCommunityGoalClient
    {
        public int RequestCount { get; private set; }

        public Task<InaraCommunityGoalsResult> GetRecentAsync(
            CancellationToken cancellationToken = default)
        {
            RequestCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class MemoryCredentialStore : IFrontierCredentialStore
    {
        private readonly SemaphoreSlim gate = new(1, 1);

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

        public async Task<IAsyncDisposable> AcquireLeaseAsync(
            CancellationToken cancellationToken = default)
        {
            await gate.WaitAsync(cancellationToken);
            return new MemoryLease(gate);
        }

        private sealed class MemoryLease(SemaphoreSlim gate) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                gate.Release();
                return ValueTask.CompletedTask;
            }
        }
    }
}
