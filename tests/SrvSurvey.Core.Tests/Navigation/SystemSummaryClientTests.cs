using System.Net;
using System.Text;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Navigation;

public sealed class SystemSummaryClientTests
{
    [Fact]
    public async Task ProviderResponsesAreMergedIntoLegacyJumpSummary()
    {
        var handler = new ProviderHandler();
        var client = new SystemSummaryClient(
            new HttpClient(handler),
            new Uri("https://edsm.test/"),
            new Uri("https://spansh.test/api/"));

        var result = await client.GetAsync("Test System", 42);

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
            DateTimeOffset.Parse("2024-01-02T03:04:05Z"),
            result.Summary.DiscoveredAt);
        Assert.Equal(
            DateTimeOffset.Parse("2025-02-03T04:05:06Z"),
            result.Summary.LastUpdatedAt);
        Assert.Equal(new SystemTrafficSummary(3, 20, 100), result.Summary.Traffic);
        Assert.Equal(2, result.Summary.PointsOfInterest.Genus);
        Assert.Equal(2, result.Summary.PointsOfInterest.Starports);
        Assert.Equal(1, result.Summary.PointsOfInterest.Outposts);
        Assert.Equal(1, result.Summary.PointsOfInterest.Settlements);
        Assert.Equal(1, result.Summary.PointsOfInterest.FleetCarriers);
        Assert.Equal(1, result.Summary.PointsOfInterest.Wars);
        Assert.Contains(
            result.Summary.Specials,
            special => special.Location == "Encoded Hub"
                && special.Details.Contains("Material Trader - Encoded"));
        Assert.Contains(
            result.Summary.Specials,
            special => special.Location == "Guardian Lab"
                && special.Details.Contains("Technology Broker - Guardian"));
        Assert.Contains(
            result.Summary.Specials,
            special => special.Location == "Engineer Base"
                && special.Details.Contains("Professor Palin Engineer"));
        Assert.Equal(5, result.Summary.Stations.Count);
        var guardianLab = result.Summary.Stations.Single(
            station => station.Name == "Guardian Lab");
        Assert.Equal("Planetary Port", guardianLab.Type);
        Assert.Equal("High Tech", guardianLab.PrimaryEconomy);
        Assert.Equal(72.5, guardianLab.Economies["High Tech"]);
        Assert.Equal("Lab Cooperative", guardianLab.ControllingFaction);
        Assert.Equal("Corporate", guardianLab.Government);
        Assert.Equal("Large", guardianLab.LandingPads?.Largest);
        Assert.Contains("Technology Broker", guardianLab.Services);
        Assert.Equal(["Narcotics", "Slaves"], guardianLab.ProhibitedCommodities);
        Assert.Equal(
            DateTimeOffset.Parse("2026-01-02T03:04:05Z"),
            guardianLab.UpdatedAt);
        Assert.Equal(
            [
                "https://edsm.test/api-system-v1/bodies?systemName=Test%20System",
                "https://edsm.test/api-system-v1/traffic?systemName=Test%20System",
                "https://spansh.test/api/dump/42/",
            ],
            handler.Requests.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task IndividualProviderFailuresReturnPartialDataAndWarnings()
    {
        var handler = new ProviderHandler
        {
            FailTraffic = true,
            MalformSpansh = true,
        };
        var client = new SystemSummaryClient(
            new HttpClient(handler),
            new Uri("https://edsm.test/"),
            new Uri("https://spansh.test/api/"));

        var result = await client.GetAsync("Test System", 42);

        Assert.True(result.Summary.IsKnown);
        Assert.Equal("K", result.Summary.StarClass);
        Assert.Null(result.Summary.Traffic);
        Assert.Equal(2, result.Warnings.Count);
        Assert.Contains(result.Warnings, warning => warning.StartsWith("EDSM traffic"));
        Assert.Contains(result.Warnings, warning => warning.StartsWith("Spansh system dump"));
    }

    [Fact]
    public async Task NoAddressSkipsSpanshRequest()
    {
        var handler = new ProviderHandler();
        var client = new SystemSummaryClient(
            new HttpClient(handler),
            new Uri("https://edsm.test/"),
            new Uri("https://spansh.test/api/"));

        var result = await client.GetAsync("Test System", 0);

        Assert.Equal(42, result.Summary.SystemAddress);
        Assert.Equal(2, handler.Requests.Count);
        Assert.DoesNotContain(handler.Requests, request => request.Contains("spansh"));
    }

    private sealed class ProviderHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        public bool FailTraffic { get; init; }

        public bool MalformSpansh { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Requests.Add(uri.AbsoluteUri);
            if (uri.AbsolutePath.EndsWith("/traffic", StringComparison.Ordinal))
            {
                return Task.FromResult(FailTraffic
                    ? Response("{}", HttpStatusCode.ServiceUnavailable)
                    : Response(TrafficJson));
            }

            if (uri.Host == "spansh.test")
            {
                return Task.FromResult(Response(
                    MalformSpansh ? "{\"system\":[]}" : SpanshJson));
            }

            return Task.FromResult(Response(BodiesJson));
        }

        private static HttpResponseMessage Response(
            string content,
            HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json"),
            };
        }

        private const string BodiesJson =
            """
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

        private const string TrafficJson =
            """
            {
              "id64": 42,
              "traffic": { "day": 3, "week": 20, "total": 100 }
            }
            """;

        private const string SpanshJson =
            """
            {
              "system": {
                "id64": 42,
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
                        "services": ["Technology Broker", "Market"],
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
                  { "state": "War" },
                  { "state": "War" },
                  { "state": "Boom" }
                ]
              }
            }
            """;
    }
}
