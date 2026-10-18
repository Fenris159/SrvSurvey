using System.Net;
using System.Text;
using System.Text.Json;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Search;

public sealed partial class NearestSystemsClientTests
{
    /// <summary>NSP subtypes have no separate color and use the current Spansh value-list contract.</summary>
    [Fact]
    public async Task SpanshLandmarkSearchDoesNotRequireAColorVariant()
    {
        var handler = new StubHandler(async request =>
        {
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            JsonElement root = document.RootElement;
            JsonElement landmark = root.GetProperty("filters").GetProperty("landmarks")[0];
            Assert.Equal("Mollusc", landmark.GetProperty("type").GetProperty("value")[0].GetString());
            Assert.Equal("Albens Bell Mollusc", landmark.GetProperty("subtype").GetProperty("value")[0].GetString());
            Assert.False(landmark.TryGetProperty("variant", out _));
            Assert.Equal(20, root.GetProperty("size").GetInt32());
            Assert.Single(root.GetProperty("filters").EnumerateObject());
            Assert.Equal(
                "asc",
                root.GetProperty("sort")[0].GetProperty("distance").GetProperty("direction").GetString()
            );
            return Json("{\"results\":[]}");
        });

        await Create(handler).SearchLandmarksAsync(new(0, 0, 0), new("mollusc", ["albens bell mollusc"], []));
        Assert.Single(handler.Paths);
    }

    /// <summary>Color searches retain the exact species and optional variants and show only matching features.</summary>
    [Fact]
    public async Task SpanshVariantSearchShowsMatchingColorsAndBodies()
    {
        var handler = new StubHandler(async request =>
        {
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            JsonElement landmark = document.RootElement.GetProperty("filters").GetProperty("landmarks")[0];
            Assert.Equal("Tussock Capillum", landmark.GetProperty("subtype").GetProperty("value")[0].GetString());
            Assert.Equal("Emerald", landmark.GetProperty("variant").GetProperty("value")[0].GetString());
            return Json(
                "{\"search_reference\":\"search-1\",\"results\":["
                    + Body("Test A", 1, "Tussock Capillum", "Emerald")
                    + "]}"
            );
        });
        NearestSystemsSearchResult result = await Create(handler)
            .SearchLandmarksAsync(new(1, 2, 3), new("Tussock", ["Tussock Capillum"], ["emerald", "teal"]));

        NearestSystemSearchRow row = Assert.Single(result.Rows);
        Assert.Contains("Emerald", row.Notes);
        Assert.Contains("body: 1", row.Notes);
        Assert.Contains("1.5k LS", row.Notes);
        Assert.DoesNotContain("Unrelated", row.Notes);
        Assert.Equal(42, row.SystemAddress);
        Assert.Equal("search-1", row.SpanshSearchReference);
    }

    /// <summary>Body duplicates cannot reduce twenty requested systems to only five or sixteen.</summary>
    [Fact]
    public async Task SpanshRecallsPagesUntilTwentyDistinctSystemsWithoutARadius()
    {
        int pages = 0;
        var handler = new StubHandler(request =>
        {
            pages++;
            string[] rows;
            if (pages == 1)
            {
                rows = Enumerable.Range(1, 20).Select(i => Body("Duplicate", i, "Albens Bell Mollusc")).ToArray();
            }
            else
            {
                Assert.EndsWith("/bodies/search/recall/page-ref/1", request.RequestUri!.AbsolutePath);
                Assert.Equal(HttpMethod.Get, request.Method);
                rows = Enumerable.Range(1, 20).Select(i => Body("System " + i, i, "Albens Bell Mollusc")).ToArray();
            }
            return Json(
                "{\"count\":40,\"search_reference\":\"page-ref\",\"results\":[" + string.Join(',', rows) + "]}"
            );
        });
        NearestSystemsSearchResult result = await Create(handler)
            .SearchLandmarksAsync(new(0, 0, 0), new(null, ["Albens Bell Mollusc"], []));

        Assert.Equal(2, pages);
        Assert.Equal(20, result.Rows.Count);
        Assert.All(result.Rows, row => Assert.True(row.Distance > 9000));
        Assert.Contains("body: 20", result.Rows[0].Notes);
        Assert.DoesNotContain("bio", result.Rows[0].Notes, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Generic Canonn searches show the actual Codex feature and accept numeric strings from the live API.</summary>
    [Fact]
    public async Task CanonnSearchUsesTwentyCandidatesWithoutBiologyEnrichment()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Contains("limit=20", request.RequestUri!.Query);
            return Json(
                """
                {"nearest":[{"distance":"30140.14","english_name":"Green Water Giant","system":"Distant","x":"1","y":"2","z":"3"}]}
                """
            );
        });
        NearestSystemsSearchResult result = await Create(handler)
            .SearchCanonnAsync(new(0, 0, 0), "Green Water Giant", "Cmdr");

        NearestSystemSearchRow row = Assert.Single(result.Rows);
        Assert.Contains("Green Water Giant", row.Notes);
        Assert.DoesNotContain("bio", row.Notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("approximate", result.CoverageNote);
        Assert.Single(handler.Paths);
    }

    /// <summary>Subtype-only Codex mappings search groups together instead of passing their child names as colors.</summary>
    [Fact]
    public async Task BellParentSearchUsesOneSubtypeOrList()
    {
        var handler = new StubHandler(async request =>
        {
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            JsonElement landmarks = document.RootElement.GetProperty("filters").GetProperty("landmarks");
            Assert.Equal(1, landmarks.GetArrayLength());
            Assert.Equal(5, landmarks[0].GetProperty("subtype").GetProperty("value").GetArrayLength());
            Assert.False(landmarks[0].TryGetProperty("variant", out _));
            return Json("{\"results\":[]}");
        });
        await Create(handler).SearchCodexAsync(new(0, 0, 0), new CodexSearchCatalog().Resolve("Bell Mollusc"), "Cmdr");
        Assert.Single(handler.Paths);
    }

    /// <summary>A category lacking a supported location index reports the coverage gap without a misleading search.</summary>
    [Fact]
    public async Task UnsupportedCodexEntriesDoNotIssueUnrelatedProviderQueries()
    {
        var handler = new StubHandler(_ =>
            Task.FromException<HttpResponseMessage>(new InvalidOperationException("No query should be sent"))
        );
        NearestSystemsSearchResult result = await Create(handler)
            .SearchCodexAsync(new(0, 0, 0), new("Ship", [new("Ship", "None")], true), "Cmdr");
        Assert.Empty(result.Rows);
        Assert.Contains("No supported location index", result.CoverageNote);
        Assert.Empty(handler.Paths);
    }

    /// <summary>The provider's documented result cap is enforced before sending a request.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public async Task CanonnRejectsInvalidLimits(int limit)
    {
        var handler = new StubHandler(_ => Json("{}"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            Create(handler).SearchCanonnAsync(new(0, 0, 0), "Codex", "Cmdr", limit)
        );
        Assert.Empty(handler.Paths);
    }

    /// <summary>Spansh recall must stop if a provider repeats a page instead of progressing.</summary>
    [Fact]
    public async Task RepeatedRecallPageStopsWithoutHundredsOfRequests()
    {
        int requests = 0;
        string rows = string.Join(
            ',',
            Enumerable.Range(1, 20).Select(i => Body("Same system", i, "Albens Bell Mollusc"))
        );
        var handler = new StubHandler(_ =>
        {
            requests++;
            Assert.True(requests <= 2, "A repeated page must stop the recall loop.");
            return Json("{\"search_reference\":\"repeat\",\"results\":[" + rows + "]}");
        });
        NearestSystemsSearchResult result = await Create(handler)
            .SearchLandmarksAsync(new(0, 0, 0), new(null, ["Albens Bell Mollusc"], []));
        Assert.Single(result.Rows);
        Assert.Equal(2, requests);
    }

    /// <summary>A failed fallback must not discard valid Spansh results, but users must see the coverage gap.</summary>
    [Theory]
    [InlineData("http")]
    [InlineData("json")]
    [InlineData("timeout")]
    public async Task PartialProviderFailureRetainsResultsAndReportsIncompleteCoverage(string failure)
    {
        var handler = new StubHandler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                return Json("{\"results\":[" + Body("Known", 1, "Albens Bell Mollusc") + "]}");
            }
            return failure switch
            {
                "http" => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
                "json" => Json("invalid"),
                _ => throw new TaskCanceledException("Provider timed out"),
            };
        });
        NearestSystemsSearchResult result = await Create(handler)
            .SearchCodexAsync(
                new(0, 0, 0),
                new("Mixed", [new("Albens Bell Mollusc", "Biology"), new("Unknown feature", null)], true),
                "Cmdr"
            );
        Assert.Equal("Known", Assert.Single(result.Rows).SystemName);
        Assert.Contains("incomplete", result.CoverageNote);
        Assert.Equal(NearestSystemSource.Spansh, result.Rows[0].Source);
    }

    /// <summary>Sources merged at one system retain both feature notes and the row's original search reference.</summary>
    [Fact]
    public async Task MixedProvidersMergeSystemsAndPreserveMatchingDetails()
    {
        var handler = new StubHandler(request =>
            request.Method == HttpMethod.Post
                ? Json("{\"search_reference\":\"saved\",\"results\":[" + Body("Known", 1, "Albens Bell Mollusc") + "]}")
                : Json(
                    "{\"nearest\":[{\"system\":\"Known\",\"distance\":9010.99,\"x\":1,\"y\":2,\"z\":3,\"english_name\":\"Unknown feature\"}]}"
                )
        );
        NearestSystemsSearchResult result = await Create(handler)
            .SearchCodexAsync(
                new(0, 0, 0),
                new("Mixed", [new("Albens Bell Mollusc", "Biology"), new("Unknown feature", null)], true),
                "Cmdr"
            );
        NearestSystemSearchRow row = Assert.Single(result.Rows);
        Assert.Equal(NearestSystemSource.Combined, row.Source);
        Assert.Equal(42, row.SystemAddress);
        Assert.Equal(9010.99, row.Distance);
        Assert.Contains("Albens Bell Mollusc", row.Notes);
        Assert.Contains("Unknown feature", row.Notes);
        Assert.Equal("saved", row.SpanshSearchReference);
    }

    /// <summary>When every source fails, an empty list must not be misreported as no matching Codex locations.</summary>
    [Theory]
    [InlineData("null")]
    [InlineData("http")]
    public async Task AllProviderFailuresAreReportedAsFailures(string failure)
    {
        var handler = new StubHandler(_ =>
            failure == "null" ? Json("null") : new HttpResponseMessage(HttpStatusCode.BadGateway)
        );
        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            Create(handler)
                .SearchCodexAsync(new(0, 0, 0), new CodexSearchCatalog().Resolve("Albens Bell Mollusc"), "Cmdr")
        );
        Assert.NotNull(exception.InnerException);
        Assert.Contains(exception.InnerException.Message, exception.Message);
    }

    /// <summary>Caller cancellation must propagate rather than being treated as a partial provider failure.</summary>
    [Fact]
    public async Task CallerCancellationPropagatesThroughGroupedSearch()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new StubHandler(_ =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<HttpResponseMessage>(cancellation.Token);
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Create(handler)
                .SearchCodexAsync(
                    new(0, 0, 0),
                    new CodexSearchCatalog().Resolve("Bell Mollusc"),
                    "Cmdr",
                    cancellation.Token
                )
        );
    }

    /// <summary>Generic type searches are valid, color-only searches are not, and malformed locations are ignored.</summary>
    [Fact]
    public async Task TypeOnlyLandmarkSearchFiltersInvalidRowsAndUsesMainStarNotes()
    {
        var handler = new StubHandler(async request =>
        {
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            JsonElement predicate = document.RootElement.GetProperty("filters").GetProperty("landmarks")[0];
            Assert.Equal("Mollusc", predicate.GetProperty("type").GetProperty("value")[0].GetString());
            Assert.False(predicate.TryGetProperty("subtype", out _));
            return Json(
                """
                {"results":[
                  {"system_name":"Star","name":"Star","distance":99,"system_x":1,"system_y":2,"system_z":3,
                   "landmarks":[{"type":"Mollusc","subtype":"Albens Bell Mollusc"}]},
                  {"system_name":"Invalid","distance":-1}, {"distance":1}
                ]}
                """
            );
        });
        NearestSystemsSearchResult result = await Create(handler)
            .SearchLandmarksAsync(new(0, 0, 0), new("Mollusc", [], []));
        NearestSystemSearchRow row = Assert.Single(result.Rows);
        Assert.Contains("Albens Bell Mollusc", row.Notes);
        Assert.Contains("main star", row.Notes);
        Assert.Null(row.SystemAddress);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Create(handler).SearchLandmarksAsync(new(0, 0, 0), new(null, [], ["Emerald"]))
        );
        Assert.Single(handler.Paths);
    }

    /// <summary>Canonn returns candidate rows, which still need sorting, de-duplication and validation.</summary>
    [Fact]
    public async Task CanonnSortsUniqueCandidatesAndHandlesMissingFeatureNames()
    {
        var handler = new StubHandler(_ =>
            Json(
                """
                {"nearest":[
                  {"system":"Far","distance":200,"x":1,"y":2,"z":3},
                  {"system":"Close","distance":10,"x":1,"y":2,"z":3},
                  {"system":"close","distance":11,"x":1,"y":2,"z":3},
                  {"system":"Invalid","distance":-1}, {"distance":1}
                ]}
                """
            )
        );
        NearestSystemsSearchResult result = await Create(handler).SearchCanonnAsync(new(0, 0, 0), "Anomaly", "Cmdr", 1);
        NearestSystemSearchRow row = Assert.Single(result.Rows);
        Assert.Equal("Close", row.SystemName);
        Assert.Contains("Anomaly", row.Notes);
    }

    private static string Body(string system, int body, string subtype, string? variant = null)
    {
        string type = subtype.StartsWith("Tussock", StringComparison.Ordinal) ? "Tussock" : "Mollusc";
        return JsonSerializer.Serialize(
            new
            {
                distance = 9010 + body,
                distance_to_arrival = 1500,
                name = system + " " + body,
                landmarks = new[]
                {
                    new
                    {
                        type,
                        subtype,
                        variant,
                    },
                    new
                    {
                        type = "Other",
                        subtype = "Unrelated",
                        variant = (string?)null,
                    },
                },
                system_id64 = 42,
                system_name = system,
                system_x = 1,
                system_y = 2,
                system_z = 3,
            }
        );
    }

    private static NearestSystemsClient Create(HttpMessageHandler handler) =>
        new(new HttpClient(handler), new Uri("https://example.test/query/"), new Uri("https://example.test/api/"));

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> send)
            : this(request => Task.FromResult(send(request))) { }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Paths.Add(request.RequestUri!.PathAndQuery);
            return send(request);
        }
    }
}
