using System.Net;
using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed partial class RavenColonialClientTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PrivacyToggleSendsOnlyTheRequestedBooleanAndPreservesOtherFields(bool isPrivate)
    {
        int requests = 0;
        RavenColonialClient client = Create(
            new StubHandler(async request =>
            {
                requests++;
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("/root/api/Chain/my%20id/setPrivate", request.RequestUri!.AbsolutePath);
                Assert.Equal("key", Assert.Single(request.Headers.GetValues("rcc-key")));
                Assert.Equal(isPrivate ? "true" : "false", await request.Content!.ReadAsStringAsync());
                return Json("""{"id":"my id","name":"Nexus","notes":"Keep me"}""");
            })
        );
        ColonizationNexus saved = await client.SetNexusPrivacyAsync(
            " my id ",
            isPrivate,
            "Fenris",
            "key",
            CancellationToken.None
        );
        Assert.Equal("Keep me", saved.Notes);
        Assert.Equal(1, requests);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.SetNexusPrivacyAsync(" ", isPrivate, "Fenris", "key", CancellationToken.None)
        );
    }

    [Fact]
    public async Task SavesMembershipLastAndPreservesOwnerWithoutReaddingRemovedCaller()
    {
        var operations = new List<string>();
        RavenColonialClient client = Create(
            new StubHandler(async request =>
            {
                string operation = request.RequestUri!.AbsolutePath.Split('/')[^1];
                operations.Add(operation);
                if (operation == "setCmdrs")
                {
                    Assert.Equal("[\"Owner\",\"Guest\"]", await request.Content!.ReadAsStringAsync());
                }
                return Json("""{"id":"n","name":"Nexus"}""");
            })
        );
        var draft = new ColonizationNexusDraft("Nexus", "", true, ["Sol"], ["Guest"], []) { Owner = "Owner" };
        await client.ConfigureNexusAsync("n", draft, "Removed caller", "key", CancellationToken.None);
        Assert.Equal(["setPrivate", "setNotes", "setSystems", "setFCs", "setCmdrs"], operations);
    }

    [Fact]
    public async Task RenamesExistingNexusWithRawJsonStringAndValidatesAcknowledgement()
    {
        RavenColonialClient client = Create(
            new StubHandler(async request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("/root/api/Chain/my%20id/setName", request.RequestUri!.AbsolutePath);
                Assert.Equal("key", Assert.Single(request.Headers.GetValues("rcc-key")));
                Assert.Equal("\"Renamed\"", await request.Content!.ReadAsStringAsync());
                return Json("""{"id":"my id","name":"Renamed"}""");
            })
        );
        Assert.Equal(
            "Renamed",
            (await client.RenameNexusAsync(" my id ", " Renamed ", "Fenris", "key", CancellationToken.None)).Name
        );
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.RenameNexusAsync(" ", "Name", "Fenris", "key", CancellationToken.None)
        );
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.RenameNexusAsync("id", " ", "Fenris", "key", CancellationToken.None)
        );
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task DeletesWithAuthenticationNoBodyAndAcceptsRavensSuccessStatuses(HttpStatusCode status)
    {
        RavenColonialClient client = Create(
            new StubHandler(request =>
            {
                Assert.Equal(HttpMethod.Delete, request.Method);
                Assert.Equal("/root/api/Chain/delete/my%20id", request.RequestUri!.AbsolutePath);
                Assert.Null(request.Content);
                Assert.Equal("key", Assert.Single(request.Headers.GetValues("rcc-key")));
                return new HttpResponseMessage(status);
            })
        );
        if (status is HttpStatusCode.OK or HttpStatusCode.NoContent)
        {
            await client.DeleteNexusAsync(" my id ", "Fenris", "key", CancellationToken.None);
        }
        else
        {
            await Assert.ThrowsAsync<RavenColonialServiceException>(() =>
                client.DeleteNexusAsync("my id", "Fenris", "key", CancellationToken.None)
            );
        }
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.DeleteNexusAsync(" ", "Fenris", "key", CancellationToken.None)
        );
    }
}
