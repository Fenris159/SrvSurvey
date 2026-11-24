using System.Net;
using System.Net.Http.Headers;
using System.Text;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Navigation;

public sealed class SystemSummaryClientTests
{
    [Theory]
    [InlineData("Pristine", "Icy body", "Icy", true, true)]
    [InlineData("Low", "Icy body", "Icy", true, false)]
    [InlineData("Pristine", "Rocky Ice world", "Rocky", false, false)]
    public async Task SpanshMiningTagsRequirePositiveBodyAndRingEvidence(
        string reserve,
        string type,
        string ring,
        bool icyRing,
        bool pristine
    )
    {
        var handler = new ProviderHandler
        {
            SpanshResponse = System.Text.Json.JsonSerializer.Serialize(
                new
                {
                    system = new
                    {
                        bodies = new[]
                        {
                            new
                            {
                                subType = type,
                                reserveLevel = reserve,
                                rings = new[] { new { type = ring } },
                            },
                        },
                    },
                }
            ),
        };
        var client = new SystemSummaryClient(
            new HttpClient(handler),
            new("https://edsm.test/"),
            new("https://spansh.test/api/")
        );
        SystemMiningResources resources = (await client.GetAsync("Test System", 42)).Summary.MiningResources;
        Assert.Equal(icyRing, resources.HasIcyRing);
        Assert.Equal(pristine, resources.HasPristineIcyRing);
        Assert.Equal(type == "Icy body", resources.HasIcyBody);
    }

    [Fact]
    public async Task PristineRockyRingsDoNotUpgradeAnotherBodysDepletedIcyRing()
    {
        var handler = new ProviderHandler
        {
            SpanshResponse =
                """{"system":{"bodies":[null,42,{"reserveLevel":"Pristine","rings":[{"type":"Rocky"}]},{"reserveLevel":"Depleted","rings":[null,{"type":"Icy"}]}]}}""",
        };
        var client = new SystemSummaryClient(
            new HttpClient(handler),
            new("https://edsm.test/"),
            new("https://spansh.test/api/")
        );
        SystemMiningResources resources = (await client.GetAsync("Test System", 42)).Summary.MiningResources;
        Assert.True(resources.HasIcyRing);
        Assert.False(resources.HasPristineIcyRing);
        Assert.False(resources.HasIcyBody);
    }

    /// <summary>Preserves discovery and update timestamps in the UTC format returned by EDSM.</summary>
    [Theory]
    [InlineData("2024-01-02 03:04:05", "2025-02-03 04:05:06")]
    [InlineData("2024-01-02T03:04:05Z", "2025-02-03T04:05:06Z")]
    public async Task EdsmDiscoveryDatesReachTheSummary(string discovered, string updated)
    {
        var handler = new ProviderHandler
        {
            BodiesResponse = $$"""
                {"id64":42,"bodyCount":1,"bodies":[{
                  "isMainStar":true,"updateTime":"{{updated}}",
                  "discovery":{"commander":"Pathfinder","date":"{{discovered}}"}
                }]}
                """,
        };
        var client = new SystemSummaryClient(
            new HttpClient(handler),
            new Uri("https://edsm.test/"),
            new Uri("https://spansh.test/api/")
        );

        SystemSummary summary = (await client.GetAsync("Test System", 0)).Summary;

        Assert.Equal("Pathfinder", summary.DiscoveredBy);
        Assert.Equal(new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero), summary.DiscoveredAt);
        Assert.Equal(new DateTimeOffset(2025, 2, 3, 4, 5, 6, TimeSpan.Zero), summary.LastUpdatedAt);
    }

    /// <summary>Uses system discovery from traffic rather than an unrelated body's discovery credit.</summary>
    [Fact]
    public async Task SystemDiscoveryTakesPrecedenceOverBodyDiscovery()
    {
        var handler = new ProviderHandler
        {
            TrafficResponse = """
                {"id64":42,"discovery":{"commander":"System Pathfinder","date":"2020-06-07 08:09:10"}}
                """,
        };
        var client = new SystemSummaryClient(
            new HttpClient(handler),
            new Uri("https://edsm.test/"),
            new Uri("https://spansh.test/api/")
        );

        SystemSummary summary = (await client.GetAsync("Test System", 0)).Summary;

        Assert.Equal("System Pathfinder", summary.DiscoveredBy);
        Assert.Equal(new DateTimeOffset(2020, 6, 7, 8, 9, 10, TimeSpan.Zero), summary.DiscoveredAt);
    }

    /// <summary>Keeps partial discovery records intact instead of borrowing another commander's body date.</summary>
    [Theory]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("\"not-a-date\"")]
    public async Task MissingOrInvalidSystemDatesDoNotBorrowBodyDates(string dateJson)
    {
        var handler = new ProviderHandler
        {
            TrafficResponse =
                "{\"id64\":42,\"discovery\":{\"commander\":\"System Pathfinder\",\"date\":" + dateJson + "}}",
        };
        var client = new SystemSummaryClient(
            new HttpClient(handler),
            new Uri("https://edsm.test/"),
            new Uri("https://spansh.test/api/")
        );

        SystemSummaryLoadResult result = await client.GetAsync("Test System", 0);

        Assert.Empty(result.Warnings);
        Assert.Equal("System Pathfinder", result.Summary.DiscoveredBy);
        Assert.Null(result.Summary.DiscoveredAt);
    }

    [Fact]
    public async Task ProviderResponsesAreMergedIntoLegacyJumpSummary()
    {
        var handler = new ProviderHandler();
        var client = new SystemSummaryClient(
            new HttpClient(handler),
            new Uri("https://edsm.test/"),
            new Uri("https://spansh.test/api/")
        );

        SystemSummaryLoadResult result = await client.GetAsync("Test System", 42);

        Assert.Empty(result.Warnings);
        Assert.Equal("Test System", result.Summary.SystemName);
        Assert.Equal(42, result.Summary.SystemAddress);
        Assert.Equal(new GalacticCoordinate(1, 2, 3), result.Summary.Position);
        Assert.Equal("K", result.Summary.StarClass);
        Assert.True(result.Summary.IsKnown);
        Assert.Equal(4, result.Summary.ScannedBodyCount);
        Assert.Equal(7, result.Summary.TotalBodyCount);
        Assert.Equal("Pathfinder", result.Summary.DiscoveredBy);
        Assert.Equal(
            DateTimeOffset.Parse("2024-01-02T03:04:05Z", global::System.Globalization.CultureInfo.InvariantCulture),
            result.Summary.DiscoveredAt
        );
        Assert.Equal(
            DateTimeOffset.Parse("2025-02-03T04:05:06Z", global::System.Globalization.CultureInfo.InvariantCulture),
            result.Summary.LastUpdatedAt
        );
        Assert.Equal(new SystemTrafficSummary(3, 20, 100), result.Summary.Traffic);
        Assert.Equal(2, result.Summary.PointsOfInterest.Genus);
        Assert.Equal(2, result.Summary.PointsOfInterest.Starports);
        Assert.Equal(1, result.Summary.PointsOfInterest.Outposts);
        Assert.Equal(1, result.Summary.PointsOfInterest.Settlements);
        Assert.Equal(1, result.Summary.PointsOfInterest.FleetCarriers);
        Assert.Equal(1, result.Summary.PointsOfInterest.Wars);
        Assert.Contains(
            result.Summary.Specials,
            special => special.Location == "Encoded Hub" && special.Details.Contains("Material Trader - Encoded")
        );
        Assert.Contains(
            result.Summary.Specials,
            special => special.Location == "Guardian Lab" && special.Details.Contains("Technology Broker - Guardian")
        );
        Assert.Contains(
            result.Summary.Specials,
            special => special.Location == "Engineer Base" && special.Details.Contains("Professor Palin Engineer")
        );
        Assert.Equal(5, result.Summary.Stations.Count);
        Assert.Equal(2, result.Summary.Factions.Count);
        Assert.Equal("Pathfinder Cooperative", result.Summary.Factions[0].Name);
        Assert.Equal(0.62, result.Summary.Factions[0].Influence);
        SystemStationSummary guardianLab = result.Summary.Stations.Single(station => station.Name == "Guardian Lab");
        Assert.Equal("Planetary Port", guardianLab.Type);
        Assert.Equal("High Tech", guardianLab.PrimaryEconomy);
        Assert.Equal(72.5, guardianLab.Economies["High Tech"]);
        Assert.Equal("Lab Cooperative", guardianLab.ControllingFaction);
        Assert.Equal("Corporate", guardianLab.Government);
        Assert.Equal("Large", guardianLab.LandingPads?.Largest);
        Assert.Contains("Technology Broker", guardianLab.Services);
        Assert.Contains("Vista Genomics", guardianLab.Services);
        Assert.Equal(["Narcotics", "Slaves"], guardianLab.ProhibitedCommodities);
        Assert.Equal(
            DateTimeOffset.Parse("2026-01-02T03:04:05Z", global::System.Globalization.CultureInfo.InvariantCulture),
            guardianLab.UpdatedAt
        );
        Assert.Equal(
            [
                "https://edsm.test/api-system-v1/bodies?systemName=Test%20System",
                "https://edsm.test/api-system-v1/traffic?systemName=Test%20System",
                "https://spansh.test/api/dump/42/",
            ],
            handler.Requests.Order(StringComparer.Ordinal).ToArray()
        );
    }

    [Fact]
    public async Task IndividualProviderFailuresReturnPartialDataAndWarnings()
    {
        var handler = new ProviderHandler { FailTraffic = true, MalformSpansh = true };
        var client = new SystemSummaryClient(
            new HttpClient(handler),
            new Uri("https://edsm.test/"),
            new Uri("https://spansh.test/api/")
        );

        SystemSummaryLoadResult result = await client.GetAsync("Test System", 42);

        Assert.True(result.Summary.IsKnown);
        Assert.Equal("K", result.Summary.StarClass);
        Assert.Null(result.Summary.Traffic);
        Assert.Equal(2, result.Warnings.Count);
        Assert.Contains(result.Warnings, warning => warning.StartsWith("EDSM traffic", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, warning => warning.StartsWith("Spansh system dump", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LastUpdatedProviderPreferenceSelectsSpanshTimestamp()
    {
        bool useSpansh = false;
        var client = new SystemSummaryClient(
            new HttpClient(new ProviderHandler()),
            new Uri("https://edsm.test/"),
            new Uri("https://spansh.test/api/"),
            () => useSpansh
        );

        SystemSummaryLoadResult edsm = await client.GetAsync("Test System", 42);
        useSpansh = true;
        SystemSummaryLoadResult spansh = await client.GetAsync("Test System", 42);

        Assert.Equal(
            DateTimeOffset.Parse("2025-02-03T04:05:06Z", global::System.Globalization.CultureInfo.InvariantCulture),
            edsm.Summary.LastUpdatedAt
        );
        Assert.Equal(
            DateTimeOffset.Parse("2026-03-04T05:06:07Z", global::System.Globalization.CultureInfo.InvariantCulture),
            spansh.Summary.LastUpdatedAt
        );
    }

    [Fact]
    public async Task NoAddressSkipsSpanshRequest()
    {
        var handler = new ProviderHandler();
        var client = new SystemSummaryClient(
            new HttpClient(handler),
            new Uri("https://edsm.test/"),
            new Uri("https://spansh.test/api/")
        );

        SystemSummaryLoadResult result = await client.GetAsync("Test System", 0);

        Assert.Equal(42, result.Summary.SystemAddress);
        Assert.Equal(2, handler.Requests.Count);
        Assert.DoesNotContain(handler.Requests, request => request.Contains("spansh"));
    }

    /// <summary>Treats Spansh 404 and empty EDSM objects as an undiscovered system without error warnings.</summary>
    [Fact]
    public async Task UndiscoveredSystemTreatsSpanshNotFoundAsNormalAndProducesNoWarnings()
    {
        var handler = new ProviderHandler
        {
            BodiesResponse = "{}",
            TrafficResponse = "{}",
            SpanshStatusCode = HttpStatusCode.NotFound,
        };
        var client = new SystemSummaryClient(
            new HttpClient(handler),
            new Uri("https://edsm.test/"),
            new Uri("https://spansh.test/api/")
        );

        SystemSummaryLoadResult result = await client.GetAsync("Undiscovered Sector A1-B c2-3", 99999);

        Assert.Empty(result.Warnings);
        Assert.False(result.Summary.IsKnown);

        // EDSM 404 is an unexpected endpoint error and must not be masked as undiscovered.
        var edsmNotFoundHandler = new ProviderHandler
        {
            EdsmStatusCode = HttpStatusCode.NotFound,
            SpanshStatusCode = HttpStatusCode.NotFound,
        };
        var edsmNotFoundClient = new SystemSummaryClient(
            new HttpClient(edsmNotFoundHandler),
            new Uri("https://edsm.test/"),
            new Uri("https://spansh.test/api/")
        );
        SystemSummaryLoadResult edsmNotFound = await edsmNotFoundClient.GetAsync("Broken Endpoint System", 99998);
        Assert.Null(edsmNotFound.Summary.IsKnown);
        Assert.Equal(2, edsmNotFound.Warnings.Count);
        Assert.All(edsmNotFound.Warnings, warning => Assert.StartsWith("EDSM ", warning, StringComparison.Ordinal));
    }

    /// <summary>Deduplicates concurrent requests and serves clean results from cache until TTL expires.</summary>
    [Fact]
    public async Task ConcurrentAndRepeatedLookupsDeduplicateAndExpireByTtl()
    {
        var clock = new AdjustableTimeProvider(
            DateTimeOffset.Parse("2026-11-23T00:00:00Z", global::System.Globalization.CultureInfo.InvariantCulture)
        );
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new ProviderHandler { Gate = gate.Task };
        var client = new SystemSummaryClient(
            new HttpClient(handler),
            new Uri("https://edsm.test/"),
            new Uri("https://spansh.test/api/"),
            timeProvider: clock,
            cacheDuration: TimeSpan.FromMinutes(2)
        );

        Task<SystemSummaryLoadResult> first = client.GetAsync("Test System", 42);
        Task<SystemSummaryLoadResult> second = client.GetAsync("Test System", 42);
        gate.SetResult();
        SystemSummaryLoadResult[] concurrent = await Task.WhenAll(first, second);

        Assert.Equal(3, handler.Requests.Count);
        Assert.All(concurrent, result => Assert.True(result.Summary.IsKnown));

        // Repeat within TTL hits cache with zero extra requests.
        SystemSummaryLoadResult cached = await client.GetAsync("Test System", 42);
        Assert.Equal(3, handler.Requests.Count);
        Assert.True(cached.Summary.IsKnown);

        // Advance past TTL and verify a fresh fetch happens.
        clock.Advance(TimeSpan.FromMinutes(3));
        await client.GetAsync("Test System", 42);
        Assert.Equal(6, handler.Requests.Count);
    }

    /// <summary>Honors EDSM 429 Retry-After cooldown and avoids sending more EDSM requests until cooldown expires.</summary>
    [Theory]
    [InlineData(90, 90)]
    [InlineData(5, 15)]
    [InlineData(900, 600)]
    [InlineData(0, 60)]
    public async Task EdsmRateLimitAppliesCooldownAndResumesAfterExpiry(
        int retryAfterSeconds,
        int expectedCooldownSeconds
    )
    {
        var clock = new AdjustableTimeProvider(
            DateTimeOffset.Parse("2026-11-23T00:00:00Z", global::System.Globalization.CultureInfo.InvariantCulture)
        );
        var handler = new ProviderHandler
        {
            EdsmStatusCode = HttpStatusCode.TooManyRequests,
            EdsmRetryAfter =
                retryAfterSeconds > 0 ? new RetryConditionHeaderValue(TimeSpan.FromSeconds(retryAfterSeconds)) : null,
            SpanshStatusCode = HttpStatusCode.NotFound,
        };
        var client = new SystemSummaryClient(
            new HttpClient(handler),
            new Uri("https://edsm.test/"),
            new Uri("https://spansh.test/api/"),
            timeProvider: clock
        );

        SystemSummaryLoadResult first = await client.GetAsync("System One", 101);
        int requestsAfterFirst = handler.Requests.Count(url =>
            url.StartsWith("https://edsm.test/", StringComparison.Ordinal)
        );
        Assert.InRange(requestsAfterFirst, 1, 2);
        Assert.Null(first.Summary.IsKnown);
        Assert.Equal(2, first.Warnings.Count);
        Assert.All(
            first.Warnings,
            warning => Assert.Contains("rate-limited", warning, StringComparison.OrdinalIgnoreCase)
        );

        // Second lookup during cooldown must not send any EDSM HTTP requests.
        SystemSummaryLoadResult duringCooldown = await client.GetAsync("System Two", 102);
        Assert.Equal(
            requestsAfterFirst,
            handler.Requests.Count(url => url.StartsWith("https://edsm.test/", StringComparison.Ordinal))
        );
        Assert.Equal(2, duringCooldown.Warnings.Count);

        // Just before expiry is still rate-limited; once expired, EDSM resumes.
        clock.Advance(TimeSpan.FromSeconds(expectedCooldownSeconds - 1));
        await client.GetAsync("System Three", 103);
        Assert.Equal(
            requestsAfterFirst,
            handler.Requests.Count(url => url.StartsWith("https://edsm.test/", StringComparison.Ordinal))
        );

        handler.EdsmStatusCode = HttpStatusCode.OK;
        clock.Advance(TimeSpan.FromSeconds(1));
        SystemSummaryLoadResult recovered = await client.GetAsync("System Four", 104);
        Assert.Empty(recovered.Warnings);
        Assert.True(recovered.Summary.IsKnown);
        Assert.Equal(
            requestsAfterFirst + 2,
            handler.Requests.Count(url => url.StartsWith("https://edsm.test/", StringComparison.Ordinal))
        );
    }

    /// <summary>Honors HTTP-date Retry-After headers and Spansh 429 cooldowns without poisoning transient failures.</summary>
    [Fact]
    public async Task SpanshRateLimitDateHeaderAndTransientFailureRecoveryWorkCorrectly()
    {
        var start = DateTimeOffset.Parse(
            "2026-11-23T00:00:00Z",
            global::System.Globalization.CultureInfo.InvariantCulture
        );
        var clock = new AdjustableTimeProvider(start);
        var handler = new ProviderHandler
        {
            BodiesResponse = "{}",
            TrafficResponse = "{}",
            SpanshStatusCode = HttpStatusCode.TooManyRequests,
            SpanshRetryAfter = new RetryConditionHeaderValue(start.AddSeconds(45)),
        };
        var client = new SystemSummaryClient(
            new HttpClient(handler),
            new Uri("https://edsm.test/"),
            new Uri("https://spansh.test/api/"),
            timeProvider: clock
        );

        SystemSummaryLoadResult first = await client.GetAsync("Unknown A", 201);
        Assert.Null(first.Summary.IsKnown);
        Assert.Single(first.Warnings);
        Assert.Contains("rate-limited", first.Warnings[0], StringComparison.OrdinalIgnoreCase);

        // Cooldown prevents extra Spansh request.
        await client.GetAsync("Unknown B", 202);
        Assert.Single(handler.Requests, url => url.StartsWith("https://spansh.test/", StringComparison.Ordinal));

        // After cooldown expires and Spansh recovers to 404, system is cleanly undiscovered.
        handler.SpanshStatusCode = HttpStatusCode.NotFound;
        clock.Advance(TimeSpan.FromSeconds(45));
        SystemSummaryLoadResult recovered = await client.GetAsync("Unknown A", 201);
        Assert.Empty(recovered.Warnings);
        Assert.False(recovered.Summary.IsKnown);
        Assert.Equal(
            2,
            handler.Requests.Count(url => url.StartsWith("https://spansh.test/", StringComparison.Ordinal))
        );
    }

    /// <summary>Rejects negative cache durations and avoids caching transient provider errors.</summary>
    [Fact]
    public async Task NegativeCacheDurationThrowsAndTransientErrorsAreNotCached()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SystemSummaryClient(cacheDuration: TimeSpan.FromSeconds(-1))
        );

        var handler = new ProviderHandler { FailTraffic = true };
        var client = new SystemSummaryClient(
            new HttpClient(handler),
            new Uri("https://edsm.test/"),
            new Uri("https://spansh.test/api/")
        );

        SystemSummaryLoadResult failed = await client.GetAsync("Test System", 42);
        Assert.Single(failed.Warnings);

        handler.FailTraffic = false;
        SystemSummaryLoadResult recovered = await client.GetAsync("Test System", 42);
        Assert.Empty(recovered.Warnings);
        Assert.NotNull(recovered.Summary.Traffic);
        // Bodies and Spansh were served from cache; only traffic was retried.
        Assert.Equal(4, handler.Requests.Count);
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset initialUtcNow) : TimeProvider
    {
        private DateTimeOffset utcNow = initialUtcNow;

        public override DateTimeOffset GetUtcNow() => utcNow;

        public void Advance(TimeSpan delta) => utcNow += delta;
    }

    private sealed class ProviderHandler : HttpMessageHandler
    {
        private readonly Lock requestLock = new();

        public List<string> Requests { get; } = [];

        public bool FailTraffic { get; set; }

        public bool MalformSpansh { get; init; }

        public HttpStatusCode EdsmStatusCode { get; set; } = HttpStatusCode.OK;

        public RetryConditionHeaderValue? EdsmRetryAfter { get; set; }

        public HttpStatusCode SpanshStatusCode { get; set; } = HttpStatusCode.OK;

        public RetryConditionHeaderValue? SpanshRetryAfter { get; set; }

        public Task? Gate { get; init; }

        public string BodiesResponse { get; init; } = BodiesJson;

        public string TrafficResponse { get; init; } = TrafficJson;
        public string SpanshResponse { get; init; } = SpanshJson;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (Gate is not null)
            {
                await Gate.ConfigureAwait(false);
            }

            Uri uri = request.RequestUri!;
            lock (requestLock)
            {
                Requests.Add(uri.AbsoluteUri);
            }

            if (uri.Host == "spansh.test")
            {
                if (SpanshStatusCode != HttpStatusCode.OK)
                {
                    return Response("{}", SpanshStatusCode, SpanshRetryAfter);
                }

                return Response(MalformSpansh ? "{\"system\":[]}" : SpanshResponse);
            }

            if (EdsmStatusCode != HttpStatusCode.OK)
            {
                return Response("{}", EdsmStatusCode, EdsmRetryAfter);
            }

            if (uri.AbsolutePath.EndsWith("/traffic", StringComparison.Ordinal))
            {
                return FailTraffic ? Response("{}", HttpStatusCode.ServiceUnavailable) : Response(TrafficResponse);
            }

            return Response(BodiesResponse);
        }

        private static HttpResponseMessage Response(
            string content,
            HttpStatusCode statusCode = HttpStatusCode.OK,
            RetryConditionHeaderValue? retryAfter = null
        )
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json"),
            };
            if (retryAfter is not null)
            {
                response.Headers.RetryAfter = retryAfter;
            }

            return response;
        }

        private const string BodiesJson = """
            {
              "id64": 42,
              "name": "Test System",
              "bodyCount": 7,
              "bodies": [
                {
                  "name": "Test System",
                  "isMainStar": true,
                  "spectralClass": "K3",
                  "updateTime": "2025-02-03T04:05:06Z",
                  "discovery": {
                    "commander": "Pathfinder",
                    "date": "2024-01-02T03:04:05Z"
                  }
                },
                { "name": "Test System 1", "updateTime": "2024-01-01T00:00:00Z" }
              ]
            }
            """;

        private const string TrafficJson = """
            {
              "id64": 42,
              "traffic": { "day": 3, "week": 20, "total": 100 }
            }
            """;

        private const string SpanshJson = """
            {
              "system": {
                "id64": 42,
                "updated_at": "2026-03-04T05:06:07Z",
                "bodyCount": 6,
                "coords": { "x": 1, "y": 2, "z": 3 },
                "bodies": [
                  {
                    "name": "Test System",
                    "type": "Star",
                    "mainStar": true,
                    "spectralClass": "K3",
                    "signals": {
                      "signals": { "$SAA_SignalType_Biological;": 2 }
                    },
                    "stations": [
                      {
                        "id": 1,
                        "name": "Guardian Lab",
                        "type": "Planetary Port",
                        "primaryEconomy": "High Tech",
                        "economies": {
                          "High Tech": 72.5,
                          "Industrial": 27.5
                        },
                        "controllingFaction": "Lab Cooperative",
                        "government": "Corporate",
                        "landingPads": {
                          "small": 2,
                          "medium": 1,
                          "large": 1
                        },
                        "services": ["Technology Broker", "Market", "Vista Genomics"],
                        "market": {
                          "prohibitedCommodities": ["Slaves", "Narcotics"]
                        },
                        "updateTime": "2026-01-02T03:04:05Z"
                      },
                      {
                        "id": 2,
                        "name": "Engineer Base",
                        "type": "Settlement",
                        "government": "Engineer",
                        "controllingFaction": "Professor Palin",
                        "services": []
                      }
                    ]
                  },
                  { "name": "Test System 1", "type": "Planet" },
                  { "name": "Test System 1 A", "type": "Barycentre" },
                  { "name": "Test System 2", "type": "Planet" },
                  { "name": "Test System 3", "type": "Planet" }
                ],
                "stations": [
                  {
                    "id": 3,
                    "name": "Encoded Hub",
                    "type": "Outpost",
                    "primaryEconomy": "Military",
                    "services": ["Material Trader"]
                  },
                  {
                    "id": 4,
                    "name": "Carrier",
                    "type": "Drake-Class Carrier",
                    "services": []
                  },
                  {
                    "id": 5,
                    "name": "Megaship",
                    "type": "Mega ship",
                    "landingPads": {},
                    "services": []
                  }
                ],
                "factions": [
                  { "name": "Pathfinder Cooperative", "influence": 0.62, "state": "War" },
                  { "name": "Survey League", "influence": 0.38, "state": "War" },
                  { "state": "Boom" }
                ]
              }
            }
            """;
    }
}
