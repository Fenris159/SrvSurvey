using System.Text;
using SrvSurvey.Core.Settlements;

namespace SrvSurvey.Core.Tests.Settlements;

public sealed class HumanSiteTemplateCatalogTests
{
    [Fact]
    public void EmbeddedCatalogPreservesEveryLegacyTemplateElement()
    {
        var catalog = HumanSiteTemplateCatalog.LoadEmbedded();

        Assert.Equal(28, catalog.Count);
        Assert.Equal(48, catalog.Templates.Sum(
            template => template.LandingPads.Count));
        Assert.Equal(398, catalog.Templates.Sum(
            template => template.SecureDoors.Count));
        Assert.Equal(594, catalog.Templates.Sum(
            template => template.NamedPoints.Count));
        Assert.Equal(144, catalog.Templates.Sum(
            template => template.DataTerminals.Count));
        Assert.Equal(160, catalog.Templates.Sum(
            template => template.ConflictZonePoints.Count));
        Assert.Equal(128, catalog.Templates.Sum(
            template => template.Buildings.Count));
        Assert.Equal(191, catalog.Templates.Sum((HumanSiteTemplate template) =>
            template.Buildings.Sum((HumanSiteBuilding building) =>
                building.Paths.Count)));
        Assert.Equal(2_711, catalog.Templates.Sum((HumanSiteTemplate template) =>
            template.Buildings.Sum((HumanSiteBuilding building) =>
                building.Paths.Sum((HumanSiteBuildingPath path) =>
                    path.Points.Count))));
    }

    [Fact]
    public void FindsTemplatesByEconomyAndSubtype()
    {
        var catalog = HumanSiteTemplateCatalog.LoadEmbedded();

        var agriculture = catalog.ForEconomy(HumanSiteEconomy.Agriculture);
        var picumnus = catalog.Find(HumanSiteEconomy.Agriculture, 1);

        Assert.Equal(5, agriculture.Count);
        Assert.Equal([1, 2, 3, 4, 5],
            agriculture.Select(template => template.SubType));
        Assert.NotNull(picumnus);
        Assert.Equal("Picumnus", picumnus.Name);
        Assert.Equal(HumanSiteLandingPadSize.Small,
            picumnus.LandingPads[0].Size);
        Assert.Equal(new HumanSiteMapPoint(149.1648, -122.47405),
            picumnus.LandingPads[0].Offset);
        Assert.Contains(picumnus.NamedPoints,
            point => point.Name == "Alarm" && point.SecurityLevel == 1);
    }

    [Fact]
    public void RetainsButIdentifiesImplausibleLegacyPoiOffsets()
    {
        var catalog = HumanSiteTemplateCatalog.LoadEmbedded();
        var allPoints = catalog.Templates.SelectMany(template =>
            template.NamedPoints.Select(point => point.Offset));

        Assert.All(allPoints, point => Assert.True(point.IsFinite));
        Assert.Contains(allPoints,
            point => !point.IsPlausibleMapOffset());
    }

    [Fact]
    public void RejectsUnknownEconomyAndMismatchedBuildingPaths()
    {
        using var unknownEconomy = Json(
            """
            [{"economy":"Mystery","subType":1,"name":"X","landingPads":[{"size":"Small","offset":{"X":0,"Y":0}}],"buildings":[{"name":"HAB","paths":[{"PathPoints":[{"X":0,"Y":0}],"PathTypes":"AA==","FillMode":0}]}]}]
            """);
        using var mismatchedPath = Json(
            """
            [{"economy":"Agriculture","subType":1,"name":"X","landingPads":[{"size":"Small","offset":{"X":0,"Y":0}}],"buildings":[{"name":"HAB","paths":[{"PathPoints":[{"X":0,"Y":0},{"X":1,"Y":1}],"PathTypes":"AA==","FillMode":0}]}]}]
            """);

        Assert.Throws<InvalidDataException>(
            () => HumanSiteTemplateCatalog.Load(unknownEconomy));
        Assert.Throws<InvalidDataException>(
            () => HumanSiteTemplateCatalog.Load(mismatchedPath));
    }

    private static MemoryStream Json(string json)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(json));
    }
}
