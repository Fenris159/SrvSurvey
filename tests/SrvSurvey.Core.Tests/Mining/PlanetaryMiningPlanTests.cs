using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Mining;

public sealed class PlanetaryMiningPlanTests
{
    [Fact]
    public void SurfaceExclusiveMaterialsLeaveSharedRingMineralsAlone()
    {
        Assert.True(PlanetaryMiningPlan.IsSurfaceExclusive("Diamond"));
        Assert.True(PlanetaryMiningPlan.IsSurfaceExclusive("Iridium"));
        Assert.True(PlanetaryMiningPlan.IsSurfaceExclusive("Periclase Dunite"));
        Assert.True(PlanetaryMiningPlan.IsSurfaceExclusive("periclasedunite"));
        Assert.False(PlanetaryMiningPlan.IsEdpmCommodity("periclasedunite"));
        Assert.False(PlanetaryMiningPlan.IsEdpmCommodity("diamond"));
        Assert.True(PlanetaryMiningPlan.IsEdpmCommodity("Monazite"));
        Assert.True(PlanetaryMiningPlan.IsEdpmCommodity("lowtemperaturediamond"));
        Assert.True(PlanetaryMiningPlan.IsSurfaceExclusive("Helium-3"));
        Assert.False(PlanetaryMiningPlan.IsSurfaceExclusive("Low Temperature Diamonds"));
        Assert.False(PlanetaryMiningPlan.IsSurfaceExclusive("Monazite"));
        Assert.False(PlanetaryMiningPlan.IsSurfaceExclusive("Musgravite"));
        Assert.False(PlanetaryMiningPlan.IsSurfaceExclusive("Thorium"));
        Assert.False(PlanetaryMiningPlan.IsSurfaceExclusive("Jadeite"));
    }

    [Fact]
    public void DiamondUsesLandableVolcanicBodyTypesAndMagmaLandmarks()
    {
        PlanetaryBodyCriteria criteria = PlanetaryMiningPlan.For(["Diamond"])!;

        Assert.Equal(
            ["Metal-rich body", "High metal content world", "Rocky body", "Rocky Ice world"],
            criteria.BodySubtypes
        );
        Assert.Equal(["Iron Magma Lava Spout", "Silicate Magma Lava Spout"], criteria.LandmarkSubtypes);
    }

    [Fact]
    public void OpenGroundDropsTheLandmarkFilterEvenWhenAnotherMaterialNeedsGeology()
    {
        PlanetaryBodyCriteria platinum = PlanetaryMiningPlan.For(["Platinum"])!;
        Assert.Empty(platinum.LandmarkSubtypes);
        Assert.Contains("Metal-rich body", platinum.BodySubtypes);
        Assert.Contains("High metal content world", platinum.BodySubtypes);

        PlanetaryBodyCriteria mixed = PlanetaryMiningPlan.For(["Platinum", "Diamond"])!;
        Assert.Empty(mixed.LandmarkSubtypes);
        Assert.Contains("Rocky Ice world", mixed.BodySubtypes);
    }

    [Fact]
    public void HeliumUsesIceGeysersSpanshActuallyIndexes()
    {
        PlanetaryBodyCriteria helium = PlanetaryMiningPlan.For(["Helium"])!;

        Assert.Contains("Carbon Dioxide Ice Geyser", helium.LandmarkSubtypes);
        Assert.Contains("Ammonia Ice Geyser", helium.LandmarkSubtypes);
        Assert.Contains("Methane Ice Geyser", helium.LandmarkSubtypes);
        Assert.Contains("Nitrogen Ice Geyser", helium.LandmarkSubtypes);
        Assert.Contains("Silicate Vapour Gas Vent", helium.LandmarkSubtypes);
        Assert.DoesNotContain(
            helium.LandmarkSubtypes,
            name => name.Contains("Helium", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public void ChosenPowerSearchesEveryOtherControllingPower()
    {
        IReadOnlyList<string> others = PlanetaryMiningPlan.OtherPowers("Aisling Duval");

        Assert.Equal(11, others.Count);
        Assert.DoesNotContain("Aisling Duval", others);
        Assert.Contains("A. Lavigny-Duval", others);
        Assert.Contains("Archon Delaine", others);

        IReadOnlyList<string> withoutArissa = PlanetaryMiningPlan.OtherPowers("Arissa Lavigny-Duval");
        Assert.DoesNotContain("A. Lavigny-Duval", withoutArissa);
        Assert.DoesNotContain("Arissa Lavigny-Duval", withoutArissa);
        Assert.Contains("Aisling Duval", withoutArissa);
    }

    [Fact]
    public void UnknownMaterialDoesNotInventABodyFilter()
    {
        Assert.Null(PlanetaryMiningPlan.For(["Void Opal"]));
    }
}
