using System.Net;
using System.Text;
using System.Text.Json;
using SrvSurvey.Core.Frontier;
using SrvSurvey.Desktop.Platform.Inara;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class InaraCommunityGoalClientTests
{
    [Fact]
    public async Task GenericReadUsesOnlyApplicationIdentityAndCachesResponse()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var now = DateTimeOffset.Parse("2026-07-31T12:00:00Z");
            var handler = new RecordingHandler(_ => Json(
                HttpStatusCode.OK,
                SuccessfulResponse));
            var client = new InaraCommunityGoalClient(
                new HttpClient(handler),
                "application-key",
                "2.0.95",
                Path.Combine(root, "community-goals.json"),
                () => now);

            var first = await client.GetRecentAsync();
            var second = await client.GetRecentAsync();

            Assert.Equal(1, handler.RequestCount);
            Assert.False(first.IsStale);
            Assert.False(second.IsStale);
            var goal = Assert.Single(first.Goals);
            Assert.Equal("Carcosa Calls for Assistance", goal.Title);
            Assert.Equal(2_913, goal.Contributors);
            Assert.Equal(2_308_981, goal.ContributionsTotal);
            Assert.Equal(1, goal.TierMaximum);
            Assert.Equal("https://inara.cz/elite/communitygoals/855/", goal.InaraUrl);

            using var request = JsonDocument.Parse(Assert.Single(handler.Bodies));
            var header = request.RootElement.GetProperty("header");
            Assert.Equal("SrvSurvey", header.GetProperty("appName").GetString());
            Assert.Equal("application-key", header.GetProperty("APIkey").GetString());
            Assert.False(header.TryGetProperty("commanderName", out _));
            Assert.False(header.TryGetProperty("commanderFrontierID", out _));
            Assert.Equal(
                "getCommunityGoalsRecent",
                request.RootElement.GetProperty("events")[0]
                    .GetProperty("eventName").GetString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExpiredCacheIsReturnedWhenRefreshFails()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var now = DateTimeOffset.Parse("2026-07-31T12:00:00Z");
            var handler = new RecordingHandler(requestCount => requestCount == 1
                ? Json(HttpStatusCode.OK, SuccessfulResponse)
                : Json(HttpStatusCode.ServiceUnavailable, "unavailable"));
            var client = new InaraCommunityGoalClient(
                new HttpClient(handler),
                "application-key",
                "2.0.95",
                Path.Combine(root, "community-goals.json"),
                () => now);

            var fresh = await client.GetRecentAsync();
            now = now.AddMinutes(16);
            var stale = await client.GetRecentAsync();

            Assert.False(fresh.IsStale);
            Assert.True(stale.IsStale);
            Assert.Single(stale.Goals);
            Assert.Contains("HTTP 503", stale.Warning);
            Assert.Equal(2, handler.RequestCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TemporaryCacheCleanupDoesNotMaskPrimaryFailure()
    {
        var primaryFailure = new InvalidOperationException("primary save failure");
        foreach (var cleanupFailure in new Exception[]
                 {
                     new IOException("cleanup I/O failure"),
                     new UnauthorizedAccessException("cleanup access failure"),
                     new System.Security.SecurityException("cleanup security failure"),
                 })
        {
            var observed = Record.Exception((Action)(() =>
            {
                try
                {
                    throw primaryFailure;
                }
                finally
                {
                    InaraCommunityGoalClient.TryDeleteTemporaryFile(
                        "community-goals.tmp",
                        _ => true,
                        _ => throw cleanupFailure);
                }
            }));

            Assert.Same(primaryFailure, observed);
        }
    }

    [Fact]
    public void EnrichmentFillsGlobalFieldsWithoutReplacingFrontierValues()
    {
        var fetchedAt = DateTimeOffset.Parse("2026-07-31T12:00:00Z");
        var frontier = Goal() with
        {
            Objective = "Frontier objective",
            CurrentTotal = 99,
            HasContributorData = false,
        };
        var inara = InaraResult(fetchedAt);

        var result = Assert.Single(InaraCommunityGoalEnricher.Enrich(
            [frontier],
            inara));

        Assert.Equal("Frontier objective", result.Objective);
        Assert.Equal(99, result.CurrentTotal);
        Assert.Equal("Expanded mission briefing", result.Description);
        Assert.Equal("Tier 0 / 1", result.TierReached);
        Assert.Equal(2_913, result.Contributors);
        Assert.True(result.HasContributorData);
        Assert.Contains(result.DataPoints!, point =>
            point.Path == "inara.fetchedAt");
    }

    [Fact]
    public void ConflictingLocationDoesNotMergeSameNamedGoals()
    {
        var frontier = Goal() with { System = "Sol" };

        var results = InaraCommunityGoalEnricher.Enrich(
            [frontier],
            InaraResult(DateTimeOffset.Parse("2026-07-31T12:00:00Z")));

        Assert.Equal(2, results.Count);
        Assert.Contains(results, goal => goal.System == "Sol");
        Assert.Contains(results, goal => goal.System == "Carcosa");
    }

    [Fact]
    public void PriorInaraOnlyGoalsAreReplacedInsteadOfAccumulating()
    {
        var first = InaraCommunityGoalEnricher.Enrich(
            [],
            InaraResult(DateTimeOffset.Parse("2026-07-31T12:00:00Z")));

        var second = InaraCommunityGoalEnricher.Enrich(
            first,
            new InaraCommunityGoalsResult(
                [],
                DateTimeOffset.Parse("2026-07-31T12:16:00Z"),
                false,
                string.Empty));

        Assert.Single(first);
        Assert.Empty(second);
    }

    private static FrontierCommunityGoalSnapshot Goal() => new(
        855,
        "Carcosa Calls for Assistance",
        string.Empty,
        string.Empty,
        string.Empty,
        "Carcosa",
        "Robardin Rock",
        DateTimeOffset.Parse("2026-08-06T10:00:00Z"),
        false,
        0,
        34_500_000,
        0,
        0,
        string.Empty,
        null,
        0,
        null,
        false);

    private static InaraCommunityGoalsResult InaraResult(
        DateTimeOffset fetchedAt) => new(
        [new InaraCommunityGoalSnapshot(
            "Carcosa Calls for Assistance",
            "Expanded mission briefing",
            "Inara objective",
            "Credits",
            "Carcosa",
            "Robardin Rock",
            DateTimeOffset.Parse("2026-08-06T10:00:00Z"),
            false,
            0,
            1,
            2_913,
            2_308_981,
            fetchedAt.AddMinutes(-2),
            "https://inara.cz/elite/communitygoals/855/")],
        fetchedAt,
        false,
        string.Empty);

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-inara-community-goals-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string content) =>
        new(status)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json"),
        };

    private sealed class RecordingHandler(
        Func<int, HttpResponseMessage> response) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return response(RequestCount);
        }
    }

    private const string SuccessfulResponse =
        """
        {
          "header": { "eventStatus": 200 },
          "events": [
            {
              "eventName": "getCommunityGoalsRecent",
              "eventStatus": 200,
              "eventData": [
                {
                  "communitygoalName": "Carcosa Calls for Assistance",
                  "starsystemName": "Carcosa",
                  "stationName": "Robardin Rock",
                  "goalExpiry": "2026-08-06T10:00:00Z",
                  "tierReached": 0,
                  "tierMax": 1,
                  "contributorsNum": 2913,
                  "contributionsTotal": 2308981,
                  "isCompleted": false,
                  "lastUpdate": "2026-07-31T11:58:00Z",
                  "goalObjectiveText": "Deliver Curated Commodity Packages",
                  "goalRewardText": "Credits",
                  "goalDescriptionText": "Expanded mission briefing",
                  "inaraURL": "https://inara.cz/elite/communitygoals/855/"
                }
              ]
            }
          ]
        }
        """;
}
