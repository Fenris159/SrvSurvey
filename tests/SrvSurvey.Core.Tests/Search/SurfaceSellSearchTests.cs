using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Search;

public sealed class SurfaceSellSearchTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhitespaceReferenceRemainsPinnedWithoutRedundantLookup(bool referenceQuoted)
    {
        PlanetaryBodyCriteria criteria = PlanetaryMiningPlan.For(["Diamond"])!;
        var provider = new StubMiningSearchProvider
        {
            Markets = _ =>
                (
                    referenceQuoted
                        ?
                        [
                            StubMiningSearchProvider.Quote("Other", "Port", "Diamond", 200),
                            StubMiningSearchProvider.Quote("Sol", "Port", "Diamond", 100),
                        ]
                        : [StubMiningSearchProvider.Quote("Other", "Port", "Diamond", 200)],
                    "Ardent"
                ),
            Imports = (system, _) => [StubMiningSearchProvider.Quote(system, "Port", "Diamond", 100)],
            BodyPages = query => new MiningPlanetaryBodyPage(
                [
                    StubMiningSearchProvider.Body(query.ReferenceSystem, "1", 0, criteria.BodySubtypes[0]) with
                    {
                        VolcanismType = criteria.VolcanismTypes is { Count: > 0 } ? criteria.VolcanismTypes[0] : "",
                    },
                ],
                false
            ),
        };
        var request = new SurfaceSellSearchRequest("  Sol  ", ["Diamond"])
        {
            ForceIncludeReference = true,
            GroupStationsBySystem = true,
            ResultLimit = 1,
        };
        SurfaceSellSearchResult result = await new SurfaceSellSearch(provider).FindAsync(
            request,
            new RecordingProgress<SurfaceSellSearchProgress>()
        );
        Assert.Equal(SurfaceSellSearchResultKind.Found, result.Kind);
        Assert.Equal("Sol", Assert.Single(result.Matches).System);
        Assert.DoesNotContain(provider.Calls, call => call.Contains("  Sol  ", StringComparison.Ordinal));
        if (referenceQuoted)
        {
            Assert.DoesNotContain("imports Sol", provider.Calls);
        }
        else
        {
            Assert.Contains("imports Sol", provider.Calls);
        }
    }
}
