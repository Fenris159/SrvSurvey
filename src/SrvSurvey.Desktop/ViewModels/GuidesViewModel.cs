using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class GuidesViewModel : INotifyPropertyChanged
{
    private GuideCategoryViewModel selectedCategory;
    private string searchText = string.Empty;
    private IReadOnlyList<GuideSearchResultViewModel> searchResults = [];

    public GuidesViewModel(IReadOnlyList<GuideCategoryViewModel> categories)
    {
        ArgumentNullException.ThrowIfNull(categories);
        if (categories.Count == 0)
        {
            throw new ArgumentException(
                "At least one guide category is required.",
                nameof(categories));
        }

        Categories = categories;
        selectedCategory = categories[0];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<GuideCategoryViewModel> Categories { get; }

    public GuideCategoryViewModel SelectedCategory
    {
        get => selectedCategory;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!SetField(ref selectedCategory, value))
            {
                return;
            }

            SearchText = string.Empty;
        }
    }

    public string SearchText
    {
        get => searchText;
        set
        {
            var normalized = value ?? string.Empty;
            if (!SetField(ref searchText, normalized))
            {
                return;
            }

            RefreshSearchResults();
            OnPropertyChanged(nameof(IsSearching));
            OnPropertyChanged(nameof(IsBrowsing));
            OnPropertyChanged(nameof(HasSearchResults));
            OnPropertyChanged(nameof(HasNoSearchResults));
            OnPropertyChanged(nameof(SearchSummary));
        }
    }

    public IReadOnlyList<GuideSearchResultViewModel> SearchResults =>
        searchResults;

    public bool IsSearching => !string.IsNullOrWhiteSpace(SearchText);

    public bool IsBrowsing => !IsSearching;

    public bool HasSearchResults => IsSearching && SearchResults.Count > 0;

    public bool HasNoSearchResults => IsSearching && SearchResults.Count == 0;

    public string SearchSummary => SearchResults.Count == 1
        ? "1 matching guide entry"
        : $"{SearchResults.Count:N0} matching guide entries";

    private void RefreshSearchResults()
    {
        var terms = SearchText
            .Split(' ', StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries);
        if (terms.Length == 0)
        {
            searchResults = [];
            OnPropertyChanged(nameof(SearchResults));
            return;
        }

        var results = new List<GuideSearchResultViewModel>();
        foreach (var category in Categories)
        {
            foreach (var section in category.Sections.Where(section =>
                MatchesAllTerms(section.SearchableText, terms)
                || MatchesAllTerms(category.SearchableText, terms)))
            {
                results.Add(new GuideSearchResultViewModel(
                    category.Title,
                    section.Title,
                    section.Summary,
                    "Guide"));
            }

            foreach (var icon in category.Icons.Where(icon =>
                MatchesAllTerms(icon.SearchableText, terms)
                || MatchesAllTerms(category.SearchableText, terms)))
            {
                results.Add(new GuideSearchResultViewModel(
                    category.Title,
                    icon.Name,
                    icon.Meaning,
                    "Icon glossary"));
            }
        }

        searchResults = results;
        OnPropertyChanged(nameof(SearchResults));
    }

    private static bool MatchesAllTerms(string value, string[] terms)
    {
        return terms.All(term => value.Contains(
            term,
            StringComparison.OrdinalIgnoreCase));
    }

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record GuideCategoryViewModel(
    string Key,
    string Number,
    string Title,
    string Summary,
    IReadOnlyList<GuideSectionViewModel> Sections,
    IReadOnlyList<GuideIconViewModel> Icons)
{
    public string SearchableText => $"{Title} {Summary}";

    public bool HasSections => Sections.Count > 0;

    public bool HasIcons => Icons.Count > 0;
}

public sealed record GuideSectionViewModel(
    string Title,
    string Summary,
    IReadOnlyList<string> Steps,
    IReadOnlyList<string> Details)
{
    public bool HasSteps => Steps.Count > 0;

    public bool HasDetails => Details.Count > 0;

    public string SearchableText => string.Join(
        ' ',
        new[] { Title, Summary }.Concat(Steps).Concat(Details));
}

public sealed record GuideIconViewModel(
    GuideIconKind Kind,
    string Symbol,
    string Name,
    string Meaning,
    string AppearsIn,
    string SearchTerms = "",
    string AssetPath = "")
{
    public bool HasAsset => !string.IsNullOrWhiteSpace(AssetPath);

    public string SearchableText =>
        $"{Symbol} {Name} {Meaning} {AppearsIn} {SearchTerms}";
}

public sealed record GuideSearchResultViewModel(
    string Category,
    string Title,
    string Summary,
    string Kind);

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
