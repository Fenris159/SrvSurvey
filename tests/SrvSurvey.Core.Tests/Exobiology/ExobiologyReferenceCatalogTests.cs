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
}
