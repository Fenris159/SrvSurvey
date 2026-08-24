using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Presentation;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class GuidesViewModelTests
{
    [Fact]
    public void CatalogCoversEveryWorkspaceAndIconFamily()
    {
        var categories = GuideCatalog.Create();

        Assert.Equal(13, categories.Count);
        Assert.Equal(
            Enumerable.Range(1, 13)
                .Select(number => number.ToString("00"))
                .ToArray(),
            categories.Select(category => category.Number).ToArray());
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
            "alternative genus candidates",
            Icon(GuideIconKind.BiologyRewardPredicted).Meaning,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "dotted group frame",
            Icon(GuideIconKind.BiologyRewardPredicted).Meaning,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "current Commander",
            Icon(GuideIconKind.BiologyRewardHighlighted).Meaning,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "external candidate data",
            Icon(GuideIconKind.BiologyRewardGlobalRegional).Meaning,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "advisory",
            Icon(GuideIconKind.BiologyRewardGlobalRegional).Meaning,
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
    [InlineData("boxel hierarchy", "Navigate the boxel hierarchy")]
    [InlineData("FSSAllBodiesFound", "Survey the current boxel")]
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
    public void BoxelGuideMatchesTheImplementedProjectWorkflow()
    {
        var categories = GuideCatalog.Create();
        var travel = categories.Single(
            category => category.Key == "travel-search");
        var boxel = categories.Single(category => category.Key == "boxel");
        var guardian = categories.Single(category => category.Key == "guardian");
        var boxelSections = boxel.Sections.ToArray();
        var instructions = string.Join(
            ' ',
            boxelSections.SelectMany(section =>
                new[] { section.Summary }
                    .Concat(section.Steps)
                    .Concat(section.Details)));

        Assert.Equal("Boxel", boxel.Title);
        Assert.Equal("06", boxel.Number);
        Assert.Equal(
            categories.ToList().IndexOf(boxel) + 1,
            categories.ToList().IndexOf(guardian));
        Assert.DoesNotContain(travel.Sections, section =>
            section.Title.Contains("boxel", StringComparison.OrdinalIgnoreCase));
        Assert.True(boxelSections.Length >= 5);
        Assert.Contains("Lowest mass code", instructions, StringComparison.Ordinal);
        Assert.Contains("lowest incomplete suffix", instructions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FSSAllBodiesFound", instructions, StringComparison.Ordinal);
        Assert.Contains("mutually exclusive", instructions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Open Library", instructions, StringComparison.Ordinal);
        Assert.Contains("Resume Selected", instructions, StringComparison.Ordinal);
        Assert.Contains("last modified date", instructions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("more than 1,000 requests", instructions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Marx's Guide to Boxels", instructions, StringComparison.Ordinal);
    }

    [Fact]
    public void CatalogDocumentsCurrentRouteBoxelOverlayAndDesktopFeatures()
    {
        var categories = GuideCatalog.Create();
        var exploration = Instructions(categories, "exploration");
        var travel = Instructions(categories, "travel-search");
        var boxel = Instructions(categories, "boxel");
        var overlays = Instructions(categories, "overlays");
        var settings = Instructions(categories, "settings-migration");

        Assert.Contains("Show flight warnings", exploration, StringComparison.Ordinal);
        Assert.Contains("8 g", exploration, StringComparison.Ordinal);
        Assert.Contains("Route bodies", travel, StringComparison.Ordinal);
        Assert.Contains("marks that destination complete", travel, StringComparison.Ordinal);
        Assert.Contains("ten rows per page", boxel, StringComparison.Ordinal);
        Assert.Contains("Review Boxel statistics", boxel, StringComparison.Ordinal);
        Assert.Contains("Export JSON + CSV", boxel, StringComparison.Ordinal);
        Assert.Contains("overlay-settings icon", overlays, StringComparison.Ordinal);
        Assert.Contains("Caption font sizes", overlays, StringComparison.Ordinal);
        Assert.Contains("Desktop placement and focus", settings, StringComparison.Ordinal);
        Assert.Contains("Default monitor", settings, StringComparison.Ordinal);
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
    public void GuardianGuideExplainsSelectionConfirmationAndOriginControls()
    {
        var guardian = GuideCatalog.Create().Single(
            category => category.Key == "guardian");
        var instructions = string.Join(
            ' ',
            guardian.Sections.SelectMany(section =>
                section.Steps.Concat(section.Details)));

        Assert.Contains("fire group", instructions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("confirmation control twice", instructions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".aerial", instructions, StringComparison.Ordinal);
        Assert.Contains(".map", instructions, StringComparison.Ordinal);
    }

    [Fact]
    public void CatalogDocumentsCurrentRedesignSharingAndGuardianEditorWorkflows()
    {
        var categories = GuideCatalog.Create();
        var gettingStarted = Instructions(categories, "getting-started");
        var exploration = Instructions(categories, "exploration");
        var guardian = Instructions(categories, "guardian");
        var settings = Instructions(categories, "settings-migration");
        var diagnostics = Instructions(categories, "diagnostics");
        var expectedCoverage = new[]
        {
            (gettingStarted, "Survey groups Exploration"),
            (gettingStarted, "Search settings"),
            (exploration, "three retries"),
            (guardian, "15x"),
            (guardian, "Start map draft"),
            (guardian, "0.1 steps"),
            (guardian, "Commander position"),
            (settings, "Configure sharing"),
            (settings, "personal API key"),
            (settings, "Monochrome dark"),
            (diagnostics, "stale plans"),
        };

        Assert.All(expectedCoverage, expected => Assert.Contains(
            expected.Item2,
            expected.Item1,
            StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("Replay Controller", string.Join(' ', categories), StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyCatalogIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new GuidesViewModel([]));
    }

    private static string Instructions(
        IReadOnlyList<GuideCategoryViewModel> categories,
        string key) => string.Join(
            ' ',
            categories.Single(category => category.Key == key)
                .Sections.SelectMany(section =>
                    new[] { section.Title, section.Summary }
                        .Concat(section.Steps)
                        .Concat(section.Details)));
}
