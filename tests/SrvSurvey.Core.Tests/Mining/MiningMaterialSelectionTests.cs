using SrvSurvey.Core.Mining;

namespace SrvSurvey.Core.Tests.Mining;

public sealed class MiningMaterialSelectionTests
{
    [Fact]
    public void DefaultAndAnyStayExclusiveOfNamedPicks()
    {
        Assert.True(MiningMaterialSelection.IsDefault(["Default"]));
        Assert.True(MiningMaterialSelection.IsAny(["Any"]));
        Assert.False(MiningMaterialSelection.IsDefault(["Any"]));
        Assert.Empty(MiningMaterialSelection.Named(["Default"]));
        Assert.Equal(["Platinum", "Painite"], MiningMaterialSelection.Named(["Platinum", "Painite"]));
        Assert.Equal(6, MiningMaterialSelection.StationLimit(["Default"], acquire: false));
        Assert.Equal(7, MiningMaterialSelection.StationLimit(["Default"], acquire: true));
        Assert.Equal(int.MaxValue, MiningMaterialSelection.StationLimit(["Any"], acquire: false));
        Assert.Equal(int.MaxValue, MiningMaterialSelection.StationLimit(["Platinum"], acquire: true));
    }

    [Fact]
    public void NamedPicksFilterHotspotsAndDefaultKeepsThemAll()
    {
        var hotspots = new Dictionary<string, int> { ["Platinum"] = 2, ["Painite"] = 1 };

        Assert.True(MiningMaterialSelection.IncludesHotspot(hotspots, ["Default"]));
        Assert.True(MiningMaterialSelection.IncludesHotspot(hotspots, ["Any"]));
        Assert.True(MiningMaterialSelection.IncludesHotspot(hotspots, ["Painite"]));
        Assert.False(MiningMaterialSelection.IncludesHotspot(hotspots, ["Alexandrite"]));
        Assert.Equal("Painite ×1", MiningMaterialSelection.HotspotText(hotspots, ["Painite"]));
        Assert.Equal("Platinum ×2, Painite ×1", MiningMaterialSelection.HotspotText(hotspots, ["Any"]));
    }
}
