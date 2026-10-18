using System.Net;
using System.Text;
using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed partial class RavenColonialClientTests
{
    [Fact]
    public async Task PlanningSystemsUseAuthenticatedCommanderSnapshotEndpoint()
    {
        RavenColonialClient client = Create(
            new StubHandler(request =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal("/root/api/v2/system/snapshots/", request.RequestUri!.AbsolutePath);
                Assert.Equal("secret-key", Assert.Single(request.Headers.GetValues("rcc-key")));
                Assert.Equal(
                    Convert.ToBase64String(Encoding.UTF8.GetBytes("Test Cmdr")),
                    Assert.Single(request.Headers.GetValues("rcc-cmdr0"))
                );
                return Json(
                    """[{"id64":47,"name":"Remote Colony","sites":[{"id":"p","name":"Planned Port","buildType":"asteroid","status":"plan"}]},null,{"id64":0,"name":"Invalid"},{"id64":48,"name":" "},{"id64":49,"name":"Broken","sites":null}]"""
                );
            })
        );
        ColonizationSystemRecord system = Assert.Single(
            await client.GetCommanderPlanningSystemsAsync(" Test Cmdr ", " secret-key ")
        );
        Assert.Equal("Remote Colony", system.Name);
        Assert.Equal(ColonizationSystemSiteStatus.Plan, Assert.Single(system.Sites).Status);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    public async Task PlanningSystemsRejectMalformedSnapshots(string json)
    {
        RavenColonialClient client = Create(new StubHandler(_ => Json(json)));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.GetCommanderPlanningSystemsAsync("Test Cmdr", "test-key")
        );
    }

    [Fact]
    public async Task PlanningSystemsAllowAnEmptyListAndPropagateAuthenticationFailures()
    {
        RavenColonialClient empty = Create(new StubHandler(_ => Json("[]")));
        Assert.Empty(await empty.GetCommanderPlanningSystemsAsync("Test Cmdr", "test-key"));
        RavenColonialClient denied = Create(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)));
        await Assert.ThrowsAsync<RavenColonialServiceException>(() =>
            denied.GetCommanderPlanningSystemsAsync("Test Cmdr", "test-key")
        );
        await Assert.ThrowsAsync<ArgumentException>(() => empty.GetCommanderPlanningSystemsAsync("", "test-key"));
        await Assert.ThrowsAsync<ArgumentException>(() => empty.GetCommanderPlanningSystemsAsync("Test Cmdr", ""));
    }
}
