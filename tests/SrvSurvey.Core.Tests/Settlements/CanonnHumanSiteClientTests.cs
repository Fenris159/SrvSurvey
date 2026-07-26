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
}
