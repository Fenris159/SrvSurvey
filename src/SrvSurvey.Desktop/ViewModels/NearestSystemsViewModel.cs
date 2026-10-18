using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class NearestSystemsViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly CodexSearchCatalog catalog = new();
    private CodexSearchQuery? selectedQuery;
    private Action? cancelSearch;
    private int searchVersion;
    private string coverageNote = string.Empty;
    private string resultOrigin = string.Empty;
    private bool disposed;
    private const string Unavailable = "\u2014";

    private readonly INearestSystemsClient nearestSystemsClient;
    private readonly IStarSystemResolver systemResolver;
    private readonly AsyncCommand searchCommand;
    private readonly AsyncCommand copySystemCommand;
    private readonly AsyncCommand copyCoordinatesCommand;
    private readonly AsyncCommand openCanonnCommand;
    private readonly AsyncCommand openSpanshCommand;
    private readonly AsyncCommand openSpanshSearchCommand;
    private NearestSystemsSearchModeOptionViewModel selectedMode;
    private string codexName = string.Empty;
    private string landmarkType = string.Empty;
    private string landmarkSubtypes = string.Empty;
    private string landmarkVariants = string.Empty;
    private string referenceSystemName = Unavailable;
    private long? referenceSystemAddress;
    private GalacticCoordinate? referencePosition;
    private string commanderName = string.Empty;
    private bool isSearching;
    private string statusMessage =
        "Choose a Codex entry or group to find up to 20 nearest systems, with no distance cutoff.";
    private IReadOnlyList<NearestSystemRowViewModel> results = [];
    private NearestSystemRowViewModel? selectedResult;
    private string? spanshSearchReference;
    private Func<string, Task>? clipboardWriter;
    private Func<Uri, Task<bool>>? uriLauncher;

    public NearestSystemsViewModel(INearestSystemsClient nearestSystemsClient, IStarSystemResolver systemResolver)
    {
        this.nearestSystemsClient =
            nearestSystemsClient ?? throw new ArgumentNullException(nameof(nearestSystemsClient));
        this.systemResolver = systemResolver ?? throw new ArgumentNullException(nameof(systemResolver));
        CodexOptions = catalog.Options.Select(option => option.Label).ToArray();
        Modes =
        [
            new(
                NearestSystemsSearchMode.Codex,
                "Codex entry or group",
                "Search the selected entry or all entries in a group."
            ),
            new(
                NearestSystemsSearchMode.Landmarks,
                "Spansh landmarks",
                "Search a landmark type or subtype; a separate variant is optional."
            ),
        ];
        selectedMode = Modes[0];
        searchCommand = new AsyncCommand(SearchAsync, CanSearch);
        copySystemCommand = new AsyncCommand(CopySystemAsync, CanUseSelectedResult);
        copyCoordinatesCommand = new AsyncCommand(CopyCoordinatesAsync, CanUseSelectedResult);
        openCanonnCommand = new AsyncCommand(OpenCanonnAsync, CanUseSelectedResult);
        openSpanshCommand = new AsyncCommand(OpenSpanshAsync, CanUseSelectedResult);
        openSpanshSearchCommand = new AsyncCommand(
            OpenOriginalSpanshSearchAsync,
            () => HasSpanshSearchReference && !IsSearching
        );
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<NearestSystemsSearchModeOptionViewModel> Modes { get; }
    public IReadOnlyList<string> CodexOptions { get; }
    public IReadOnlyList<string> LandmarkTypes => catalog.Types;
    public IReadOnlyList<string> LandmarkSubtypeOptions => catalog.Subtypes;
    public IReadOnlyList<string> LandmarkVariantOptions => catalog.Variants;
    public string CriteriaSummary =>
        selectedQuery is { MatchVariants: true, Entries.Count: > 1 } query
            ? "Selected entries: "
                + string.Join(", ", query.Entries.Select(entry => ShortEntryName(entry.Name, query.Label)))
            : string.Empty;
    public string CoverageNote
    {
        get => coverageNote;
        private set => SetField(ref coverageNote, value);
    }
    public string ResultOrigin
    {
        get => resultOrigin;
        private set => SetField(ref resultOrigin, value);
    }

    public NearestSystemsSearchModeOptionViewModel SelectedMode
    {
        get => selectedMode;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!SetField(ref selectedMode, value))
            {
                return;
            }

            CancelPendingSearch();

            Results = [];
            SelectedResult = null;
            SpanshSearchReference = null;
            StatusMessage = value.Description;
            OnPropertyChanged(nameof(IsCodexMode));
            OnPropertyChanged(nameof(IsLandmarkMode));
            OnPropertyChanged(nameof(SearchButtonText));
            searchCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsCodexMode => SelectedMode.Mode == NearestSystemsSearchMode.Codex;

    public bool IsLandmarkMode => SelectedMode.Mode == NearestSystemsSearchMode.Landmarks;

    public string CodexName
    {
        get => codexName;
        set
        {
            if (SetField(ref codexName, value ?? string.Empty))
            {
                CancelPendingSearch();
                selectedQuery = null;
                OnPropertyChanged(nameof(CriteriaSummary));
                searchCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string LandmarkType
    {
        get => landmarkType;
        set
        {
            if (SetField(ref landmarkType, value ?? string.Empty))
            {
                CancelPendingSearch();
                searchCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string LandmarkSubtypes
    {
        get => landmarkSubtypes;
        set
        {
            if (SetField(ref landmarkSubtypes, value ?? string.Empty))
            {
                CancelPendingSearch();
                searchCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string LandmarkVariants
    {
        get => landmarkVariants;
        set
        {
            if (SetField(ref landmarkVariants, value ?? string.Empty))
            {
                CancelPendingSearch();
                searchCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ReferenceSystemName
    {
        get => referenceSystemName;
        private set => SetField(ref referenceSystemName, value);
    }

    public string ReferencePosition => referencePosition?.ToString() ?? Unavailable;

    public bool HasReferenceSystemAddress => referenceSystemAddress is > 0;

    public long? ReferenceSystemAddress => referenceSystemAddress;

    public string ReferenceSystemAddressText => SystemAddressFormatter.Format(referenceSystemAddress);

    public string ReferenceSummary =>
        referencePosition is null ? "Waiting for current-system coordinates" : $"Searching from {ReferenceSystemName}";

    public IReadOnlyList<NearestSystemRowViewModel> Results
    {
        get => results;
        private set
        {
            if (SetField(ref results, value))
            {
                OnPropertyChanged(nameof(HasResults));
            }
        }
    }

    public bool HasResults => Results.Count > 0;

    public NearestSystemRowViewModel? SelectedResult
    {
        get => selectedResult;
        set
        {
            if (!SetField(ref selectedResult, value))
            {
                return;
            }

            RaiseSelectedResultCommands();
            OnPropertyChanged(nameof(HasSpanshSearchReference));
            openSpanshSearchCommand.RaiseCanExecuteChanged();
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public string SearchButtonText =>
        IsSearching
            ? "Searching\u2026"
            : (IsCodexMode) switch
            {
                true => "Find nearest",
                false => "Find landmarks",
            };

    public bool IsSearching
    {
        get => isSearching;
        private set
        {
            if (!SetField(ref isSearching, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SearchButtonText));
            searchCommand.RaiseCanExecuteChanged();
            RaiseSelectedResultCommands();
            openSpanshSearchCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasSpanshSearchReference => !string.IsNullOrWhiteSpace(SpanshSearchReference);

    public ICommand SearchCommand => searchCommand;

    public ICommand CopySystemCommand => copySystemCommand;

    public ICommand CopyCoordinatesCommand => copyCoordinatesCommand;

    public ICommand OpenCanonnCommand => openCanonnCommand;

    public ICommand OpenSpanshCommand => openSpanshCommand;

    public ICommand OpenSpanshSearchCommand => openSpanshSearchCommand;

    public void UpdateContext(
        string? systemName,
        GalacticCoordinate? position,
        string? currentCommanderName,
        long? systemAddress = null
    )
    {
        string nextSystemName = string.IsNullOrWhiteSpace(systemName) ? Unavailable : systemName;
        long? nextSystemAddress = systemAddress is > 0 ? systemAddress : null;
        string nextCommanderName = currentCommanderName?.Trim() ?? string.Empty;
        if (
            string.Equals(referenceSystemName, nextSystemName, StringComparison.OrdinalIgnoreCase)
            && referencePosition == position
            && referenceSystemAddress == nextSystemAddress
            && string.Equals(commanderName, nextCommanderName, StringComparison.Ordinal)
        )
        {
            return;
        }

        searchVersion++;
        cancelSearch?.Invoke();
        cancelSearch = null;
        IsSearching = false;
        ReferenceSystemName = nextSystemName;
        referencePosition = position;
        referenceSystemAddress = nextSystemAddress;
        commanderName = nextCommanderName;
        OnPropertyChanged(nameof(ReferencePosition));
        OnPropertyChanged(nameof(HasReferenceSystemAddress));
        OnPropertyChanged(nameof(ReferenceSystemAddress));
        OnPropertyChanged(nameof(ReferenceSystemAddressText));
        OnPropertyChanged(nameof(ReferenceSummary));
        searchCommand.RaiseCanExecuteChanged();
    }

    public void SetPlatformServices(Func<string, Task>? writer, Func<Uri, Task<bool>>? launcher)
    {
        clipboardWriter = writer;
        uriLauncher = launcher;
    }

    /// <summary>Stops work when the workspace owner closes; stale callbacks cannot repopulate its results.</summary>
    public void Dispose()
    {
        disposed = true;
        searchVersion++;
        cancelSearch?.Invoke();
        cancelSearch = null;
        IsSearching = false;
        SetPlatformServices(null, null);
    }

    /// <summary>Autofills the same editable Codex search used by the workspace, retaining exact descendants.</summary>
    public async Task SearchCodexAsync(CodexSearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        SelectedMode = Modes.Single(option => option.Mode == NearestSystemsSearchMode.Codex);
        CodexName = query.Label;
        selectedQuery = query;
        OnPropertyChanged(nameof(CriteriaSummary));
        await SearchAsync();
    }

    /// <summary>Cancels superseded work and keeps results tied to their captured reference coordinates.</summary>
    public async Task SearchAsync()
    {
        if (disposed)
        {
            return;
        }
        if (referencePosition is not { } position)
        {
            StatusMessage = "Current-system coordinates are required before searching.";
            return;
        }
        if (!HasValidInputs())
        {
            StatusMessage = IsCodexMode
                ? "Choose a Codex entry or group."
                : "Choose a landmark type or subtype. A variant is optional.";
            return;
        }
        int version = ++searchVersion;
        cancelSearch?.Invoke();
        using var operation = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        cancelSearch = operation.Cancel;
        try
        {
            IsSearching = true;
            Results = [];
            SelectedResult = null;
            SpanshSearchReference = null;
            CoverageNote = string.Empty;
            ResultOrigin =
                $"Nearest published matches from {ReferenceSystemName}; up to 20 unique systems, no distance cutoff.";
            StatusMessage = "Searching published Codex locations…";
            NearestSystemsSearchResult searchResult = IsCodexMode
                ? await nearestSystemsClient.SearchCodexAsync(
                    position,
                    selectedQuery ?? catalog.Resolve(CodexName),
                    commanderName,
                    operation.Token
                )
                : await nearestSystemsClient.SearchLandmarksAsync(
                    position,
                    new CodexLandmarkFilter(
                        LandmarkType.Trim(),
                        ParseValues(LandmarkSubtypes),
                        ParseValues(LandmarkVariants)
                    ),
                    operation.Token
                );
            if (version != searchVersion)
            {
                return;
            }
            Results = searchResult.Rows.Select(row => new NearestSystemRowViewModel(row)).ToArray();
            SelectedResult = Results.Count > 0 ? Results[0] : null;
            SpanshSearchReference = searchResult.SpanshSearchReference;
            CoverageNote = searchResult.CoverageNote ?? string.Empty;
            StatusMessage =
                Results.Count == 0
                    ? "No published locations matched these criteria. No distance cutoff was applied."
                    : $"Found {Results.Count:N0} matching system(s), nearest first.";
        }
        catch (OperationCanceledException)
        {
            if (version == searchVersion)
            {
                StatusMessage = "The Codex location search timed out.";
            }
        }
        catch (Exception exception)
            when (exception is HttpRequestException or JsonException or InvalidDataException or ArgumentException)
        {
            if (version == searchVersion)
            {
                StatusMessage = "The Codex location search failed: " + exception.Message;
            }
        }
        finally
        {
            if (version == searchVersion)
            {
                cancelSearch = null;
                IsSearching = false;
            }
        }
    }

    public Task CopySystemAsync()
    {
        return CopySelectedTextAsync(SelectedResult?.SystemName, "system name");
    }

    public Task CopyCoordinatesAsync()
    {
        return CopySelectedTextAsync(SelectedResult?.Coordinate.ToString(), "galactic coordinates");
    }

    public Task OpenCanonnAsync()
    {
        if (SelectedResult is not { } selected)
        {
            StatusMessage = "Select a result first.";
            return Task.CompletedTask;
        }

        string system = Uri.EscapeDataString(selected.SystemName);
        return LaunchAsync(new Uri($"https://signals.canonn.tech/?system={system}"), "Canonn Signals");
    }

    public async Task OpenSpanshAsync()
    {
        if (SelectedResult is not { } selected)
        {
            StatusMessage = "Select a result first.";
            return;
        }

        long? address = selected.SystemAddress;
        if (address is null)
        {
            try
            {
                StatusMessage = $"Resolving {selected.SystemName} on Spansh\u2026";
                IReadOnlyList<StarSystemReference> systems = await systemResolver.SearchAsync(selected.SystemName);
                address = systems
                    .FirstOrDefault(system =>
                        string.Equals(system.Name, selected.SystemName, StringComparison.OrdinalIgnoreCase)
                    )
                    ?.SystemAddress;
            }
            catch (Exception exception)
                when (exception is HttpRequestException or JsonException or TaskCanceledException)
            {
                StatusMessage = "The Spansh system address could not be resolved: " + exception.Message;
                return;
            }
        }

        if (address is null or <= 0)
        {
            StatusMessage = "Spansh did not return an address for the selected system.";
            return;
        }

        await LaunchAsync(new Uri($"https://spansh.co.uk/system/{address.Value}"), "Spansh");
    }

    public Task OpenOriginalSpanshSearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SpanshSearchReference))
        {
            StatusMessage = "Run a Spansh landmark search first.";
            return Task.CompletedTask;
        }

        string reference = Uri.EscapeDataString(SpanshSearchReference);
        return LaunchAsync(new Uri($"https://spansh.co.uk/bodies/search/{reference}/1"), "the original Spansh search");
    }

    private string? SpanshSearchReference
    {
        get => SelectedResult?.SpanshSearchReference ?? spanshSearchReference;
        set
        {
            if (!SetField(ref spanshSearchReference, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasSpanshSearchReference));
            openSpanshSearchCommand.RaiseCanExecuteChanged();
        }
    }

    private bool CanSearch()
    {
        return !disposed && !IsSearching && referencePosition is not null && HasValidInputs();
    }

    private void CancelPendingSearch()
    {
        if (cancelSearch is null)
        {
            return;
        }
        searchVersion++;
        cancelSearch();
        cancelSearch = null;
        IsSearching = false;
        StatusMessage = "Search criteria changed. Run the search again.";
    }

    private bool HasValidInputs() =>
        IsCodexMode
            ? !string.IsNullOrWhiteSpace(CodexName)
            : !string.IsNullOrWhiteSpace(LandmarkType) || ParseValues(LandmarkSubtypes).Length > 0;

    private static string[] ParseValues(string text) =>
        text.Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string ShortEntryName(string name, string label)
    {
        string prefix = label + " - ";
        if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return name[prefix.Length..];
        }
        string suffix = " " + label;
        return name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? name[..^suffix.Length] : name;
    }

    private bool CanUseSelectedResult()
    {
        return SelectedResult is not null && !IsSearching;
    }

    private async Task CopySelectedTextAsync(string? text, string label)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            StatusMessage = "Select a result first.";
            return;
        }

        if (clipboardWriter is null)
        {
            StatusMessage = "The desktop clipboard is not available.";
            return;
        }

        try
        {
            await clipboardWriter(text);
            StatusMessage = $"Copied the {label}.";
        }
        catch (Exception exception)
            when (exception is InvalidOperationException or NotSupportedException or UnauthorizedAccessException)
        {
            StatusMessage = $"The {label} could not be copied: " + exception.Message;
        }
    }

    private async Task LaunchAsync(Uri uri, string label)
    {
        if (uriLauncher is null)
        {
            StatusMessage = "The desktop link launcher is not available.";
            return;
        }

        try
        {
            bool launched = await uriLauncher(uri);
            StatusMessage = launched ? $"Opened {label}." : $"The operating system could not open {label}.";
        }
        catch (Exception exception)
            when (exception is InvalidOperationException or NotSupportedException or UriFormatException)
        {
            StatusMessage = $"{label} could not be opened: " + exception.Message;
        }
    }

    private void RaiseSelectedResultCommands()
    {
        copySystemCommand.RaiseCanExecuteChanged();
        copyCoordinatesCommand.RaiseCanExecuteChanged();
        openCanonnCommand.RaiseCanExecuteChanged();
        openSpanshCommand.RaiseCanExecuteChanged();
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
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

    private sealed class AsyncCommand(Func<Task> execute, Func<bool> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            return canExecute();
        }

        public async void Execute(object? parameter)
        {
            if (CanExecute(parameter))
            {
                await execute();
            }
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

public enum NearestSystemsSearchMode
{
    Codex,
    Landmarks,
}

public sealed record NearestSystemsSearchModeOptionViewModel(
    NearestSystemsSearchMode Mode,
    string Label,
    string Description
);

public sealed class NearestSystemRowViewModel
{
    public NearestSystemRowViewModel(NearestSystemSearchRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        SystemName = row.SystemName;
        Distance = $"{row.Distance:N1} ly";
        Notes = row.Notes;
        Coordinate = row.Coordinate;
        SystemAddress = row.SystemAddress;
        Source = row.Source == NearestSystemSource.Combined ? "Spansh + Canonn" : row.Source.ToString();
        SpanshSearchReference = row.SpanshSearchReference;
    }

    public string SystemName { get; }

    public string Distance { get; }

    public string Notes { get; }

    public GalacticCoordinate Coordinate { get; }

    public long? SystemAddress { get; }

    public bool HasSystemAddress => SystemAddress is > 0;

    public string SystemAddressText => SystemAddressFormatter.Format(SystemAddress);

    public string Source { get; }

    public string? SpanshSearchReference { get; }
}

internal static class SystemAddressFormatter
{
    public static string Format(long? systemAddress)
    {
        string value = FormatValue(systemAddress);
        return value.Length == 0 ? string.Empty : "id64 " + value;
    }

    public static string FormatValue(long? systemAddress)
    {
        return systemAddress is > 0
            ? systemAddress.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;
    }
}
