using System.Net;
using System.Text;
using System.Text.Json;
using SrvSurvey.Core.Settlements;

namespace SrvSurvey.Core.Tests.Settlements;

public sealed class CanonnHumanSiteClientTests
{
    [Fact]
    public void ParseReadsLegacyEnvelopeAndKeepsFirstMarketSubmission()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new object[]
        {
            new
            {
                raw_json =
                    "{\"name\":\"Haberlandt Survey\",\"marketId\":12345,"
                    + "\"systemAddress\":42,\"bodyId\":3,"
                    + "\"stationEconomy\":\"$economy_Agri;\","
                    + "\"lat\":12.5,\"long\":-45.25,\"subType\":4,"
                    + "\"heading\":370,\"calcMethod\":\"ManualFoot\","
                    + "\"availblePads\":{\"Small\":2,\"Medium\":0,\"Large\":1}}",
            },
            new
            {
                raw_json =
                    "{\"name\":\"Duplicate\",\"marketId\":12345,"
                    + "\"systemAddress\":42,\"bodyId\":3,"
                    + "\"stationEconomy\":\"$economy_Agri;\","
                    + "\"lat\":12.5,\"long\":-45.25,\"subType\":1,"
                    + "\"heading\":90}",
            },
            new
            {
                raw_json =
                    "{\"name\":\"Wrong system\",\"marketId\":67890,"
                    + "\"systemAddress\":99,\"bodyId\":3,"
                    + "\"stationEconomy\":\"$economy_Agri;\","
                    + "\"lat\":12.5,\"long\":-45.25}",
            },
            new { raw_json = "{" },
        });

        var result = CanonnHumanSiteClient.Parse(bytes, 42);

        var station = Assert.Single(result.Stations);
        Assert.Equal("Haberlandt Survey", station.Name);
        Assert.Equal(12345, station.MarketId);
        Assert.Equal(4, station.SubType);
        Assert.Equal(10, station.Heading);
        Assert.Equal(new HumanSiteLandingPads(2, 0, 1), station.AvailablePads);
        Assert.Equal(HumanSiteGeometrySource.ManualFoot, station.GeometrySource);
        Assert.Equal(2, result.Warnings.Count);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not json")]
    public void ParseRejectsInvalidOuterEnvelope(string payload)
    {
        Assert.Throws<InvalidDataException>(() =>
            CanonnHumanSiteClient.Parse(Encoding.UTF8.GetBytes(payload), 42));
    }

    [Fact]
    public async Task GetStationsTreatsNotFoundAsAnEmptyResult()
    {
        var handler = new StubHandler(new HttpResponseMessage(
            HttpStatusCode.NotFound));
        var client = new CanonnHumanSiteClient(
            new HttpClient(handler),
            new Uri("https://example.test/query/"));

        var result = await client.GetStationsAsync(42);

        Assert.Empty(result.Stations);
        Assert.Empty(result.Warnings);
        Assert.Equal(
            new Uri("https://example.test/query/42"),
            handler.RequestUri);
    }

    [Fact]
    public async Task PublishStationUsesLegacyPayloadAndEndpointContract()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(
            HttpStatusCode.OK)
        {
            Content = new StringContent("accepted"),
        });
        var client = new CanonnHumanSiteClient(
            new HttpClient(handler),
            new Uri("https://example.test/query/"),
            new Uri("https://example.test/publish"));
        var submission = new CanonnHumanSiteSubmission(
            DateTimeOffset.Parse("2026-07-25T12:00:00Z"),
            new Version(2, 0, 95, 0),
            "Haberlandt Survey",
            12345,
            42,
            3,
            "$economy_Agri;",
            "OnFootSettlement",
            new HumanSiteSurfaceLocation(12.5, -45.25),
            4,
            275,
            HumanSiteGeometrySource.ManualFoot,
            280,
            new HumanSiteSurfaceLocation(12.5001, -45.2501),
            "foot",
            0,
            6_000_000,
            new HumanSiteLandingPads(2, 0, 1));

        var result = await client.PublishStationAsync(submission);

        Assert.Equal("accepted", result);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal(new Uri("https://example.test/publish"), handler.RequestUri);
        using var payload = JsonDocument.Parse(handler.Content!);
        var root = payload.RootElement;
        Assert.Equal("2.0.95.0", root.GetProperty("clientVer").GetString());
        Assert.Equal(12345, root.GetProperty("marketId").GetInt64());
        Assert.Equal(275, root.GetProperty("heading").GetDouble());
        Assert.Equal("ManualFoot", root.GetProperty("calcMethod").GetString());
        Assert.Equal(2,
            root.GetProperty("availblePads").GetProperty("Small").GetInt32());
        Assert.False(root.TryGetProperty("availablePads", out _));
    }

    [Fact]
    public async Task PublishStationRejectsIncompleteDataBeforeSending()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(
            HttpStatusCode.OK));
        var client = new CanonnHumanSiteClient(new HttpClient(handler));
        var submission = new CanonnHumanSiteSubmission(
            DateTimeOffset.UtcNow,
            new Version(2, 0, 95, 0),
            "Test",
            0,
            42,
            3,
            "$economy_Agri;",
            "OnFootSettlement",
            new HumanSiteSurfaceLocation(0, 0),
            4,
            275,
            HumanSiteGeometrySource.AutoDock,
            275,
            new HumanSiteSurfaceLocation(0, 0),
            "sidewinder",
            1,
            6_000_000,
            HumanSiteLandingPads.Empty);

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.PublishStationAsync(submission));

        Assert.Null(handler.RequestUri);
    }

    private sealed class StubHandler(HttpResponseMessage response)
        : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(response);
        }
    }

    private sealed class RecordingHandler(HttpResponseMessage response)
        : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }

        public Uri? RequestUri { get; private set; }

        public string? Content { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri;
            Content = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return response;
        }
    }
}
