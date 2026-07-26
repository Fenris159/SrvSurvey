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

    [Fact]
    public void PublishedCsvParsesQuotedFieldsAndResolvesBlankEntryIds()
    {
        var references = ExobiologyReferenceCatalog.LoadEmbedded();
        var resolved = references.FindByDisplayName(
            "Aleoida Coronamus - Lime");
        Assert.NotNull(resolved);
        var csv = string.Join(
            "\r\n",
            "\"RegionID\",\"RegionName\",\"EnglishName\",\"Found\",\"NotExpectedToBeFound\",\"EntryID\",\"Name\",\"Varient\"",
            "\"1\",\"Galactic Centre\",\"Aleoida Arcus - Yellow\",\"0\",\"0\",\"2310101\",\"$Codex_Ent_Aleoids_01_B_Name;\",\"B\"",
            "\"18\",\"Inner Orion Spur\",\"Aleoida Coronamus - Lime\",\"0\",\"0\",\"\",\"value with \"\"quotes\"\", and comma\",\"Lime\"",
            "\"18\",\"Inner Orion Spur\",\"Unpublished variant\",\"0\",\"0\",\"\",\"\",\"test\"",
            "\"18\",\"Inner Orion Spur\",\"Already found\",\"1\",\"0\",\"2310102\",\"ignored\",\"ignored\"");

        var catalog = RegionalCodexCandidateCatalog.ParsePublishedCsv(
            System.Text.Encoding.UTF8.GetBytes(csv),
            references);

        Assert.Equal(2, catalog.Count);
        Assert.True(catalog.IsCandidate(1, 2310101));
        Assert.True(catalog.IsCandidate(18, resolved.EntryId));
        Assert.Equal("Lime", catalog.Entries.Single(
            entry => entry.RegionId == 18).Variant);
    }

    [Theory]
    [InlineData("\"RegionID\",\"RegionName\"\r\n\"1\",\"Galactic Centre\"")]
    [InlineData("\"RegionID\",\"RegionName\",\"EnglishName\",\"Found\",\"NotExpectedToBeFound\",\"EntryID\",\"Name\",\"Varient\"\r\n\"1\",\"Galactic Centre\",\"Test\",\"maybe\",\"0\",\"1\",\"name\",\"A\"")]
    [InlineData("\"RegionID\",\"RegionName\",\"EnglishName\",\"Found\",\"NotExpectedToBeFound\",\"EntryID\",\"Name\",\"Varient\"\r\n\"99\",\"Unknown\",\"Test\",\"0\",\"0\",\"1\",\"name\",\"A\"")]
    [InlineData("\"RegionID\",\"RegionName\",\"EnglishName\",\"Found\",\"NotExpectedToBeFound\",\"EntryID\",\"Name\",\"Varient\"\r\n\"1\",\"Galactic Centre\",\"Test\",\"1\",\"0\",\"not-an-id\",\"name\",\"A\"")]
    [InlineData("\"RegionID\",\"RegionName\",\"EnglishName\",\"Found\",\"NotExpectedToBeFound\",\"EntryID\",\"Name\",\"Varient\"\r\n\"1\",\"Galactic Centre\",\"unterminated,\"0\",\"0\",\"1\",\"name\",\"A\"")]
    public void PublishedCsvRejectsIncompatibleOrMalformedContent(string csv)
    {
        Assert.Throws<InvalidDataException>(() =>
            RegionalCodexCandidateCatalog.ParsePublishedCsv(
                System.Text.Encoding.UTF8.GetBytes(csv),
                ExobiologyReferenceCatalog.LoadEmbedded()));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
