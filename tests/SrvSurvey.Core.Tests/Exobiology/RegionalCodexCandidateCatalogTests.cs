using SrvSurvey.Core.Exobiology;

namespace SrvSurvey.Core.Tests.Exobiology;

public sealed class RegionalCodexCandidateCatalogTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-regional-codex-tests-{Guid.NewGuid():N}");

    [Fact]
    public void ImportedLegacyCatalogLoadsWithoutChangingItsBytes()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(
            temporaryDirectory,
            RegionalCodexCandidateCatalog.LegacyFileName);
        const string json =
            "{\"Inner Orion Spur\":[\"2310101_Aleoida_Arcus - Green\"]}";
        File.WriteAllText(path, json);

        var catalog = RegionalCodexCandidateCatalog.Load(temporaryDirectory);

        Assert.True(catalog.HasData);
        Assert.True(catalog.IsCandidate(18, 2310101));
        Assert.False(catalog.IsCandidate(18, 2310102));
        Assert.Equal("Aleoida_Arcus - Green", Assert.Single(
            catalog.Entries).Variant);
        Assert.Equal(json, File.ReadAllText(path));
        Assert.Empty(catalog.Warnings);
    }

    [Fact]
    public void MalformedImportedCatalogIsPreservedAndFailsClosed()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(
            temporaryDirectory,
            RegionalCodexCandidateCatalog.LegacyFileName);
        const string json =
            "{\"Inner Orion Spur\":[\"2310101_valid\",42]}";
        File.WriteAllText(path, json);

        var catalog = RegionalCodexCandidateCatalog.Load(temporaryDirectory);

        Assert.False(catalog.HasData);
        Assert.False(catalog.IsCandidate(18, 2310101));
        Assert.Single(catalog.Warnings);
        Assert.Equal(json, File.ReadAllText(path));
    }

    [Fact]
    public void LegacySerializationIsDeterministicAndRoundTrips()
    {
        var catalog = RegionalCodexCandidateCatalog.FromEntries(
        [
            new(18, "ignored", 2310102, "Second"),
            new(18, "ignored", 2310101, "First"),
        ]);
        Directory.CreateDirectory(temporaryDirectory);
        File.WriteAllText(
            Path.Combine(
                temporaryDirectory,
                RegionalCodexCandidateCatalog.LegacyFileName),
            catalog.SerializeLegacy());

        var reloaded = RegionalCodexCandidateCatalog.Load(temporaryDirectory);

        Assert.Equal(2, reloaded.Count);
        Assert.Equal(2310101, reloaded.Entries[0].EntryId);
        Assert.True(reloaded.IsCandidate(18, 2310102));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
