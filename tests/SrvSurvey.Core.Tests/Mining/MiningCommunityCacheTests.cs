using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Mining;

public sealed class MiningCommunityCacheTests
{
    [Fact]
    public void NewestMarketWinsAndUnknownDistanceDoesNotMasqueradeAsNearby()
    {
        var cache = new MiningCommunityCache();
        var now = DateTimeOffset.Parse("2026-09-06T12:00:00Z");
        cache.Apply("""{"$schemaRef":"https://eddn.edcd.io/schemas/commodity/3","message":{"timestamp":"2026-09-06T11:59:00Z","systemName":"Sol","stationName":"Test","marketId":1,"commodities":[{"name":"platinum","sellPrice":200,"buyPrice":250,"demand":10,"stock":20}]}}""", now);
        var query = new MiningMarketQuery("Sol", "Platinum", false);
        Assert.Empty(cache.Markets(query, new GalacticCoordinate(0, 0, 0), now));
        cache.Apply("""{"$schemaRef":"https://eddn.edcd.io/schemas/journal/1","message":{"timestamp":"2026-09-06T11:59:00Z","event":"FSDJump","StarSystem":"Sol","StarPos":[0,0,0],"ControllingPower":"Aisling Duval","PowerplayState":"Exploited"}}""", now);
        Assert.Equal(200, Assert.Single(cache.Markets(query, new GalacticCoordinate(0, 0, 0), now)).Price);
        Assert.Equal(250, Assert.Single(cache.Markets(query with { Buying = true }, new GalacticCoordinate(0, 0, 0), now)).Price);
        Assert.Empty(cache.Markets(query, new GalacticCoordinate(0, 0, 0), now.AddDays(2)));
        Assert.Equal("Aisling Duval", cache.Power("Sol")?.Power);
    }
}
