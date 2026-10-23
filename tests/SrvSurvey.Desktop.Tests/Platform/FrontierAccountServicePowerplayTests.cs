using System.Net;
using SrvSurvey.Core.Frontier;
using SrvSurvey.Desktop.Platform.Frontier;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed partial class FrontierAccountServiceTests
{
    /// <summary>Verifies remote journal recovery, snapshot persistence, and the successful-check polling interval.</summary>
    [Fact]
    public async Task RefreshImportsPowerplayFromFrontierDailyJournalAndCachesIt()
    {
        string root = Path.Combine(Path.GetTempPath(), $"SrvSurvey-powerplay-{Guid.NewGuid():N}");
        var now = DateTimeOffset.Parse(
            "2026-10-23T12:00:00Z",
            global::System.Globalization.CultureInfo.InvariantCulture
        );
        var requests = new List<string>();
        var store = new MemoryCredentialStore { Document = LinkedCredential(now) };
        try
        {
            using FrontierAccountService service = CreateService(
                store,
                request =>
                {
                    string path = request.RequestUri!.AbsolutePath;
                    requests.Add(path);
                    now = now.AddSeconds(1);
                    return path switch
                    {
                        "/profile" => Json(HttpStatusCode.OK, """{"commander":{"name":"Fenris","rank":{"power":0}}}"""),
                        "/journal/2026/10/22" => Json(
                            HttpStatusCode.OK,
                            """
                            {"event":"Commander","FID":"F123"}
                            {"timestamp":"2026-10-22T20:00:00Z","event":"Powerplay","Power":"A. Lavigny-Duval","Rank":160,"Merits":1260198}
                            """
                        ),
                        _ => Json(HttpStatusCode.NoContent, ""),
                    };
                },
                root,
                () => now
            );

            FrontierAccountSnapshot snapshot = await service.RefreshAsync();
            Assert.NotNull(snapshot.Powerplay);
            Assert.Equal(160, snapshot.Powerplay.Rank);
            Assert.Equal(1260198, snapshot.Powerplay.Merits);
            Assert.Equal("A. Lavigny-Duval", snapshot.Powerplay.Power);
            Assert.Empty(snapshot.PowerplayError);
            Assert.Contains("/journal/2026/10/23", requests);
            Assert.Contains("/journal/2026/10/22", requests);
            Assert.Equal(2, requests.Count(path => path.StartsWith("/journal/", StringComparison.Ordinal)));
            Assert.Equal(
                snapshot.Powerplay,
                (await new FrontierProfileCacheStore(Path.Combine(root, "cache.json")).LoadAsync())!.Powerplay
            );

            requests.Clear();
            now = now.AddMinutes(2);
            FrontierAccountSnapshot cached = await service.RefreshAsync();
            Assert.Equal(snapshot.Powerplay, cached.Powerplay);
            Assert.DoesNotContain(requests, path => path.StartsWith("/journal/", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>Verifies that a confirmed new membership survives unavailable older history without inheriting old numeric values.</summary>
    [Theory]
    [InlineData("""{"timestamp":"2026-10-23T03:00:00Z","event":"PowerplayJoin","Power":"A. Lavigny-Duval"}""", false)]
    [InlineData("""{"timestamp":"2026-10-23T03:00:00Z","event":"PowerplayDefect","ToPower":"Aisling Duval"}""", false)]
    [InlineData("""{"timestamp":"2026-10-23T03:00:00Z","event":"PowerplayDefect","ToPower":"Aisling Duval"}""", true)]
    public async Task MembershipResetNeedsNoOlderJournalEvenWhenNumericStatusIsUnknown(
        string transition,
        bool fullSnapshotFirst
    )
    {
        string root = Path.Combine(Path.GetTempPath(), $"SrvSurvey-powerplay-baseline-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var now = DateTimeOffset.Parse(
            "2026-10-23T12:00:00Z",
            global::System.Globalization.CultureInfo.InvariantCulture
        );
        var prior = new FrontierPowerplaySnapshot("A. Lavigny-Duval", 160, 1000, now.AddDays(-2));
        await new FrontierProfileCacheStore(Path.Combine(root, "cache.json")).SaveAsync(
            FrontierCapiSnapshotParser.Parse("""{"commander":{"name":"Fenris"}}""", null, now.AddDays(-2)) with
            {
                Powerplay = prior,
                PowerplayFetchedAt = now.AddDays(-2),
            }
        );
        var store = new MemoryCredentialStore { Document = LinkedCredential(now) };
        int journalRequests = 0;
        try
        {
            using FrontierAccountService service = CreateService(
                store,
                request =>
                {
                    now = now.AddSeconds(1);
                    string path = request.RequestUri!.AbsolutePath;
                    if (path.StartsWith("/journal/", StringComparison.Ordinal))
                    {
                        journalRequests++;
                        string prefix = """{"event":"Commander","FID":"F123"}""" + "\n";
                        if (fullSnapshotFirst)
                        {
                            prefix +=
                                """{"timestamp":"2026-10-23T02:00:00Z","event":"Powerplay","Power":"A. Lavigny-Duval","Rank":160,"Merits":1000}"""
                                + "\n";
                        }
                        return journalRequests == 1
                            ? Json(HttpStatusCode.OK, prefix + transition)
                            : Json(HttpStatusCode.ServiceUnavailable, "older history unavailable");
                    }
                    return path == "/profile"
                        ? Json(HttpStatusCode.OK, """{"commander":{"name":"Fenris"}}""")
                        : Json(HttpStatusCode.NoContent, "");
                },
                root,
                () => now
            );

            FrontierAccountSnapshot snapshot = await service.RefreshAsync();

            Assert.NotNull(snapshot.Powerplay);
            Assert.Equal(
                transition.Contains("PowerplayJoin", StringComparison.Ordinal) ? prior.Power : "Aisling Duval",
                snapshot.Powerplay.Power
            );
            Assert.Null(snapshot.Powerplay.Rank);
            Assert.Null(snapshot.Powerplay.Merits);
            Assert.Empty(snapshot.PowerplayError);
            Assert.Equal(1, journalRequests);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>Verifies that a completed empty search cannot exclude older dates from later recovery.</summary>
    [Fact]
    public async Task AnEarlierEmptySearchDoesNotExcludeOlderDatesFromTheNextRecovery()
    {
        string root = Path.Combine(Path.GetTempPath(), $"SrvSurvey-powerplay-retry-{Guid.NewGuid():N}");
        var now = DateTimeOffset.Parse(
            "2026-10-23T12:00:00Z",
            global::System.Globalization.CultureInfo.InvariantCulture
        );
        var store = new MemoryCredentialStore { Document = LinkedCredential(now) };
        bool available = false;
        var requests = new List<string>();
        try
        {
            using FrontierAccountService service = CreateService(
                store,
                request =>
                {
                    now = now.AddSeconds(1);
                    string path = request.RequestUri!.AbsolutePath;
                    requests.Add(path);
                    return path switch
                    {
                        "/profile" => Json(HttpStatusCode.OK, """{"commander":{"name":"Fenris"}}"""),
                        "/journal/2026/10/20" when available => Json(
                            HttpStatusCode.OK,
                            """
                            {"event":"Commander","FID":"F123"}
                            {"timestamp":"2026-10-20T03:00:00Z","event":"Powerplay","Power":"Aisling Duval","Rank":80,"Merits":500}
                            """
                        ),
                        _ => Json(HttpStatusCode.NoContent, ""),
                    };
                },
                root,
                () => now
            );

            Assert.Null((await service.RefreshAsync()).Powerplay);
            Assert.Equal(7, requests.Count(path => path.StartsWith("/journal/", StringComparison.Ordinal)));
            now = now.AddMinutes(16);
            available = true;
            requests.Clear();

            FrontierAccountSnapshot recovered = await service.RefreshAsync();

            Assert.NotNull(recovered.Powerplay);
            Assert.Equal(80, recovered.Powerplay.Rank);
            Assert.Contains("/journal/2026/10/20", requests);
            Assert.Equal(4, requests.Count(path => path.StartsWith("/journal/", StringComparison.Ordinal)));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>Verifies that an explicit unpledged event stops recovery without extra historical requests.</summary>
    [Theory]
    [InlineData("""{"timestamp":"2026-10-23T03:00:00Z","event":"PowerplayLeave"}""")]
    [InlineData("""{"timestamp":"2026-10-23T03:00:00Z","event":"Powerplay","Power":""}""")]
    public async Task AnExplicitUnpledgedStatusIsACompleteDailyJournalBaseline(string entry)
    {
        string root = Path.Combine(Path.GetTempPath(), $"SrvSurvey-powerplay-unpledged-{Guid.NewGuid():N}");
        var now = DateTimeOffset.Parse(
            "2026-10-23T12:00:00Z",
            global::System.Globalization.CultureInfo.InvariantCulture
        );
        var store = new MemoryCredentialStore { Document = LinkedCredential(now) };
        int journals = 0;
        try
        {
            using FrontierAccountService service = CreateService(
                store,
                request =>
                {
                    now = now.AddSeconds(1);
                    string path = request.RequestUri!.AbsolutePath;
                    if (path.StartsWith("/journal/", StringComparison.Ordinal))
                    {
                        journals++;
                        return Json(HttpStatusCode.OK, """{"event":"Commander","FID":"F123"}""" + "\n" + entry);
                    }
                    return path == "/profile"
                        ? Json(HttpStatusCode.OK, """{"commander":{"name":"Fenris"}}""")
                        : Json(HttpStatusCode.NoContent, "");
                },
                root,
                () => now
            );

            FrontierAccountSnapshot snapshot = await service.RefreshAsync();

            Assert.NotNull(snapshot.Powerplay);
            Assert.Equal(string.Empty, snapshot.Powerplay.Power);
            Assert.Null(snapshot.Powerplay.Rank);
            Assert.Null(snapshot.Powerplay.Merits);
            Assert.Empty(snapshot.PowerplayError);
            Assert.Equal(1, journals);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>Verifies that cross-day rank and merit deltas form a complete status before older history is requested.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DailyPowerplayDeltasReplayInterveningDaysBeforeAdvancingTheCache(bool hasCachedStatus)
    {
        string root = Path.Combine(Path.GetTempPath(), $"SrvSurvey-powerplay-days-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var now = DateTimeOffset.Parse(
            "2026-10-23T12:00:00Z",
            global::System.Globalization.CultureInfo.InvariantCulture
        );
        var prior = new FrontierPowerplaySnapshot("A. Lavigny-Duval", 160, 100, now.AddDays(-2));
        if (hasCachedStatus)
        {
            await new FrontierProfileCacheStore(Path.Combine(root, "cache.json")).SaveAsync(
                FrontierCapiSnapshotParser.Parse("""{"commander":{"name":"Fenris"}}""", null, now.AddDays(-2)) with
                {
                    Powerplay = prior,
                    PowerplayFetchedAt = now.AddDays(-2),
                }
            );
        }
        var requests = new List<string>();
        var store = new MemoryCredentialStore { Document = LinkedCredential(now) };
        try
        {
            using FrontierAccountService service = CreateService(
                store,
                request =>
                {
                    now = now.AddSeconds(1);
                    string path = request.RequestUri!.AbsolutePath;
                    requests.Add(path);
                    return path switch
                    {
                        "/profile" => Json(HttpStatusCode.OK, """{"commander":{"name":"Fenris"}}"""),
                        "/journal/2026/10/23" => Json(
                            HttpStatusCode.OK,
                            """
                            {"event":"LoadGame","FID":"F123"}
                            {"timestamp":"2026-10-23T03:00:00Z","event":"PowerplayMerits","Power":"A. Lavigny-Duval","TotalMerits":250}
                            """
                        ),
                        "/journal/2026/10/22" => Json(
                            HttpStatusCode.OK,
                            """
                            {"event":"Commander","FID":"F123"}
                            {"timestamp":"2026-10-22T03:00:00Z","event":"PowerplayRank","Power":"A. Lavigny-Duval","Rank":161}
                            """
                        ),
                        "/journal/2026/10/21" => Json(HttpStatusCode.ServiceUnavailable, "older history unavailable"),
                        _ => Json(HttpStatusCode.NoContent, ""),
                    };
                },
                root,
                () => now
            );

            FrontierAccountSnapshot snapshot = await service.RefreshAsync();

            Assert.NotNull(snapshot.Powerplay);
            Assert.Equal(161, snapshot.Powerplay.Rank);
            Assert.Equal(250, snapshot.Powerplay.Merits);
            Assert.Equal(prior.Power, snapshot.Powerplay.Power);
            Assert.Equal(2, requests.Count(path => path.StartsWith("/journal/", StringComparison.Ordinal)));
            Assert.Equal(
                snapshot.Powerplay,
                (await new FrontierProfileCacheStore(Path.Combine(root, "cache.json")).LoadAsync())!.Powerplay
            );
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>Verifies that a recovered baseline remains sufficient after a later partial update changes the pledged power.</summary>
    [Theory]
    [InlineData(
        """{"timestamp":"2026-10-22T03:00:00Z","event":"Powerplay","Power":"A. Lavigny-Duval","Rank":160,"Merits":1000}"""
    )]
    [InlineData("""{"timestamp":"2026-10-22T03:00:00Z","event":"Powerplay","Power":""}""")]
    public async Task RecoveredBaselineSurvivesAPartialPowerChange(string baseline)
    {
        string root = Path.Combine(Path.GetTempPath(), $"SrvSurvey-powerplay-baseline-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var now = DateTimeOffset.Parse(
            "2026-10-23T12:00:00Z",
            global::System.Globalization.CultureInfo.InvariantCulture
        );
        var requests = new List<string>();
        var store = new MemoryCredentialStore { Document = LinkedCredential(now) };
        try
        {
            using FrontierAccountService service = CreateService(
                store,
                request =>
                {
                    now = now.AddSeconds(1);
                    string path = request.RequestUri!.AbsolutePath;
                    requests.Add(path);
                    return path switch
                    {
                        "/profile" => Json(HttpStatusCode.OK, """{"commander":{"name":"Fenris"}}"""),
                        "/journal/2026/10/23" => Json(
                            HttpStatusCode.OK,
                            """
                            {"event":"Commander","FID":"F123"}
                            {"timestamp":"2026-10-23T03:00:00Z","event":"PowerplayRank","Power":"Aisling Duval","Rank":161}
                            """
                        ),
                        "/journal/2026/10/22" => Json(
                            HttpStatusCode.OK,
                            """{"event":"Commander","FID":"F123"}""" + "\n" + baseline
                        ),
                        "/journal/2026/10/21" => Json(HttpStatusCode.ServiceUnavailable, "older history unavailable"),
                        _ => Json(HttpStatusCode.NoContent, ""),
                    };
                },
                root,
                () => now
            );

            FrontierAccountSnapshot snapshot = await service.RefreshAsync();

            Assert.NotNull(snapshot.Powerplay);
            Assert.Equal("Aisling Duval", snapshot.Powerplay.Power);
            Assert.Equal(161, snapshot.Powerplay.Rank);
            Assert.Null(snapshot.Powerplay.Merits);
            Assert.Empty(snapshot.PowerplayError);
            Assert.Equal(2, requests.Count(path => path.StartsWith("/journal/", StringComparison.Ordinal)));
            Assert.Equal(
                snapshot.Powerplay,
                (await new FrontierProfileCacheStore(Path.Combine(root, "cache.json")).LoadAsync())!.Powerplay
            );
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>Verifies that an incomplete journal response cannot overwrite credentials renewed after an authorization challenge.</summary>
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task PartialJournalAfterTokenRotationPreservesTheRefreshedCredentials(HttpStatusCode challenge)
    {
        string root = Path.Combine(Path.GetTempPath(), $"SrvSurvey-powerplay-tokens-{Guid.NewGuid():N}");
        var now = DateTimeOffset.Parse(
            "2026-10-23T12:00:00Z",
            global::System.Globalization.CultureInfo.InvariantCulture
        );
        var store = new MemoryCredentialStore { Document = LinkedCredential(now) };
        int journalRequests = 0;
        try
        {
            using FrontierAccountService service = CreateService(
                store,
                request =>
                {
                    now = now.AddSeconds(1);
                    string path = request.RequestUri!.AbsolutePath;
                    if (path.StartsWith("/journal/", StringComparison.Ordinal))
                    {
                        journalRequests++;
                        return journalRequests == 1 ? Json(challenge, "") : Json(HttpStatusCode.PartialContent, "");
                    }
                    return path switch
                    {
                        "/profile" => Json(HttpStatusCode.OK, """{"commander":{"name":"Fenris"}}"""),
                        "/token" => Json(
                            HttpStatusCode.OK,
                            """{"access_token":"rotated-access","refresh_token":"rotated-refresh","token_type":"Bearer","expires_in":14400}"""
                        ),
                        _ => Json(HttpStatusCode.NoContent, ""),
                    };
                },
                root,
                () => now
            );

            FrontierAccountSnapshot snapshot = await service.RefreshAsync();

            Assert.Contains("incomplete", snapshot.PowerplayError);
            Assert.Equal(2, journalRequests);
            Assert.Equal("rotated-access", store.Document.Accounts["F123"].AccessToken);
            Assert.Equal("rotated-refresh", store.Document.Accounts["F123"].RefreshToken);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>Verifies that incomplete or unavailable history preserves the previously cached membership.</summary>
    [Theory]
    [InlineData(HttpStatusCode.PartialContent)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.NoContent)]
    public async Task UnavailableJournalsKeepTheLastKnownPowerplayStatus(HttpStatusCode status)
    {
        string root = Path.Combine(Path.GetTempPath(), $"SrvSurvey-powerplay-retain-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var now = DateTimeOffset.Parse(
            "2026-10-23T12:00:00Z",
            global::System.Globalization.CultureInfo.InvariantCulture
        );
        var prior = new FrontierPowerplaySnapshot("A. Lavigny-Duval", 160, 1260198, now.AddDays(-1));
        var cache = new FrontierProfileCacheStore(Path.Combine(root, "cache.json"));
        await cache.SaveAsync(
            FrontierCapiSnapshotParser.Parse("""{"commander":{"name":"Fenris"}}""", null, now.AddHours(-1)) with
            {
                Powerplay = prior,
                PowerplayFetchedAt = now.AddHours(-1),
            }
        );
        var store = new MemoryCredentialStore { Document = LinkedCredential(now) };
        int journalRequests = 0;
        try
        {
            using FrontierAccountService service = CreateService(
                store,
                request =>
                {
                    now = now.AddSeconds(1);
                    string path = request.RequestUri!.AbsolutePath;
                    if (path.StartsWith("/journal/", StringComparison.Ordinal))
                    {
                        journalRequests++;
                        return Json(
                            status,
                            """
                            {"event":"Commander","FID":"F123"}
                            {"timestamp":"2026-10-23T03:00:00Z","event":"PowerplayLeave"}
                            """
                        );
                    }
                    return path == "/profile"
                        ? Json(HttpStatusCode.OK, """{"commander":{"name":"Fenris","rank":{"power":0}}}""")
                        : Json(HttpStatusCode.NoContent, "");
                },
                root,
                () => now
            );
            FrontierAccountSnapshot snapshot = await service.RefreshAsync();
            Assert.Equal(prior, snapshot.Powerplay);
            Assert.Equal(status == HttpStatusCode.NoContent ? 2 : 1, journalRequests);
            Assert.Equal(status == HttpStatusCode.NoContent, snapshot.PowerplayError.Length == 0);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
