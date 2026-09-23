using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Mining;

public sealed class MiningCommodityNameTests
{
    [Fact]
    public void EverySearchCommodityRoundTripsThroughTheSharedIdentity()
    {
        IEnumerable<string> names = PlanetaryMiningPlan.EdpmCommodityNames.Concat(PlanetaryMiningPlan.Materials);
        foreach (string name in names)
        {
            Assert.Equal(name, MiningCommodityName.Canonical(MiningCommodityName.Normalize(name)));
            Assert.True(MiningCommodityName.Same(name, MiningCommodityName.Normalize(name)));
        }
    }

    [Theory]
    [InlineData("periclasedunite", "Periclase Dunite", "PER")]
    [InlineData("methanolmonohydratecrystals", "Methanol Monohydrate Crystals", "MNL")]
    [InlineData("lowtemperaturediamond", "Low Temperature Diamonds", "LTD")]
    [InlineData("helium3", "Helium-3", "HE3")]
    [InlineData("opal", "Void Opal", "VOP")]
    public void ProviderNamesGetTheCatalogNameAndShortCode(string provider, string catalog, string code)
    {
        Assert.Equal(catalog, MiningCommodityName.Canonical(provider));
        Assert.Equal(code, MiningCommodityCode.Abbreviate(provider));
    }

    [Fact]
    public void DiamondIsDistinctFromLowTemperatureDiamonds()
    {
        Assert.False(MiningCommodityName.Same("Diamond", "Low Temperature Diamonds"));
        Assert.Equal("Diamond", MiningCommodityName.Canonical("diamond"));
        Assert.Equal("Low Temperature Diamonds", MiningCommodityName.Canonical("lowtemperaturediamond"));
    }

    [Fact]
    public void SurfaceMaterialTagsHaveUniqueShortCodes()
    {
        string[] codes = PlanetaryMiningPlan.Materials.Select(MiningCommodityCode.Abbreviate).ToArray();
        Assert.Equal(codes.Length, codes.Distinct(StringComparer.Ordinal).Count());
    }
}
