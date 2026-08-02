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
    public async Task GenericJobDetectsAndStructuresExobiologyBodies()
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
        Assert.Null(hop.Notes);
        Assert.Equal(["A 1", "A 2"], hop.BioTargets.Select(body => body.BodyName));
        Assert.Empty(hop.BioTargets[0].Species);
        Assert.Equal(["Stratum Tectonicas"], hop.BioTargets[1].Species);
        Assert.False(hop.Refuel);
        Assert.False(hop.Neutron);
    }

    [Theory]
    [InlineData(SpanshRouteKind.Riches)]
    [InlineData(SpanshRouteKind.Exobiology)]
    public async Task ValuableWorldRoutesReadResultArrays(
        SpanshRouteKind kind)
    {
        var client = CreateClient(
            """
            {
              "status": "ok",
              "result": [
                {
                  "name": "Exomastery Stop",
                  "id64": 42,
                  "x": 1,
                  "y": 2,
                  "z": 3,
                  "bodies": []
                }
              ]
            }
            """);

        var hops = await client.GetRouteAsync(
            new SpanshRouteReference(RouteId, kind));

        var hop = Assert.Single(hops);
        Assert.Equal("Exomastery Stop", hop.Name);
        Assert.Equal(42, hop.SystemAddress);
    }

    [Fact]
    public async Task ValuableWorldRouteAggregatesStructuredBodiesBySystem()
    {
        var client = CreateClient(
            """
            {
              "status": "ok",
              "result": [
                {
                  "name": "Valuable System",
                  "id64": 42,
                  "x": 1,
                  "y": 2,
                  "z": 3,
                  "bodies": [{
                    "id": 2,
                    "name": "Valuable System A 2",
                    "subtype": "Earth-like world",
                    "distance_to_arrival": 1234.56,
                    "estimated_scan_value": 125000,
                    "estimated_mapping_value": 625000,
                    "terraforming_state": "Candidate for terraforming"
                  }]
                },
                {
                  "name": "Valuable System",
                  "id64": 42,
                  "x": 1,
                  "y": 2,
                  "z": 3,
                  "bodies": [{
                    "id": 3,
                    "name": "Valuable System A 3",
                    "body_subtype": "Water world",
                    "distance_to_arrival_ls": "4321.5",
                    "estimatedScanValue": "75000",
                    "estimatedMappingValue": 250000
                  }]
                }
              ]
            }
            """);

        var hops = await client.GetRouteAsync(
            new SpanshRouteReference(RouteId, SpanshRouteKind.Riches));

        var hop = Assert.Single(hops);
        Assert.Null(hop.Notes);
        Assert.Equal(["A 2", "A 3"], hop.BioTargets.Select(body => body.BodyName));
        var first = hop.BioTargets[0];
        Assert.Equal("Earth-like world", first.Subtype);
        Assert.Equal(1234.56, first.DistanceToArrivalLs);
        Assert.Equal(125000, first.EstimatedScanValue);
        Assert.Equal(625000, first.EstimatedMappingValue);
        Assert.True(first.IsTerraformable);
        Assert.False(first.IsBiological);
        var second = hop.BioTargets[1];
        Assert.Equal(4321.5, second.DistanceToArrivalLs);
        Assert.Equal(75000, second.EstimatedScanValue);
        Assert.Equal(250000, second.EstimatedMappingValue);
    }

    [Fact]
    public async Task ExobiologyRouteAggregatesBodiesBySystemIntoStructuredBio()
    {
        var client = CreateClient(
            """
            {
              "status": "ok",
              "result": [
                {
                  "name": "Test System",
                  "id64": 42,
                  "x": 1,
                  "y": 2,
                  "z": 3,
                  "bodies": [{
                    "id": 2,
                    "name": "Test System A 2",
                    "landmarks": [
                      { "subtype": "Stratum Tectonicas", "value": 19010800 },
                      { "subtype": "Stratum Tectonicas", "value": 19010800 }
                    ]
                  }]
                },
                {
                  "name": "Test System",
                  "id64": 42,
                  "x": 1,
                  "y": 2,
                  "z": 3,
                  "bodies": [
                    {
                      "id": 2,
                      "name": "Test System A 2",
                      "landmarks": [{
                        "subtype": "Bacterium Acies",
                        "estimated_value": 8418000
                      }]
                    },
                    {
                      "id": 4,
                      "name": "Test System B 1",
                      "landmarks": null
                    }
                  ]
                }
              ]
            }
            """);

        var hops = await client.GetRouteAsync(
            new SpanshRouteReference(RouteId, SpanshRouteKind.Exobiology));

        var hop = Assert.Single(hops);
        Assert.Null(hop.Notes);
        Assert.Equal(["A 2", "B 1"], hop.BioTargets.Select(body => body.BodyName));
        Assert.Equal(
            ["Stratum Tectonicas", "Bacterium Acies"],
            hop.BioTargets[0].Species);
        Assert.Equal(27428800, hop.BioTargets[0].EstimatedBiologyValue);
        Assert.True(hop.BioTargets[0].IsBiological);
        Assert.Empty(hop.BioTargets[1].Species);
        Assert.True(hop.BioTargets[1].IsBiological);
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
                  {
                    "system": "Colonia",
                    "id64": 2,
                    "x": -1,
                    "y": 2,
                    "z": 3,
                    "neutron_star": true,
                    "bodies": [{
                      "id": 4,
                      "name": "Colonia 4",
                      "subtype": "Water world",
                      "distance_to_arrival": 912.25
                    }]
                  }
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
        Assert.True(hops[1].Neutron);
        var body = Assert.Single(hops[1].BioTargets);
        Assert.Equal("4", body.BodyName);
        Assert.Equal("Water world", body.Subtype);
        Assert.Equal(912.25, body.DistanceToArrivalLs);
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
                    "has_neutron": true,
                    "bodies": [{
                      "id": 1,
                      "name": "Jackson's Lighthouse 1",
                      "subtype": "Rocky body"
                    }]
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
        Assert.Equal("1", Assert.Single(hop.BioTargets).BodyName);
    }

    [Fact]
    public async Task FleetCarrierRoutePreservesRestockGuidance()
    {
        var client = CreateClient(
            """
            {
              "status": "ok",
              "result": {
                "jumps": [
                  {
                    "name": "Carrier Stop",
                    "id64": 81,
                    "x": 4,
                    "y": 5,
                    "z": 6,
                    "distance": 499.76,
                    "distance_to_destination": 21502.09,
                    "fuel_remaining": 1000,
                    "tritium_in_market": 2799,
                    "fuel_used": 93,
                    "has_icy_ring": true,
                    "is_system_pristine": true,
                    "must_restock": true,
                    "restock_amount": 3892,
                    "bodies": [{
                      "id": 1,
                      "name": "Carrier Stop 1",
                      "subtype": "Rocky body"
                    }]
                  }
                ]
              }
            }
            """);

        var hops = await client.GetRouteAsync(
            new SpanshRouteReference(RouteId, SpanshRouteKind.FleetCarrier));

        var hop = Assert.Single(hops);
        Assert.Equal("Carrier Stop", hop.Name);
        Assert.Contains("restock", hop.Notes, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(hop.BioTargets);
        var carrier = Assert.IsType<FollowRouteCarrierHop>(hop.Carrier);
        Assert.Equal(499.76, carrier.DistanceLy);
        Assert.Equal(21502.09, carrier.RemainingLy);
        Assert.Equal(1000, carrier.FuelRemainingTonnes);
        Assert.Equal(2799, carrier.TritiumInMarketTonnes);
        Assert.Equal(93, carrier.FuelUsedTonnes);
        Assert.True(carrier.HasIcyRing);
        Assert.True(carrier.IsSystemPristine);
        Assert.True(carrier.MustRestock);
        Assert.Equal(3892, carrier.RestockAmountTonnes);
    }

    [Fact]
    public async Task ColonisationRouteReadsJumpObjects()
    {
        var client = CreateClient(
            """
            {
              "status": "ok",
              "result": {
                "jumps": [
                  {
                    "name": "Candidate System",
                    "id64": 91,
                    "x": 7,
                    "y": 8,
                    "z": 9,
                    "body_count": 17,
                    "bodies": [{
                      "id": 1,
                      "name": "Candidate System 1",
                      "subtype": "Rocky body"
                    }]
                  }
                ]
              }
            }
            """);

        var hops = await client.GetRouteAsync(
            new SpanshRouteReference(RouteId, SpanshRouteKind.Colonisation));

        var hop = Assert.Single(hops);
        Assert.Equal("Candidate System", hop.Name);
        Assert.Equal(91, hop.SystemAddress);
        Assert.Empty(hop.BioTargets);
    }

    [Fact]
    public async Task TradeRouteReadsNestedSourceAndDestinations()
    {
        var client = CreateClient(
            """
            {
              "status": "ok",
              "result": [
                {
                  "source": {
                    "system": "Sol",
                    "system_id64": 1,
                    "station": "Galileo",
                    "x": 0,
                    "y": 0,
                    "z": 0
                  },
                  "destination": {
                    "system": "Barnard's Star",
                    "system_id64": 2,
                    "station": "Miller Depot",
                    "x": 1,
                    "y": 2,
                    "z": 3
                  }
                },
                {
                  "source": {
                    "system": "Barnard's Star",
                    "system_id64": 2,
                    "station": "Miller Depot",
                    "x": 1,
                    "y": 2,
                    "z": 3
                  },
                  "destination": {
                    "system": "Achenar",
                    "system_id64": 3,
                    "station": "Dawes Hub",
                    "x": 4,
                    "y": 5,
                    "z": 6
                  }
                }
              ]
            }
            """);

        var hops = await client.GetRouteAsync(
            new SpanshRouteReference(RouteId, SpanshRouteKind.Trade));

        Assert.Equal(["Sol", "Barnard's Star", "Achenar"], hops.Select(hop => hop.Name));
        Assert.Equal(1, hops[0].SystemAddress);
        Assert.Equal("Station: Galileo", hops[0].Notes);
        Assert.Equal("Station: Dawes Hub", hops[2].Notes);
    }

    [Theory]
    [InlineData(
        "{\"status\":\"ok\",\"result\":{\"system_jumps\":[{\"system\":\"Sol\"}]}}",
        "Sol")]
    [InlineData(
        "{\"status\":\"ok\",\"result\":{\"jumps\":[{\"name\":\"Colonia\"}]}}",
        "Colonia")]
    [InlineData(
        "{\"status\":\"ok\",\"result\":[{\"source\":{\"system\":\"Achenar\"},\"destination\":{\"system\":\"Sol\"}}]}",
        "Achenar")]
    public async Task BareJobIdsAutoDetectTheReturnedRouteShape(
        string response,
        string expectedFirstSystem)
    {
        var client = CreateClient(response);

        var hops = await client.GetRouteAsync(
            new SpanshRouteReference(RouteId, SpanshRouteKind.Generic));

        Assert.NotEmpty(hops);
        Assert.Equal(expectedFirstSystem, hops[0].Name);
    }

    [Fact]
    public async Task PendingRouteIsPolledUsingUppercaseJobId()
    {
        var handler = new SequenceHandler(
            "{\"state\":\"queued\",\"status\":\"waiting\"}",
            """
            {
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
