using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class NearestSystemsViewModel : INotifyPropertyChanged
{
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
    private string biologicalSignal = string.Empty;
    private string genus = string.Empty;
    private string species = string.Empty;
    private string variantColors = string.Empty;
    private string referenceSystemName = Unavailable;
    private GalacticCoordinate? referencePosition;
    private string commanderName = string.Empty;
    private bool isSearching;
    private string statusMessage =
        "Enter a biological signal or missing variants to find nearby systems.";
    private IReadOnlyList<NearestSystemRowViewModel> results = [];
    private NearestSystemRowViewModel? selectedResult;
    private string? spanshSearchReference;
    private Func<string, Task>? clipboardWriter;
    private Func<Uri, Task<bool>>? uriLauncher;

    public NearestSystemsViewModel(
        INearestSystemsClient nearestSystemsClient,
        IStarSystemResolver systemResolver)
    {
        this.nearestSystemsClient = nearestSystemsClient
            ?? throw new ArgumentNullException(nameof(nearestSystemsClient));
        this.systemResolver = systemResolver
            ?? throw new ArgumentNullException(nameof(systemResolver));
        Modes =
        [
            new(
                NearestSystemsSearchMode.CanonnSignal,
                "Biological signal",
                "Find the nearest systems containing a Canonn codex signal."),
            new(
                NearestSystemsSearchMode.MissingVariants,
                "Missing variants",
                "Find nearby bodies with selected biological color variants."),
        ];
        selectedMode = Modes[0];
        searchCommand = new AsyncCommand(SearchAsync, CanSearch);
        copySystemCommand = new AsyncCommand(
            CopySystemAsync,
            CanUseSelectedResult);
        copyCoordinatesCommand = new AsyncCommand(
            CopyCoordinatesAsync,
            CanUseSelectedResult);
        openCanonnCommand = new AsyncCommand(
            OpenCanonnAsync,
            CanUseSelectedResult);
        openSpanshCommand = new AsyncCommand(
            OpenSpanshAsync,
            CanUseSelectedResult);
        openSpanshSearchCommand = new AsyncCommand(
            OpenOriginalSpanshSearchAsync,
            () => HasSpanshSearchReference && !IsSearching);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<NearestSystemsSearchModeOptionViewModel> Modes { get; }

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

            Results = [];
            SelectedResult = null;
            SpanshSearchReference = null;
            StatusMessage = value.Description;
            OnPropertyChanged(nameof(IsCanonnMode));
            OnPropertyChanged(nameof(IsVariantMode));
            OnPropertyChanged(nameof(SearchButtonText));
            searchCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsCanonnMode => SelectedMode.Mode
        == NearestSystemsSearchMode.CanonnSignal;

    public bool IsVariantMode => SelectedMode.Mode
        == NearestSystemsSearchMode.MissingVariants;

    public string BiologicalSignal
    {
        get => biologicalSignal;
        set
        {
            if (SetField(ref biologicalSignal, value ?? string.Empty))
            {
                searchCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string Genus
    {
        get => genus;
        set
        {
            if (SetField(ref genus, value ?? string.Empty))
            {
                searchCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string Species
    {
        get => species;
        set
        {
            if (SetField(ref species, value ?? string.Empty))
            {
                searchCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string VariantColors
    {
        get => variantColors;
        set
        {
            if (SetField(ref variantColors, value ?? string.Empty))
            {
                searchCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ReferenceSystemName
    {
        get => referenceSystemName;
        private set => SetField(ref referenceSystemName, value);
    }

    public string ReferencePosition => referencePosition?.ToString()
        ?? Unavailable;

    public string ReferenceSummary => referencePosition is null
        ? "Waiting for current-system coordinates"
        : $"Searching from {ReferenceSystemName}";

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
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public string SearchButtonText => IsSearching
        ? "Searching\u2026"
        : IsCanonnMode
            ? "Find nearest"
            : "Find variants";

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

    public bool HasSpanshSearchReference => !string.IsNullOrWhiteSpace(
        SpanshSearchReference);

    public ICommand SearchCommand => searchCommand;

    public ICommand CopySystemCommand => copySystemCommand;

    public ICommand CopyCoordinatesCommand => copyCoordinatesCommand;

    public ICommand OpenCanonnCommand => openCanonnCommand;

    public ICommand OpenSpanshCommand => openSpanshCommand;

    public ICommand OpenSpanshSearchCommand => openSpanshSearchCommand;

    public void UpdateContext(
        string? systemName,
        GalacticCoordinate? position,
        string? currentCommanderName)
    {
        var nextSystemName = string.IsNullOrWhiteSpace(systemName)
            ? Unavailable
            : systemName;
        var nextCommanderName = currentCommanderName?.Trim() ?? string.Empty;
        if (string.Equals(
                referenceSystemName,
                nextSystemName,
                StringComparison.OrdinalIgnoreCase)
            && referencePosition == position
            && string.Equals(
                commanderName,
                nextCommanderName,
                StringComparison.Ordinal))
        {
            return;
        }

        ReferenceSystemName = nextSystemName;
        referencePosition = position;
        commanderName = nextCommanderName;
        OnPropertyChanged(nameof(ReferencePosition));
        OnPropertyChanged(nameof(ReferenceSummary));
        searchCommand.RaiseCanExecuteChanged();
    }

    public void SetPlatformServices(
        Func<string, Task>? writer,
        Func<Uri, Task<bool>>? launcher)
    {
        clipboardWriter = writer;
        uriLauncher = launcher;
    }

    public async Task SearchCodexSignalAsync(string signal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signal);
        SelectedMode = Modes.Single(option =>
            option.Mode == NearestSystemsSearchMode.CanonnSignal);
        BiologicalSignal = signal.Trim();
        await SearchAsync();
    }

    public async Task SearchCodexVariantsAsync(
        string genus,
        string species,
        IReadOnlyList<string> variants)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(genus);
        ArgumentException.ThrowIfNullOrWhiteSpace(species);
        ArgumentNullException.ThrowIfNull(variants);
        SelectedMode = Modes.Single(option =>
            option.Mode == NearestSystemsSearchMode.MissingVariants);
        Genus = genus.Trim();
        Species = species.Trim();
        VariantColors = string.Join(", ", variants.Where(
            variant => !string.IsNullOrWhiteSpace(variant)));
        await SearchAsync();
    }

    public async Task SearchAsync()
    {
        if (referencePosition is not { } position)
        {
            StatusMessage =
                "Current-system coordinates are required before searching.";
            return;
        }

        if (!HasValidInputs())
        {
            StatusMessage = IsCanonnMode
                ? "Enter the biological signal to find."
                : "Enter a genus, species, and at least one variant color.";
            return;
        }

        try
        {
            IsSearching = true;
            Results = [];
            SelectedResult = null;
            SpanshSearchReference = null;
            StatusMessage = $"Searching near {ReferenceSystemName}\u2026";
            var searchResult = IsCanonnMode
                ? await nearestSystemsClient.SearchCanonnAsync(
                    position,
                    BiologicalSignal.Trim(),
                    commanderName)
                : await nearestSystemsClient.SearchMissingVariantsAsync(
                    position,
                    Genus.Trim(),
                    Species.Trim(),
                    ParseVariantColors());
            Results = searchResult.Rows
                .Select(row => new NearestSystemRowViewModel(row))
                .ToArray();
            SelectedResult = Results.Count > 0 ? Results[0] : null;
            SpanshSearchReference = searchResult.SpanshSearchReference;
            StatusMessage = Results.Count == 0
                ? "No nearby systems matched this search."
                : $"Found {Results.Count:N0} nearby system(s).";
        }
        catch (TaskCanceledException)
        {
            StatusMessage = "The nearby-system search timed out.";
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or JsonException
                or InvalidDataException)
        {
            StatusMessage = "The nearby-system search failed: "
                + exception.Message;
        }
        finally
        {
            IsSearching = false;
        }
    }

    public Task CopySystemAsync()
    {
        return CopySelectedTextAsync(
            SelectedResult?.SystemName,
            "system name");
    }

    public Task CopyCoordinatesAsync()
    {
        return CopySelectedTextAsync(
            SelectedResult?.Coordinate.ToString(),
            "galactic coordinates");
    }

    public Task OpenCanonnAsync()
    {
        if (SelectedResult is not { } selected)
        {
            StatusMessage = "Select a result first.";
            return Task.CompletedTask;
        }

        var system = Uri.EscapeDataString(selected.SystemName);
        return LaunchAsync(
            new Uri($"https://signals.canonn.tech/?system={system}"),
            "Canonn Signals");
    }

    public async Task OpenSpanshAsync()
    {
        if (SelectedResult is not { } selected)
        {
            StatusMessage = "Select a result first.";
            return;
        }

        var address = selected.SystemAddress;
        if (address is null)
        {
            try
            {
                StatusMessage = $"Resolving {selected.SystemName} on Spansh\u2026";
                var systems = await systemResolver.SearchAsync(
                    selected.SystemName);
                address = systems.FirstOrDefault(system =>
                    string.Equals(
                        system.Name,
                        selected.SystemName,
                        StringComparison.OrdinalIgnoreCase))?.SystemAddress;
            }
            catch (Exception exception) when (
                exception is HttpRequestException
                    or JsonException
                    or TaskCanceledException)
            {
                StatusMessage = "The Spansh system address could not be resolved: "
                    + exception.Message;
                return;
            }
        }

        if (address is null or <= 0)
        {
            StatusMessage =
                "Spansh did not return an address for the selected system.";
            return;
        }

        await LaunchAsync(
            new Uri($"https://spansh.co.uk/system/{address.Value}"),
            "Spansh");
    }

    public Task OpenOriginalSpanshSearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SpanshSearchReference))
        {
            StatusMessage = "Run a missing-variant search first.";
            return Task.CompletedTask;
        }

        var reference = Uri.EscapeDataString(SpanshSearchReference);
        return LaunchAsync(
            new Uri($"https://spansh.co.uk/bodies/search/{reference}/1"),
            "the original Spansh search");
    }

    private string? SpanshSearchReference
    {
        get => spanshSearchReference;
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
        return !IsSearching
            && referencePosition is not null
            && HasValidInputs();
    }

    private bool HasValidInputs()
    {
        return IsCanonnMode
            ? !string.IsNullOrWhiteSpace(BiologicalSignal)
            : !string.IsNullOrWhiteSpace(Genus)
                && !string.IsNullOrWhiteSpace(Species)
                && ParseVariantColors().Length > 0;
    }

    private string[] ParseVariantColors()
    {
        return VariantColors.Split(
                [',', ';', '\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
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
        catch (Exception exception) when (
            exception is InvalidOperationException
                or NotSupportedException
                or UnauthorizedAccessException)
        {
            StatusMessage = $"The {label} could not be copied: "
                + exception.Message;
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
            var launched = await uriLauncher(uri);
            StatusMessage = launched
                ? $"Opened {label}."
                : $"The operating system could not open {label}.";
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or NotSupportedException
                or UriFormatException)
        {
            StatusMessage = $"{label} could not be opened: "
                + exception.Message;
        }
    }

    private void RaiseSelectedResultCommands()
    {
        copySystemCommand.RaiseCanExecuteChanged();
        copyCoordinatesCommand.RaiseCanExecuteChanged();
        openCanonnCommand.RaiseCanExecuteChanged();
        openSpanshCommand.RaiseCanExecuteChanged();
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

    private sealed class AsyncCommand(
        Func<Task> execute,
        Func<bool> canExecute) : ICommand
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
    CanonnSignal,
    MissingVariants,
}

public sealed record NearestSystemsSearchModeOptionViewModel(
    NearestSystemsSearchMode Mode,
    string Label,
    string Description);

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
        Source = row.Source.ToString();
    }

    public string SystemName { get; }

    public string Distance { get; }

    public string Notes { get; }

    public GalacticCoordinate Coordinate { get; }

    public long? SystemAddress { get; }

    public string Source { get; }
}
