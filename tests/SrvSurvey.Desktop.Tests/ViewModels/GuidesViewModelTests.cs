using System.Globalization;
using SrvSurvey.Desktop.Presentation;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class GuidesViewModelTests
{
    [Fact]
    public void CatalogCoversEveryWorkspaceAndIconFamily()
    {
        IReadOnlyList<GuideCategoryViewModel> categories = GuideCatalog.Create();

        Assert.Equal(19, categories.Count);
        Assert.Equal(
            Enumerable.Range(1, 19).Select(number => number.ToString("00", CultureInfo.InvariantCulture)).ToArray(),
            categories.Select(category => category.Number).ToArray()
        );
        Assert.Equal(categories.Count, categories.Select(category => category.Key).Distinct().Count());
        Assert.Equal(categories.Count, categories.Select(category => category.Number).Distinct().Count());
        Assert.All(categories, category => Assert.True(category.HasSections || category.HasIcons));
        Assert.True(categories.Sum(category => category.Sections.Count) >= 35);

        GuideIconViewModel[] icons = categories.SelectMany(category => category.Icons).ToArray();
        Assert.True(icons.Length >= 35);
        Assert.All(Enum.GetValues<GuideIconKind>(), kind => Assert.Contains(icons, icon => icon.Kind == kind));
    }

    [Fact]
    public void GlossaryDocumentsEveryBundledRouteAndBodyIcon()
    {
        GuideIconViewModel[] icons = GuideCatalog
            .Create()
            .SelectMany(category => category.Icons)
            .Where(icon => icon.HasAsset)
            .ToArray();

        Assert.All(
            RouteBodyAssetResolver.SupportedVisuals,
            visual =>
                Assert.Contains(
                    icons,
                    icon =>
                        icon.AssetPath == visual.AssetPath
                        && icon.Name == visual.AccessibleName
                        && !string.IsNullOrWhiteSpace(icon.Meaning)
                )
        );
        Assert.Contains(
            icons,
            icon => icon.AssetPath.EndsWith("/Assets/Routes/refuel-star.png", StringComparison.Ordinal)
        );
        Assert.Contains(
            icons,
            icon => icon.AssetPath.EndsWith("/Assets/Routes/neutron-star.png", StringComparison.Ordinal)
        );
        Assert.Equal(RouteBodyAssetResolver.SupportedVisuals.Count + 2, icons.Length);
    }

    [Fact]
    public void GlossaryDocumentsCanonnSignalIndicatorBesideBiologyPips()
    {
        GuideIconViewModel icon = GuideCatalog
            .Create()
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
        GuideIconViewModel[] icons = GuideCatalog.Create().SelectMany(category => category.Icons).ToArray();
        GuideIconViewModel Icon(GuideIconKind kind) => icons.Single(icon => icon.Kind == kind);

        Assert.Contains(
            "confirmed",
            Icon(GuideIconKind.BiologyRewardKnown).Meaning,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Contains(
            "Canonn, or Spansh",
            Icon(GuideIconKind.BiologyRewardKnown).Meaning,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Contains(
            "hatching",
            Icon(GuideIconKind.BiologyRewardPredicted).Meaning,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Contains(
            "only by the biology prediction data",
            Icon(GuideIconKind.BiologyRewardPredicted).Meaning,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Contains(
            "alternative genus candidates",
            Icon(GuideIconKind.BiologyRewardPredicted).Meaning,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Contains(
            "dotted group frame",
            Icon(GuideIconKind.BiologyRewardPredicted).Meaning,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Contains(
            "current Commander",
            Icon(GuideIconKind.BiologyRewardHighlighted).Meaning,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Contains(
            "external candidate data",
            Icon(GuideIconKind.BiologyRewardGlobalRegional).Meaning,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Contains(
            "advisory",
            Icon(GuideIconKind.BiologyRewardGlobalRegional).Meaning,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Contains(
            "already been analyzed",
            Icon(GuideIconKind.BiologyRewardDimmed).Meaning,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Contains(
            "question mark",
            Icon(GuideIconKind.BiologyRewardUnknown).Meaning,
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public void GlossaryDocumentsDynamicNearAndFarBearingChevrons()
    {
        GuideIconViewModel icon = GuideCatalog
            .Create()
            .SelectMany(category => category.Icons)
            .Single(icon => icon.Kind == GuideIconKind.DirectionalChevron);

        Assert.Contains("open chevron", icon.Meaning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("double chevron", icon.Meaning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1 km", icon.Meaning, StringComparison.Ordinal);
    }

    [Fact]
    public void GlossaryDocumentsLegacyGuardianRendererStates()
    {
        GuideIconViewModel[] icons = GuideCatalog.Create().SelectMany(category => category.Icons).ToArray();
        GuideIconViewModel Icon(GuideIconKind kind) => icons.Single(icon => icon.Kind == kind);

        Assert.Contains(
            "90-degree radial glow",
            Icon(GuideIconKind.GuardianActiveObelisk).Meaning,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Contains(
            "individual heading",
            Icon(GuideIconKind.GuardianRelic).Meaning,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Contains("orange Orb", Icon(GuideIconKind.GuardianArtifact).Meaning, StringComparison.Ordinal);
        Assert.Contains(
            "translucent gray",
            Icon(GuideIconKind.GuardianPoiStates).Meaning,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Contains(
            "not a generic X",
            Icon(GuideIconKind.GuardianBrokenObelisk).Meaning,
            StringComparison.OrdinalIgnoreCase
        );
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
    [InlineData("Ctrl+Alt+F1", "Rig tracking with key chords")]
    [InlineData("=helium", "Surface survey and exobiology — bookmarks")]
    [InlineData(".alphaflip", "Guardian survey — map and alignment")]
    [InlineData(".threat", "Human settlements — alignment and material surveys")]
    [InlineData("@@", "Settlement mapping — measurements and calibration")]
    [InlineData("Clear rigs automatically", "Rig tracking with key chords")]
    [InlineData("Rhino cargo", "Ship, Rhino, and cargo")]
    [InlineData("Meta users must launch Elite through SteamVR", "Pair the headset and activate overlays")]
    [InlineData("VDXR", "Choose a Meta compatibility bridge")]
    [InlineData("no custom library location", "Windows and Linux setup")]
    public void SearchFindsWorkflowAndGlossaryContent(string query, string expectedTitle)
    {
        var viewModel = new GuidesViewModel(GuideCatalog.Create()) { SearchText = query };

        Assert.True(viewModel.IsSearching);
        Assert.True(viewModel.HasSearchResults);
        Assert.Contains(viewModel.SearchResults, result => result.Title == expectedTitle);
    }

    [Fact]
    public void BoxelGuideMatchesTheImplementedProjectWorkflow()
    {
        IReadOnlyList<GuideCategoryViewModel> categories = GuideCatalog.Create();
        GuideCategoryViewModel travel = categories.Single(category => category.Key == "travel-search");
        GuideCategoryViewModel boxel = categories.Single(category => category.Key == "boxel");
        GuideCategoryViewModel guardian = categories.Single(category => category.Key == "guardian");
        GuideSectionViewModel[] boxelSections = boxel.Sections.ToArray();
        string instructions = string.Join(
            ' ',
            boxelSections.SelectMany(section => new[] { section.Summary }.Concat(section.Steps).Concat(section.Details))
        );

        Assert.Equal("Boxel", boxel.Title);
        Assert.Equal("07", boxel.Number);
        Assert.Equal(categories.ToList().IndexOf(boxel) + 1, categories.ToList().IndexOf(guardian));
        Assert.DoesNotContain(
            travel.Sections,
            section => section.Title.Contains("boxel", StringComparison.OrdinalIgnoreCase)
        );
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
        IReadOnlyList<GuideCategoryViewModel> categories = GuideCatalog.Create();
        string exploration = Instructions(categories, "exploration");
        string travel = Instructions(categories, "travel-search");
        string boxel = Instructions(categories, "boxel");
        string overlays = Instructions(categories, "overlays");
        string settings = Instructions(categories, "settings-migration");

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
        var viewModel = new GuidesViewModel(GuideCatalog.Create()) { SearchText = "Guardian obelisk" };

        viewModel.SelectedCategory = viewModel.Categories.Single(category => category.Key == "guardian");

        Assert.Equal(string.Empty, viewModel.SearchText);
        Assert.True(viewModel.IsBrowsing);
        Assert.Equal("Guardian sites", viewModel.SelectedCategory.Title);
    }

    [Fact]
    public void VirtualRealityGuideDocumentsSupportedRoutesAndFailureStates()
    {
        GuideCategoryViewModel virtualReality = GuideCatalog
            .Create()
            .Single(category => category.Key == "virtual-reality");
        string instructions = string.Join(
            ' ',
            virtualReality.Sections.SelectMany(section =>
                new[] { section.Title, section.Summary }.Concat(section.Steps).Concat(section.Details)
            )
        );

        Assert.Equal("VR & headset overlays", virtualReality.Title);
        Assert.Contains("SteamVR headset", instructions, StringComparison.Ordinal);
        Assert.Contains("Meta Quest or Rift", instructions, StringComparison.Ordinal);
        Assert.Contains("Windows Mixed Reality", instructions, StringComparison.Ordinal);
        Assert.Contains("OpenXR-only", instructions, StringComparison.Ordinal);
        Assert.Contains("Meta compatibility bridge", instructions, StringComparison.Ordinal);
        Assert.Contains("OpenComposite is not a compatible workaround", instructions, StringComparison.Ordinal);
        Assert.Contains("no custom library location is required", instructions, StringComparison.Ordinal);
        Assert.Contains("NEEDS ATTENTION", instructions, StringComparison.Ordinal);
        Assert.Contains("The VR runtime rejected an overlay", instructions, StringComparison.Ordinal);
        Assert.Contains("Connected with zero panels", instructions, StringComparison.Ordinal);
    }

    [Fact]
    public void GuardianGuideExplainsSelectionConfirmationAndOriginControls()
    {
        GuideCategoryViewModel guardian = GuideCatalog.Create().Single(category => category.Key == "guardian");
        string instructions = string.Join(
            ' ',
            guardian.Sections.SelectMany(section => section.Steps.Concat(section.Details))
        );

        Assert.Contains("fire group", instructions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("confirmation control twice", instructions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".aerial", instructions, StringComparison.Ordinal);
        Assert.Contains(".map", instructions, StringComparison.Ordinal);
    }

    [Fact]
    public void CatalogDocumentsCurrentRedesignSharingAndGuardianEditorWorkflows()
    {
        IReadOnlyList<GuideCategoryViewModel> categories = GuideCatalog.Create();
        string gettingStarted = Instructions(categories, "getting-started");
        string exploration = Instructions(categories, "exploration");
        string guardian = Instructions(categories, "guardian");
        string settings = Instructions(categories, "settings-migration");
        string diagnostics = Instructions(categories, "diagnostics");
        (string, string)[] expectedCoverage = new[]
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

        Assert.All(
            expectedCoverage,
            expected => Assert.Contains(expected.Item2, expected.Item1, StringComparison.OrdinalIgnoreCase)
        );
        Assert.DoesNotContain("Replay Controller", string.Join(' ', categories), StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyCatalogIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new GuidesViewModel([]));
    }

    /// <summary>Accordion expansion is independent of reading and only one group remains open.</summary>
    [Fact]
    public void ExpandingAnotherCategoryKeepsTheTaskUntilATopicIsSelected()
    {
        var guides = new GuidesViewModel(GuideCatalog.Create());
        GuideTopicViewModel original = guides.SelectedTopic;
        GuideNavigationCategoryViewModel mining = guides.Navigation.Single(category =>
            category.Category.Key == "surface-mining"
        );
        mining.IsExpanded = true;
        mining.IsExpanded = true;
        Assert.Single(guides.Navigation, category => category.IsExpanded);
        Assert.Same(original, guides.SelectedTopic);
        mining.Topics[1].OpenCommand.Execute(null);
        Assert.Same(mining.Topics[1], guides.SelectedTopic);
        Assert.True(mining.Topics[1].IsSelected);
        Assert.False(original.IsSelected);
        mining.IsExpanded = false;
        guides.SelectedTopic.OpenCommand.Execute(null);
        Assert.True(mining.IsExpanded);
    }

    /// <summary>Search includes category words and opens the full instructions rather than a summary.</summary>
    [Fact]
    public void SearchOpensTaskAndClearingItRetainsCurrentSelection()
    {
        var guides = new GuidesViewModel(GuideCatalog.Create());
        var changes = new List<string?>();
        guides.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        guides.SearchText = "surface\tmining\nprofitable";
        GuideSearchResultViewModel result = Assert.Single(guides.SearchResults);
        Assert.Equal("Guide", result.Kind);
        Assert.Equal("Surface mining", result.Category);
        Assert.NotEmpty(result.Summary);
        result.OpenCommand.Execute(null);
        Assert.Same(result.Topic, guides.SelectedTopic);
        Assert.True(guides.IsBrowsing);
        Assert.True(guides.SelectedTopic.HasSteps);
        Assert.Equal(Enumerable.Range(1, result.Topic.Steps.Count), result.Topic.Steps.Select(step => step.Number));
        Assert.All(result.Topic.Steps, step => Assert.False(string.IsNullOrWhiteSpace(step.Text)));
        guides.SearchText = "nonexistent-guides-query";
        Assert.True(guides.HasNoSearchResults);
        Assert.Equal("0 matching guide entries", guides.SearchSummary);
        guides.ClearSearchCommand.Execute(null);
        Assert.Same(result.Topic, guides.SelectedTopic);
        Assert.False(guides.HasNoSearchResults);
        Assert.False(guides.HasSearchResults);
        int count = changes.Count;
        guides.SearchText = string.Empty;
        Assert.Equal(count, changes.Count);
        guides.SearchText = "   ";
        Assert.Empty(guides.SearchResults);
        Assert.True(guides.IsBrowsing);
        guides.SearchText = null!;
        Assert.Equal(string.Empty, guides.SearchText);
        Assert.Contains(nameof(GuidesViewModel.SelectedTopic), changes);
    }

    /// <summary>Symbol results keep their rendered illustration, and every catalog entry can be opened.</summary>
    [Fact]
    public void EveryTaskAndSymbolIsReachableFromTheAccordion()
    {
        var guides = new GuidesViewModel(GuideCatalog.Create());
        GuideTopicViewModel[] topics = guides.Navigation.SelectMany(category => category.Topics).ToArray();
        Assert.Equal(guides.Categories.Sum(category => category.Sections.Count + category.Icons.Count), topics.Length);
        foreach (GuideTopicViewModel topic in topics)
        {
            topic.OpenCommand.Execute(null);
            Assert.Same(topic, guides.SelectedTopic);
            Assert.False(string.IsNullOrWhiteSpace(topic.Title));
            Assert.False(string.IsNullOrWhiteSpace(topic.Summary));
            Assert.Equal(topic.Details.Count > 0, topic.HasDetails);
            Assert.Equal(topic.Illustrations.Count > 0, topic.HasIllustrations);
            Assert.Equal(topic.Steps.Count > 0, topic.HasSteps);
            Assert.Single(topics, entry => entry.IsSelected);
            if (topic.Icon is not null)
            {
                Assert.Equal(topic.Icon, Assert.Single(topic.Illustrations));
                Assert.Null(topic.Section);
                Assert.Empty(topic.Steps);
                Assert.Empty(topic.Details);
            }
        }
        guides.SearchText = "conflict power post";
        GuideSearchResultViewModel hit = Assert.Single(guides.SearchResults);
        Assert.Equal("Icon glossary", hit.Kind);
        Assert.Equal("1 matching guide entry", guides.SearchSummary);
        hit.OpenCommand.Execute(null);
        Assert.Equal(GuideIconKind.ConflictPowerPost, guides.SelectedTopic.Icon!.Kind);
        GuideTopicViewModel biology = topics.Single(topic => topic.Title == "Predictions and bio signals");
        Assert.Contains(biology.Illustrations, icon => icon.Kind == GuideIconKind.BiologyRewardPredicted);
    }

    /// <summary>An empty category cannot leave the reader without a subject.</summary>
    [Fact]
    public void EmptyCategoryAndNullInputsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new GuidesViewModel(null!));
        Assert.Throws<ArgumentException>(() => new GuidesViewModel([new("empty", "01", "Empty", "", [], [])]));
        var guides = new GuidesViewModel(GuideCatalog.Create());
        Assert.Throws<ArgumentNullException>(() => guides.SelectedCategory = null!);
        guides.SelectedCategory = guides.Categories[0];
        Assert.True(guides.IsBrowsing);
    }

    /// <summary>Recently added workflows have their own discoverable task guides.</summary>
    [Theory]
    [InlineData("Planetary Mining", "Plan mining for Powerplay")]
    [InlineData("mine–sell", "Find profitable surface mining locations")]
    [InlineData("SELECTED MARKER", "Edit or remove a mapped deposit")]
    [InlineData("Remember selection", "Wayland screen capture")]
    [InlineData("Reset input detection", "Linux keyboard input sources")]
    [InlineData("Global Shortcuts", "Approve or change desktop shortcuts")]
    [InlineData("notification animations", "Bypass Window Management on Linux")]
    [InlineData("Composition Scanner", "Create a ship configuration")]
    [InlineData("Left Right", "Firegroups cockpit visibility")]
    [InlineData("Retry selected deliveries", "Resolve unconfirmed deliveries")]
    public void CurrentFeatureDocumentationIsSearchable(string query, string title)
    {
        var guides = new GuidesViewModel(GuideCatalog.Create()) { SearchText = query };
        Assert.Contains(guides.SearchResults, result => result.Title == title);
    }

    private static string Instructions(IReadOnlyList<GuideCategoryViewModel> categories, string key) =>
        string.Join(
            ' ',
            categories
                .Single(category => category.Key == key)
                .Sections.SelectMany(section =>
                    new[] { section.Title, section.Summary }.Concat(section.Steps).Concat(section.Details)
                )
        );
}
