using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class SettingsWorkspaceViewModel : INotifyPropertyChanged
{
    private const string ApplicationCategoryKey = "application";
    private const string DesktopCategoryKey = "desktop";
    private const string GlobalOverlaysCategoryKey = "global-overlays";
    private const string InputCategoryKey = "input";
    private const string PrivacyCategoryKey = "privacy";
    private const string ScreenshotsCategoryKey = "screenshots";
    private const string DataCategoryKey = "data";

    private readonly IReadOnlyList<SettingsSearchEntry> catalog;
    private SettingsCategoryViewModel selectedCategory;
    private IReadOnlyList<SettingsSearchGroupViewModel> groupedSearchResults = [];
    private string searchQuery = string.Empty;
    private int selectedSearchResultIndex = -1;

    public SettingsWorkspaceViewModel()
    {
        Categories =
        [
            new(ApplicationCategoryKey, "Application"),
            new(DesktopCategoryKey, "Desktop"),
            new(GlobalOverlaysCategoryKey, "Global overlays"),
            new(InputCategoryKey, "Input"),
            new(PrivacyCategoryKey, "Privacy & sharing"),
            new(ScreenshotsCategoryKey, "Screenshots"),
            new(DataCategoryKey, "Data & migration"),
        ];
        selectedCategory = Categories[0];
        catalog = CreateCatalog();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<SettingsCategoryViewModel> Categories { get; }

    public IReadOnlyList<SettingsSearchEntry> SearchCatalog => catalog;

    public SettingsCategoryViewModel SelectedCategory
    {
        get => selectedCategory;
        set
        {
            if (value is null || ReferenceEquals(selectedCategory, value))
            {
                return;
            }

            selectedCategory = value;
            OnPropertyChanged();
            RaiseCategoryVisibilityChanged();
            if (HasSearchQuery)
            {
                SearchQuery = string.Empty;
            }
        }
    }

    public string SearchQuery
    {
        get => searchQuery;
        set
        {
            var normalized = value ?? string.Empty;
            if (searchQuery == normalized)
            {
                return;
            }

            searchQuery = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSearchQuery));
            RefreshSearchResults();
        }
    }

    public bool HasSearchQuery => !string.IsNullOrWhiteSpace(SearchQuery);

    public IReadOnlyList<SettingsSearchGroupViewModel> GroupedSearchResults
    {
        get => groupedSearchResults;
        private set
        {
            groupedSearchResults = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSearchResults));
        }
    }

    public bool HasSearchResults => GroupedSearchResults.Count > 0;

    public bool IsApplicationSelected =>
        SelectedCategory.Key == ApplicationCategoryKey;
    public bool IsDesktopSelected => SelectedCategory.Key == DesktopCategoryKey;
    public bool IsGlobalOverlaysSelected =>
        SelectedCategory.Key == GlobalOverlaysCategoryKey;
    public bool IsInputSelected => SelectedCategory.Key == InputCategoryKey;
    public bool IsPrivacySelected => SelectedCategory.Key == PrivacyCategoryKey;
    public bool IsScreenshotsSelected =>
        SelectedCategory.Key == ScreenshotsCategoryKey;
    public bool IsDataSelected => SelectedCategory.Key == DataCategoryKey;

    public SettingsSearchResultViewModel? SelectedSearchResult =>
        GetFlattenedResults().ElementAtOrDefault(selectedSearchResultIndex);

    public void MoveSearchSelection(int delta)
    {
        var results = GetFlattenedResults();
        if (results.Count == 0)
        {
            SetSelectedSearchResult(-1);
            return;
        }

        int nextIndex;
        if (selectedSearchResultIndex < 0)
        {
            nextIndex = delta < 0 ? results.Count - 1 : 0;
        }
        else
        {
            nextIndex = (selectedSearchResultIndex + delta + results.Count)
                % results.Count;
        }

        SetSelectedSearchResult(nextIndex);
    }

    public SettingsSearchResultViewModel? ActivateSelectedSearchResult()
    {
        var result = SelectedSearchResult;
        if (result is not null)
        {
            ActivateSearchResult(result);
        }

        return result;
    }

    public void ActivateSearchResult(SettingsSearchResultViewModel result)
    {
        ArgumentNullException.ThrowIfNull(result);
        SelectedCategory = Categories.Single(
            category => category.Key == result.CategoryKey);
        SearchQuery = string.Empty;
    }

    public void ClearSearch()
    {
        SearchQuery = string.Empty;
    }

    public void SelectCategory(string key)
    {
        SelectedCategory = Categories.Single(category => category.Key == key);
    }

    private IReadOnlyList<SettingsSearchResultViewModel> GetFlattenedResults()
    {
        return GroupedSearchResults.SelectMany(group => group.Results).ToArray();
    }

    private void RefreshSearchResults()
    {
        var terms = SearchQuery.Split(
            [' ', '\t'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0)
        {
            GroupedSearchResults = [];
            SetSelectedSearchResult(-1);
            return;
        }

        var matches = catalog
            .Where(entry => terms.All(term => entry.SearchText.Contains(
                term,
                StringComparison.OrdinalIgnoreCase)))
            .Select(entry => new SettingsSearchResultViewModel(entry))
            .ToArray();
        GroupedSearchResults = Categories
            .Select(category => new SettingsSearchGroupViewModel(
                category.Name,
                matches.Where(result => result.CategoryKey == category.Key).ToArray()))
            .Where(group => group.Results.Count > 0)
            .ToArray();
        SetSelectedSearchResult(matches.Length > 0 ? 0 : -1);
    }

    private void SetSelectedSearchResult(int index)
    {
        var results = GetFlattenedResults();
        for (var resultIndex = 0; resultIndex < results.Count; resultIndex++)
        {
            results[resultIndex].IsSelected = resultIndex == index;
        }

        selectedSearchResultIndex = index;
        OnPropertyChanged(nameof(SelectedSearchResult));
    }

    private void RaiseCategoryVisibilityChanged()
    {
        OnPropertyChanged(nameof(IsApplicationSelected));
        OnPropertyChanged(nameof(IsDesktopSelected));
        OnPropertyChanged(nameof(IsGlobalOverlaysSelected));
        OnPropertyChanged(nameof(IsInputSelected));
        OnPropertyChanged(nameof(IsPrivacySelected));
        OnPropertyChanged(nameof(IsScreenshotsSelected));
        OnPropertyChanged(nameof(IsDataSelected));
    }

    private static IReadOnlyList<SettingsSearchEntry> CreateCatalog() =>
    [
        new("Language", ApplicationCategoryKey, "LanguageComboBox", "LanguageCard",
            "translation locale localization display language"),
        new("Application window and focus", DesktopCategoryKey, "FocusGameOnStartCheckBox",
            "DesktopBehaviorCard", "monitor scale minimize tray game elite focus startup jump reduce motion animation"),
        new("Preferred commander", DesktopCategoryKey, "PreferredCommanderComboBox",
            "PreferredCommanderCard", "profile frontier id journal commander selection"),
        new("Global overlay configuration", GlobalOverlaysCategoryKey, "OpenThemeWorkspaceButton",
            "GlobalOverlaysCard", "overlay theme appearance opacity layout typography colors"),
        new("Keyboard shortcuts", InputCategoryKey, "KeyboardEnabledCheckBox",
            "KeyboardHookCard", "hotkey key chord global keyboard hook"),
        new("Shortcut bindings", InputCategoryKey, "ShortcutBindingsExpander",
            "ShortcutBindingsExpander", "hotkey key chord edit reset"),
        new("Controller input", InputCategoryKey, "ControllerEnabledCheckBox",
            "ControllerInputCard", "gamepad joystick hotas sdl device"),
        new("System nicknames", PrivacyCategoryKey, "SystemNicknamesCheckBox",
            "SystemNicknamesCard", "raven colonial personal public names"),
        new("Network publication", PrivacyCategoryKey, "EddnUploadCheckBox",
            "NetworkPrivacyCard", "eddn spansh canonn green gas giant sharing upload privacy"),
        new("Inara API key", PrivacyCategoryKey, "InaraApiKeyTextBox",
            "InaraCard", "commander publication upload credential api token"),
        new("Screenshot processing", ScreenshotsCategoryKey, "ScreenshotEnabledCheckBox",
            "ScreenshotProcessingCard", "image convert source target folder graphics"),
        new("Codex reference images", DataCategoryKey, "CodexCacheTextBox",
            "CodexImagesCard", "cache flora local image folder"),
        new("Dock-to-dock travel log", DataCategoryKey, "DockToDockEnabledCheckBox",
            "DockToDockCard", "journey trip station csv export"),
        new("Elite journal source", DataCategoryKey, "JournalDirectoryTextBox",
            "JournalSourceCard", "log folder proton profile data source"),
        new("Import SrvSurvey User Data", DataCategoryKey, "LegacyProfilePathTextBox",
            "LegacyImportSection", "legacy migrate migration old profile backup restore"),
    ];

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record SettingsCategoryViewModel(string Key, string Name);

public sealed record SettingsSearchGroupViewModel(
    string CategoryName,
    IReadOnlyList<SettingsSearchResultViewModel> Results);

public sealed class SettingsSearchResultViewModel : INotifyPropertyChanged
{
    private bool isSelected;

    public SettingsSearchResultViewModel(SettingsSearchEntry entry)
    {
        Title = entry.Title;
        CategoryKey = entry.CategoryKey;
        TargetControlName = entry.TargetControlName;
        HighlightControlName = entry.HighlightControlName;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title { get; }
    public string CategoryKey { get; }
    public string TargetControlName { get; }
    public string HighlightControlName { get; }

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected == value)
            {
                return;
            }

            isSelected = value;
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
}

public sealed record SettingsSearchEntry(
    string Title,
    string CategoryKey,
    string TargetControlName,
    string HighlightControlName,
    string Keywords)
{
    public string SearchText => $"{Title} {Keywords}";
}
