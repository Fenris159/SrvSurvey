using System.Net;
using System.Text;
using System.Text.Json;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Search;

public sealed class NearestSystemsClientTests
{
    [Fact]
    public async Task CanonnSearchEnrichesNearestSystemsWithBiologyNotes()
    {
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith(
                    "/nearest/codex",
                    StringComparison.Ordinal))
            {
                Assert.Contains("x=1.5", request.RequestUri.Query);
                Assert.Contains("name=Stratum", request.RequestUri.Query);
                return Json(
                    "{\"nearest\":["
                        + "{\"distance\":2.5,\"system\":\"Test A\",\"x\":1,\"y\":2,\"z\":3},"
                        + "{\"distance\":5.5,\"system\":\"Test B\",\"x\":4,\"y\":5,\"z\":6}]}");
            }

            Assert.Contains("odyssey=Y", request.RequestUri.Query);
            return request.RequestUri.Query.Contains("Test%20A")
                ? Json(
                    "{\"codex\":["
                        + "{\"body\":\"A 1\",\"english_name\":\"Stratum Tectonicas\",\"entryid\":1,\"hud_category\":\"Biology\"},"
                        + "{\"body\":\"A 1\",\"english_name\":\"Bacterium\",\"entryid\":2,\"hud_category\":\"Biology\"}]}")
                : Json("{\"codex\":[]}");
        });
        var client = Create(handler);

        var result = await client.SearchCanonnAsync(
            new GalacticCoordinate(1.5, 2.5, 3.5),
            "Stratum",
            "Test Cmdr");

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("Test A", result.Rows[0].SystemName);
        Assert.Equal("Body A1: 2 signals", result.Rows[0].Notes);
        Assert.Equal(
            "No bio signals in system",
            result.Rows[1].Notes);
        Assert.All(result.Rows, row =>
            Assert.Equal(NearestSystemSource.Canonn, row.Source));
    }

    [Fact]
    public async Task SpanshVariantSearchUsesLegacyShapeAndLimitsUniqueSystems()
    {
        string? requestBody = null;
        var handler = new StubHandler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/bodies/search", request.RequestUri!.AbsolutePath);
            requestBody = await request.Content!.ReadAsStringAsync();
            return Json(
                "{\"search_reference\":\"search-1\",\"results\":["
                    + Body("Test A 1", "Test A", 42, 2.5, "Emerald") + ","
                    + Body("Test A 2", "Test A", 42, 3.0, "Teal") + ","
                    + Body("Test B 3", "Test B", 43, 5.5, "Teal")
                    + "]}");
        });
        var client = Create(handler);

        var result = await client.SearchMissingVariantsAsync(
            new GalacticCoordinate(1, 2, 3),
            "tussock",
            "Tussock Capillum",
            ["emerald", "teal"]);

        Assert.Equal("search-1", result.SpanshSearchReference);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("Test A", result.Rows[0].SystemName);
        Assert.Contains("body: 1", result.Rows[0].Notes);
        Assert.Contains("1.5k LS", result.Rows[0].Notes);
        Assert.Equal(42, result.Rows[0].SystemAddress);
        using var document = JsonDocument.Parse(requestBody!);
        var root = document.RootElement;
        var landmark = root.GetProperty("filters")
            .GetProperty("landmarks")[0];
        Assert.Equal("Tussock", landmark.GetProperty("type").GetString());
        Assert.Equal(
            "Tussock Capillum",
            landmark.GetProperty("subtype")[0].GetString());
        Assert.Equal(
            "Emerald",
            landmark.GetProperty("variant")[0].GetString());
        Assert.Equal(1, root.GetProperty("reference_coords")
            .GetProperty("x").GetDouble());
    }

    [Fact]
    public void CanonnNotesFallBackWhenBodyIsMissing()
    {
        var notes = NearestSystemsClient.SummarizeCanonnSystemPoi(
        [
            new CanonnCodexEntry(
                null,
                "Stratum",
                1,
                "Biology"),
            new CanonnCodexEntry(
                "A 1",
                "Geology",
                2,
                "Geology"),
        ]);

        Assert.Equal("System bio signals: 2", notes);
    }

    private static string Body(
        string name,
        string system,
        long address,
        double distance,
        string variant)
    {
        return $$"""
            {"distance":{{distance}},"distance_to_arrival":1500,"name":"{{name}}","signals":[{"name":"Biological","count":3}],"landmarks":[{"subtype":"Tussock Capillum","variant":"{{variant}}"}],"system_id64":{{address}},"system_name":"{{system}}","system_x":1,"system_y":2,"system_z":3}
            """;
    }

    private static NearestSystemsClient Create(HttpMessageHandler handler)
    {
        return new NearestSystemsClient(
            new HttpClient(handler),
            new Uri("https://example.test/query/"),
            new Uri("https://example.test/api/"));
    }

    private static HttpResponseMessage Json(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> send)
            : this(request => Task.FromResult(send(request)))
        {
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return send(request);
        }
    }
}
