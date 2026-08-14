using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Search;

public sealed class BoxelPlanetClassifierTests
{
    public static TheoryData<string, BoxelPlanetClass> JournalPlanetClasses { get; } = new()
    {
        { "Metal rich body", BoxelPlanetClass.MetalRich },
        { "High metal content body", BoxelPlanetClass.HighMetalContent },
        { "Rocky body", BoxelPlanetClass.Rocky },
        { "Icy body", BoxelPlanetClass.Icy },
        { "Rocky ice body", BoxelPlanetClass.RockyIce },
        { "Earthlike body", BoxelPlanetClass.Earthlike },
        { "Water world", BoxelPlanetClass.WaterWorld },
        { "Ammonia world", BoxelPlanetClass.AmmoniaWorld },
        { "Water giant", BoxelPlanetClass.WaterGiant },
        { "Water giant with life", BoxelPlanetClass.WaterGiantWithLife },
        { "Gas giant with water based life", BoxelPlanetClass.GasGiantWaterLife },
        { "Gas giant with ammonia based life", BoxelPlanetClass.GasGiantAmmoniaLife },
        { "Sudarsky class I gas giant", BoxelPlanetClass.SudarskyI },
        { "Sudarsky class II gas giant", BoxelPlanetClass.SudarskyII },
        { "Sudarsky class III gas giant", BoxelPlanetClass.SudarskyIII },
        { "Sudarsky class IV gas giant", BoxelPlanetClass.SudarskyIV },
        { "Sudarsky class V gas giant", BoxelPlanetClass.SudarskyV },
        { "Helium rich gas giant", BoxelPlanetClass.HeliumRichGasGiant },
        { "Helium gas giant", BoxelPlanetClass.HeliumGasGiant },
    };

    [Theory]
    [MemberData(nameof(JournalPlanetClasses))]
    public void MapsAllNineteenJournalPlanetClasses(
        string planetClass,
        BoxelPlanetClass expected)
    {
        Assert.True(BoxelPlanetClassifier.TryFromPlanetClass(planetClass, out var classified));
        Assert.Equal(expected, classified);
        Assert.Equal(planetClass, BoxelPlanetClassifier.ToPlanetClassString(classified));
    }

    [Fact]
    public void EarthPrefixMapsToEarthlike()
    {
        Assert.True(BoxelPlanetClassifier.TryFromPlanetClass("Earth-like", out var classified));
        Assert.Equal(BoxelPlanetClass.Earthlike, classified);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("K")]
    [InlineData("A_BlueWhiteSuperGiant")]
    [InlineData("Barycentre")]
    [InlineData("Unknown rocky body")]
    public void RejectsEmptyStarsAndUnknownClasses(string? planetClass)
    {
        Assert.False(BoxelPlanetClassifier.TryFromPlanetClass(planetClass, out var classified));
        Assert.Equal(BoxelPlanetClass.Unknown, classified);
    }

    [Theory]
    [InlineData("Terraformable", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("Terraformed", false)]
    [InlineData("terraformable", false)]
    public void TerraformableRequiresExactJournalToken(string? terraformState, bool expected)
    {
        Assert.Equal(expected, BoxelPlanetClassifier.IsTerraformable(terraformState));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("None", false)]
    [InlineData("none", false)]
    [InlineData("Nitrogen", true)]
    public void AtmosphereIgnoresNoneAndEmpty(string? atmosphereType, bool expected)
    {
        Assert.Equal(expected, BoxelPlanetClassifier.HasAtmosphere(atmosphereType));
    }

    [Theory]
    [InlineData(true, "Nitrogen", true)]
    [InlineData(true, "None", false)]
    [InlineData(false, "Nitrogen", false)]
    [InlineData(false, null, false)]
    public void AtmosphericLandableRequiresLandableAndAtmosphere(
        bool isLandable,
        string? atmosphereType,
        bool expected)
    {
        Assert.Equal(
            expected,
            BoxelPlanetClassifier.IsAtmosphericLandable(isLandable, atmosphereType));
    }

    [Fact]
    public void HeliumPercentUsesCaseInsensitiveNameAndIgnoresZero()
    {
        Assert.True(BoxelPlanetClassifier.TryGetHeliumPercent(
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["helium"] = 28.5,
            },
            out var percent));
        Assert.Equal(28.5, percent);

        Assert.False(BoxelPlanetClassifier.TryGetHeliumPercent(
            new Dictionary<string, double> { ["Helium"] = 0 },
            out _));
        Assert.False(BoxelPlanetClassifier.TryGetHeliumPercent(
            new Dictionary<string, double> { ["Hydrogen"] = 80 },
            out _));
        Assert.False(BoxelPlanetClassifier.TryGetHeliumPercent(
            new Dictionary<string, double>(),
            out _));
    }

    [Theory]
    [InlineData(BoxelPlanetClass.WaterWorld, true, false)]
    [InlineData(BoxelPlanetClass.HighMetalContent, true, true)]
    [InlineData(BoxelPlanetClass.MetalRich, true, true)]
    [InlineData(BoxelPlanetClass.Rocky, true, true)]
    [InlineData(BoxelPlanetClass.Icy, false, true)]
    [InlineData(BoxelPlanetClass.RockyIce, false, true)]
    [InlineData(BoxelPlanetClass.Earthlike, false, false)]
    [InlineData(BoxelPlanetClass.AmmoniaWorld, false, false)]
    [InlineData(BoxelPlanetClass.HeliumGasGiant, false, false)]
    public void ExtraColumnsMatchDisplaySlices(
        BoxelPlanetClass classified,
        bool terraformableColumn,
        bool landableColumns)
    {
        Assert.Equal(
            terraformableColumn,
            BoxelPlanetClassifier.ShowsTerraformableColumn(classified));
        Assert.Equal(
            landableColumns,
            BoxelPlanetClassifier.ShowsLandableColumns(classified));
    }
}
