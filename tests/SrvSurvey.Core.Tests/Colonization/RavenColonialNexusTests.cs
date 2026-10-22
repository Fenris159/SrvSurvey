using System.Net;
using System.Text;
using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed partial class RavenColonialClientTests
{
    [Fact]
    public async Task NexusIndexAuthenticatesAndRetainsPublicAndPrivateMemberships()
    {
        RavenColonialClient client = Create(
            new StubHandler(request =>
            {
                Assert.Equal("/root/api/Cmdr/nexus", request.RequestUri!.AbsolutePath);
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal("test-key", Assert.Single(request.Headers.GetValues("rcc-key")));
                Assert.Equal(
                    Convert.ToBase64String(Encoding.UTF8.GetBytes("Cmdr Étoile")),
                    Assert.Single(request.Headers.GetValues("rcc-cmdr0"))
                );
                return Json(
                    """[null,{"id":"b","name":"Beta","owner":"Other","open":false,"destination":"Far Away"},{"id":"a","name":"Alpha","open":true},{"id":"a","name":"Duplicate"},{"id":"","name":"Invalid"},{"id":"c","name":" "}]"""
                );
            })
        );
        IReadOnlyList<ColonizationNexusSummary> entries = await client.GetCommanderNexusesAsync(
            " Cmdr Étoile ",
            " test-key "
        );
        Assert.Equal(["a", "b"], entries.Select(entry => entry.Id));
        Assert.True(entries[0].Open);
        Assert.False(entries[1].Open);
        Assert.Equal("Other", entries[1].Owner);
        Assert.Equal("Far Away", entries[1].Destination);
    }

    [Fact]
    public async Task NexusDetailsReadNestedProjectsAndAssignmentsWithOmittedZeroValues()
    {
        RavenColonialClient client = Create(
            new StubHandler(request =>
            {
                Assert.Equal("/root/api/Chain/test%20route", request.RequestUri!.AbsolutePath);
                Assert.Equal("test-key", Assert.Single(request.Headers.GetValues("rcc-key")));
                Assert.True(request.Headers.Contains("rcc-cmdr0"));
                return Json(
                    """
                    {"id":"test route","name":"Test route","owner":"Cmdr Test","open":true,"notes":"Route notes",
                     "cmdrs":[null,"","Cmdr Other"],"hubs":[12],
                     "fcs":[null,{"marketId":0},{"marketId":42,"name":"ABC-123","cargo":{"steel":50}},{"marketId":42}],
                     "systems":[null,{"id64":"12","name":"Test System","nickname":"Hub","total":"100","needs":{"steel":100},"fcs":[42],"buildTypes":{"outpost":1},
                     "builds":[null,{"buildId":""},{"buildId":"build","buildName":"Test Port","maxNeed":100,"sumNeed":100}]}]}
                    """
                );
            })
        );
        ColonizationNexus nexus = Assert.IsType<ColonizationNexus>(
            await client.GetNexusAsync(" test route ", "Cmdr Test", "test-key")
        );
        Assert.Equal("Cmdr Test", nexus.Owner);
        Assert.Equal("Route notes", nexus.Notes);
        Assert.True(nexus.Open);
        Assert.Equal("Cmdr Other", Assert.Single(nexus.Commanders));
        Assert.Equal(12, Assert.Single(nexus.Hubs));
        Assert.Equal(50, Assert.Single(nexus.FleetCarriers).Cargo["steel"]);
        ColonizationNexusSystem system = Assert.Single(nexus.Systems);
        Assert.Equal(12, system.SystemAddress);
        Assert.Equal("Hub", system.Nickname);
        Assert.Equal(100, system.Total);
        Assert.Null(system.Progress);
        Assert.Equal(100, system.Needs!["steel"]);
        Assert.Equal(42, Assert.Single(system.FleetCarrierIds));
        Assert.Equal(1, system.BuildTypes["outpost"]);
        Assert.Equal("build", Assert.Single(system.Builds).BuildId);
    }

    [Theory]
    [InlineData("{\"id\":\"n\",\"name\":\"Nexus\"}")]
    [InlineData("{\"id\":\"n\",\"name\":\"Nexus\",\"cmdrs\":null,\"fcs\":null,\"hubs\":null,\"systems\":null}")]
    public async Task NexusDetailsNormalizeOptionalCollections(string body)
    {
        RavenColonialClient client = Create(new StubHandler(_ => Json(body)));
        ColonizationNexus nexus = Assert.IsType<ColonizationNexus>(await client.GetNexusAsync("n", "Test", "key"));
        Assert.Empty(nexus.Commanders);
        Assert.Empty(nexus.FleetCarriers);
        Assert.Empty(nexus.Systems);
        Assert.Empty(nexus.Hubs);
        Assert.Null(nexus.Notes);
    }

    [Fact]
    public async Task NexusNormalizesOptionalSystemCollections()
    {
        RavenColonialClient client = Create(
            new StubHandler(_ =>
                Json(
                    """{"id":"n","name":"Nexus","systems":[{"id64":1,"name":"Test","builds":null,"fcs":null,"buildTypes":null}]}"""
                )
            )
        );
        ColonizationNexus nexus = Assert.IsType<ColonizationNexus>(await client.GetNexusAsync("n", "Test", "key"));
        ColonizationNexusSystem system = Assert.Single(nexus.Systems);
        Assert.Empty(system.Builds);
        Assert.Empty(system.FleetCarrierIds);
        Assert.Empty(system.BuildTypes);
        Assert.Null(system.Needs);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"id\":\"wrong\",\"name\":\"Other\"}")]
    [InlineData("{\"id\":\"n\",\"name\":\" \"}")]
    [InlineData("{\"id\":\"n\",\"name\":\"Nexus\",\"systems\":[{\"id64\":0,\"name\":\"Invalid\"}]}")]
    [InlineData("{\"id\":\"n\",\"name\":\"Nexus\",\"systems\":[{\"id64\":1,\"name\":\" \"}]}")]
    public async Task NexusRejectsMalformedOrMismatchedDetails(string body)
    {
        RavenColonialClient client = Create(new StubHandler(_ => Json(body)));
        await Assert.ThrowsAsync<InvalidDataException>(() => client.GetNexusAsync("n", "Test", "key"));
    }

    [Fact]
    public async Task NexusHandlesEmptyIndexMissingDetailsAndAuthenticationFailures()
    {
        RavenColonialClient client = Create(new StubHandler(_ => Json("[]")));
        Assert.Empty(await client.GetCommanderNexusesAsync("Test", "key"));
        RavenColonialClient missing = Create(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        Assert.Null(await missing.GetNexusAsync("n", "Test", "key"));
        RavenColonialClient denied = Create(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)));
        await Assert.ThrowsAsync<RavenColonialServiceException>(() => denied.GetCommanderNexusesAsync("Test", "key"));
        await Assert.ThrowsAsync<RavenColonialServiceException>(() => denied.GetNexusAsync("n", "Test", "key"));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetNexusAsync(" ", "Test", "key"));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetCommanderNexusesAsync("", "key"));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetCommanderNexusesAsync("Test", ""));
        RavenColonialClient malformed = Create(new StubHandler(_ => Json("{}")));
        await Assert.ThrowsAsync<InvalidDataException>(() => malformed.GetCommanderNexusesAsync("Test", "key"));
    }
}
