using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class SphereLimitViewModel : INotifyPropertyChanged
{
    private const string Unavailable = "—";

    private readonly CommanderProfileStore profileStore;
    private readonly IStarSystemResolver systemResolver;
    private readonly SphereLimitState state = new();
    private readonly AsyncCommand searchCommand;
    private readonly AsyncCommand enableCommand;
    private readonly AsyncCommand disableCommand;
    private IReadOnlyList<StarSystemReference> searchResults = [];
    private StarSystemReference? selectedCenterSystem;
    private string query = string.Empty;
    private string radius = SphereLimitState.DefaultRadius.ToString(
        "G",
        CultureInfo.CurrentCulture);
    private string statusMessage = "Waiting for a commander profile.";
    private string currentSystemName = Unavailable;
    private GalacticCoordinate? currentPosition;
    private string centerPosition = Unavailable;
    private string distanceToCenter = Unavailable;
    private string limitSummary = "No spherical limit configured";
    private string currentSystemResult = "Waiting for system coordinates";
    private string destinationSystemName = "n/a";
    private string destinationDistance = Unavailable;
    private string destinationResult = "No Galaxy Map destination selected";
    private bool isDestinationInside;
    private bool isDestinationUnknown;
    private EliteStatus? status;
    private string? musicTrack;
    private long destinationSystemAddress;
    private GalacticCoordinate? resolvedDestinationPosition;
    private NavRouteSnapshot? latestNavRoute;
    private bool isSearching;
    private string? frontierId;
    private string? commanderName;
    private bool isOdyssey = true;

    public SphereLimitViewModel(
        CommanderProfileStore profileStore,
        IStarSystemResolver systemResolver)
    {
        this.profileStore = profileStore
            ?? throw new ArgumentNullException(nameof(profileStore));
        this.systemResolver = systemResolver
            ?? throw new ArgumentNullException(nameof(systemResolver));
        searchCommand = new AsyncCommand(SearchSystemsAsync, CanSearch);
        SearchCommand = searchCommand;
        enableCommand = new AsyncCommand(EnableAsync, CanEnable);
        EnableCommand = enableCommand;
        disableCommand = new AsyncCommand(DisableAsync, CanDisable);
        DisableCommand = disableCommand;
        UpdateDisplay();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Query
    {
        get => query;
        set
        {
            if (!SetField(ref query, value))
            {
                return;
            }

            if (selectedCenterSystem is not null
                && !string.Equals(
                    selectedCenterSystem.Name,
                    value?.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                SelectedCenterSystem = null;
            }

            searchCommand.RaiseCanExecuteChanged();
        }
    }

    public string Radius
    {
        get => radius;
        set => SetField(ref radius, value);
    }

    public IReadOnlyList<StarSystemReference> SearchResults
    {
        get => searchResults;
        private set
        {
            if (SetField(ref searchResults, value))
            {
                OnPropertyChanged(nameof(HasSearchResults));
            }
        }
    }

    public bool HasSearchResults => SearchResults.Count > 0;

    public StarSystemReference? SelectedCenterSystem
    {
        get => selectedCenterSystem;
        set
        {
            if (SetField(ref selectedCenterSystem, value))
            {
                enableCommand.RaiseCanExecuteChanged();
                UpdateDisplay();
            }
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public string CurrentSystemName
    {
        get => currentSystemName;
        private set => SetField(ref currentSystemName, value);
    }

    public string CurrentPosition => currentPosition?.ToString() ?? Unavailable;

    public string CenterSystemName => selectedCenterSystem?.Name
        ?? state.CenterSystemName
        ?? Unavailable;

    public string CenterPosition
    {
        get => centerPosition;
        private set => SetField(ref centerPosition, value);
    }

    public string DistanceToCenter
    {
        get => distanceToCenter;
        private set => SetField(ref distanceToCenter, value);
    }

    public string LimitSummary
    {
        get => limitSummary;
        private set => SetField(ref limitSummary, value);
    }

    public string CurrentSystemResult
    {
        get => currentSystemResult;
        private set => SetField(ref currentSystemResult, value);
    }

    public bool IsActive => state.IsActive;

    public bool ShouldShowGalaxyMapOverlay => IsGalaxyMapOpen && state.IsActive;

    private bool IsGalaxyMapOpen => OverlayGameModeResolver.Resolve(
        status,
        musicTrack: musicTrack) == OverlayGameMode.GalaxyMap;

    public string DestinationSystemName
    {
        get => destinationSystemName;
        private set => SetField(ref destinationSystemName, value);
    }

    public string DestinationDistance
    {
        get => destinationDistance;
        private set => SetField(ref destinationDistance, value);
    }

    public string DestinationResult
    {
        get => destinationResult;
        private set => SetField(ref destinationResult, value);
    }

    public bool IsDestinationInside
    {
        get => isDestinationInside;
        private set => SetField(ref isDestinationInside, value);
    }

    public bool IsDestinationUnknown
    {
        get => isDestinationUnknown;
        private set => SetField(ref isDestinationUnknown, value);
    }

    public string StatusLabel => state.IsActive ? "ACTIVE" : "INACTIVE";

    public string SearchButtonText => IsSearching ? "Searching…" : "Find system";

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
            enableCommand.RaiseCanExecuteChanged();
        }
    }

    public ICommand SearchCommand { get; }

    public ICommand EnableCommand { get; }

    public ICommand DisableCommand { get; }

    public void LoadProfile(
        string profileFrontierId,
        string? profileCommanderName,
        bool profileIsOdyssey,
        SphereLimitSnapshot snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileFrontierId);
        ArgumentNullException.ThrowIfNull(snapshot);
        frontierId = profileFrontierId;
        commanderName = profileCommanderName;
        isOdyssey = profileIsOdyssey;
        state.Reset(snapshot);
        Radius = state.Radius.ToString("G", CultureInfo.CurrentCulture);
        SearchResults = [];
        if (state.Center is { } center
            && state.CenterSystemName is { } centerName)
        {
            var savedCenter = new StarSystemReference(centerName, 0, center);
            Query = centerName;
            SearchResults = [savedCenter];
            SelectedCenterSystem = savedCenter;
        }
        else
        {
            Query = string.Empty;
            SelectedCenterSystem = null;
        }

        if (state.IsActive)
        {
            StatusMessage = "Loaded the active legacy spherical limit.";
        }
        else if (state.Center is not null)
        {
            StatusMessage = "Loaded the saved spherical limit; it is currently disabled.";
        }
        else
        {
            StatusMessage = "No spherical limit is configured for this commander.";
        }
        enableCommand.RaiseCanExecuteChanged();
        disableCommand.RaiseCanExecuteChanged();
        UpdateDisplay();
    }

    public void SetProfileError(string message)
    {
        frontierId = null;
        StatusMessage = message;
        enableCommand.RaiseCanExecuteChanged();
        disableCommand.RaiseCanExecuteChanged();
    }

    public void UpdateCurrentSystem(
        string? systemName,
        GalacticCoordinate? position)
    {
        var nextSystemName = string.IsNullOrWhiteSpace(systemName)
            ? Unavailable
            : systemName;
        if (string.Equals(
                currentSystemName,
                nextSystemName,
                StringComparison.OrdinalIgnoreCase)
            && currentPosition == position)
        {
            return;
        }

        CurrentSystemName = nextSystemName;
        currentPosition = position;
        OnPropertyChanged(nameof(CurrentPosition));
        UpdateDisplay();
    }

    public async Task UpdateNavigationAsync(
        NavRouteSnapshot? navRoute,
        EliteStatus? nextStatus,
        string? nextMusicTrack = null)
    {
        ApplyNavigationInputs(navRoute, nextStatus, nextMusicTrack);
        var destination = ResolveRouteDestination();
        if (destination is null)
        {
            ClearDestinationDisplay();
            return;
        }

        var targetChanged = ApplyDestinationIdentity(destination.Value);
        var destinationName = destination.Value.Name;
        var destinationAddress = destination.Value.Address;
        var destinationPosition = resolvedDestinationPosition;
        if (!state.IsActive)
        {
            SetInactiveDestinationDisplay();
            return;
        }

        if (destinationPosition is null && targetChanged)
        {
            destinationPosition = await ResolveDestinationPositionAsync(
                destinationName,
                destinationAddress);
        }

        if (destinationPosition is null)
        {
            ApplyUnknownDestinationDisplay();
            return;
        }

        ApplyDestinationEvaluation(destinationName, destinationPosition.Value);
    }

    private void ApplyNavigationInputs(
        NavRouteSnapshot? navRoute,
        EliteStatus? nextStatus,
        string? nextMusicTrack)
    {
        if (navRoute is not null)
        {
            latestNavRoute = navRoute;
        }

        if (nextStatus is not null)
        {
            status = nextStatus;
        }

        musicTrack = nextMusicTrack;
        OnPropertyChanged(nameof(ShouldShowGalaxyMapOverlay));
    }

    private bool ApplyDestinationIdentity(
        (string Name, long Address, GalacticCoordinate? Position) destination)
    {
        var targetChanged = destinationSystemAddress != destination.Address
            || !string.Equals(
                DestinationSystemName,
                destination.Name,
                StringComparison.OrdinalIgnoreCase);
        if (targetChanged)
        {
            resolvedDestinationPosition = null;
        }

        destinationSystemAddress = destination.Address;
        DestinationSystemName = destination.Name;
        resolvedDestinationPosition = destination.Position
            ?? resolvedDestinationPosition;
        return targetChanged;
    }

    private async Task<GalacticCoordinate?> ResolveDestinationPositionAsync(
        string destinationName,
        long destinationAddress)
    {
        DestinationDistance = Unavailable;
        DestinationResult = "Resolving destination coordinates…";
        IsDestinationInside = false;
        IsDestinationUnknown = false;
        try
        {
            var matches = await systemResolver.SearchAsync(destinationName);
            var destinationPosition = SelectDestinationPosition(
                matches,
                destinationName,
                destinationAddress);
            resolvedDestinationPosition = destinationPosition;
            return destinationPosition;
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or TaskCanceledException
                or System.Text.Json.JsonException)
        {
            DestinationResult = "Destination coordinates are unavailable: "
                + exception.Message;
            return null;
        }
    }

    private static GalacticCoordinate? SelectDestinationPosition(
        IReadOnlyList<StarSystemReference> matches,
        string destinationName,
        long destinationAddress)
    {
        return matches.FirstOrDefault(candidate =>
                destinationAddress > 0
                && candidate.SystemAddress == destinationAddress)
            ?.Position
            ?? matches.FirstOrDefault(candidate => string.Equals(
                candidate.Name,
                destinationName,
                StringComparison.OrdinalIgnoreCase))
            ?.Position;
    }

    private void ApplyUnknownDestinationDisplay()
    {
        DestinationDistance = Unavailable;
        if (!DestinationResult.StartsWith(
                "Destination coordinates are unavailable:",
                StringComparison.Ordinal))
        {
            DestinationResult = "Destination distance is unknown";
        }

        IsDestinationInside = false;
        IsDestinationUnknown = true;
    }

    private void ApplyDestinationEvaluation(
        string destinationName,
        GalacticCoordinate destinationPosition)
    {
        var evaluation = state.Evaluate(destinationName, destinationPosition);
        if (evaluation is null)
        {
            DestinationDistance = Unavailable;
            DestinationResult = "The spherical limit is disabled";
            IsDestinationInside = false;
        }
        else
        {
            DestinationDistance = $"{evaluation.Distance:N2} ly";
            DestinationResult = evaluation.IsInside
                ? $"Within the {state.Radius:N2} ly limit"
                : $"Exceeds the {state.Radius:N2} ly limit";
            IsDestinationInside = evaluation.IsInside;
        }
        IsDestinationUnknown = false;
    }

    private (string Name, long Address, GalacticCoordinate? Position)?
        ResolveRouteDestination()
    {
        var routeDestination = latestNavRoute?.Route.Count > 1
            ? latestNavRoute.Route[^1]
            : null;
        if (routeDestination is not null)
        {
            if (string.IsNullOrWhiteSpace(routeDestination.StarSystem))
            {
                return null;
            }

            return (
                routeDestination.StarSystem,
                routeDestination.SystemAddress,
                routeDestination.Position);
        }

        var statusDestination = status?.Destination;
        if (statusDestination is null
            || string.IsNullOrWhiteSpace(statusDestination.Name))
        {
            return null;
        }

        return (
            statusDestination.Name,
            statusDestination.System,
            null);
    }

    private void ClearDestinationDisplay()
    {
        destinationSystemAddress = 0;
        resolvedDestinationPosition = null;
        DestinationSystemName = "n/a";
        DestinationDistance = Unavailable;
        DestinationResult = "No Galaxy Map destination selected";
        IsDestinationInside = false;
        IsDestinationUnknown = false;
    }

    private void SetInactiveDestinationDisplay()
    {
        DestinationDistance = Unavailable;
        DestinationResult = "The spherical limit is disabled";
        IsDestinationInside = false;
        IsDestinationUnknown = false;
    }

    public async Task SearchSystemsAsync()
    {
        if (!CanSearch())
        {
            StatusMessage = "Enter a system name to search.";
            return;
        }

        try
        {
            IsSearching = true;
            StatusMessage = $"Searching for {Query.Trim()}…";
            var results = await systemResolver.SearchAsync(Query.Trim());
            SearchResults = results;
            SelectedCenterSystem = results.FirstOrDefault(system =>
                    string.Equals(
                        system.Name,
                        Query.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                ?? (results.Count > 0 ? results[0] : null);
            StatusMessage = results.Count switch
            {
                0 => "No matching system was returned by Spansh.",
                1 => "Found 1 matching system. Review it before enabling.",
                _ => $"Found {results.Count:N0} matches. Choose the center system.",
            };
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or TaskCanceledException
                or System.Text.Json.JsonException)
        {
            SearchResults = [];
            SelectedCenterSystem = null;
            StatusMessage = "The system lookup failed without changing your limit: "
                + exception.Message;
        }
        finally
        {
            IsSearching = false;
        }
    }

    public async Task EnableAsync()
    {
        if (!TryParseRadius(Radius, out var parsedRadius))
        {
            StatusMessage = $"Radius must be between "
                + $"{SphereLimitState.MinimumRadius:N0} and "
                + $"{SphereLimitState.MaximumRadius:N0} light-years.";
            return;
        }

        if (!state.TryEnable(
                SelectedCenterSystem,
                parsedRadius,
                out var error))
        {
            StatusMessage = error ?? "The spherical limit is invalid.";
            return;
        }

        await SaveAsync("Spherical limit enabled and saved.");
    }

    public async Task DisableAsync()
    {
        state.Disable();
        await SaveAsync("Spherical limit disabled; its center and radius were retained.");
    }

    private bool CanSearch()
    {
        return !IsSearching && !string.IsNullOrWhiteSpace(Query);
    }

    private bool CanEnable()
    {
        return frontierId is not null
            && SelectedCenterSystem is not null
            && !IsSearching;
    }

    private bool CanDisable()
    {
        return frontierId is not null && state.IsActive;
    }

    private async Task SaveAsync(string successMessage)
    {
        UpdateDisplay();
        enableCommand.RaiseCanExecuteChanged();
        disableCommand.RaiseCanExecuteChanged();
        if (frontierId is null)
        {
            StatusMessage = "A commander profile is required before this setting can be saved.";
            return;
        }

        try
        {
            await profileStore.SaveSphereLimitAsync(
                frontierId,
                commanderName,
                isOdyssey,
                state.CreateSnapshot());
            StatusMessage = successMessage;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            StatusMessage = "The limit changed for this session but could not be saved: "
                + exception.Message;
        }
    }

    private void UpdateDisplay()
    {
        var resolvedCenter = selectedCenterSystem?.Position ?? state.Center;
        CenterPosition = resolvedCenter?.ToString() ?? Unavailable;
        OnPropertyChanged(nameof(CenterSystemName));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(ShouldShowGalaxyMapOverlay));

        var distanceCenter = state.IsActive ? state.Center : resolvedCenter;
        var distance = distanceCenter is { } center
            && currentPosition is { } current
                ? center.DistanceTo(current)
                : (double?)null;
        DistanceToCenter = distance is null
            ? Unavailable
            : $"{distance:N2} ly";
        if (state.CenterSystemName is not null)
        {
            LimitSummary = $"{state.Radius:N0} ly around {state.CenterSystemName}";
        }
        else
        {
            LimitSummary = selectedCenterSystem is null
                ? "No spherical limit configured"
                : $"Candidate center: {selectedCenterSystem.Name}";
        }

        var evaluation = currentPosition is { } position
            ? state.Evaluate(CurrentSystemName, position)
            : null;
        CurrentSystemResult = evaluation is null
            ? (state.IsActive) switch
            {
                true => "Waiting for current system coordinates",
                false => "Enable the limit to evaluate the current system"
            }
            : (evaluation.IsInside) switch
            {
                true => "Current system is inside the limit",
                false => "Current system is outside the limit"
            };
    }

    private static bool TryParseRadius(string value, out double result)
    {
        const NumberStyles styles = NumberStyles.Float
            | NumberStyles.AllowThousands;
        if ((!double.TryParse(value, styles, CultureInfo.CurrentCulture, out result)
                && !double.TryParse(
                    value,
                    styles,
                    CultureInfo.InvariantCulture,
                    out result))
            || !SphereLimitState.IsValidRadius(result))
        {
            result = 0;
            return false;
        }

        return true;
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
