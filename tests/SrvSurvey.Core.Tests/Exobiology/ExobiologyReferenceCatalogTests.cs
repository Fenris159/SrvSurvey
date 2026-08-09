using SrvSurvey.Core.Exobiology;

namespace SrvSurvey.Core.Tests.Exobiology;

public sealed class ExobiologyReferenceCatalogTests
{
    [Fact]
    public void EmbeddedCatalogFindsOdysseyVariantAndSpecies()
    {
        var catalog = ExobiologyReferenceCatalog.LoadEmbedded();

        var byVariant = catalog.FindByVariant(
            "$Codex_Ent_Aleoids_01_B_Name;");
        var bySpecies = catalog.FindBySpecies(
            "$Codex_Ent_Aleoids_01_Name;");

        Assert.Equal(1070, catalog.Count);
        Assert.Equal(814, catalog.BiologyEntries.Count);
        Assert.NotNull(byVariant);
        Assert.Equal(2310101, byVariant.EntryId);
        Assert.Equal(7_252_500, byVariant.Reward);
        Assert.True(byVariant.IsBiology);
        Assert.Equal("Biology", byVariant.HudCategory);
        Assert.Equal("odyssey", byVariant.Platform);
        Assert.Equal("Aleoids", byVariant.SubClass);
        Assert.Equal("LCU No Fool Like One", byVariant.ImageCommander);
        Assert.Equal(
            "https://storage.googleapis.com/canonn-downloads/codex_images/Fool/Boerth%20GR-W%20e1-134(A%2010%20e)_00002.png",
            byVariant.ImageUrl);
        Assert.Equal(byVariant, bySpecies);
        Assert.Equal(byVariant, catalog.FindByEntryId(2310101));
        Assert.Equal(
            byVariant,
            catalog.FindByDisplayName(byVariant.DisplayName?.ToLowerInvariant()));
        Assert.Equal("23101", byVariant.EntryIdPrefix);
        Assert.Equal(
            "$Codex_Ent_Aleoids_Genus_Name;",
            ExobiologyReferenceCatalog.GetGenusName(
                byVariant.SpeciesName));

        var touristEntry = catalog.FindByEntryId(1200102);
        Assert.NotNull(touristEntry);
        Assert.False(touristEntry.IsBiology);
        Assert.Equal("Green Water Giant", touristEntry.DisplayName);
        Assert.Equal("Tourist", touristEntry.HudCategory);
        Assert.Equal("Planets", touristEntry.SubClass);
        Assert.Equal(0, touristEntry.Reward);
        Assert.Contains(touristEntry, catalog.Entries);
        Assert.DoesNotContain(touristEntry, catalog.BiologyEntries);
    }

    [Theory]
    [InlineData(2310101, "aleoida-arcus-yellow")]
    [InlineData(2100402, "Anemone-Croceum")]
    [InlineData(2100201, "Brain-Trees-Roseum-Brain-Tree")]
    public void LocalImageNamesMatchLegacyFloraContract(
        long entryId,
        string expected)
    {
        var entry = ExobiologyReferenceCatalog.LoadEmbedded()
            .FindByEntryId(entryId);

        Assert.NotNull(entry);
        Assert.Equal(expected, entry.GetLegacyLocalImageName());
    }

    [Theory]
    [InlineData(2100201, "$Codex_Ent_Brancae_Name;")]
    [InlineData(2100202, "$Codex_Ent_Brancae_Name;")]
    [InlineData(2100301, "$Codex_Ent_Cone_Name;")]
    [InlineData(2100401, "$Codex_Ent_Sphere_Name;")]
    [InlineData(2101400, "$Codex_Ent_Vents_Name;")]
    [InlineData(2101500, "$Codex_Ent_Ground_Struct_Ice_Name;")]
    [InlineData(2100501, "$Codex_Ent_Tube_Name;")]
    public void LegacyBiologyUsesCanonicalJournalGenus(
        long entryId,
        string expectedGenus)
    {
        var reference = ExobiologyReferenceCatalog.LoadEmbedded()
            .FindByEntryId(entryId);

        Assert.NotNull(reference);
        Assert.Equal(
            expectedGenus,
            ExobiologyReferenceCatalog.GetGenusName(reference));
    }

    [Fact]
    public void LoadRejectsNonObjectReference()
    {
        using var stream = new MemoryStream("[]"u8.ToArray());

        Assert.Throws<InvalidDataException>(
            () => ExobiologyReferenceCatalog.Load(stream));
    }

    [Theory]
    [InlineData("$Codex_Ent_Ingensradices_Genus_Name;", 15)]
    [InlineData("$Codex_Ent_Barnacles_Name;", 85)]
    [InlineData("$Codex_Ent_Vents_Name;", 100)]
    [InlineData("Aleoida", 150)]
    [InlineData("Tussock", 200)]
    [InlineData("Fungoida", 300)]
    [InlineData("Bacterium", 500)]
    [InlineData("Osseus", 800)]
    [InlineData("Electricae", 1_000)]
    [InlineData("Unknown genus", 50)]
    [InlineData(null, 50)]
    public void SampleDistanceMatchesLegacyGenusContract(
        string? genusName,
        int expected)
    {
        Assert.Equal(
            expected,
            ExobiologyReferenceCatalog.GetSampleDistanceMeters(genusName));
    }

    [Theory]
    [InlineData("$Codex_Ent_Aleoids_Genus_Name;", "Aleoida")]
    [InlineData("$Codex_Ent_Bacterial_Genus_Name;", "Bacterium")]
    [InlineData("$Codex_Ent_Brancae_Name;", "Brain Trees")]
    [InlineData("$Codex_Ent_Ground_Struct_Ice_Name;", "Crystalline Shards")]
    [InlineData("$Codex_Ent_Ingensradices_Genus_Name;", "Radicoida")]
    [InlineData("custom_tracker", "Custom Tracker")]
    public void GenusDisplayNameMatchesLegacyLabels(
        string genusName,
        string expected)
    {
        Assert.Equal(
            expected,
            ExobiologyReferenceCatalog.GetGenusDisplayName(genusName));
    }
}
