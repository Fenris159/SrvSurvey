using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Search;

public sealed class CodexSearchCatalogTests
{
    [Theory]
    [InlineData("Albens Bell Mollusc", "Albens Bell Mollusc", null)]
    [InlineData("Tussock Capillum - Emerald", "Tussock Capillum", "Emerald")]
    [InlineData("Green Water Giant", "Green Water Giant", null)]
    [InlineData("Aleoida Arcus", "Aleoida Arcus", null)]
    public void EntryNamesUseOnlyVerifiedSubtypeAndVariantValues(string name, string subtype, string? color)
    {
        var catalog = new CodexSearchCatalog();
        CodexLandmarkFilter filter = Assert.Single(catalog.Plan(catalog.Resolve(name)).Landmarks);
        Assert.Contains(subtype, filter.Subtypes);
        Assert.Equal(color is null ? [] : [color], filter.Variants);
    }

    [Fact]
    public void DifferentSpeciesColorSetsRemainSeparateOrQueries()
    {
        var query = new CodexSearchQuery(
            "Missing colors",
            [new("Aleoida Arcus - Green", "Biology"), new("Tussock Capillum - Emerald", "Biology")],
            true
        );
        CodexSearchPlan plan = new CodexSearchCatalog().Plan(query);
        Assert.Equal(2, plan.Landmarks.Count);
        Assert.All(plan.Landmarks, filter => Assert.Single(filter.Subtypes));
    }

    [Fact]
    public void UnknownNamesUseCanonnAndPreserveTheirNames()
    {
        var catalog = new CodexSearchCatalog();
        CodexSearchQuery query = catalog.Resolve("  A new Codex name  ");
        Assert.Equal("A new Codex name", query.Label);
        Assert.Equal("A new Codex name", Assert.Single(catalog.Plan(query).CanonnNames));
        Assert.Empty(catalog.Plan(query).Landmarks);
    }

    [Fact]
    public void RootSearchUsesActualEntriesInsteadOfTheHeadingAsASignal()
    {
        var catalog = new CodexSearchCatalog();
        CodexSearchPlan plan = catalog.Plan(catalog.Resolve("The Codex"));
        Assert.Single(plan.Landmarks);
        Assert.DoesNotContain("The Codex", plan.CanonnNames);
        Assert.Equal(12, plan.UnsupportedNames.Count);
        Assert.Contains("Mollusc", catalog.Types);
        Assert.Contains("Emerald", catalog.Variants);
    }

    [Fact]
    public void CurrentReferenceCoverageIncludesAllBiologyWithoutMakingUpOtherMappings()
    {
        var catalog = new CodexSearchCatalog();
        var entries = ExobiologyReferenceCatalog.LoadEmbedded();
        CodexSearchPlan biology = catalog.Plan(CodexSearchCatalog.FromEntries("Biology", entries.BiologyEntries, true));
        Assert.Empty(biology.CanonnNames);
        Assert.Empty(biology.UnsupportedNames);
        Assert.NotEmpty(biology.Landmarks);
    }
}
