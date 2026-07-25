using System.Security.Cryptography;
using SrvSurvey.Core.Settlements;

namespace SrvSurvey.Core.Tests.Settlements;

public sealed class HumanSiteTemplateCatalogExporterTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-template-export-{Guid.NewGuid():N}");

    [Fact]
    public async Task ExportRoundTripsEveryTemplateAndAuthoredElement()
    {
        var source = HumanSiteTemplateCatalog.LoadEmbedded();
        var template = source.Templates[0];
        var session = new HumanSiteTemplateAuthoringSession(template);
        session.AddNamedPoint(
            "QA Point",
            new HumanSiteMapPoint(1.25, -2.5),
            securityLevel: 2,
            floor: 3);
        session.AddCircle(new HumanSiteMapPoint(5, 6), radius: 7);
        session.CommitBuilding("QA Building");
        var updated = source.WithTemplate(session.Template);
        var path = Path.Combine(directory, "humanSiteTemplates.json");

        var result = await new HumanSiteTemplateCatalogExporter()
            .ExportAsync(updated, path);

        await using var stream = File.OpenRead(path);
        var reloaded = HumanSiteTemplateCatalog.Load(stream);
        Assert.Equal(source.Count, reloaded.Count);
        var match = reloaded.Find(template.Economy, template.SubType)!;
        Assert.Equal("QA Point", match.NamedPoints[^1].Name);
        Assert.Equal("QA Building", match.Buildings[^1].Name);
        Assert.Null(result.BackupPath);
        Assert.Equal(result.Sha256, Convert.ToHexString(
            SHA256.HashData(await File.ReadAllBytesAsync(path))));
    }

    [Fact]
    public async Task ExistingDestinationGetsByteIdenticalBackup()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "humanSiteTemplates.json");
        var original = new byte[] { 0, 1, 2, 3, 255 };
        await File.WriteAllBytesAsync(path, original);

        var catalog = HumanSiteTemplateCatalog.LoadEmbedded();
        var result = await new HumanSiteTemplateCatalogExporter()
            .ExportAsync(catalog, path);

        Assert.NotNull(result.BackupPath);
        Assert.Equal(original, await File.ReadAllBytesAsync(
            result.BackupPath!));
        await using var stream = File.OpenRead(path);
        Assert.Equal(catalog.Count, HumanSiteTemplateCatalog.Load(stream).Count);
    }

    [Fact]
    public async Task ConcurrentDestinationChangeIsNeverOverwritten()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "humanSiteTemplates.json");
        await File.WriteAllTextAsync(path, "original");
        var exporter = new HumanSiteTemplateCatalogExporter(
            target => File.WriteAllTextAsync(target, "newer"));

        var exception = await Assert.ThrowsAsync<IOException>(() =>
            exporter.ExportAsync(
                HumanSiteTemplateCatalog.LoadEmbedded(),
                path));

        Assert.Contains("changed during export", exception.Message);
        Assert.Equal("newer", await File.ReadAllTextAsync(path));
        var backup = Assert.Single(Directory.GetFiles(
            directory,
            "humanSiteTemplates.json.backup-*"));
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
}
