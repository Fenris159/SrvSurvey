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
        int totalLandingPads = 0;
        int totalSecureDoors = 0;
        int totalNamedPoints = 0;
        int totalDataTerminals = 0;
        int totalConflictZonePoints = 0;
        int totalBuildings = 0;
        int totalBuildingPaths = 0;
        int totalPathPoints = 0;

        foreach (HumanSiteTemplate template in catalog.Templates)
        {
            totalLandingPads += template.LandingPads.Count;
            totalSecureDoors += template.SecureDoors.Count;
            totalNamedPoints += template.NamedPoints.Count;
            totalDataTerminals += template.DataTerminals.Count;
            totalConflictZonePoints += template.ConflictZonePoints.Count;
            totalBuildings += template.Buildings.Count;

            foreach (HumanSiteBuilding building in template.Buildings)
            {
                totalBuildingPaths += building.Paths.Count;

                foreach (HumanSiteBuildingPath path in building.Paths)
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

        IReadOnlyList<HumanSiteTemplate> agriculture = catalog.ForEconomy(HumanSiteEconomy.Agriculture);
        HumanSiteTemplate? picumnus = catalog.Find(HumanSiteEconomy.Agriculture, 1);

        var subtypes = new List<int>(5);
        foreach (HumanSiteTemplate template in agriculture)
        {
            subtypes.Add(template.SubType);
        }
        Assert.Equal(5, agriculture.Count);
        Assert.Equal([1, 2, 3, 4, 5], subtypes);
        Assert.NotNull(picumnus);
        Assert.Equal("Picumnus", picumnus.Name);
        Assert.Equal(HumanSiteLandingPadSize.Small, picumnus.LandingPads[0].Size);
        Assert.Equal(new HumanSiteMapPoint(149.1648, -122.47405), picumnus.LandingPads[0].Offset);
        bool hasAlarm = false;
        foreach (HumanSiteNamedPointOfInterest point in picumnus.NamedPoints)
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
        foreach (HumanSiteTemplate template in catalog.Templates)
        {
            foreach (HumanSiteNamedPointOfInterest point in template.NamedPoints)
            {
                allPoints.Add(point.Offset);
            }
        }

        foreach (HumanSiteMapPoint point in allPoints)
        {
            Assert.True(point.IsFinite);
        }

        bool hasImprobableOffset = false;
        foreach (HumanSiteMapPoint point in allPoints)
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
        using MemoryStream unknownEconomy = Json(
            """
            [{"economy":"Mystery","subType":1,"name":"X","landingPads":[{"size":"Small","offset":{"X":0,"Y":0}}],"buildings":[{"name":"HAB","paths":[{"PathPoints":[{"X":0,"Y":0}],"PathTypes":"AA==","FillMode":0}]}]}]
            """
        );
        using MemoryStream mismatchedPath = Json(
            """
            [{"economy":"Agriculture","subType":1,"name":"X","landingPads":[{"size":"Small","offset":{"X":0,"Y":0}}],"buildings":[{"name":"HAB","paths":[{"PathPoints":[{"X":0,"Y":0},{"X":1,"Y":1}],"PathTypes":"AA==","FillMode":0}]}]}]
            """
        );

        Assert.Throws<InvalidDataException>(() => LoadTemplateCatalog(unknownEconomy));
        Assert.Throws<InvalidDataException>(() => LoadTemplateCatalog(mismatchedPath));
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
