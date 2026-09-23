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
    public void DiamondUsesLandableBodyTypesAndBothMagmaFamilies()
    {
        PlanetaryBodyCriteria criteria = PlanetaryMiningPlan.For(["Diamond"])!;

        Assert.Equal(
            ["Metal-rich body", "High metal content world", "Rocky body", "Rocky Ice world"],
            criteria.BodySubtypes
        );
        Assert.Empty(criteria.LandmarkSubtypes);
        Assert.Equal(6, criteria.VolcanismTypes?.Count);
        Assert.Contains("Major Metallic Magma", criteria.VolcanismTypes!);
        Assert.Contains("Minor Rocky Magma", criteria.VolcanismTypes!);
    }

    [Fact]
    public void MonaziteUsesRockyBodiesWithIronOrSilicateMagma()
    {
        PlanetaryBodyCriteria criteria = PlanetaryMiningPlan.For(["Monazite"])!;

        Assert.Equal(["Rocky body"], criteria.BodySubtypes);
        Assert.Empty(criteria.LandmarkSubtypes);
        Assert.Equal(6, criteria.VolcanismTypes?.Count);
    }

    [Theory]
    [InlineData("White Dwarf (DA) Star")]
    [InlineData("White Dwarf (DB) Star")]
    [InlineData("White Dwarf (DC) Star")]
    [InlineData("White Dwarf (DX) Star")]
    public void PericlaseRequiresMetallicMagmaAndAnyWhiteDwarfSubtype(string starSubtype)
    {
        PlanetaryBodyCriteria periclase = PlanetaryMiningPlan.For(["Periclase Dunite"])!;

        Assert.Equal(["Rocky body"], periclase.BodySubtypes);
        Assert.Empty(periclase.LandmarkSubtypes);
        Assert.Equal(["Metallic Magma", "Minor Metallic Magma", "Major Metallic Magma"], periclase.VolcanismTypes);
        Assert.True(periclase.RequiresWhiteDwarfHost);
        Assert.True(PlanetaryMiningPlan.IsWhiteDwarf(new MiningBodyParent(1, "Star", starSubtype)));
        Assert.False(PlanetaryMiningPlan.IsWhiteDwarf(new MiningBodyParent(2, "Star", "K (Yellow-Orange) Star")));
        MiningPlanetaryBody body = new("Example", "Example B 1", "Rocky body", "", 0.1, 100);
        Assert.True(PlanetaryMiningPlan.Matches(periclase, body with { VolcanismType = "Minor Metallic Magma" }));
        Assert.False(
            PlanetaryMiningPlan.Matches(
                periclase,
                body with
                {
                    Landmarks = ["Iron Magma Lava Spout"],
                    VolcanismType = "Rocky Magma",
                }
            )
        );
    }

    [Fact]
    public void MaterialMatchesRequireBodyTypeAndGeologyWhenTheCatalogCallsForIt()
    {
        PlanetaryBodyCriteria monazite = PlanetaryMiningPlan.For(["Monazite"])!;
        PlanetaryBodyCriteria platinum = PlanetaryMiningPlan.For(["Platinum"])!;
        var rockyWithMagma = new MiningPlanetaryBody(
            "Alpha",
            "Alpha 1",
            "Rocky body",
            "",
            0.3,
            100,
            VolcanismType: "Minor Metallic Magma"
        );

        Assert.True(PlanetaryMiningPlan.Matches(monazite, rockyWithMagma));
        Assert.False(PlanetaryMiningPlan.Matches(monazite, rockyWithMagma with { VolcanismType = "" }));
        Assert.False(PlanetaryMiningPlan.Matches(platinum, rockyWithMagma));
        Assert.True(
            PlanetaryMiningPlan.Matches(platinum, rockyWithMagma with { Subtype = "High metal content world" })
        );
    }

    [Fact]
    public void OpenGroundDropsTheVolcanismFilterEvenWhenAnotherMaterialNeedsGeology()
    {
        PlanetaryBodyCriteria platinum = PlanetaryMiningPlan.For(["Platinum"])!;
        Assert.Empty(platinum.LandmarkSubtypes);
        Assert.Contains("Metal-rich body", platinum.BodySubtypes);
        Assert.Contains("High metal content world", platinum.BodySubtypes);

        PlanetaryBodyCriteria mixed = PlanetaryMiningPlan.For(["Platinum", "Diamond"])!;
        Assert.Empty(mixed.LandmarkSubtypes);
        Assert.Empty(mixed.VolcanismTypes!);
        Assert.Contains("Rocky Ice world", mixed.BodySubtypes);
    }

    [Fact]
    public void HeliumLeavesVolcanismUnfilteredBecauseSpanshHasNoHeliumGeyserType()
    {
        PlanetaryBodyCriteria helium = PlanetaryMiningPlan.For(["Helium"])!;

        Assert.Empty(helium.LandmarkSubtypes);
        Assert.Empty(helium.VolcanismTypes!);
    }

    [Fact]
    public void JadeiteUsesTheSilicateVapourBodyVolcanismFamily()
    {
        PlanetaryBodyCriteria jadeite = PlanetaryMiningPlan.For(["Jadeite"])!;

        Assert.Equal(
            ["Silicate Vapour Geysers", "Minor Silicate Vapour Geysers", "Major Silicate Vapour Geysers"],
            jadeite.VolcanismTypes
        );
    }

    [Theory]
    [InlineData("Sapphire", 3)]
    [InlineData("Ruby", 3)]
    [InlineData("Osmium", 3)]
    [InlineData("Alexandrite", 6)]
    [InlineData("Serendibite", 6)]
    [InlineData("Bastnasite", 6)]
    [InlineData("Quartz Pyroxenite", 3)]
    [InlineData("Olivine", 6)]
    public void EveryOtherGeologyMaterialUsesBodyVolcanismWithoutSiteLandmarks(string material, int familySize)
    {
        PlanetaryBodyCriteria criteria = PlanetaryMiningPlan.For([material])!;

        Assert.Empty(criteria.LandmarkSubtypes);
        Assert.Equal(familySize, criteria.VolcanismTypes?.Count);
        Assert.Contains("Metallic Magma", criteria.VolcanismTypes!);
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
