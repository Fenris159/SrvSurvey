using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class GalaxyMapOverlayViewModel : INotifyPropertyChanged, IDisposable
{
    private const string SelectedLabel = "SELECTED";
    private readonly ISystemSummaryClient summaryClient;
    private readonly GalaxyMapSettingsStore settingsStore;
    private readonly SystemNicknameViewModel systemNicknames;
    private CancellationTokenSource? loadCancellation;
    private NavRouteSnapshot? navRoute;
    private GalaxyMapTarget? selectedTarget;
    private GalaxyMapTarget? primaryTarget;
    private GalaxyMapTarget? secondaryTarget;
    private ExplorationPoiCatalog poiCatalog = ExplorationPoiCatalog.Empty;
    public string PrimaryPoiText =>
        string.Join(
            "\n",
            poiCatalog
                .ForSystem(primaryTarget?.Name, primaryTarget?.SystemAddress)
                .Select(p => new ExplorationPoiItemViewModel(p, null))
                .Select(p => p.Name + " · " + p.Categories + " · " + p.RatingText)
        );
    public bool HasPrimaryPois => PrimaryPoiText.Length > 0;
    public string SecondaryPoiText =>
        string.Join(
            " · ",
            poiCatalog.ForSystem(secondaryTarget?.Name, secondaryTarget?.SystemAddress).Select(p => p.Name)
        );
    public bool HasSecondaryPois => SecondaryPoiText.Length > 0;

    public IReadOnlyList<ExplorationPoiItemViewModel> PrimaryPois =>
        PoiRows(primaryTarget?.Name ?? PrimarySystem?.Name, primaryTarget?.SystemAddress, PrimarySystemDisplay.Name);
    public IReadOnlyList<ExplorationPoiItemViewModel> SecondaryPois =>
        PoiRows(
            secondaryTarget?.Name ?? SecondarySystem?.Name,
            secondaryTarget?.SystemAddress,
            SecondarySystemDisplay.Name
        );

    /// <summary>Builds catalogue context without repeating a matching system title as an attraction row.</summary>
    private ExplorationPoiItemViewModel[] PoiRows(string? name, long? address, string displayedSystem) =>
        poiCatalog
            .ForSystem(name, address)
            .Select(p => new ExplorationPoiItemViewModel(p, null)
            {
                ShowOverlayTitle = !string.Equals(p.Name, displayedSystem, StringComparison.OrdinalIgnoreCase),
            })
            .ToArray();

    /// <summary>Applies a shared catalogue snapshot to the selected-system intelligence presentation.</summary>
    public void ApplyPoiCatalog(ExplorationPoiCatalog catalog)
    {
        poiCatalog = catalog;
        RaisePoiProperties();
    }

    /// <summary>Refreshes bound POI context after catalogue or target evidence changes.</summary>
    private void RaisePoiProperties()
    {
        OnPropertyChanged(nameof(PrimaryPoiText));
        OnPropertyChanged(nameof(HasPrimaryPois));
        OnPropertyChanged(nameof(SecondaryPoiText));
        OnPropertyChanged(nameof(HasSecondaryPois));
        OnPropertyChanged(nameof(PrimaryPois));
        OnPropertyChanged(nameof(SecondaryPois));
    }

    private string? currentSystemName;
    private long? currentSystemAddress;
    private EliteStatus? status;
    private string? musicTrack;
    private GalaxyMapSystemViewModel? primarySystem;
    private GalaxyMapSystemViewModel? secondarySystem;
    private IReadOnlyList<GalaxyMapFactionViewModel> factions = [];
    private HashSet<string> questTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
        SystemNicknameViewModel systemNicknames
    )
    {
        this.summaryClient = summaryClient ?? throw new ArgumentNullException(nameof(summaryClient));
        this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        this.systemNicknames = systemNicknames ?? throw new ArgumentNullException(nameof(systemNicknames));
        GalaxyMapPreferences preferences = settingsStore.Load();
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

    public bool ShouldShow =>
        AutoShow && OverlayGameModeResolver.Resolve(status, musicTrack: musicTrack) == OverlayGameMode.GalaxyMap;

    public bool IsGalaxyMapOpen =>
        OverlayGameModeResolver.Resolve(status, musicTrack: musicTrack) == OverlayGameMode.GalaxyMap;

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
                OnPropertyChanged(nameof(PrimarySystemDisplay));
                RaisePoiProperties();
            }
        }
    }

    public bool HasPrimarySystem => PrimarySystem is not null;

    public GalaxyMapSystemViewModel PrimarySystemDisplay => PrimarySystem ?? GalaxyMapSystemViewModel.Empty;

    public GalaxyMapSystemViewModel? SecondarySystem
    {
        get => secondarySystem;
        private set
        {
            if (SetField(ref secondarySystem, value))
            {
                OnPropertyChanged(nameof(HasSecondarySystem));
                OnPropertyChanged(nameof(SecondarySystemDisplay));
                RaisePoiProperties();
            }
        }
    }

    public bool HasSecondarySystem => SecondarySystem is not null;

    public GalaxyMapSystemViewModel SecondarySystemDisplay => SecondarySystem ?? GalaxyMapSystemViewModel.Empty;

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

    /// <summary>
    /// Installs representative Galaxy Map overlay content for the position editor.
    /// </summary>
    internal void InstallEditorPreview(OverlayPreviewSimulationState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        poiCatalog = OverlayEditorPreviewFactories.CreateExplorationPoiCatalog();
        ExplorationPoi poi = poiCatalog.Items[0];
        primaryTarget = new GalaxyMapTarget(poi.SystemName, poi.SystemAddress ?? 0, SelectedLabel);
        PrimarySystem = new GalaxyMapSystemViewModel(
            Label: "SELECTED",
            Name: poi.SystemName,
            DiscoveryText: "Previously discovered",
            DiscoveredByText: "Discovered by CMDR Raven · 2 m ago",
            DetailsText: "2 black holes · 5 T Tauri stars",
            UpdatedText: "Data updated 2 m ago",
            IsQuestTagged: false
        );
        SecondarySystem = new GalaxyMapSystemViewModel(
            Label: "CURRENT",
            Name: state.CurrentSystem,
            DiscoveryText: "Previously discovered",
            DiscoveredByText: "Discovered by CMDR Corvus",
            DetailsText: string.Empty,
            UpdatedText: "Data updated 8 m ago"
        );
        Factions = [];
        RouteFooter = string.Empty;
        OnPropertyChanged(nameof(RouteFooter));
        OnPropertyChanged(nameof(HasRouteFooter));
        DataStatus = "Galaxy Map data ready";
        IsLoading = false;
    }

    public void UpdateQuestTags(IEnumerable<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        var next = tags.Where(tag => !string.IsNullOrWhiteSpace(tag)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (questTags.SetEquals(next))
        {
            return;
        }

        questTags = next;
        if (PrimarySystem is { } primary)
        {
            PrimarySystem = primary with { IsQuestTagged = IsQuestTagged(primaryTarget?.Name) };
        }

        if (SecondarySystem is { } secondary)
        {
            SecondarySystem = secondary with { IsQuestTagged = IsQuestTagged(secondaryTarget?.Name) };
        }
    }

    public void ApplyUpdate(
        string? nextCurrentSystemName,
        long? nextCurrentSystemAddress,
        NavRouteSnapshot? nextNavRoute,
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        EliteStatus? nextStatus,
        bool isBootstrapRead = false,
        string? nextMusicTrack = null
    )
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(journalEvents);
        currentSystemName = nextCurrentSystemName;
        currentSystemAddress = nextCurrentSystemAddress;
        if (nextNavRoute is not null)
        {
            navRoute = nextNavRoute;
            selectedTarget = null;
            if (nextNavRoute.Route.Count > 0)
            {
                routeWasCleared = false;
            }
        }

        bool wasGalaxyMapOpen = IsGalaxyMapOpen;
        if (nextStatus is not null)
        {
            status = nextStatus;
        }
        musicTrack = nextMusicTrack;

        foreach (JournalEventEnvelope journalEvent in journalEvents)
        {
            if (isBootstrapRead)
            {
                continue;
            }

            switch (journalEvent.EventName)
            {
                case "FSDTarget":
                    GalaxyMapTarget? target = ParseTarget(journalEvent.Payload);
                    if (IsRouteNextHop(target))
                    {
                        break;
                    }

                    selectedTarget = target;
                    routeWasCleared = false;
                    break;

                case "NavRouteClear":
                    selectedTarget = null;
                    navRoute = null;
                    routeWasCleared = true;
                    break;
            }
        }

        if (wasGalaxyMapOpen != IsGalaxyMapOpen)
        {
            OnPropertyChanged(nameof(IsGalaxyMapOpen));
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
        (GalaxyMapTarget? Primary, GalaxyMapTarget? Secondary) targets = ResolveTargets();
        SetRouteFooter();
        if (
            targets.Primary == primaryTarget
            && targets.Secondary == secondaryTarget
            && (targets.Primary is null || PrimarySystem is not null)
            && (targets.Secondary is null || SecondarySystem is not null)
        )
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
        PendingLoad = LoadTargetsAsync(targets.Primary, targets.Secondary, loadCancellation.Token);
    }

    /// <summary>Selects the navigation target from journal, status and existing route evidence.</summary>
    private (GalaxyMapTarget? Primary, GalaxyMapTarget? Secondary) ResolveTargets()
    {
        if (
            status?.Destination is { Body: 0, System: > 0 } selected
            && !string.IsNullOrWhiteSpace(selected.Name)
            && selected.System != currentSystemAddress
            && (navRoute is not { Route.Count: > 1 } plotted || selected.System != plotted.Route[1].SystemAddress)
        )
        {
            return (new GalaxyMapTarget(selected.Name, selected.System, SelectedLabel), null);
        }

        if (selectedTarget is not null)
        {
            return (selectedTarget with { Label = SelectedLabel }, null);
        }

        if (navRoute is { Route.Count: > 1 } route)
        {
            NavRouteEntry final = route.Route[^1];
            NavRouteEntry next = route.Route[1];
            return (
                new GalaxyMapTarget(final.StarSystem, final.SystemAddress, "DESTINATION"),
                final.SystemAddress != next.SystemAddress
                    ? new GalaxyMapTarget(next.StarSystem, next.SystemAddress, "NEXT JUMP")
                    : null
            );
        }

        if (status?.Destination is { Body: 0, System: > 0 } destination && !string.IsNullOrWhiteSpace(destination.Name))
        {
            return (new GalaxyMapTarget(destination.Name, destination.System, SelectedLabel), null);
        }

        if (!routeWasCleared && !string.IsNullOrWhiteSpace(currentSystemName))
        {
            return (new GalaxyMapTarget(currentSystemName, currentSystemAddress ?? 0, "CURRENT"), null);
        }

        return (null, null);
    }

    private async Task LoadTargetsAsync(
        GalaxyMapTarget? primary,
        GalaxyMapTarget? secondary,
        CancellationToken cancellationToken
    )
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
            Task<SystemSummaryLoadResult> primaryTask = summaryClient.GetAsync(
                primary.Name,
                primary.SystemAddress,
                cancellationToken
            );
            Task<SystemSummaryLoadResult>? secondaryTask = secondary is null
                ? null
                : summaryClient.GetAsync(secondary.Name, secondary.SystemAddress, cancellationToken);
            SystemSummaryLoadResult primaryResult = await primaryTask;
            SystemSummaryLoadResult? secondaryResult = secondaryTask is null ? null : await secondaryTask;
            cancellationToken.ThrowIfCancellationRequested();

            PrimarySystem = Project(primary.Label, primaryResult.Summary);
            SecondarySystem = secondaryResult is null ? null : Project(secondary!.Label, secondaryResult.Summary);
            Factions = primaryResult
                .Summary.Factions.Select(faction => new GalaxyMapFactionViewModel(
                    faction.Name,
                    (faction.Influence * 100).ToString("0", CultureInfo.InvariantCulture) + "%",
                    faction.State ?? string.Empty
                ))
                .ToArray();
            string[] warnings = primaryResult.Warnings.Concat(secondaryResult?.Warnings ?? []).ToArray();
            DataStatus = warnings.Length == 0 ? "Data from EDSM and Spansh" : string.Join(" · ", warnings);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
            when (exception is IOException or HttpRequestException or InvalidDataException or JsonException)
        {
            PrimarySystem = new GalaxyMapSystemViewModel(
                primary.Label,
                ResolveName(primary.Name),
                "System data unavailable",
                string.Empty,
                string.Empty
            );
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

    private GalaxyMapSystemViewModel Project(string label, SystemSummary summary)
    {
        string discovery = summary.IsKnown switch
        {
            false => "Undiscovered system",
            null => "Discovery status unavailable",
            _ when summary.TotalBodyCount > 0 && summary.ScannedBodyCount < summary.TotalBodyCount =>
                $"Scanned {summary.ScannedBodyCount:N0} of " + $"{summary.TotalBodyCount:N0} bodies",
            _ when summary.TotalBodyCount > 0 => $"Fully scanned · {summary.TotalBodyCount:N0} bodies",
            _ => "Known system · body count unavailable",
        };
        string discovered = !string.IsNullOrWhiteSpace(summary.DiscoveredBy)
            ? "Discovered by "
                + summary.DiscoveredBy
                + (
                    summary.DiscoveredAt switch
                    {
                        DateTimeOffset discoveredAt => " · "
                            + discoveredAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                        null => string.Empty,
                    }
                )
            : string.Empty;
        string updated =
            summary.LastUpdatedAt is { } updatedAt && (summary.DiscoveredAt is null || updatedAt > summary.DiscoveredAt)
                ? "Last updated " + updatedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
                : string.Empty;
        string details =
            summary.PointsOfInterest.Genus > 0
                ? $"{summary.PointsOfInterest.Genus:N0} biological genera"
                : string.Empty;
        return new GalaxyMapSystemViewModel(
            label,
            ResolveName(summary.SystemName),
            discovery,
            discovered,
            details,
            updated,
            IsQuestTagged(summary.SystemName)
        );
    }

    private bool IsQuestTagged(string? name) => !string.IsNullOrWhiteSpace(name) && questTags.Contains(name);

    private void SetRouteFooter()
    {
        string value = string.Empty;
        if (selectedTarget is null && navRoute is { Route.Count: > 1 } route)
        {
            double distance = 0d;
            for (int index = 1; index < route.Route.Count; index++)
            {
                if (route.Route[index - 1].Position is { } from && route.Route[index].Position is { } to)
                {
                    distance += from.DistanceTo(to);
                }
            }

            value =
                distance > 0
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

    private bool IsRouteNextHop(GalaxyMapTarget? target)
    {
        return target is not null
            && navRoute is { Route.Count: > 2 } route
            && route.Route[1].SystemAddress == target.SystemAddress;
    }

    /// <summary>Reads a validated system target from the journal's target event.</summary>
    private static GalaxyMapTarget? ParseTarget(JsonElement payload)
    {
        if (
            !payload.TryGetProperty("Name", out JsonElement nameValue)
            || nameValue.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(nameValue.GetString())
        )
        {
            return null;
        }

        long address =
            payload.TryGetProperty("SystemAddress", out JsonElement addressValue)
            && addressValue.TryGetInt64(out long parsedAddress)
                ? parsedAddress
                : 0;
        return new GalaxyMapTarget(nameValue.GetString()!, address, SelectedLabel);
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
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SettingsStatus =
                "Galaxy Map preferences changed for this session " + "but could not be saved: " + exception.Message;
        }
    }

    private void RaiseFactionProperties()
    {
        OnPropertyChanged(nameof(HasFactions));
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

    private sealed record GalaxyMapTarget(string Name, long SystemAddress, string Label);
}

public sealed record GalaxyMapSystemViewModel(
    string Label,
    string Name,
    string DiscoveryText,
    string DiscoveredByText,
    string DetailsText,
    string UpdatedText = "",
    bool IsQuestTagged = false
)
{
    public static GalaxyMapSystemViewModel Empty { get; } =
        new(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);

    public bool HasDiscoveredBy => !string.IsNullOrWhiteSpace(DiscoveredByText);

    public bool HasDetails => !string.IsNullOrWhiteSpace(DetailsText);

    public bool HasUpdated => !string.IsNullOrWhiteSpace(UpdatedText);
}

public sealed record GalaxyMapFactionViewModel(string Name, string Influence, string State)
{
    public bool HasState => !string.IsNullOrWhiteSpace(State);
}
