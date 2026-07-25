using System.Security.Cryptography;
using SrvSurvey.Core.Guardian;

namespace SrvSurvey.Core.Tests.Guardian;

public sealed class GuardianSiteTemplateAuthoringTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-guardian-template-{Guid.NewGuid():N}");

    [Fact]
    public void DraftEditsDoNotMutateSourceTemplate()
    {
        var source = CreateTemplate();
        var session = new GuardianSiteTemplateAuthoringSession(source);

        session.UpdateMetadata(
            "Edited",
            "edited.png",
            new GuardianMapPoint(12, 34),
            1.5);
        session.AddPoint(new GuardianPointOfInterest(
            "p2",
            GuardianPoiType.Orb,
            45,
            20,
            0));
        session.AddPoint(new GuardianPointOfInterest(
            "d1",
            GuardianPoiType.DestructiblePanel,
            90,
            30,
            10));
        session.UpdatePoint(
            "p1",
            new GuardianPointOfInterest(
                "p1",
                GuardianPoiType.Tablet,
                180,
                40,
                0));
        session.SetObeliskGroupLabel("B", new GuardianMapPoint(90, 50));
        session.RemoveObeliskGroupLabel("A");

        Assert.Equal("Original", source.Name);
        Assert.Single(source.PointsOfInterest);
        Assert.Empty(source.DestructiblePanels);
        Assert.Contains("A", source.ObeliskGroupNameLocations.Keys);
        Assert.Equal("Edited", session.Template.Name);
        Assert.Equal("edited.png", session.Template.BackgroundImage);
        Assert.Equal(new GuardianMapPoint(12, 34), session.Template.ImageOffset);
        Assert.Equal(1.5, session.Template.ScaleFactor);
        Assert.Equal(2, session.Template.PointsOfInterest.Count);
        Assert.Equal(
            GuardianPoiType.Tablet,
            session.Template.PointsOfInterest.Single(point => point.Name == "p1").Type);
        Assert.Single(session.Template.DestructiblePanels);
        Assert.DoesNotContain("A", session.Template.ObeliskGroupNameLocations.Keys);
        Assert.Equal(
            new GuardianMapPoint(90, 50),
            session.Template.ObeliskGroupNameLocations["B"]);
    }

    [Fact]
    public async Task ExportRoundTripsEditedCatalogAndBacksUpDestination()
    {
        var catalog = GuardianSiteTemplateCatalog.LoadEmbedded();
        var source = catalog.Find("Beta")!;
        var session = new GuardianSiteTemplateAuthoringSession(source);
        session.AddPoint(new GuardianPointOfInterest(
            "qa1",
            GuardianPoiType.Orb,
            12.5,
            45.5,
            0));
        session.SetObeliskGroupLabel("QA", new GuardianMapPoint(30, 60));
        var updated = catalog.WithTemplate(session.Template);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "guardianSiteTemplates.json");
        var original = new byte[] { 0, 1, 2, 3, 255 };
        await File.WriteAllBytesAsync(path, original);

        var result = await new GuardianSiteTemplateCatalogExporter()
            .ExportAsync(updated, path);

        Assert.NotNull(result.BackupPath);
        Assert.Equal(original, await File.ReadAllBytesAsync(result.BackupPath!));
        Assert.Equal(
            result.Sha256,
            Convert.ToHexString(SHA256.HashData(
                await File.ReadAllBytesAsync(path))));
        await using var stream = File.OpenRead(path);
        var roundTrip = GuardianSiteTemplateCatalog.Load(stream);
        Assert.Equal(updated.Count, roundTrip.Count);
        var beta = roundTrip.Find("Beta")!;
        Assert.Contains(beta.PointsOfInterest, point => point.Name == "qa1");
        Assert.Equal(new GuardianMapPoint(30, 60), beta.ObeliskGroupNameLocations["QA"]);
    }

    [Fact]
    public async Task ConcurrentDestinationChangeIsNeverOverwritten()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "guardianSiteTemplates.json");
        await File.WriteAllTextAsync(path, "original");
        var exporter = new GuardianSiteTemplateCatalogExporter(
            target => File.WriteAllTextAsync(target, "newer"));

        var exception = await Assert.ThrowsAsync<IOException>(() =>
            exporter.ExportAsync(
                GuardianSiteTemplateCatalog.LoadEmbedded(),
                path));

        Assert.Contains("changed during export", exception.Message);
        Assert.Equal("newer", await File.ReadAllTextAsync(path));
        var backup = Assert.Single(Directory.GetFiles(
            directory,
            "guardianSiteTemplates.json.backup-*"));
        Assert.Equal("original", await File.ReadAllTextAsync(backup));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static GuardianSiteTemplate CreateTemplate()
    {
        return new GuardianSiteTemplate(
            "Test",
            "Original",
            string.Empty,
            new GuardianMapPoint(0, 0),
            1,
            [
                new GuardianPointOfInterest(
                    "p1",
                    GuardianPoiType.Orb,
                    0,
                    10,
                    0),
            ],
            [],
            new Dictionary<string, GuardianMapPoint>
            {
                ["A"] = new GuardianMapPoint(0, 20),
            });
    }
}
