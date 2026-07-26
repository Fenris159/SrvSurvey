using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class GalaxyMapOverlayViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ISystemSummaryClient summaryClient;
    private readonly GalaxyMapSettingsStore settingsStore;
    private readonly SystemNicknameViewModel systemNicknames;
    private CancellationTokenSource? loadCancellation;
    private NavRouteSnapshot? navRoute;
    private GalaxyMapTarget? selectedTarget;
    private GalaxyMapTarget? primaryTarget;
    private GalaxyMapTarget? secondaryTarget;
    private string? currentSystemName;
    private long? currentSystemAddress;
    private GalacticCoordinate? currentPosition;
    private EliteStatus? status;
    private GalaxyMapSystemViewModel? primarySystem;
    private GalaxyMapSystemViewModel? secondarySystem;
    private IReadOnlyList<GalaxyMapFactionViewModel> factions = [];
    private bool routeWasCleared;
    private bool isLoading;
    private bool autoShow;
    private bool showFactions;
    private string settingsStatus = string.Empty;
    private string dataStatus = "Waiting for Galaxy Map data.";
    private bool disposed;

    public GalaxyMapOverlayViewModel(
        ISystemSummaryClient summaryClient,
        GalaxyMapSettingsStore settingsStore,
        SystemNicknameViewModel systemNicknames)
    {
        this.summaryClient = summaryClient
            ?? throw new ArgumentNullException(nameof(summaryClient));
        this.settingsStore = settingsStore
            ?? throw new ArgumentNullException(nameof(settingsStore));
        this.systemNicknames = systemNicknames
            ?? throw new ArgumentNullException(nameof(systemNicknames));
        var preferences = settingsStore.Load();
        autoShow = preferences.AutoShow;
        showFactions = preferences.ShowFactions;
        systemNicknames.NamesChanged += OnNamesChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool AutoShow
    {
        get => autoShow;
        set
        {
            if (SetField(ref autoShow, value))
            {
                SavePreferences();
                OnPropertyChanged(nameof(ShouldShow));
                if (value)
                {
                    RefreshTargets();
                }
                else
                {
                    CancelPendingLoad();
                }
            }
        }
    }

    public bool ShowFactions
    {
        get => showFactions;
        set
        {
            if (SetField(ref showFactions, value))
            {
                SavePreferences();
                RaiseFactionProperties();
            }
        }
    }

    public bool ShouldShow => AutoShow
        && status?.GuiFocus == GuiFocus.GalaxyMap;

    public string SettingsStatus
    {
        get => settingsStatus;
        private set
        {
            if (SetField(ref settingsStatus, value))
            {
                OnPropertyChanged(nameof(HasSettingsStatus));
            }
        }
    }

    public bool HasSettingsStatus => !string.IsNullOrWhiteSpace(SettingsStatus);

    public GalaxyMapSystemViewModel? PrimarySystem
    {
        get => primarySystem;
        private set
        {
            if (SetField(ref primarySystem, value))
            {
                OnPropertyChanged(nameof(HasPrimarySystem));
            }
        }
    }

    public bool HasPrimarySystem => PrimarySystem is not null;

    public GalaxyMapSystemViewModel? SecondarySystem
    {
        get => secondarySystem;
        private set
        {
            if (SetField(ref secondarySystem, value))
            {
                OnPropertyChanged(nameof(HasSecondarySystem));
            }
        }
    }

    public bool HasSecondarySystem => SecondarySystem is not null;

    public IReadOnlyList<GalaxyMapFactionViewModel> Factions
    {
        get => factions;
        private set
        {
            if (SetField(ref factions, value))
            {
                RaiseFactionProperties();
            }
        }
    }

    public bool HasFactions => ShowFactions && Factions.Count > 0;

    public string RouteFooter { get; private set; } = string.Empty;

    public bool HasRouteFooter => !string.IsNullOrWhiteSpace(RouteFooter);

    public bool IsLoading
    {
        get => isLoading;
        private set => SetField(ref isLoading, value);
    }

    public string DataStatus
    {
        get => dataStatus;
        private set => SetField(ref dataStatus, value);
    }

    public Task PendingLoad { get; private set; } = Task.CompletedTask;

    public void ApplyUpdate(
        string? nextCurrentSystemName,
        long? nextCurrentSystemAddress,
        GalacticCoordinate? nextCurrentPosition,
        NavRouteSnapshot? nextNavRoute,
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        EliteStatus? nextStatus,
        bool isBootstrapRead = false)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(journalEvents);
        currentSystemName = nextCurrentSystemName;
        currentSystemAddress = nextCurrentSystemAddress;
        currentPosition = nextCurrentPosition;
        if (nextNavRoute is not null)
        {
            navRoute = nextNavRoute;
            if (nextNavRoute.Route.Count > 0)
            {
                routeWasCleared = false;
            }
        }

        if (nextStatus is not null)
        {
            status = nextStatus;
        }

        foreach (var journalEvent in journalEvents)
        {
            if (isBootstrapRead)
            {
                continue;
            }

            switch (journalEvent.EventName)
            {
                case "FSDTarget":
                    selectedTarget = ParseTarget(journalEvent.Payload);
                    routeWasCleared = false;
                    break;

                case "NavRouteClear":
                    selectedTarget = null;
                    navRoute = null;
                    routeWasCleared = true;
                    break;
            }
        }

        OnPropertyChanged(nameof(ShouldShow));
        if (ShouldShow)
        {
            RefreshTargets();
        }
        else
        {
            CancelPendingLoad();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        systemNicknames.NamesChanged -= OnNamesChanged;
        CancelPendingLoad();
    }

    private void RefreshTargets()
    {
        var targets = ResolveTargets();
        SetRouteFooter();
        if (targets.Primary == primaryTarget
            && targets.Secondary == secondaryTarget
            && (targets.Primary is null || PrimarySystem is not null)
            && (targets.Secondary is null || SecondarySystem is not null))
        {
            return;
        }

        primaryTarget = targets.Primary;
        secondaryTarget = targets.Secondary;
        PrimarySystem = null;
        SecondarySystem = null;
        Factions = [];
        loadCancellation?.Cancel();
        loadCancellation?.Dispose();
        loadCancellation = new CancellationTokenSource();
        PendingLoad = LoadTargetsAsync(
            targets.Primary,
            targets.Secondary,
            loadCancellation.Token);
    }

    private (GalaxyMapTarget? Primary, GalaxyMapTarget? Secondary)
        ResolveTargets()
    {
        if (navRoute is { Route.Count: > 1 } route)
        {
            var final = route.Route[^1];
            var next = route.Route[1];
            return (
                new GalaxyMapTarget(
                    final.StarSystem,
                    final.SystemAddress,
                    "DESTINATION"),
                final.SystemAddress != next.SystemAddress
                    ? new GalaxyMapTarget(
                        next.StarSystem,
                        next.SystemAddress,
                        "NEXT JUMP")
                    : null);
        }

        if (selectedTarget is not null)
        {
            return (selectedTarget with { Label = "SELECTED" }, null);
        }

        if (status?.Destination is { Body: 0, System: > 0 } destination
            && !string.IsNullOrWhiteSpace(destination.Name))
        {
            return (
                new GalaxyMapTarget(
                    destination.Name,
                    destination.System,
                    "SELECTED"),
                null);
        }

        if (!routeWasCleared && !string.IsNullOrWhiteSpace(currentSystemName))
        {
            return (
                new GalaxyMapTarget(
                    currentSystemName,
                    currentSystemAddress ?? 0,
                    "CURRENT"),
                null);
        }

        return (null, null);
    }

    private async Task LoadTargetsAsync(
        GalaxyMapTarget? primary,
        GalaxyMapTarget? secondary,
        CancellationToken cancellationToken)
    {
        IsLoading = primary is not null;
        if (primary is null)
        {
            PrimarySystem = null;
            SecondarySystem = null;
            Factions = [];
            DataStatus = "No route or selected Galaxy Map system.";
            IsLoading = false;
            return;
        }

        try
        {
            var primaryTask = summaryClient.GetAsync(
                primary.Name,
                primary.SystemAddress,
                cancellationToken);
            var secondaryTask = secondary is null
                ? null
                : summaryClient.GetAsync(
                    secondary.Name,
                    secondary.SystemAddress,
                    cancellationToken);
            var primaryResult = await primaryTask;
            var secondaryResult = secondaryTask is null
                ? null
                : await secondaryTask;
            cancellationToken.ThrowIfCancellationRequested();

            PrimarySystem = Project(primary.Label, primaryResult.Summary);
            SecondarySystem = secondaryResult is null || secondary is null
                ? null
                : Project(secondary.Label, secondaryResult.Summary);
            Factions = primaryResult.Summary.Factions
                .Select(faction => new GalaxyMapFactionViewModel(
                    faction.Name,
                    (faction.Influence * 100).ToString("0", CultureInfo.InvariantCulture)
                        + "%",
                    faction.State ?? string.Empty))
                .ToArray();
            var warnings = primaryResult.Warnings
                .Concat(secondaryResult?.Warnings ?? [])
                .ToArray();
            DataStatus = warnings.Length == 0
                ? "Data from EDSM and Spansh"
                : string.Join(" · ", warnings);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception) when (
            exception is IOException
                or HttpRequestException
                or InvalidDataException
                or JsonException)
        {
            PrimarySystem = new GalaxyMapSystemViewModel(
                primary.Label,
                ResolveName(primary.Name),
                "System data unavailable",
                string.Empty,
                string.Empty);
            SecondarySystem = null;
            Factions = [];
            DataStatus = "Galaxy Map data is unavailable: " + exception.Message;
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                IsLoading = false;
            }
        }
    }

    private GalaxyMapSystemViewModel Project(
        string label,
        SystemSummary summary)
    {
        var discovery = summary.IsKnown switch
        {
            false => "Undiscovered system",
            null => "Discovery status unavailable",
            _ when summary.TotalBodyCount > 0
                && summary.ScannedBodyCount < summary.TotalBodyCount =>
                $"Scanned {summary.ScannedBodyCount:N0} of "
                    + $"{summary.TotalBodyCount:N0} bodies",
            _ when summary.TotalBodyCount > 0 =>
                $"Fully scanned · {summary.TotalBodyCount:N0} bodies",
            _ => "Known system · body count unavailable",
        };
        var discovered = !string.IsNullOrWhiteSpace(summary.DiscoveredBy)
            ? "Discovered by " + summary.DiscoveredBy
                + (summary.DiscoveredAt is { } discoveredAt
                    ? " · " + discoveredAt.ToLocalTime().ToString("g")
                    : string.Empty)
            : string.Empty;
        var updated = summary.LastUpdatedAt is { } updatedAt
            && (summary.DiscoveredAt is null
                || updatedAt > summary.DiscoveredAt)
                    ? "Last updated "
                        + updatedAt.ToLocalTime().ToString("g")
                    : string.Empty;
        var details = summary.PointsOfInterest.Genus > 0
            ? $"{summary.PointsOfInterest.Genus:N0} biological genera"
            : string.Empty;
        return new GalaxyMapSystemViewModel(
            label,
            ResolveName(summary.SystemName),
            discovery,
            discovered,
            details,
            updated);
    }

    private void SetRouteFooter()
    {
        var value = string.Empty;
        if (navRoute is { Route.Count: > 1 } route)
        {
            var distance = 0d;
            for (var index = 1; index < route.Route.Count; index++)
            {
                if (route.Route[index - 1].Position is { } from
                    && route.Route[index].Position is { } to)
                {
                    distance += from.DistanceTo(to);
                }
            }

            value = distance > 0
                ? $"{route.Route.Count - 1:N0} jumps · {distance:N1} ly"
                : $"{route.Route.Count - 1:N0} jumps";
        }

        if (string.Equals(RouteFooter, value, StringComparison.Ordinal))
        {
            return;
        }

        RouteFooter = value;
        OnPropertyChanged(nameof(RouteFooter));
        OnPropertyChanged(nameof(HasRouteFooter));
    }

    private static GalaxyMapTarget? ParseTarget(JsonElement payload)
    {
        if (!payload.TryGetProperty("Name", out var nameValue)
            || nameValue.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(nameValue.GetString()))
        {
            return null;
        }

        var address = payload.TryGetProperty("SystemAddress", out var addressValue)
            && addressValue.TryGetInt64(out var parsedAddress)
                ? parsedAddress
                : 0;
        return new GalaxyMapTarget(nameValue.GetString()!, address, "SELECTED");
    }

    private string ResolveName(string name)
    {
        return systemNicknames.Resolve(name);
    }

    private void OnNamesChanged(object? sender, EventArgs eventArgs)
    {
        primaryTarget = null;
        secondaryTarget = null;
        if (ShouldShow)
        {
            RefreshTargets();
        }
    }

    private void CancelPendingLoad()
    {
        loadCancellation?.Cancel();
        loadCancellation?.Dispose();
        loadCancellation = null;
        IsLoading = false;
    }

    private void SavePreferences()
    {
        try
        {
            settingsStore.Save(new GalaxyMapPreferences(AutoShow, ShowFactions));
            SettingsStatus = string.Empty;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            SettingsStatus = "Galaxy Map preferences changed for this session "
                + "but could not be saved: " + exception.Message;
        }
    }

    private void RaiseFactionProperties()
    {
        OnPropertyChanged(nameof(HasFactions));
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

    private sealed record GalaxyMapTarget(
        string Name,
        long SystemAddress,
        string Label);
}

public sealed record GalaxyMapSystemViewModel(
    string Label,
    string Name,
    string DiscoveryText,
    string DiscoveredByText,
    string DetailsText,
    string UpdatedText = "")
{
    public bool HasDiscoveredBy => !string.IsNullOrWhiteSpace(DiscoveredByText);

    public bool HasDetails => !string.IsNullOrWhiteSpace(DetailsText);

    public bool HasUpdated => !string.IsNullOrWhiteSpace(UpdatedText);
}

public sealed record GalaxyMapFactionViewModel(
    string Name,
    string Influence,
    string State)
{
    public bool HasState => !string.IsNullOrWhiteSpace(State);
}
