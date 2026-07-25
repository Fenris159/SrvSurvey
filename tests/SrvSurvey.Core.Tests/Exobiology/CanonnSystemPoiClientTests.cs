using System.Net;
using System.Text;
using SrvSurvey.Core.Exobiology;

namespace SrvSurvey.Core.Tests.Exobiology;

public sealed class CanonnSystemPoiClientTests
{
    [Fact]
    public async Task GetAsyncParsesStringAndNumericSurfaceCoordinates()
    {
        var handler = new RecordingHandler(
            """
            {
              "system": "Shinrarta Dezhra",
              "codex": [
                {
                  "body": "AB 2 f",
                  "english_name": "Bacterium Vesicula - Red",
                  "entryid": 2320502,
                  "hud_category": "Biology",
                  "latitude": "-19.770820",
                  "longitude": "-6.824600",
                  "scanned": "true"
                },
                {
                  "body": "AB 2 g",
                  "english_name": "Stratum Tectonicas - Green",
                  "entryid": "2370401",
                  "hud_category": "Biology",
                  "latitude": 12.5,
                  "longitude": 42.25,
                  "scanned": false
                }
              ]
            }
            """);
        var client = new CanonnSystemPoiClient(
            new HttpClient(handler),
            new Uri("https://example.test/query"));

        var result = await client.GetAsync(
            " Shinrarta Dezhra ",
            " CMDR Test ");

        Assert.Equal("Shinrarta Dezhra", result.SystemName);
        Assert.Collection(
            result.Signals,
            signal =>
            {
                Assert.Equal("AB 2 f", signal.BodyName);
                Assert.Equal(2320502, signal.EntryId);
                Assert.Equal(-19.770820, signal.Location.Latitude, 6);
                Assert.Equal(-6.824600, signal.Location.Longitude, 6);
                Assert.True(signal.IsCommanderScan);
            },
            signal =>
            {
                Assert.Equal(2370401, signal.EntryId);
                Assert.Equal(12.5, signal.Location.Latitude);
                Assert.False(signal.IsCommanderScan);
            });
        Assert.Equal(
            "https://example.test/query/getSystemPoi?system=Shinrarta%20Dezhra&odyssey=Y&cmdr=CMDR%20Test",
            handler.RequestUri?.AbsoluteUri);
    }

    [Fact]
    public async Task GetAsyncRejectsRowsThatCannotGuideSurfaceNavigation()
    {
        var handler = new RecordingHandler(
            """
            {
              "codex": [
                {"body":null,"entryid":1,"hud_category":"Biology","latitude":"1","longitude":"2"},
                {"body":"A 1","entryid":2,"hud_category":"Geology","latitude":"1","longitude":"2"},
                {"body":"A 1","entryid":null,"hud_category":"Biology","latitude":"1","longitude":"2"},
                {"body":"A 1","entryid":3,"hud_category":"Biology","latitude":"91","longitude":"2"},
                {"body":"A 1","entryid":4,"hud_category":"Biology","latitude":"1","longitude":"invalid"},
                {"body":"A 1","entryid":5,"hud_category":"biology","latitude":"1","longitude":"2"}
              ]
            }
            """);
        var client = new CanonnSystemPoiClient(
            new HttpClient(handler),
            new Uri("https://example.test/"));

        var result = await client.GetAsync("System", string.Empty);

        var signal = Assert.Single(result.Signals);
        Assert.Equal(5, signal.EntryId);
        Assert.Equal("System", result.SystemName);
    }

    [Fact]
    public async Task GetAsyncRejectsResponseForAnotherSystem()
    {
        var handler = new RecordingHandler(
            """{"system":"Other","codex":[]}""");
        var client = new CanonnSystemPoiClient(
            new HttpClient(handler),
            new Uri("https://example.test/"));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => client.GetAsync("Expected", string.Empty));

        Assert.Contains("Other", exception.Message);
        Assert.Contains("Expected", exception.Message);
    }

    private sealed class RecordingHandler(string payload) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    payload,
                    Encoding.UTF8,
                    "application/json"),
            });
        }
    }
}
