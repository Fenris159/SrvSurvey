using System.Net;
using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed partial class RavenColonialClientTests
{
    [Fact]
    public async Task ArchitectCatalogUsesPublicCommanderMapAndCanonicalArchitectIdentities()
    {
        RavenColonialClient client = Create(
            new StubHandler(request =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal("/root/api/Cmdr/Cmdr%20A%2FB/map/architect", request.RequestUri!.AbsolutePath);
                Assert.False(request.Headers.Contains("rcc-key"));
                return Json(
                    """
                    {"systems":[
                        {"name":"Nickname","cat":[1,2,0],"infos":"{\"architect\":\"cmdr a/b\",\"name\":\"Synuefai A\",\"id64\":42}"},
                        {"name":"synuefai a","cat":[0]},
                        {"name":"Helped","cat":[4],"infos":"{ id64: 99}"},
                        {"name":"Other architect","cat":[0],"infos":"{\"architect\":\"Someone Else\"}"},
                        {"name":"Synuefai B","cat":[0],"infos":"{broken"},
                        {"name":"Synuefai C","cat":[0],"infos":"<b>display only</b>"},
                        {"name":"Synuefai D","cat":[0],"infos":"{\"id64\":-1}"},
                        {"name":"Synuefai E","cat":[0],"infos":"{\"id64\":\"bad\"}"},
                        {"name":"Not architect","cat":["0",null]},
                        {"name":"No category"},
                        {"name":"Bad category","cat":{}},
                        {"name":" ","cat":[0]},
                        null
                    ]}
                    """
                );
            })
        );
        IReadOnlyList<ColonizationArchitectSystem> systems = await client.GetArchitectSystemsAsync(" Cmdr A/B ");
        Assert.Equal(
            ["Synuefai A", "Synuefai B", "Synuefai C", "Synuefai D", "Synuefai E"],
            systems.Select(system => system.Name)
        );
        Assert.Equal(42, systems[0].SystemAddress);
        Assert.All(systems.Skip(1), system => Assert.Equal(0, system.SystemAddress));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"systems\":{}}")]
    public async Task ArchitectCatalogRejectsInvalidMapWithoutReturningAnEmptySuccess(string json)
    {
        RavenColonialClient client = Create(new StubHandler(_ => Json(json)));
        await Assert.ThrowsAsync<InvalidDataException>(() => client.GetArchitectSystemsAsync("Test Cmdr"));
    }

    [Fact]
    public async Task ArchitectCatalogSupportsEmptyResultsAndPropagatesFailures()
    {
        RavenColonialClient empty = Create(new StubHandler(_ => Json("{\"systems\":[]}")));
        Assert.Empty(await empty.GetArchitectSystemsAsync("Test Cmdr"));
        await Assert.ThrowsAsync<ArgumentException>(() => empty.GetArchitectSystemsAsync(" "));
        RavenColonialClient failed = Create(
            new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))
        );
        await Assert.ThrowsAsync<RavenColonialServiceException>(() => failed.GetArchitectSystemsAsync("Test Cmdr"));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            empty.GetArchitectSystemsAsync("Test Cmdr", cancellation.Token)
        );
    }
}
