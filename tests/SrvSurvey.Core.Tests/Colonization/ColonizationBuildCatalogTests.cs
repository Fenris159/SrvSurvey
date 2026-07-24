using System.Text;
using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed class ColonizationBuildCatalogTests
{
    [Fact]
    public void EmbeddedCatalogPreservesEveryLegacyBuildDefinition()
    {
        var catalog = ColonizationBuildCatalog.LoadEmbedded();

        Assert.Equal(55, catalog.Count);
        Assert.Equal(
            24,
            catalog.Builds.Count(build =>
                build.Location == ColonizationBuildLocation.Orbital));
        Assert.Equal(
            31,
            catalog.Builds.Count(build =>
                build.Location == ColonizationBuildLocation.Surface));
        Assert.Equal(
            109,
            catalog.Builds
                .SelectMany(build => build.Layouts)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());
    }

    [Fact]
    public void FindsBuildTypesAndPreservesAmbiguousLegacyLayout()
    {
        var catalog = ColonizationBuildCatalog.LoadEmbedded();

        var coriolis = catalog.FindByBuildType("NO_TRUSS");
        var tellus = catalog.FindByLayout("Tellus");

        Assert.NotNull(coriolis);
        Assert.Equal("Coriolis Starport", coriolis.DisplayName);
        Assert.Equal(14_076, coriolis.CommodityCosts["steel"]);
        Assert.Equal(3, coriolis.Layouts.Count);
        Assert.Equal(2, tellus.Count);
        Assert.Equal(["tellus", "molae"],
            tellus.Select(build => build.BuildType));
    }

    [Fact]
    public void LocationResultsUseTierThenNameOrdering()
    {
        var catalog = ColonizationBuildCatalog.LoadEmbedded();

        var orbital = catalog.ForLocation(
            ColonizationBuildLocation.Orbital);

        Assert.Equal(24, orbital.Count);
        Assert.True(orbital[0].Tier <= orbital[^1].Tier);
        Assert.All(
            orbital,
            build => Assert.Equal(
                ColonizationBuildLocation.Orbital,
                build.Location));
    }

    [Fact]
    public void RejectsUnknownLocationAndIncompleteRows()
    {
        using var unknownLocation = Json(
            """
            [{"buildType":"x","category":"X","tier":1,"location":"space","displayName":"X","layouts":["x"],"cargo":{"steel":1}}]
            """);
        using var incomplete = Json(
            """
            [{"buildType":"x","category":"X","tier":1,"location":"orbital","displayName":"X","layouts":[],"cargo":{"steel":1}}]
            """);

        Assert.Throws<InvalidDataException>(
            () => ColonizationBuildCatalog.Load(unknownLocation));
        Assert.Throws<InvalidDataException>(
            () => ColonizationBuildCatalog.Load(incomplete));
    }

    private static MemoryStream Json(string json)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(json));
    }
}
