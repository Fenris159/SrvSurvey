using System.Net;
using System.Text;
using System.Text.Json;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed class ColonizationBodyCatalogTests
{
    [Fact]
    public void OrdinaryScanWithoutANameCannotInventABarycentreIdentity()
    {
        var catalog = new ColonizationBodyJournalCatalog(42, "Example");
        catalog.Apply([Event("{\"event\":\"Scan\",\"SystemAddress\":42,\"BodyID\":1,\"PlanetClass\":\"Rocky body\"}")]);
        Assert.Throws<InvalidDataException>(() => catalog.CreateBodies());
    }

    [Fact]
    public void CompleteHonkCanUseTheStoredLocalFssFlagWithoutARepeatedCompletionEvent()
    {
        var catalog = new ColonizationBodyJournalCatalog(42, "Example");
        catalog.Apply([
            Event("{\"event\":\"Scan\",\"SystemAddress\":42,\"BodyID\":0,\"BodyName\":\"Example\",\"StarType\":\"G\"}"),
            Event("{\"event\":\"FSSDiscoveryScan\",\"SystemAddress\":42,\"BodyCount\":1,\"Progress\":1}"),
        ]);
        Assert.False(catalog.CanUpload);
        catalog.Apply([], knownLocalFssComplete: true);
        Assert.True(catalog.CanUpload);
    }

    [Fact]
    public void AsteroidAndRingScansCountForCompletenessButAreNotUploadedAsPlanets()
    {
        var catalog = new ColonizationBodyJournalCatalog(42, "Example");
        catalog.Apply([
            Event("{\"event\":\"Scan\",\"SystemAddress\":42,\"BodyID\":0,\"BodyName\":\"Example\",\"StarType\":\"G\"}"),
            Event(
                "{\"event\":\"Scan\",\"SystemAddress\":42,\"BodyID\":1,\"BodyName\":\"Example 1\",\"PlanetClass\":\"Rocky body\"}"
            ),
            Event("{\"event\":\"Scan\",\"SystemAddress\":42,\"BodyID\":2,\"BodyName\":\"Example A Belt Cluster 1\"}"),
            Event(
                "{\"event\":\"Scan\",\"SystemAddress\":42,\"BodyID\":3,\"BodyName\":\"Example 1 A Ring\",\"PlanetClass\":\"Icy body\"}"
            ),
            Event("{\"event\":\"FSSAllBodiesFound\",\"SystemAddress\":42,\"Count\":4}"),
        ]);
        Assert.True(catalog.CanUpload);
        Assert.Equal(4, catalog.ScannedBodyCount);
        Assert.Equal([0, 1], catalog.CreateBodies().Select(body => body.Number));
    }

    [Fact]
    public void MalformedBeltsAreAReportedSourceFailure()
    {
        using var spansh = JsonDocument.Parse(
            "{\"system\":{\"id64\":42,\"name\":\"Example\",\"bodies\":[{\"bodyId\":0,\"name\":\"Example\",\"type\":\"Star\",\"belts\":[null]}]}}"
        );
        using var edsm = JsonDocument.Parse("{\"id64\":42,\"name\":\"Example\",\"bodies\":[]}");
        Assert.Throws<InvalidDataException>(() =>
            ColonizationBodyCatalog.FromProviders(spansh.RootElement, edsm.RootElement, 42, "Example")
        );
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"system\":null}")]
    [InlineData("{\"system\":{\"id64\":null,\"name\":\"Example\",\"bodies\":[]}}")]
    [InlineData("{\"system\":{\"id64\":43,\"name\":\"Example\",\"bodies\":[]}}")]
    [InlineData("{\"system\":{\"id64\":42,\"name\":\"Example\",\"bodies\":[null]}}")]
    [InlineData(
        "{\"system\":{\"id64\":42,\"name\":\"Example\",\"bodies\":[{\"name\":\"Example\",\"type\":\"Star\"}]}}"
    )]
    public void InvalidSourceCatalogsCannotProduceAReplacement(string spanshJson)
    {
        using var spansh = JsonDocument.Parse(spanshJson);
        using var edsm = JsonDocument.Parse("{\"id64\":42,\"name\":\"Example\",\"bodies\":[]}");
        Assert.Throws<InvalidDataException>(() =>
            ColonizationBodyCatalog.FromProviders(spansh.RootElement, edsm.RootElement, 42, "Example")
        );
    }

    [Fact]
    public void EmptyCatalogCannotReplaceRavenBodies()
    {
        Assert.Throws<InvalidDataException>(() => ColonizationBodyCatalog.Merge([]));
    }

    [Fact]
    public void LocalPayloadIncludesBarycentresStarBeltsAndObservedSignals()
    {
        var catalog = new ColonizationBodyJournalCatalog(42, "Example");
        catalog.Apply([
            Event(
                "{\"event\":\"Scan\",\"SystemAddress\":42,\"BodyID\":0,\"BodyName\":\"Example\",\"StarType\":\"G\",\"Rings\":[{\"Name\":\"Example A Belt\",\"RingClass\":\"eRingClass_Rocky\",\"InnerRad\":299792458}]}"
            ),
            Event("{\"event\":\"ScanBaryCentre\",\"SystemAddress\":42,\"BodyID\":2}"),
            Event(
                "{\"event\":\"Scan\",\"SystemAddress\":42,\"BodyID\":1,\"BodyName\":\"Example 1\",\"PlanetClass\":\"Earthlike body\",\"Parents\":[{\"Null\":0}],\"Landable\":true,\"TidalLock\":true,\"TerraformState\":\"Terraformable\",\"Atmosphere\":\"thin argon atmosphere\",\"Volcanism\":\"water geysers\",\"SurfaceTemperature\":250}"
            ),
            Event(
                "{\"event\":\"FSSBodySignals\",\"SystemAddress\":42,\"BodyID\":1,\"Signals\":[{\"Type\":\"$SAA_SignalType_Biological;\",\"Count\":1},{\"Type\":\"$SAA_SignalType_Geological;\",\"Count\":2}]}"
            ),
        ]);
        IReadOnlyList<ColonizationSystemBody> bodies = catalog.CreateBodies();
        Assert.Equal([0, 100000, 1, 2], bodies.Select(body => body.Number));
        Assert.Equal(1, bodies[1].DistanceLightSeconds);
        Assert.Equal("Rocky", bodies[1].Subtype);
        Assert.Equal("Example barycentre 2", bodies[3].Name);
        Assert.Equal("bc", bodies[3].Type);
        Assert.Equal("elw", bodies[2].Type);
        Assert.Equal(
            ["atmosphere", "bio", "geo", "landable", "terraformable", "tidal", "volcanism"],
            bodies[2].Features.Order(StringComparer.Ordinal)
        );
    }

    private const string SystemBodies = """
        {"id64":42,"name":"Example","bodies":[
          {"bodyId":0,"name":"Example","type":"Star","subType":"G (White-Yellow) Star","solarRadius":1,
           "belts":[{"name":"Example A Belt","type":"Rocky","innerRadius":299792458}]},
          {"bodyId":1,"name":"Example 1","type":"Planet","subType":"High metal content world","parents":[{"Star":0}],
           "radius":6000,"gravity":1,"surfaceTemperature":250,"isLandable":true,"rotationalPeriodTidallyLocked":true,
           "terraformingState":"Candidate for terraforming","atmosphereType":"Carbon dioxide","volcanismType":"Silicate magma",
           "rings":[{}],"signals":{"signals":{"$SAA_SignalType_Biological;":2,"$SAA_SignalType_Geological;":1}}},
          {"bodyId":2,"name":"Example barycentre 2","type":"Barycentre"}
        ]}
        """;

    [Fact]
    public void ProvidersUnifyJournalIdsUnitsBeltsAndComplementaryFields()
    {
        using var spansh = JsonDocument.Parse("{\"system\":" + SystemBodies + "}");
        using var edsm = JsonDocument.Parse(
            """
            {"id64":42,"name":"Example","bodies":[
              {"id":99999,"bodyId":null,"name":"Example","type":"Star","subType":"G (White-Yellow) Star","solarRadius":1,
               "belts":[{"name":"Example A Belt","type":"Rocky","innerRadius":299792.458}]},
              {"bodyId":3,"name":"Example 2","type":"Planet","subType":"Water world","radius":5000,"gravity":0.8}
            ]}
            """
        );

        IReadOnlyList<ColonizationSystemBody> recovered = ColonizationBodyCatalog.FromProviders(
            spansh.RootElement,
            edsm.RootElement,
            42,
            "Example"
        );

        Assert.Equal([0, 100000, 1, 2, 3], recovered.Select(body => body.Number));
        Assert.Equal(695700000, recovered[0].Radius);
        Assert.Equal(1, recovered[1].DistanceLightSeconds, 10);
        ColonizationSystemBody planet = recovered.Single(body => body.Number == 1);
        Assert.Equal(6000000, planet.Radius);
        Assert.Equal(1, planet.Gravity);
        Assert.Equal([0], planet.Parents);
        Assert.Equal(
            ["atmosphere", "bio", "geo", "landable", "rings", "terraformable", "tidal", "volcanism"],
            planet.Features.Order(StringComparer.Ordinal)
        );
        Assert.Equal("bc", recovered.Single(body => body.Number == 2).Type);
        Assert.Equal("ww", recovered.Single(body => body.Number == 3).Type);
    }

    [Theory]
    [InlineData("Earthlike world", "elw")]
    [InlineData("Ammonia world", "aw")]
    [InlineData("Metal-rich body", "mrb")]
    [InlineData("Rocky body", "rb")]
    [InlineData("Rocky ice body", "ri")]
    [InlineData("Icy body", "ib")]
    [InlineData("Water giant", "wg")]
    [InlineData("Class I gas giant", "gg")]
    public void PlanetClassesUseRavenTypes(string subtype, string expected)
    {
        string json = JsonSerializer.Serialize(
            new
            {
                id64 = 42,
                name = "Example",
                bodies = new[]
                {
                    new
                    {
                        bodyId = 1,
                        name = "Example 1",
                        type = "Planet",
                        subType = subtype,
                    },
                },
            }
        );
        using var spansh = JsonDocument.Parse("{\"system\":" + json + "}");
        using var edsm = JsonDocument.Parse(json);
        Assert.Equal(
            expected,
            Assert
                .Single(ColonizationBodyCatalog.FromProviders(spansh.RootElement, edsm.RootElement, 42, "Example"))
                .Type
        );
    }

    [Fact]
    public void MergePreservesKnownFieldsButDoesNotRetainAbsentBodies()
    {
        using var extension = JsonDocument.Parse("{\"custom\":true}");
        ColonizationSystemBody known = new()
        {
            Number = 1,
            Name = "Example 1",
            Type = "hmc",
            Radius = 6000000,
            Features = ["bio"],
            ExtensionData = new() { ["custom"] = extension.RootElement.GetProperty("custom").Clone() },
        };
        ColonizationSystemBody recovered = new()
        {
            Number = 1,
            Name = "Example 1",
            Type = "hmc",
            Radius = 5000000,
            Gravity = 1,
            Features = ["geo"],
        };
        ColonizationSystemBody absent = new()
        {
            Number = 2,
            Name = "Example 2",
            Type = "rb",
        };

        ColonizationSystemBody result = Assert.Single(ColonizationBodyCatalog.Merge([recovered], [known, absent]));

        Assert.Equal(6000000, result.Radius);
        Assert.Equal(1, result.Gravity);
        Assert.Equal(["bio", "geo"], result.Features.Order(StringComparer.Ordinal));
        Assert.True(result.ExtensionData["custom"].GetBoolean());
    }

    [Theory]
    [InlineData("{\"name\":\"Other\",\"num\":1,\"type\":\"rb\"}")]
    [InlineData("{\"name\":\"Example 1\",\"num\":2,\"type\":\"rb\"}")]
    public void ConflictingBodyIdentitiesCannotBePublished(string conflicting)
    {
        ColonizationSystemBody first = new()
        {
            Number = 1,
            Name = "Example 1",
            Type = "hmc",
        };
        ColonizationSystemBody second = JsonSerializer.Deserialize<ColonizationSystemBody>(conflicting)!;
        Assert.Throws<InvalidDataException>(() => ColonizationBodyCatalog.Merge([first, second]));
    }

    [Fact]
    public void LocalCompletionAndSpecificEventsGateUploads()
    {
        var catalog = new ColonizationBodyJournalCatalog(42, "Example");
        Assert.False(catalog.Apply([Event("{\"event\":\"Scan\",\"SystemAddress\":43,\"BodyID\":0}")]));
        Assert.True(
            catalog.Apply([
                Event("{\"event\":\"FSSDiscoveryScan\",\"SystemAddress\":42,\"BodyCount\":2,\"Progress\":1}"),
            ])
        );
        Assert.False(catalog.CanUpload);
        Assert.True(catalog.Apply([Event("{\"event\":\"FSSAllBodiesFound\",\"SystemAddress\":42,\"Count\":2}")]));
        Assert.False(catalog.CanUpload);
        Assert.False(
            catalog.Apply([
                Event(
                    "{\"event\":\"Scan\",\"SystemAddress\":42,\"BodyID\":0,\"BodyName\":\"Example\",\"StarType\":\"G\",\"Radius\":695700000}"
                ),
                Event(
                    "{\"event\":\"Scan\",\"SystemAddress\":42,\"BodyID\":1,\"BodyName\":\"Example 1\",\"PlanetClass\":\"Rocky body\",\"SurfaceGravity\":9.80665,\"Radius\":1000000}"
                ),
            ])
        );
        Assert.Equal(2, catalog.ScannedBodyCount);
        Assert.True(catalog.CanUpload);
        Assert.True(catalog.Apply([Event("{\"event\":\"NavBeaconScan\",\"SystemAddress\":42}")]));
        Assert.Equal(1, catalog.CreateBodies().Single(body => body.Number == 1).Gravity);
    }

    [Theory]
    [InlineData("H", "bh")]
    [InlineData("N", "ns")]
    [InlineData("DAB", "wd")]
    public void LocalStellarTypesRemainDistinct(string starType, string expected)
    {
        var catalog = new ColonizationBodyJournalCatalog(42, "Example");
        catalog.Apply([
            Event(
                JsonSerializer.Serialize(
                    new
                    {
                        @event = "Scan",
                        SystemAddress = 42,
                        BodyID = 0,
                        BodyName = "Example",
                        StarType = starType,
                    }
                )
            ),
        ]);
        Assert.Equal(expected, Assert.Single(catalog.CreateBodies()).Type);
    }

    [Fact]
    public async Task RecoveryUsesBothId64SourcesAndStopsOnProviderFailure()
    {
        var requests = new List<string>();
        using var http = new HttpClient(
            new Handler(request =>
            {
                requests.Add(request.RequestUri!.AbsoluteUri);
                return request.RequestUri.Host == "spansh.co.uk"
                    ? Json("{\"system\":" + SystemBodies + "}")
                    : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            })
        );
        var client = new ColonizationBodyRecoveryClient(http);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.RecoverAsync(42, "Example", []));

        Assert.Equal(
            ["https://spansh.co.uk/api/dump/42", "https://www.edsm.net/api-system-v1/bodies?systemId64=42"],
            requests.Order(StringComparer.Ordinal)
        );
    }

    [Fact]
    public async Task RavenReceivesFullBodyArrayOnTheId64Route()
    {
        string? submitted = null;
        using var http = new HttpClient(
            new Handler(request =>
            {
                Assert.Equal(HttpMethod.Put, request.Method);
                Assert.Equal("https://raven.test/api/v2/system/42/bodies", request.RequestUri!.AbsoluteUri);
                submitted = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return Json(submitted);
            })
        );
        var client = new RavenColonialClient(http, new Uri("https://raven.test/"));

        IReadOnlyList<ColonizationSystemBody> result = await client.UpdateSystemBodiesAsync(
            42,
            [
                new()
                {
                    Number = 0,
                    Name = "Example",
                    Type = "st",
                    Radius = 695700000,
                },
            ]
        );

        Assert.Equal(695700000, Assert.Single(result).Radius);
        using var body = JsonDocument.Parse(submitted!);
        Assert.Equal(JsonValueKind.Array, body.RootElement.ValueKind);
        Assert.Equal(0, body.RootElement[0].GetProperty("num").GetInt32());
    }

    private static JournalEventEnvelope Event(string json)
    {
        Assert.True(JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? value, out _));
        return value!;
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(reply(request));
    }
}
