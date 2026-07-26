using System.Net;
using System.Text;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Exploration;

public sealed class SystemBodyDataClientTests
{
    [Fact]
    public async Task ProvidersProjectLegacyBodyDetailsAndFreshBiology()
    {
        var handler = new ProviderHandler();
        var client = new SystemBodyDataClient(
            new HttpClient(handler),
            new Uri("https://edsm.test/"),
            new Uri("https://spansh.test/api/"));

        var result = await client.GetAsync("Test System", 42);

        Assert.Empty(result.Warnings);
        Assert.Equal(["EDSM", "Spansh"], result.Providers
            .Select(provider => provider.Provider));
        Assert.Equal(
            [
                "https://edsm.test/api-system-v1/bodies?systemName=Test%20System",
                "https://spansh.test/api/dump/42/",
            ],
            handler.Requests.Order(StringComparer.Ordinal));
        var edsm = result.Providers[0].Snapshot;
        Assert.Equal(3, edsm.ExpectedBodyCount);
        var edsmPlanet = edsm.Bodies.Single(body => body.BodyId == 1);
        Assert.Equal(SystemBodyKind.LandablePlanet, edsmPlanet.Kind);
        Assert.Equal("Metal rich body", edsmPlanet.PlanetClass);
        Assert.Equal(1.2, edsmPlanet.Mass);
        Assert.Equal(6_000_000, edsmPlanet.RadiusMeters);
        Assert.Equal(15, edsmPlanet.SurfaceGravity);
        Assert.Equal(1_000, edsmPlanet.SurfacePressure);
        Assert.Equal(599_584_916, edsmPlanet.SemiMajorAxis);
        Assert.Equal("CarbonDioxide", edsmPlanet.AtmosphereType);
        Assert.Equal(
            99,
            edsmPlanet.AtmosphereComposition["CarbonDioxideRich"]);
        Assert.Equal(20, edsmPlanet.Materials["iron"]);
        Assert.Equal(
            new SystemBodyParentSnapshot(SystemBodyParentKind.Star, 0),
            Assert.Single(edsmPlanet.Parents));
        Assert.Equal(10, Assert.Single(edsmPlanet.Rings).InnerRadius);

        var spansh = result.Providers[1].Snapshot;
        Assert.Equal(new GalacticCoordinate(1, 2, 3), spansh.StarPosition);
        var spanshStar = spansh.Bodies.Single(body => body.BodyId == 0);
        Assert.Equal("K", spanshStar.StarClass);
        Assert.Equal(695_700_000, spanshStar.RadiusMeters);
        var spanshPlanet = spansh.Bodies.Single(body => body.BodyId == 1);
        Assert.Equal(2, spanshPlanet.BiologicalSignalCount);
        var organism = Assert.Single(spanshPlanet.Organisms);
        Assert.Equal("$Codex_Ent_Aleoids_Genus_Name;", organism.Genus);
        Assert.Equal("Aleoida", organism.GenusLocalized);
    }

    [Fact]
    public async Task StaleSignalCountIsIgnoredButGenusRemainsAvailableForConsent()
    {
        var handler = new ProviderHandler
        {
            SpanshJson = ProviderHandler.SpanshJsonTemplate.Replace(
                "2024-01-02T03:04:05Z",
                "2022-11-28T23:59:59Z",
                StringComparison.Ordinal),
        };
        var client = new SystemBodyDataClient(
            new HttpClient(handler),
            new Uri("https://edsm.test/"),
            new Uri("https://spansh.test/api/"));

        var result = await client.GetAsync("Test System", 42);

        var planet = result.Providers[1].Snapshot.Bodies.Single(
            body => body.BodyId == 1);
        Assert.Equal(0, planet.BiologicalSignalCount);
        Assert.Single(planet.Organisms);
    }

    [Fact]
    public async Task ProviderAddressMismatchIsIsolatedWithoutLosingOtherData()
    {
        var handler = new ProviderHandler
        {
            SpanshJson = ProviderHandler.SpanshJsonTemplate.Replace(
                "\"id64\": 42",
                "\"id64\": 99",
                StringComparison.Ordinal),
        };
        var client = new SystemBodyDataClient(
            new HttpClient(handler),
            new Uri("https://edsm.test/"),
            new Uri("https://spansh.test/api/"));

        var result = await client.GetAsync("Test System", 42);

        Assert.Equal("EDSM", Assert.Single(result.Providers).Provider);
        Assert.Contains(
            "address 99, not 42",
            Assert.Single(result.Warnings),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task OversizedProviderResponseIsRejectedIndependently()
    {
        var handler = new ProviderHandler { OversizeEdsm = true };
        var client = new SystemBodyDataClient(
            new HttpClient(handler),
            new Uri("https://edsm.test/"),
            new Uri("https://spansh.test/api/"));

        var result = await client.GetAsync("Test System", 42);

        Assert.Equal("Spansh", Assert.Single(result.Providers).Provider);
        Assert.Contains(
            result.Warnings,
            warning => warning.Contains("16 MiB", StringComparison.Ordinal));
    }

    private sealed class ProviderHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        public string SpanshJson { get; init; } = SpanshJsonTemplate;

        public bool OversizeEdsm { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.AbsoluteUri);
            if (request.RequestUri.Host == "spansh.test")
            {
                return Task.FromResult(Response(SpanshJson));
            }

            var response = Response(EdsmJson);
            if (OversizeEdsm)
            {
                response.Content.Headers.ContentLength = 16 * 1024 * 1024 + 1;
            }

            return Task.FromResult(response);
        }

        private static HttpResponseMessage Response(string content)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    content,
                    Encoding.UTF8,
                    "application/json"),
            };
        }

        private const string EdsmJson =
            """
            {
              "id64": 42,
              "name": "Test System",
              "bodyCount": 3,
              "bodies": [
                {
                  "bodyId": 0,
                  "name": "Test System",
                  "type": "Star",
                  "subType": "Main Sequence Star (K)",
                  "radius": 695700
                },
                {
                  "bodyId": 1,
                  "name": "Test System 1",
                  "type": "Planet",
                  "subType": "Metal-rich body",
                  "isLandable": true,
                  "earthMasses": 1.2,
                  "radius": 6000,
                  "gravity": 1.5,
                  "surfacePressure": 0.01,
                  "surfaceTemperature": 180,
                  "semiMajorAxis": 2,
                  "distanceToArrival": 44,
                  "atmosphereType": "Thin Carbon dioxide atmosphere",
                  "atmosphereComposition": { "Carbon dioxide-rich": 99 },
                  "materials": { "Iron": 20 },
                  "parents": [{ "Star": 0 }],
                  "rings": [{
                    "name": "Test System 1 A Ring",
                    "type": "Rocky",
                    "innerRadius": 10,
                    "outerRadius": 20
                  }]
                }
              ]
            }
            """;

        public const string SpanshJsonTemplate =
            """
            {
              "system": {
                "id64": 42,
                "name": "Test System",
                "bodyCount": 3,
                "coords": { "x": 1, "y": 2, "z": 3 },
                "bodies": [
                  {
                    "bodyId": 0,
                    "name": "Test System",
                    "type": "Star",
                    "subType": "Main Sequence Star (K)",
                    "solarRadius": 1
                  },
                  {
                    "bodyId": 1,
                    "name": "Test System 1",
                    "type": "Planet",
                    "subType": "Rocky body",
                    "isLandable": true,
                    "signals": {
                      "updateTime": "2024-01-02T03:04:05Z",
                      "signals": { "$SAA_SignalType_Biological;": 2 },
                      "genuses": ["$Codex_Ent_Aleoids_Genus_Name;"]
                    }
                  }
                ]
              }
            }
            """;
    }
}
