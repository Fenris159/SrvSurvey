using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed partial class RavenColonialClientTests
{
    [Fact]
    public async Task SystemListUpdateUsesAuthenticatedOrderedNamesAndNeverPostsAnEmptyList()
    {
        int requests = 0;
        RavenColonialClient client = Create(
            new StubHandler(async request =>
            {
                requests++;
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("/root/api/Chain/my%20id/setSystems", request.RequestUri!.AbsolutePath);
                Assert.Equal("key", Assert.Single(request.Headers.GetValues("rcc-key")));
                Assert.Equal("[\"Beta\",\"Alpha\"]", await request.Content!.ReadAsStringAsync());
                return Json("""{"id":"my id","name":"Nexus","notes":"Preserved"}""");
            })
        );
        ColonizationNexus result = await client.SetNexusSystemsAsync(
            " my id ",
            [" Beta ", "Alpha", "beta", " "],
            "Test",
            "key",
            CancellationToken.None
        );
        Assert.Equal("Preserved", result.Notes);
        Assert.Equal(1, requests);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.SetNexusSystemsAsync("id", [], "Test", "key", CancellationToken.None)
        );
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.SetNexusSystemsAsync("id", [" "], "Test", "key", CancellationToken.None)
        );
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.SetNexusSystemsAsync(" ", ["Sol"], "Test", "key", CancellationToken.None)
        );
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            client.SetNexusSystemsAsync("id", null!, "Test", "key", CancellationToken.None)
        );
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.ConfigureNexusAsync("id", new("Nexus", "", false, [], [], []), "Test", "key", CancellationToken.None)
        );
        Assert.Equal(1, requests);
    }
}
