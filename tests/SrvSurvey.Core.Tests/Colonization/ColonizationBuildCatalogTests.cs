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
        Assert.Equal(24, catalog.Builds.Count(build => build.Location == ColonizationBuildLocation.Orbital));
        Assert.Equal(31, catalog.Builds.Count(build => build.Location == ColonizationBuildLocation.Surface));
        Assert.Equal(
            109,
            catalog.Builds.SelectMany(build => build.Layouts).Distinct(StringComparer.OrdinalIgnoreCase).Count()
        );
    }

    [Fact]
    public void FindsBuildTypesAndPreservesAmbiguousLegacyLayout()
    {
        var catalog = ColonizationBuildCatalog.LoadEmbedded();

        ColonizationBuildCost? coriolis = catalog.FindByBuildType("NO_TRUSS");
        IReadOnlyList<ColonizationBuildCost> tellus = catalog.FindByLayout("Tellus");

        Assert.NotNull(coriolis);
        Assert.Equal("Coriolis Starport", coriolis.DisplayName);
        Assert.Equal(14_076, coriolis.CommodityCosts["steel"]);
        Assert.Equal(3, coriolis.Layouts.Count);
        Assert.Equal(2, tellus.Count);
        Assert.Equal(["tellus", "molae"], tellus.Select(build => build.BuildType));
        Assert.Contains("Vesta", catalog.SiteBuildTypes, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("no_truss", catalog.SiteBuildTypes, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(catalog.SiteBuildTypes, value => value.EndsWith('?'));
        Assert.Equal(
            catalog
                .Builds.SelectMany(build => build.Layouts.Concat([build.BuildType]))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            catalog.SiteBuildTypes.Count
        );
    }

    [Fact]
    public void LocationResultsUseTierThenNameOrdering()
    {
        var catalog = ColonizationBuildCatalog.LoadEmbedded();

        IReadOnlyList<ColonizationBuildCost> orbital = catalog.ForLocation(ColonizationBuildLocation.Orbital);

        Assert.Equal(24, orbital.Count);
        Assert.True(orbital[0].Tier <= orbital[^1].Tier);
        Assert.All(orbital, build => Assert.Equal(ColonizationBuildLocation.Orbital, build.Location));
    }

    [Fact]
    public void ClassifiesOrbitalSiteBuildTypesIncludingJournalGuesses()
    {
        var catalog = ColonizationBuildCatalog.LoadEmbedded();

        Assert.True(catalog.IsOrbitalSiteBuildType("vesta"));
        Assert.True(catalog.IsOrbitalSiteBuildType("Vesta (primary)"));
        Assert.True(catalog.IsOrbitalSiteBuildType("outpost?"));
        Assert.True(catalog.IsOrbitalSiteBuildType("installation?"));
        Assert.True(catalog.IsOrbitalSiteBuildType("no truss"));
        Assert.True(catalog.IsOrbitalSiteBuildType("orbis?"));
        Assert.False(catalog.IsOrbitalSiteBuildType("hestia"));
        Assert.False(catalog.IsOrbitalSiteBuildType("Hestia"));
        Assert.False(catalog.IsOrbitalSiteBuildType("settlement?"));
        Assert.False(catalog.IsOrbitalSiteBuildType("aphrodite"));
        Assert.False(catalog.IsOrbitalSiteBuildType(string.Empty));
        Assert.False(catalog.IsOrbitalSiteBuildType(null));
        Assert.Equal("outpost", ColonizationBuildCatalog.NormalizeSiteBuildTypeKey("outpost?"));
        Assert.Equal("Vesta", ColonizationBuildCatalog.NormalizeSiteBuildTypeKey("Vesta (primary)"));
        Assert.Equal("no_truss", ColonizationBuildCatalog.NormalizeSiteBuildTypeKey("no truss"));
        Assert.True(catalog.TryResolveSiteBuildType("Vesta (primary)", out ColonizationBuildCost? vesta));
        Assert.Equal("vesta", vesta!.BuildType, StringComparer.OrdinalIgnoreCase);
        Assert.False(catalog.TryResolveSiteBuildType("outpost?", out _));
        Assert.False(catalog.TryResolveSiteBuildType("installation?", out _));
    }

    [Fact]
    public void RejectsUnknownLocationAndIncompleteRows()
    {
        using MemoryStream unknownLocation = Json(
            """
            [{"buildType":"x","category":"X","tier":1,"location":"space","displayName":"X","layouts":["x"],"cargo":{"steel":1}}]
            """
        );
        using MemoryStream incomplete = Json(
            """
            [{"buildType":"x","category":"X","tier":1,"location":"orbital","displayName":"X","layouts":[],"cargo":{"steel":1}}]
            """
        );

        Assert.Throws<InvalidDataException>(() => ColonizationBuildCatalog.Load(unknownLocation));
        Assert.Throws<InvalidDataException>(() => ColonizationBuildCatalog.Load(incomplete));
    }

    private static MemoryStream Json(string json)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(json));
    }
}
