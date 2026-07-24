using System.Net;
using System.Text;
using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Routes;

public sealed class SpanshRouteClientTests
{
    private static readonly Guid RouteId = Guid.Parse(
        "74FA2952-2048-11F1-8302-B948FF6DF5C1");

    [Fact]
    public async Task GenericRoutePreservesCoordinatesAndSummarizesBodies()
    {
        var client = CreateClient(
            """
            {
              "state": "completed",
              "status": "ok",
              "result": [
                {
                  "name": "Test System",
                  "id64": 42,
                  "x": 1.5,
                  "y": -2,
                  "z": 3,
                  "bodies": [
                    {
                      "id": 2,
                      "name": "Test System A 2",
                      "landmarks": [
                        { "subtype": "Stratum Tectonicas" },
                        { "subtype": "Stratum Tectonicas" }
                      ]
                    },
                    {
                      "id": 1,
                      "name": "Test System A 1",
                      "landmarks": null
                    }
                  ]
                }
              ]
            }
            """);

        var hops = await client.GetRouteAsync(
            new SpanshRouteReference(RouteId, SpanshRouteKind.Generic));

        var hop = Assert.Single(hops);
        Assert.Equal("Test System", hop.Name);
        Assert.Equal(42, hop.SystemAddress);
        Assert.Equal(new GalacticCoordinate(1.5, -2, 3), hop.Position);
        Assert.Equal(
            "Scan: [A1]\r\nStratum Tectonicas: [A2]",
            hop.Notes);
        Assert.False(hop.Refuel);
        Assert.False(hop.Neutron);
    }

    [Theory]
    [InlineData(SpanshRouteKind.Tourist)]
    [InlineData(SpanshRouteKind.Neutron)]
    public async Task TouristAndNeutronRoutesReadSystemJumps(
        SpanshRouteKind kind)
    {
        var client = CreateClient(
            """
            {
              "state": "completed",
              "status": "ok",
              "result": {
                "system_jumps": [
                  { "system": "Sol", "id64": 1, "x": 0, "y": 0, "z": 0 },
                  { "system": "Colonia", "id64": 2, "x": -1, "y": 2, "z": 3 }
                ]
              }
            }
            """);

        var hops = await client.GetRouteAsync(
            new SpanshRouteReference(RouteId, kind));

        Assert.Equal(2, hops.Count);
        Assert.Equal("Sol", hops[0].Name);
        Assert.Equal("Colonia", hops[1].Name);
        Assert.Equal(new GalacticCoordinate(-1, 2, 3), hops[1].Position);
    }

    [Fact]
    public async Task GalaxyRoutePreservesRefuelAndNeutronGuidance()
    {
        var client = CreateClient(
            """
            {
              "state": "completed",
              "status": "ok",
              "result": {
                "jumps": [
                  {
                    "name": "Jackson's Lighthouse",
                    "id64": 7,
                    "x": 1,
                    "y": 2,
                    "z": 3,
                    "must_refuel": true,
                    "has_neutron": true
                  }
                ]
              }
            }
            """);

        var hops = await client.GetRouteAsync(
            new SpanshRouteReference(RouteId, SpanshRouteKind.Galaxy));

        var hop = Assert.Single(hops);
        Assert.True(hop.Refuel);
        Assert.True(hop.Neutron);
    }

    [Fact]
    public async Task PendingRouteIsPolledUsingUppercaseJobId()
    {
        var handler = new SequenceHandler(
            "{\"state\":\"queued\",\"status\":\"ok\"}",
            """
            {
              "state": "completed",
              "status": "ok",
              "result": [{ "name": "Sol", "id64": 1, "x": 0, "y": 0, "z": 0 }]
            }
            """);
        var client = new SpanshRouteClient(
            new HttpClient(handler),
            new Uri("https://example.test/api/"),
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1));

        var hops = await client.GetRouteAsync(
            new SpanshRouteReference(RouteId, SpanshRouteKind.Generic));

        Assert.Single(hops);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(
            handler.Requests,
            uri => Assert.Equal(
                "https://example.test/api/results/74FA2952-2048-11F1-8302-B948FF6DF5C1",
                uri.AbsoluteUri));
    }

    [Fact]
    public async Task PendingRouteTimesOutWithLastKnownState()
    {
        var client = new SpanshRouteClient(
            new HttpClient(new SequenceHandler(
                "{\"state\":\"queued\",\"status\":\"waiting\"}")),
            new Uri("https://example.test/api/"),
            TimeSpan.Zero,
            TimeSpan.Zero);

        var exception = await Assert.ThrowsAsync<TimeoutException>(
            () => client.GetRouteAsync(
                new SpanshRouteReference(RouteId, SpanshRouteKind.Generic)));

        Assert.Contains("queued", exception.Message);
        Assert.Contains("waiting", exception.Message);
    }

    [Fact]
    public async Task CompletedFailureAndMalformedPayloadAreRejected()
    {
        var failed = CreateClient(
            "{\"state\":\"completed\",\"status\":\"error\"}");
        var malformed = CreateClient(
            "{\"state\":\"completed\",\"status\":\"ok\",\"result\":{}}");
        var reference = new SpanshRouteReference(
            RouteId,
            SpanshRouteKind.Generic);

        var failedException = await Assert.ThrowsAsync<InvalidDataException>(
            () => failed.GetRouteAsync(reference));
        var malformedException = await Assert.ThrowsAsync<InvalidDataException>(
            () => malformed.GetRouteAsync(reference));

        Assert.Contains("error", failedException.Message);
        Assert.Contains("route hops", malformedException.Message);
    }

    [Fact]
    public async Task HttpFailuresAreNotHidden()
    {
        var handler = new SequenceHandler("{}")
        {
            StatusCode = HttpStatusCode.ServiceUnavailable,
        };
        var client = new SpanshRouteClient(
            new HttpClient(handler),
            new Uri("https://example.test/api/"));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetRouteAsync(
                new SpanshRouteReference(RouteId, SpanshRouteKind.Generic)));
    }

    private static SpanshRouteClient CreateClient(string response)
    {
        return new SpanshRouteClient(
            new HttpClient(new SequenceHandler(response)),
            new Uri("https://example.test/api/"));
    }

    private sealed class SequenceHandler(params string[] responses)
        : HttpMessageHandler
    {
        private int requestIndex;

        public HttpStatusCode StatusCode { get; init; } = HttpStatusCode.OK;

        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            var responseIndex = Math.Min(
                Interlocked.Increment(ref requestIndex) - 1,
                responses.Length - 1);
            return Task.FromResult(new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(
                    responses[responseIndex],
                    Encoding.UTF8,
                    "application/json"),
                RequestMessage = request,
            });
        }
    }
}
