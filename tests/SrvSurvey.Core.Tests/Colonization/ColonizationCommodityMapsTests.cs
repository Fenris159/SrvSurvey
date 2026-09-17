using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed class ColonizationCommodityMapsTests
{
    [Fact]
    public void NormalizeNeedMapMergesKeysAndClampsNegatives()
    {
        Dictionary<string, int> normalized = ColonizationCommodityMaps.NormalizeNeedMap(
            new Dictionary<string, int>
            {
                ["$Steel_name;"] = 10,
                ["steel"] = 5,
                ["aluminium"] = -3,
                [" "] = 9,
            }
        );

        Assert.Equal(15, normalized["steel"]);
        Assert.Equal(0, normalized["aluminium"]);
        Assert.False(normalized.ContainsKey(" "));
    }

    [Fact]
    public void PhantomZeroPatchMapOnlyClearsNegativeSlots()
    {
        Dictionary<string, int> zeroes = ColonizationCommodityMaps.PhantomZeroPatchMap(
            new Dictionary<string, int>
            {
                ["steel"] = 75,
                ["$Titanium_name;"] = -1,
                ["aluminium"] = -4,
            }
        );

        Assert.Equal(2, zeroes.Count);
        Assert.Equal(0, zeroes["titanium"]);
        Assert.Equal(0, zeroes["aluminium"]);
        Assert.False(zeroes.ContainsKey("steel"));
    }

    [Fact]
    public void CreateDepotUpdateSignatureIsStableAcrossKeyOrder()
    {
        string left = ColonizationCommodityMaps.CreateDepotUpdateSignature(
            "build-1",
            100,
            new Dictionary<string, int> { ["steel"] = 75, ["titanium"] = 10 },
            includeDepot: true,
            depotFailed: false
        );
        string right = ColonizationCommodityMaps.CreateDepotUpdateSignature(
            "build-1",
            100,
            new Dictionary<string, int> { ["titanium"] = 10, ["steel"] = 75 },
            includeDepot: true,
            depotFailed: false
        );

        Assert.Equal(left, right);
        Assert.NotEqual(
            left,
            ColonizationCommodityMaps.CreateDepotUpdateSignature(
                "build-1",
                100,
                new Dictionary<string, int> { ["steel"] = 74, ["titanium"] = 10 },
                includeDepot: true,
                depotFailed: false
            )
        );
    }
}
