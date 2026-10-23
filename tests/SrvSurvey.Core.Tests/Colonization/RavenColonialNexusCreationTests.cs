using System.Net;
using System.Text.Json;
using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed partial class RavenColonialClientTests
{
    [Fact]
    public async Task CreatesNexusWithOnlyItsNameAndAuthenticatedCommander()
    {
        RavenColonialClient client = Create(
            new StubHandler(async request =>
            {
                Assert.Equal(HttpMethod.Put, request.Method);
                Assert.Equal("/root/api/Chain/create", request.RequestUri!.AbsolutePath);
                Assert.Equal("test-key", Assert.Single(request.Headers.GetValues("rcc-key")));
                Assert.True(request.Headers.Contains("rcc-cmdr0"));
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                Assert.Equal("New route", body.RootElement.GetProperty("name").GetString());
                Assert.Single(body.RootElement.EnumerateObject());
                return Json("""{"id":"new","name":"New route","owner":"Test","cmdrs":["Test"]}""");
            })
        );
        ColonizationNexus created = await client.CreateNexusAsync(
            " New route ",
            "Test",
            "test-key",
            CancellationToken.None
        );
        Assert.Equal("new", created.Id);
        Assert.Equal("Test", created.Owner);
        Assert.Empty(created.Systems);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.CreateNexusAsync(" ", "Test", "key", CancellationToken.None)
        );
    }

    [Fact]
    public async Task ConfiguresListsInOrderPreservingOwnerAndSendingCorrectJsonTypes()
    {
        var requests = new List<(string Path, string Body)>();
        RavenColonialClient client = Create(
            new StubHandler(async request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("key", Assert.Single(request.Headers.GetValues("rcc-key")));
                if (request.RequestUri!.AbsolutePath.Contains("/FC/", StringComparison.Ordinal))
                {
                    return new HttpResponseMessage(HttpStatusCode.Conflict);
                }
                requests.Add((request.RequestUri.AbsolutePath, await request.Content!.ReadAsStringAsync()));
                return Json("""{"id":"test route","name":"New route","cmdrs":null,"fcs":null,"systems":null}""");
            })
        );
        var draft = new ColonizationNexusDraft(
            "New route",
            "Some notes",
            true,
            [" Beta ", "Alpha", "beta", " "],
            [" Other ", "Test", "other"],
            [42, 43, 42, 0, -1]
        );
        ColonizationNexus saved = await client.ConfigureNexusAsync(
            " test route ",
            draft,
            "Test",
            "key",
            CancellationToken.None
        );
        Assert.Equal(
            ["setPrivate", "setNotes", "setSystems", "setFCs", "setCmdrs"],
            requests.Select(request => request.Path.Split('/')[^1])
        );
        Assert.All(requests, request => Assert.StartsWith("/root/api/Chain/test%20route/", request.Path));
        Assert.Equal("true", requests[0].Body);
        Assert.Equal("\"Some notes\"", requests[1].Body);
        Assert.Equal(
            ["Beta", "Alpha"],
            Assert.IsType<string[]>(JsonSerializer.Deserialize<string[]>(requests[2].Body))
        );
        Assert.Equal(
            ["Test", "Other"],
            Assert.IsType<string[]>(JsonSerializer.Deserialize<string[]>(requests[4].Body))
        );
        Assert.Equal([42L, 43L], Assert.IsType<long[]>(JsonSerializer.Deserialize<long[]>(requests[3].Body)));
        Assert.Empty(saved.Commanders);
        Assert.Empty(saved.FleetCarriers);
        Assert.Empty(saved.Systems);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.ConfigureNexusAsync(" ", draft, "Test", "key", CancellationToken.None)
        );
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            client.ConfigureNexusAsync("id", null!, "Test", "key", CancellationToken.None)
        );
    }

    [Fact]
    public async Task StopsConfigurationAtFailureWithoutRepeatingCreate()
    {
        int requests = 0;
        RavenColonialClient client = Create(
            new StubHandler(_ =>
            {
                requests++;
                return requests == 2
                    ? new HttpResponseMessage(HttpStatusCode.BadRequest)
                    : Json("""{"id":"n","name":"Nexus"}""");
            })
        );
        await Assert.ThrowsAsync<RavenColonialServiceException>(() =>
            client.ConfigureNexusAsync(
                "n",
                new("Nexus", "", false, ["Sol"], [], []),
                "Test",
                "key",
                CancellationToken.None
            )
        );
        Assert.Equal(2, requests);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"id\":\" \",\"name\":\"Nexus\"}")]
    [InlineData("{\"id\":\"id\",\"name\":\" \"}")]
    public async Task RejectsUnusableCreationAcknowledgements(string json)
    {
        RavenColonialClient client = Create(new StubHandler(_ => Json(json)));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.CreateNexusAsync("Nexus", "Test", "key", CancellationToken.None)
        );
    }

    [Fact]
    public async Task RejectsConfigurationAcknowledgementForAnotherNexusAndAuthenticationFailure()
    {
        RavenColonialClient client = Create(new StubHandler(_ => Json("""{"id":"other","name":"Other"}""")));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.ConfigureNexusAsync(
                "n",
                new("Nexus", "", true, ["Sol"], [], []),
                "Test",
                "key",
                CancellationToken.None
            )
        );
        RavenColonialClient denied = Create(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)));
        await Assert.ThrowsAsync<RavenColonialServiceException>(() =>
            denied.CreateNexusAsync("Nexus", "Test", "key", CancellationToken.None)
        );
    }

    [Fact]
    public async Task FindsNamedCarriersWithMarketIdsAndSkipsInvalidOrDuplicateMatches()
    {
        RavenColonialClient client = Create(
            new StubHandler(request =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal("/root/api/FC/query/Test%20Carrier", request.RequestUri!.AbsolutePath);
                Assert.Equal("key", Assert.Single(request.Headers.GetValues("rcc-key")));
                Assert.True(request.Headers.Contains("rcc-cmdr0"));
                return Json(
                    """[null,{"market_id":"42","name":"ABC-123","carrier_name":"Test Carrier"},{"market_id":42,"name":"Duplicate"},{"market_id":43,"name":"XYZ-456"},{"market_id":0,"name":"Invalid"},{"market_id":44,"name":" "}]"""
                );
            })
        );
        IReadOnlyList<ColonizationNexusCarrierMatch> matches = await client.FindNexusCarriersAsync(
            " Test Carrier ",
            "Fenris",
            "key",
            CancellationToken.None
        );
        Assert.Equal(2, matches.Count);
        Assert.Equal("Test Carrier (ABC-123)", matches[0].Label);
        Assert.Equal(42, matches[0].MarketId);
        Assert.Equal("XYZ-456", matches[1].Label);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.FindNexusCarriersAsync(" ", "Fenris", "key", CancellationToken.None)
        );
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task EnsuresSearchedCarriersAreRegisteredBeforeLinkingThem(HttpStatusCode registrationStatus)
    {
        var paths = new List<string>();
        RavenColonialClient client = Create(
            new StubHandler(request =>
            {
                string path = request.RequestUri!.AbsolutePath;
                paths.Add(path);
                return path.EndsWith("/spansh", StringComparison.Ordinal)
                    ? new HttpResponseMessage(registrationStatus)
                    : Json("""{"id":"n","name":"Nexus"}""");
            })
        );
        var draft = new ColonizationNexusDraft("Nexus", "", true, ["Sol"], [], [42]);
        if (registrationStatus == HttpStatusCode.BadRequest)
        {
            await Assert.ThrowsAsync<RavenColonialServiceException>(() =>
                client.ConfigureNexusAsync("n", draft, "Fenris", "key", CancellationToken.None)
            );
            Assert.DoesNotContain(paths, path => path.EndsWith("/setFCs", StringComparison.Ordinal));
        }
        else
        {
            await client.ConfigureNexusAsync("n", draft, "Fenris", "key", CancellationToken.None);
            Assert.EndsWith("/setFCs", paths[^2]);
            Assert.EndsWith("/setCmdrs", paths[^1]);
        }
        Assert.Contains("/root/api/FC/42/spansh", paths);
    }
}
