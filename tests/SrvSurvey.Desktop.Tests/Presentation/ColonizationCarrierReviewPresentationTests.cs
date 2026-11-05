using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Desktop.Tests.Presentation;

public sealed partial class ColonizationShoppingPresentationTests
{
    /// <summary>The architect fixture route cannot shadow the structured architect-system catalog response.</summary>
    [Fact]
    public async Task CarrierReviewArchitectFixtureKeepsTheSystemCatalogAvailable()
    {
        using var http = new HttpClient(new RavenHandler { SystemArchitect = "Test Cmdr" });
        var client = new RavenColonialClient(http);
        Assert.Empty(await client.GetArchitectSystemsAsync("Test Cmdr"));
        Assert.Equal("Test Cmdr", await client.GetSystemArchitectAsync("Sol"));
    }
}
