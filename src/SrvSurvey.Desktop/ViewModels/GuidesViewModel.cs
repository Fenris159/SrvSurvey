using System.Windows.Input;
using SrvSurvey.Desktop.Localization;

namespace SrvSurvey.Desktop.ViewModels;

/// <summary>Coordinates the accordion contents, single-topic reader, and full-text search.</summary>
public sealed class GuidesViewModel : WorkspaceObservable
{
    private GuideCategoryViewModel selectedCategory;
    private GuideTopicViewModel selectedTopic;
    private string searchText = string.Empty;
    private IReadOnlyList<GuideSearchResultViewModel> searchResults = [];

    /// <summary>Builds navigable topics from the catalog and opens the first task.</summary>
    public GuidesViewModel(IReadOnlyList<GuideCategoryViewModel> categories)
    {
        ArgumentNullException.ThrowIfNull(categories);
        if (categories.Count == 0 || categories.Any(category => !category.HasSections && !category.HasIcons))
        {
            throw new ArgumentException("Each guide category must contain a topic.", nameof(categories));
        }

        Categories = categories;
        GuideIconViewModel[] icons = categories.SelectMany(category => category.Icons).ToArray();
        Navigation = categories
            .Select(category => new GuideNavigationCategoryViewModel(category, icons, SelectTopic, ExpandCategory))
            .ToArray();
        selectedCategory = categories[0];
        selectedTopic = Navigation[0].Topics[0];
        Navigation[0].IsExpanded = true;
        selectedTopic.SetSelected(true);
        ClearSearchCommand = new WorkspaceCommand(() => SearchText = string.Empty);
    }

    public IReadOnlyList<GuideCategoryViewModel> Categories { get; }
    public IReadOnlyList<GuideNavigationCategoryViewModel> Navigation { get; }
    public ICommand ClearSearchCommand { get; }

    public GuideCategoryViewModel SelectedCategory
    {
        get => selectedCategory;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            GuideTopicViewModel topic = Navigation.Single(category => category.Category == value).Topics[0];
            selectedCategory = value;
            SelectTopic(topic);
        }
    }

    public GuideTopicViewModel SelectedTopic => selectedTopic;

    public string SearchText
    {
        get => searchText;
        set
        {
            if (!Set(ref searchText, value ?? string.Empty))
            {
                return;
            }

            RefreshSearchResults();
            Changed(nameof(IsSearching));
            Changed(nameof(IsBrowsing));
            Changed(nameof(HasSearchResults));
            Changed(nameof(HasNoSearchResults));
            Changed(nameof(SearchSummary));
        }
    }

    public IReadOnlyList<GuideSearchResultViewModel> SearchResults => searchResults;
    public bool IsSearching => !string.IsNullOrWhiteSpace(SearchText);
    public bool IsBrowsing => !IsSearching;
    public bool HasSearchResults => IsSearching && SearchResults.Count > 0;
    public bool HasNoSearchResults => IsSearching && SearchResults.Count == 0;
    public string SearchSummary =>
        SearchResults.Count == 1 ? "1 matching guide entry" : $"{SearchResults.Count:N0} matching guide entries";

    /// <summary>Opens one topic, updates the navigation highlight, and returns from search to reading.</summary>
    private void SelectTopic(GuideTopicViewModel topic)
    {
        selectedTopic.SetSelected(false);
        selectedTopic = topic;
        selectedCategory = topic.Category;
        topic.SetSelected(true);
        Navigation.Single(category => category.Category == topic.Category).IsExpanded = true;
        SearchText = string.Empty;
        Changed(nameof(SelectedCategory));
        Changed(nameof(SelectedTopic));
    }

    /// <summary>Expanding a category closes the other groups without changing the reading task.</summary>
    private void ExpandCategory(GuideNavigationCategoryViewModel expanded)
    {
        foreach (GuideNavigationCategoryViewModel category in Navigation.Where(category => category != expanded))
        {
            category.IsExpanded = false;
        }
    }

    /// <summary>Matches every query word against category context and the entire topic, including commands.</summary>
    private void RefreshSearchResults()
    {
        string[] terms = SearchText.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        searchResults =
            terms.Length == 0
                ? []
                : Navigation
                    .SelectMany(category => category.Topics)
                    .Where(topic => MatchesAllTerms(topic.SearchableText, terms))
                    .Select(topic => new GuideSearchResultViewModel(topic))
                    .ToArray();
        Changed(nameof(SearchResults));
    }

    /// <summary>Checks a prepared search string once per topic, regardless of query length.</summary>
    private static bool MatchesAllTerms(string value, string[] terms) =>
        terms.All(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
}

/// <summary>One expandable category and its selectable task and symbol entries.</summary>
public sealed class GuideNavigationCategoryViewModel : WorkspaceObservable
{
    private readonly Action<GuideNavigationCategoryViewModel> expanded;
    private bool isExpanded;

    /// <summary>Creates tasks and symbol entries while retaining the original catalog content.</summary>
    internal GuideNavigationCategoryViewModel(
        GuideCategoryViewModel category,
        IReadOnlyList<GuideIconViewModel> icons,
        Action<GuideTopicViewModel> select,
        Action<GuideNavigationCategoryViewModel> expanded
    )
    {
        Category = category;
        this.expanded = expanded;
        Topics = category
            .Sections.Select(section => new GuideTopicViewModel(category, section, null, icons, select))
            .Concat(category.Icons.Select(icon => new GuideTopicViewModel(category, null, icon, icons, select)))
            .ToArray();
    }

    public GuideCategoryViewModel Category { get; }
    public string Title => Category.Title;
    public IReadOnlyList<GuideTopicViewModel> Topics { get; }
    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            if (Set(ref isExpanded, value) && value)
            {
                expanded(this);
            }
        }
    }
}

/// <summary>One readable subject with numbered steps and accurate, captioned symbol examples.</summary>
public sealed class GuideTopicViewModel : WorkspaceObservable
{
    private bool isSelected;

    /// <summary>Adapts either an instruction section or a glossary symbol for the same reader.</summary>
    internal GuideTopicViewModel(
        GuideCategoryViewModel category,
        GuideSectionViewModel? section,
        GuideIconViewModel? icon,
        IReadOnlyList<GuideIconViewModel> icons,
        Action<GuideTopicViewModel> select
    )
    {
        Category = category;
        Section = section;
        Icon = icon;
        Steps = section?.Steps.Select((text, index) => new GuideStepViewModel(index + 1, text)).ToArray() ?? [];
        Illustrations = icon is not null
            ? [icon]
            : icons.Where(candidate => section?.IllustrationKinds?.Contains(candidate.Kind) == true).ToArray();
        OpenCommand = new WorkspaceCommand(() => select(this));
    }

    public GuideCategoryViewModel Category { get; }
    public GuideSectionViewModel? Section { get; }
    public GuideIconViewModel? Icon { get; }
    public string Title => Section?.Title ?? Icon!.Name;
    public string Summary => Section?.Summary ?? Icon!.Meaning;
    public IReadOnlyList<GuideStepViewModel> Steps { get; }
    public IReadOnlyList<string> Details => Section?.Details ?? [];
    public IReadOnlyList<GuideIconViewModel> Illustrations { get; }
    public bool HasInstructions => Section is not null;
    public bool HasSteps => Steps.Count > 0;
    public bool HasDetails => Details.Count > 0;
    public bool HasIllustrations => Illustrations.Count > 0;
    public ICommand OpenCommand { get; }
    public bool IsSelected => isSelected;

    /// <summary>Searches original command text and the visible language's complete instructions.</summary>
    public string SearchableText
    {
        get
        {
            IEnumerable<string> parts = new[] { Category.Title, Category.Summary, Title, Summary }
                .Concat(Details)
                .Concat(Steps.Select(step => step.Text));
            if (Icon is { } icon)
            {
                parts = parts.Concat([icon.Symbol, icon.AppearsIn, icon.SearchTerms]);
            }
            return string.Join(' ', parts.Select(text => $"{text} {LocalizationCatalog.Translate(text)}"));
        }
    }

    /// <summary>Updates the task highlight independently of the expanded category.</summary>
    internal void SetSelected(bool value) => Set(ref isSelected, value, nameof(IsSelected));
}

/// <summary>An instruction with its reading-order number.</summary>
public sealed record GuideStepViewModel(int Number, string Text);

/// <summary>A catalog category containing task instructions and visual references.</summary>
public sealed record GuideCategoryViewModel(
    string Key,
    string Number,
    string Title,
    string Summary,
    IReadOnlyList<GuideSectionViewModel> Sections,
    IReadOnlyList<GuideIconViewModel> Icons
)
{
    public string SearchableText => $"{Title} {Summary}";

    public bool HasSections => Sections.Count > 0;

    public bool HasIcons => Icons.Count > 0;
}

/// <summary>A task with an introduction, ordered instructions, notes, and optional illustrated symbols.</summary>
public sealed record GuideSectionViewModel(
    string Title,
    string Summary,
    IReadOnlyList<string> Steps,
    IReadOnlyList<string> Details,
    IReadOnlyList<GuideIconKind>? IllustrationKinds = null
)
{
    public bool HasSteps => Steps.Count > 0;

    public bool HasDetails => Details.Count > 0;

    public string SearchableText => string.Join(' ', new[] { Title, Summary }.Concat(Steps).Concat(Details));
}

/// <summary>A rendered symbol and its meaning in the application.</summary>
public sealed record GuideIconViewModel(
    GuideIconKind Kind,
    string Symbol,
    string Name,
    string Meaning,
    string AppearsIn,
    string SearchTerms = "",
    string AssetPath = ""
)
{
    public bool HasAsset => !string.IsNullOrWhiteSpace(AssetPath);

    public string SearchableText => $"{Symbol} {Name} {Meaning} {AppearsIn} {SearchTerms}";
}

/// <summary>A search hit that opens its complete task or illustrated reference.</summary>
public sealed record GuideSearchResultViewModel(GuideTopicViewModel Topic)
{
    public string Category => Topic.Category.Title;
    public string Title => Topic.Title;
    public string Summary => Topic.Summary;
    public string Kind => Topic.Icon is null ? "Guide" : "Icon glossary";
    public ICommand OpenCommand => Topic.OpenCommand;
}

public enum GuideIconKind
{
    Glyph,
    Asset,
    BiologyRewardKnown,
    BiologyRewardPredicted,
    BiologyRewardHighlighted,
    BiologyRewardGlobalRegional,
    BiologyRewardDimmed,
    BiologyRewardUnknown,
    CanonnSignals,
    DirectionalChevron,
    RadarCommander,
    RadarShip,
    RadarSrv,
    RadarSample,
    RadarHistoricalScan,
    RadarBookmark,
    GroundTarget,
    JumpRoute,
    GuardianRelic,
    GuardianArtifact,
    GuardianEmptyPuddle,
    GuardianObelisk,
    GuardianActiveObelisk,
    GuardianBrokenObelisk,
    GuardianPylon,
    GuardianComponent,
    GuardianCommander,
    GuardianSiteHeading,
    GuardianTowerHeading,
    GuardianSurveyNeeded,
    GuardianPoiStates,
    HumanLandingPad,
    HumanDoor,
    HumanTerminal,
    HumanMaterial,
    HumanCommander,
    HumanShip,
    HumanSrv,
    HumanQuestTarget,
    HumanFloor,
    ConflictCheckpoint,
    ConflictPowerPost,
}
