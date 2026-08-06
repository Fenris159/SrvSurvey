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
        var totalLandingPads = 0;
        var totalSecureDoors = 0;
        var totalNamedPoints = 0;
        var totalDataTerminals = 0;
        var totalConflictZonePoints = 0;
        var totalBuildings = 0;
        var totalBuildingPaths = 0;
        var totalPathPoints = 0;

        foreach (var template in catalog.Templates)
        {
            totalLandingPads += template.LandingPads.Count;
            totalSecureDoors += template.SecureDoors.Count;
            totalNamedPoints += template.NamedPoints.Count;
            totalDataTerminals += template.DataTerminals.Count;
            totalConflictZonePoints += template.ConflictZonePoints.Count;
            totalBuildings += template.Buildings.Count;

            foreach (var building in template.Buildings)
            {
                totalBuildingPaths += building.Paths.Count;

                foreach (var path in building.Paths)
                {
                    totalPathPoints += path.Points.Count;
                }
            }
        }

        Assert.Equal(48, totalLandingPads);
        Assert.Equal(398, totalSecureDoors);
        Assert.Equal(594, totalNamedPoints);
        Assert.Equal(144, totalDataTerminals);
        Assert.Equal(160, totalConflictZonePoints);
        Assert.Equal(128, totalBuildings);
        Assert.Equal(191, totalBuildingPaths);
        Assert.Equal(2_711, totalPathPoints);
    }

    [Fact]
    public void FindsTemplatesByEconomyAndSubtype()
    {
        var catalog = HumanSiteTemplateCatalog.LoadEmbedded();

        var agriculture = catalog.ForEconomy(HumanSiteEconomy.Agriculture);
        var picumnus = catalog.Find(HumanSiteEconomy.Agriculture, 1);

        var subtypes = new List<int>(5);
        foreach (var template in agriculture)
        {
            subtypes.Add(template.SubType);
        }
        Assert.Equal(5, agriculture.Count);
        Assert.Equal([1, 2, 3, 4, 5], subtypes);
        Assert.NotNull(picumnus);
        Assert.Equal("Picumnus", picumnus.Name);
        Assert.Equal(HumanSiteLandingPadSize.Small,
            picumnus.LandingPads[0].Size);
        Assert.Equal(new HumanSiteMapPoint(149.1648, -122.47405),
            picumnus.LandingPads[0].Offset);
        var hasAlarm = false;
        foreach (var point in picumnus.NamedPoints)
        {
            if (point.Name == "Alarm" && point.SecurityLevel == 1)
            {
                hasAlarm = true;
                break;
            }
        }
        Assert.True(hasAlarm);
    }

    [Fact]
    public void RetainsButIdentifiesImplausibleLegacyPoiOffsets()
    {
        var catalog = HumanSiteTemplateCatalog.LoadEmbedded();
        var allPoints = new List<HumanSiteMapPoint>();
        foreach (var template in catalog.Templates)
        {
            foreach (var point in template.NamedPoints)
            {
                allPoints.Add(point.Offset);
            }
        }

        foreach (var point in allPoints)
        {
            Assert.True(point.IsFinite);
        }

        var hasImprobableOffset = false;
        foreach (var point in allPoints)
        {
            if (!point.IsPlausibleMapOffset())
            {
                hasImprobableOffset = true;
                break;
            }
        }
        Assert.True(hasImprobableOffset);
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

        void LoadUnknownEconomy() => LoadTemplateCatalog(unknownEconomy);
        void LoadMismatchedPath() => LoadTemplateCatalog(mismatchedPath);

        Assert.Throws<InvalidDataException>(
            LoadUnknownEconomy);
        Assert.Throws<InvalidDataException>(
            LoadMismatchedPath);
    }

    private static MemoryStream Json(string json)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(json));
    }

    private static void LoadTemplateCatalog(Stream catalogJson)
    {
        _ = HumanSiteTemplateCatalog.Load(catalogJson);
    }
}
