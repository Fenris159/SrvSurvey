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

        Assert.True(catalog.Count > 100);
        Assert.NotNull(byVariant);
        Assert.Equal(2310101, byVariant.EntryId);
        Assert.Equal(7_252_500, byVariant.Reward);
        Assert.Equal(byVariant, bySpecies);
        Assert.Equal("23101", byVariant.EntryIdPrefix);
    }

    [Fact]
    public void LoadRejectsNonObjectReference()
    {
        using var stream = new MemoryStream("[]"u8.ToArray());

        Assert.Throws<InvalidDataException>(
            () => ExobiologyReferenceCatalog.Load(stream));
    }
}
