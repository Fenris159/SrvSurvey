using SrvSurvey.Core.Guardian;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Guardian;

public sealed class GuardianSiteCatalogTests
{
    [Fact]
    public void EmbeddedCatalogLoadsEveryLegacyReferenceSet()
    {
        var catalog = GuardianSiteCatalog.LoadEmbedded();

        Assert.Equal(759, catalog.Count);
        Assert.Equal(
            566,
            catalog.Sites.Count(site => site.Kind == GuardianSiteKind.Ruins));
        Assert.Equal(
            163,
            catalog.Sites.Count(site => site.Kind == GuardianSiteKind.Structure));
        Assert.Equal(
            30,
            catalog.Sites.Count(site => site.Kind == GuardianSiteKind.Beacon));

        var ruin = Assert.Single(
            catalog.Sites,
            site => site.DisplayId == "GR 1");
        Assert.Equal("Synuefe XR-H d11-102", ruin.SystemName);
        Assert.Equal("1 b", ruin.BodyName);
        Assert.Equal("Beta", ruin.SiteType);
        Assert.Equal(100, ruin.SurveyProgress);
        Assert.True(ruin.IsSurveyComplete);
        Assert.Equal(new GalacticCoordinate(357.34375, -49.34375, -74.75), ruin.Position);
        Assert.All(
            catalog.Sites.Where(site => site.Kind == GuardianSiteKind.Beacon),
            beacon => Assert.Equal("GB", beacon.DisplayId));
    }

    [Fact]
    public void SearchMatchesLegacyFieldsAndFiltersSiteTypes()
    {
        var catalog = GuardianSiteCatalog.LoadEmbedded();

        var byAddress = catalog.Search(new GuardianSiteQuery(
            Text: "3515254557027"));
        var onlyGamma = catalog.Search(new GuardianSiteQuery(
            Text: "Synuefe",
            Kinds: new HashSet<GuardianSiteKind> { GuardianSiteKind.Ruins },
            SiteTypes: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Gamma",
            },
            SortBy: GuardianSiteSort.System));

        Assert.Contains(
            byAddress,
            match => match.Site.DisplayId == "GR 1");
        Assert.NotEmpty(onlyGamma);
        Assert.All(
            onlyGamma,
            match =>
            {
                Assert.Equal(GuardianSiteKind.Ruins, match.Site.Kind);
                Assert.Equal("Gamma", match.Site.SiteType);
                Assert.Contains(
                    "Synuefe",
                    match.Site.SystemName,
                    StringComparison.OrdinalIgnoreCase);
            });
    }

    [Fact]
    public void SearchCalculatesAndSortsDistanceFromOrigin()
    {
        var catalog = GuardianSiteCatalog.LoadEmbedded();

        var matches = catalog.Search(new GuardianSiteQuery(
            Kinds: new HashSet<GuardianSiteKind> { GuardianSiteKind.Ruins },
            Origin: new GalacticCoordinate(357.34375, -49.34375, -74.75)));

        Assert.Equal("GR 1", matches[0].Site.DisplayId);
        Assert.Equal(0, matches[0].Distance);
        Assert.True(matches[1].Distance > 0);
    }

    [Fact]
    public void FindBySystemAddressReturnsAllSiteKindsInSystem()
    {
        var catalog = GuardianSiteCatalog.LoadEmbedded();
        var address = catalog.Sites
            .GroupBy(site => site.SystemAddress)
            .First(group => group.Select(site => site.Kind).Distinct().Count() > 1)
            .Key;

        var matches = catalog.FindBySystemAddress(address);

        Assert.True(matches.Count > 1);
        Assert.True(matches.Select(site => site.Kind).Distinct().Count() > 1);
    }

    [Fact]
    public void LoadRejectsMalformedReferenceShape()
    {
        using var invalid = new MemoryStream("{}"u8.ToArray());
        using var emptyStructures = new MemoryStream("[]"u8.ToArray());
        using var emptyBeacons = new MemoryStream("[]"u8.ToArray());

        Assert.Throws<InvalidDataException>(
            () => GuardianSiteCatalog.Load(
                invalid,
                emptyStructures,
                emptyBeacons));
    }
}
