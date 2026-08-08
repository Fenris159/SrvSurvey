using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Presentation;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class GuidesViewModelTests
{
    [Fact]
    public void CatalogCoversEveryWorkspaceAndIconFamily()
    {
        var categories = GuideCatalog.Create();

        Assert.Equal(12, categories.Count);
        Assert.Equal(
            categories.Count,
            categories.Select(category => category.Key).Distinct().Count());
        Assert.Equal(
            categories.Count,
            categories.Select(category => category.Number).Distinct().Count());
        Assert.All(categories, category =>
            Assert.True(category.HasSections || category.HasIcons));
        Assert.True(categories.Sum(category => category.Sections.Count) >= 35);

        var icons = categories.SelectMany(category => category.Icons).ToArray();
        Assert.True(icons.Length >= 35);
        Assert.All(Enum.GetValues<GuideIconKind>(), kind =>
            Assert.Contains(icons, icon => icon.Kind == kind));
    }

    [Fact]
    public void GlossaryDocumentsEveryBundledRouteAndBodyIcon()
    {
        var icons = GuideCatalog.Create()
            .SelectMany(category => category.Icons)
            .Where(icon => icon.HasAsset)
            .ToArray();

        Assert.All(RouteBodyAssetResolver.SupportedVisuals, visual =>
            Assert.Contains(icons, icon =>
                icon.AssetPath == visual.AssetPath
                && icon.Name == visual.AccessibleName
                && !string.IsNullOrWhiteSpace(icon.Meaning)));
        Assert.Contains(icons, icon => icon.AssetPath.EndsWith(
            "/Assets/Routes/refuel-star.png",
            StringComparison.Ordinal));
        Assert.Contains(icons, icon => icon.AssetPath.EndsWith(
            "/Assets/Routes/neutron-star.png",
            StringComparison.Ordinal));
        Assert.Equal(
            RouteBodyAssetResolver.SupportedVisuals.Count + 2,
            icons.Length);
    }

    [Fact]
    public void GlossaryDocumentsCanonnSignalIndicatorBesideBiologyPips()
    {
        var icon = GuideCatalog.Create()
            .SelectMany(category => category.Icons)
            .Single(icon => icon.Kind == GuideIconKind.CanonnSignals);

        Assert.Contains("Canonn", icon.Name, StringComparison.Ordinal);
        Assert.Contains("beside", icon.Meaning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PIPs", icon.Meaning, StringComparison.Ordinal);
        Assert.Equal("System biology overlay", icon.AppearsIn);
    }

    [Fact]
    public void GlossaryDocumentsEveryBiologyRewardPipStateAndModifier()
    {
        var icons = GuideCatalog.Create()
            .SelectMany(category => category.Icons)
            .ToArray();
        GuideIconViewModel Icon(GuideIconKind kind) =>
            icons.Single(icon => icon.Kind == kind);

        Assert.Contains(
            "confirmed",
            Icon(GuideIconKind.BiologyRewardKnown).Meaning,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "hatching",
            Icon(GuideIconKind.BiologyRewardPredicted).Meaning,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "first-discovery",
            Icon(GuideIconKind.BiologyRewardHighlighted).Meaning,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "already been analyzed",
            Icon(GuideIconKind.BiologyRewardDimmed).Meaning,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "question mark",
            Icon(GuideIconKind.BiologyRewardUnknown).Meaning,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GlossaryDocumentsDynamicNearAndFarBearingChevrons()
    {
        var icon = GuideCatalog.Create()
            .SelectMany(category => category.Icons)
            .Single(icon => icon.Kind == GuideIconKind.DirectionalChevron);

        Assert.Contains("open chevron", icon.Meaning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("double chevron", icon.Meaning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1 km", icon.Meaning, StringComparison.Ordinal);
    }

    [Fact]
    public void GlossaryDocumentsLegacyGuardianRendererStates()
    {
        var icons = GuideCatalog.Create()
            .SelectMany(category => category.Icons)
            .ToArray();
        GuideIconViewModel Icon(GuideIconKind kind) =>
            icons.Single(icon => icon.Kind == kind);

        Assert.Contains(
            "90-degree radial glow",
            Icon(GuideIconKind.GuardianActiveObelisk).Meaning,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "individual heading",
            Icon(GuideIconKind.GuardianRelic).Meaning,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "orange Orb",
            Icon(GuideIconKind.GuardianArtifact).Meaning,
            StringComparison.Ordinal);
        Assert.Contains(
            "translucent gray",
            Icon(GuideIconKind.GuardianPoiStates).Meaning,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "not a generic X",
            Icon(GuideIconKind.GuardianBrokenObelisk).Meaning,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("journal folder", "First launch")]
    [InlineData("marketID repair", "Completed build-site repair")]
    [InlineData("primary port order", "Primary port order safety")]
    [InlineData("power post", "Conflict-zone power post")]
    [InlineData("hatched reward", "Predicted reward PIPs")]
    [InlineData("rocky body route", "Rocky body")]
    [InlineData("fuel scoop", "Fuel-scoop stop")]
    [InlineData("checksum manifest", "Import an original SrvSurvey profile")]
    public void SearchFindsWorkflowAndGlossaryContent(
        string query,
        string expectedTitle)
    {
        var viewModel = new GuidesViewModel(GuideCatalog.Create())
        {
            SearchText = query,
        };

        Assert.True(viewModel.IsSearching);
        Assert.True(viewModel.HasSearchResults);
        Assert.Contains(
            viewModel.SearchResults,
            result => result.Title == expectedTitle);
    }

    [Fact]
    public void SelectingCategoryReturnsToBrowsingMode()
    {
        var viewModel = new GuidesViewModel(GuideCatalog.Create())
        {
            SearchText = "Guardian obelisk",
        };

        viewModel.SelectedCategory = viewModel.Categories.Single(
            category => category.Key == "guardian");

        Assert.Equal(string.Empty, viewModel.SearchText);
        Assert.True(viewModel.IsBrowsing);
        Assert.Equal("Guardian sites", viewModel.SelectedCategory.Title);
    }

    [Fact]
    public void EmptyCatalogIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new GuidesViewModel([]));
    }
}
