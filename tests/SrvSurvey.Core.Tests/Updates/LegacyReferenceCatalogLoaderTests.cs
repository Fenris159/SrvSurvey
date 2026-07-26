using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Updates;

namespace SrvSurvey.Core.Tests.Updates;

public sealed class LegacyReferenceCatalogLoaderTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void LoadUsesEveryCompleteLegacyPublishedCatalog()
    {
        WriteCompleteLegacyReferenceLayout();

        var result = LegacyReferenceCatalogLoader.Load(root);

        Assert.Equal(7, result.LocalCatalogCount);
        Assert.Empty(result.Warnings);
        Assert.All(result.Sources, source => Assert.True(source.IsLocal));
        Assert.Equal(
            ExobiologyReferenceCatalog.LoadEmbedded().Count,
            result.Exobiology.Count);
        Assert.True(result.BiologyCriteria.Roots.Count > 0);
        Assert.True(result.GuardianSites.Count > 0);
        Assert.True(result.GuardianPublishedSites.Count > 0);
        Assert.True(result.GuardianTemplates.Count > 0);
        Assert.True(result.HumanSiteTemplates.Count > 0);
        Assert.True(result.GreenGasGiants.TemperatureCount > 0);
    }

    [Fact]
    public void LoadRejectsTruncatedButValidLegacyCatalog()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(
            Path.Combine(root, "codexRef.json"),
            """
            {
              "one": {
                "entryid": "1234567",
                "name": "$Codex_Ent_Test_Name;",
                "hud_category": "Biology",
                "platform": "odyssey"
              }
            }
            """);

        var result = LegacyReferenceCatalogLoader.Load(root);

        Assert.Equal(0, result.LocalCatalogCount);
        var warning = Assert.Single(result.Warnings);
        Assert.Contains("coverage", warning);
        Assert.Contains("Embedded reference data remains active", warning);
        Assert.Equal(
            ExobiologyReferenceCatalog.LoadEmbedded().Count,
            result.Exobiology.Count);
    }

    [Fact]
    public void LoadRejectsMalformedArchiveWithoutChangingIt()
    {
        var published = Path.Combine(root, "pub");
        Directory.CreateDirectory(published);
        var archivePath = Path.Combine(published, "guardian.zip");
        var original = new byte[] { 1, 2, 3, 4, 5 };
        File.WriteAllBytes(archivePath, original);

        var result = LegacyReferenceCatalogLoader.Load(root);

        Assert.Contains(
            result.Warnings,
            warning => warning.Contains(
                "Guardian published surveys",
                StringComparison.Ordinal));
        Assert.Equal(original, File.ReadAllBytes(archivePath));
        Assert.True(result.GuardianPublishedSites.Count > 0);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private void WriteCompleteLegacyReferenceLayout()
    {
        var published = Path.Combine(root, "pub");
        var criteria = Path.Combine(published, "bio-criteria");
        var settlements = Path.Combine(published, "settlements");
        Directory.CreateDirectory(criteria);
        Directory.CreateDirectory(settlements);

        CopyResource(
            "SrvSurvey.Core.Resources.codexRef.json",
            Path.Combine(root, "codexRef.json"));
        CopyResource(
            "SrvSurvey.Core.Resources.allRuins.json",
            Path.Combine(published, "allRuins.json"));
        CopyResource(
            "SrvSurvey.Core.Resources.allStructures.json",
            Path.Combine(published, "allStructures.json"));
        CopyResource(
            "SrvSurvey.Core.Resources.guardian.zip",
            Path.Combine(published, "guardian.zip"));
        CopyResource(
            "SrvSurvey.Core.Resources.guardianSiteTemplates.json",
            Path.Combine(published, "guardianSiteTemplates.json"));
        CopyResource(
            "SrvSurvey.Core.Resources.humanSiteTemplates.json",
            Path.Combine(settlements, "humanSiteTemplates.json"));
        CopyResource(
            "SrvSurvey.Core.Resources.ggg.json",
            Path.Combine(published, "ggg.json"));

        var assembly = typeof(ExobiologyReferenceCatalog).Assembly;
        const string prefix = "SrvSurvey.Core.Resources.bio-criteria.";
        foreach (var resourceName in assembly.GetManifestResourceNames()
                     .Where(name => name.StartsWith(prefix, StringComparison.Ordinal))
                     .Where(name => name.EndsWith(".json", StringComparison.Ordinal)))
        {
            CopyResource(
                resourceName,
                Path.Combine(criteria, resourceName[prefix.Length..]));
        }
    }

    private static void CopyResource(string resourceName, string destination)
    {
        var assembly = typeof(ExobiologyReferenceCatalog).Assembly;
        using var source = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Test resource {resourceName} was not found.");
        using var target = File.Create(destination);
        source.CopyTo(target);
    }
}
