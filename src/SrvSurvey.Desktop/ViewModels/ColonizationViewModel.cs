using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Frontier;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class ColonizationViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly TimeSpan DockingRefreshDelay = TimeSpan.FromSeconds(4);
    private const string FleetCarrierStationType = "FleetCarrier";
    private const string FleetCarrierSyncOffMessage = "Automatic Fleet Carrier cargo sync is off.";

    private readonly IRavenColonialClient client;
    private Action<ColonizationProject>? projectPreviewOpener;
    private readonly ColonizationBuildCatalog buildCatalog;
    private readonly ColonizationSettingsStore settingsStore;
    private readonly CommanderProfileStore? commanderProfileStore;
    private readonly LegacyColonizationProfileStore? legacyProfileStore;
    private readonly ColonizationDeliveryRecovery recovery;
    private ColonizationOverlayPreferences overlayPreferences;
    private readonly AsyncCommand refreshCommand;
    private readonly AsyncCommand saveProjectsCommand;
    private readonly AsyncCommand saveRavenApiKeyCommand;
    private readonly AsyncCommand publishFleetCarrierCommand;
    private readonly AsyncCommand syncFleetCarrierCargoCommand;
    private readonly Func<TimeSpan, CancellationToken, Task> delayAsync;
    private CancellationTokenSource? dockingRefreshCancellation;
    private IReadOnlyList<ColonizationProjectRowViewModel> projects = [];
    private IReadOnlyList<ColonizationResourceRowViewModel> constructionResources = [];
    private HashSet<string> hiddenProjectIds = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<ColonizationFleetCarrier> LinkedFleetCarriers => recovery.FleetCarriers;
    public long? DetectedSquadronCarrierMarketId => recovery.DetectedSquadronCarrierMarketId;
    private CargoSnapshot? shipCargo;
    private EliteStatus? latestStatus;
    private IReadOnlyList<double> currentStarPosition = [];
    private string? primaryProjectId;
    private bool isBusy;
    private bool hasUnsavedProjectVisibility;
    private bool shipCargoPublishingEnabled;
    private bool sharedCargoSuppressed;
    private bool isFleetCarrierSyncBusy;
    private bool isShipCargoPublishingBusy;
    private string ravenApiKey = string.Empty;
    private string ravenCredentialStatus = "Load a commander profile to configure a Raven API key.";
    private string fleetCarrierSyncStatus = FleetCarrierSyncOffMessage;
    private string shipCargoPublishingStatus = "Automatic ship cargo publishing is off.";
    private string? currentShipType;
    private string? currentShipName;
    private string statusMessage;
    private bool refreshPending;
    private IReadOnlyList<ColonizationPendingContributionRowViewModel> unconfirmedContributions = [];
    private string? contributionRecoveryOwner;

    private string projectSummary = "No projects loaded.";
    private string constructionTitle = "No construction depot active";
    private string constructionStatus = "Dock at a construction site and open Construction Services.";

    /// <summary>Initializes commander-scoped colonization workflows, delivery recovery, and bounded retry timing.</summary>
    public ColonizationViewModel(
        ColonizationSettingsStore settingsStore,
        IRavenColonialClient? client = null,
        ColonizationBuildCatalog? buildCatalog = null,
        CommanderProfileStore? commanderProfileStore = null,
        LegacyColonizationProfileStore? legacyProfileStore = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        Func<DateTimeOffset>? utcNow = null
    )
    {
        this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        this.client = client ?? new RavenColonialClient();
        this.commanderProfileStore = commanderProfileStore;
        this.legacyProfileStore = legacyProfileStore;
        this.delayAsync = delayAsync ?? Task.Delay;
        recovery = new ColonizationDeliveryRecovery(
            this.client,
            settingsStore,
            new DeliveryObserver(this),
            this.delayAsync,
            utcNow
        )
        {
            IsEnabled = settingsStore.LoadEnabled(),
            FleetCarrierCargoSyncEnabled = settingsStore.LoadFleetCarrierCargoSyncEnabled(),
        };
        this.buildCatalog = buildCatalog ?? ColonizationBuildCatalog.LoadEmbedded();
        overlayPreferences = settingsStore.LoadOverlayPreferences();
        shipCargoPublishingEnabled = settingsStore.LoadShipCargoPublishingEnabled();
        statusMessage = IsEnabled
            ? "Raven Colonial access is enabled. Waiting for a commander profile."
            : "Raven Colonial access is off. No project data will be fetched or published.";
        refreshCommand = new AsyncCommand(RefreshAsync, () => IsEnabled && !IsBusy && CommanderName is not null);
        saveProjectsCommand = new AsyncCommand(
            SaveProjectVisibilityAsync,
            () => IsEnabled && !IsBusy && HasUnsavedProjectVisibility && CommanderName is not null
        );
        saveRavenApiKeyCommand = new AsyncCommand(SaveRavenApiKeyAsync, CanSaveRavenApiKey);
        publishFleetCarrierCommand = new AsyncCommand(PublishCurrentFleetCarrierAsync, CanPublishCurrentFleetCarrier);
        syncFleetCarrierCargoCommand = new AsyncCommand(
            () => SyncFleetCarrierCargoAsync(force: true),
            CanSyncFleetCarrierCargo
        );
        RetryUnconfirmedContributionsCommand = new AsyncCommand(
            RetryUnconfirmedContributionsAsync,
            () => IsEnabled && CanReconcileSelectedContributions()
        );
        DismissConfirmedContributionsCommand = new AsyncCommand(
            () =>
            {
                DismissConfirmedContributions();
                return Task.CompletedTask;
            },
            CanReconcileSelectedContributions
        );
        RefreshCommand = refreshCommand;
        SaveProjectsCommand = saveProjectsCommand;
        SaveRavenApiKeyCommand = saveRavenApiKeyCommand;
        PublishFleetCarrierCommand = publishFleetCarrierCommand;
        SyncFleetCarrierCargoCommand = syncFleetCarrierCargoCommand;
        ProjectEditor = new ColonizationProjectEditorViewModel(this.client, this.buildCatalog, OnProjectCreatedAsync);
        SystemEditor = new ColonizationSystemEditorViewModel(this.client, this.buildCatalog);
        CommodityOverlay = new ColonizationCommodityOverlayViewModel();
        CommodityOverlay.ApplyPreferences(overlayPreferences);
        UpdateProjectEditorContext();
        UpdateSystemEditorContext();
        UpdateCommodityPlan();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Connects the native read-only build popout without coupling project rows to a desktop window.</summary>
    internal void SetProjectPreviewOpener(Action<ColonizationProject>? opener) => projectPreviewOpener = opener;

    /// <summary>Opens the clicked build independently of its Show checkbox and current primary project.</summary>
    public void OpenProjectPreview(ColonizationProject project)
    {
        if (IsEnabled)
        {
            projectPreviewOpener?.Invoke(project);
        }
    }

    /// <summary>Creates an isolated preview sharing the bounded public reader and current Raven consent.</summary>
    internal ColonizationProjectPreviewViewModel CreateProjectPreview(string buildId)
    {
        return new ColonizationProjectPreviewViewModel(
            client as IRavenColonialProjectReader
                ?? throw new InvalidOperationException("The Raven build preview reader is unavailable."),
            buildId,
            () => IsEnabled,
            () => recovery.ShipCargoCapacity
        );
    }

    public ICommand RefreshCommand { get; }

    public ICommand SaveProjectsCommand { get; }

    public ICommand SaveRavenApiKeyCommand { get; }

    public ICommand PublishFleetCarrierCommand { get; }

    public ICommand SyncFleetCarrierCargoCommand { get; }

    public ColonizationProjectEditorViewModel ProjectEditor { get; }

    public ColonizationSystemEditorViewModel SystemEditor { get; }

    public ColonizationCommodityOverlayViewModel CommodityOverlay { get; }

    public bool AutoShowCommodityOverlay
    {
        get => overlayPreferences.AutoShow;
        set => SaveOverlayPreferences(overlayPreferences with { AutoShow = value });
    }

    public bool ShowCommodityOverlayOnRightPanel
    {
        get => overlayPreferences.ShowOnRightPanel;
        set => SaveOverlayPreferences(overlayPreferences with { ShowOnRightPanel = value });
    }

    public bool ShowFleetCarrierCargo
    {
        get => overlayPreferences.ShowFleetCarrierCargo;
        set => SaveOverlayPreferences(overlayPreferences with { ShowFleetCarrierCargo = value });
    }

    public bool ShowFleetCarrierDelta
    {
        get => overlayPreferences.ShowFleetCarrierDelta;
        set => SaveOverlayPreferences(overlayPreferences with { ShowFleetCarrierDelta = value });
    }

    public bool InlineFleetCarrierCargo
    {
        get => overlayPreferences.InlineFleetCarrierCargo;
        set => SaveOverlayPreferences(overlayPreferences with { InlineFleetCarrierCargo = value });
    }

    public bool CollapseCoveredCommodityGroups
    {
        get => overlayPreferences.CollapseCoveredGroups;
        set => SaveOverlayPreferences(overlayPreferences with { CollapseCoveredGroups = value });
    }

    public bool UseCompactScrollingCommoditiesList
    {
        get => overlayPreferences.UseCompactScrollingCommoditiesList;
        set => SaveOverlayPreferences(overlayPreferences with { UseCompactScrollingCommoditiesList = value });
    }

    public bool HighlightAlmostCoveredFleetCarrierLoads
    {
        get => overlayPreferences.HighlightAlmostCoveredFleetCarrierLoads;
        set => SaveOverlayPreferences(overlayPreferences with { HighlightAlmostCoveredFleetCarrierLoads = value });
    }

    public string RavenApiKey
    {
        get => ravenApiKey;
        set
        {
            if (SetField(ref ravenApiKey, value ?? string.Empty))
            {
                saveRavenApiKeyCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasCommanderProfile => recovery.FrontierId is not null;

    public bool HasStoredRavenApiKey => !string.IsNullOrWhiteSpace(recovery.ApiKey);

    public string RavenCredentialStatus
    {
        get => ravenCredentialStatus;
        private set => SetField(ref ravenCredentialStatus, value);
    }

    public bool FleetCarrierCargoSyncEnabled
    {
        get => recovery.FleetCarrierCargoSyncEnabled;
        set
        {
            if (value == recovery.FleetCarrierCargoSyncEnabled)
            {
                return;
            }

            try
            {
                settingsStore.SaveFleetCarrierCargoSyncEnabled(value);
                recovery.FleetCarrierCargoSyncEnabled = value;
                OnPropertyChanged();
                FleetCarrierSyncStatus = value
                    ? (HasStoredRavenApiKey) switch
                    {
                        true => "Fleet Carrier cargo will sync from matching Market.json updates.",
                        false => "Save a Raven API key before Fleet Carrier cargo can sync.",
                    }
                    : FleetCarrierSyncOffMessage;
                syncFleetCarrierCargoCommand.RaiseCanExecuteChanged();
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                FleetCarrierSyncStatus = "The Fleet Carrier sync preference could not be saved: " + exception.Message;
            }
        }
    }

    public bool IsFleetCarrierSyncBusy
    {
        get => isFleetCarrierSyncBusy;
        private set
        {
            if (SetField(ref isFleetCarrierSyncBusy, value))
            {
                OnPropertyChanged(nameof(FleetCarrierSyncButtonText));
                OnPropertyChanged(nameof(FleetCarrierPublishButtonText));
                RaiseCommandStates();
            }
        }
    }

    public string FleetCarrierSyncButtonText => IsFleetCarrierSyncBusy ? "Syncing..." : "Sync current market";

    public string FleetCarrierPublishButtonText =>
        IsFleetCarrierSyncBusy ? "Working..." : "Publish/link current carrier";

    public string FleetCarrierSyncStatus
    {
        get => fleetCarrierSyncStatus;
        private set => SetField(ref fleetCarrierSyncStatus, value);
    }

    public bool ShipCargoPublishingEnabled
    {
        get => shipCargoPublishingEnabled;
        set
        {
            if (value == shipCargoPublishingEnabled)
            {
                return;
            }

            try
            {
                settingsStore.SaveShipCargoPublishingEnabled(value);
                shipCargoPublishingEnabled = value;
                OnPropertyChanged();
                ShipCargoPublishingStatus = GetShipCargoReadyStatus();
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                ShipCargoPublishingStatus =
                    "The ship cargo publishing preference could not be saved: " + exception.Message;
            }
        }
    }

    public bool IsShipCargoPublishingBusy
    {
        get => isShipCargoPublishingBusy;
        private set => SetField(ref isShipCargoPublishingBusy, value);
    }

    public string ShipCargoPublishingStatus
    {
        get => shipCargoPublishingStatus;
        private set => SetField(ref shipCargoPublishingStatus, value);
    }

    public bool SharedCargoSuppressed => sharedCargoSuppressed;

    public void SetSharedCargoSuppressed(bool value)
    {
        if (sharedCargoSuppressed == value)
        {
            return;
        }

        sharedCargoSuppressed = value;
        OnPropertyChanged(nameof(SharedCargoSuppressed));
        if (value)
        {
            shipCargo = null;
            UpdateCommodityPlan();
        }

        ShipCargoPublishingStatus = GetShipCargoReadyStatus();
    }

    /// <summary>
    /// Enables Raven integration and invalidates pending repair warnings when it is disabled.
    /// </summary>
    public bool IsEnabled
    {
        get => recovery.IsEnabled;
        set
        {
            if (value == recovery.IsEnabled)
            {
                return;
            }

            try
            {
                settingsStore.SaveEnabled(value);
                recovery.IsEnabled = value;
                OnPropertyChanged();
                RaiseCommandStates();
                if (value)
                {
                    StatusMessage = CommanderName is null
                        ? "Raven Colonial access is enabled. Waiting for a commander profile."
                        : "Raven Colonial access is enabled. Select Refresh projects to fetch data.";
                }
                else
                {
                    CancelDockingRefresh();
                    ClearProjects();
                    StatusMessage = "Raven Colonial access is off. No project data will be fetched or published.";
                }

                if (ShipCargoPublishingEnabled)
                {
                    ShipCargoPublishingStatus = GetShipCargoReadyStatus();
                }

                UpdateProjectEditorContext();
                UpdateSystemEditorContext();
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                StatusMessage = "The Raven Colonial preference could not be saved: " + exception.Message;
            }
        }
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetField(ref isBusy, value))
            {
                OnPropertyChanged(nameof(RefreshButtonText));
                OnPropertyChanged(nameof(SaveButtonText));
                RaiseCommandStates();
            }
        }
    }

    public string RefreshButtonText => IsBusy ? "Refreshing..." : "Refresh projects";

    public string SaveButtonText => IsBusy ? "Saving..." : "Save selection";

    public string? CommanderName => recovery.CommanderName;

    public string CommanderStatus =>
        CommanderName is null ? "No commander profile is active." : $"Commander: {CommanderName}";

    public IReadOnlyList<ColonizationProjectRowViewModel> Projects
    {
        get => projects;
        private set
        {
            if (ReferenceEquals(projects, value))
            {
                return;
            }

            projects = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasProjects));
            OnPropertyChanged(nameof(HasNoProjects));
        }
    }

    public bool HasProjects => Projects.Count > 0;

    public bool HasNoProjects => !HasProjects;

    public bool HasUnsavedProjectVisibility
    {
        get => hasUnsavedProjectVisibility;
        private set
        {
            if (SetField(ref hasUnsavedProjectVisibility, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string ProjectSummary
    {
        get => projectSummary;
        private set => SetField(ref projectSummary, value);
    }

    /// <summary>
    /// Combines general Raven status with the current system's independent docking-repair warning.
    /// </summary>
    public string StatusMessage
    {
        get =>
            CombineMessages(
                statusMessage,
                recovery.BuildSiteRepairWarning is { } warning ? ColonizationDeliveryMessages.Describe(warning) : null
            ) ?? string.Empty;
        private set => SetField(ref statusMessage, value);
    }

    public string ConstructionTitle
    {
        get => constructionTitle;
        private set => SetField(ref constructionTitle, value);
    }

    public string ConstructionStatus
    {
        get => constructionStatus;
        private set => SetField(ref constructionStatus, value);
    }

    public IReadOnlyList<ColonizationResourceRowViewModel> ConstructionResources
    {
        get => constructionResources;
        private set
        {
            if (ReferenceEquals(constructionResources, value))
            {
                return;
            }

            constructionResources = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasConstructionResources));
        }
    }

    public bool HasConstructionResources => ConstructionResources.Count > 0;

    /// <summary>Updates credentials and invalidates requests owned by a superseded Frontier profile.</summary>
    public void SetCommanderProfile(string? frontierId, bool isOdyssey, string? apiKey)
    {
        recovery.SetProfile(frontierId, isOdyssey, apiKey);
        RavenApiKey = recovery.ApiKey ?? string.Empty;
        NotifyContributionRecovery();
        string apiKeyStatus = recovery.ApiKey is null
            ? "No Raven API key is saved for this commander."
            : "A Raven API key is saved for this commander.";
        RavenCredentialStatus = recovery.FrontierId is null
            ? "Load a commander profile to configure a Raven API key."
            : apiKeyStatus;
        if (!FleetCarrierCargoSyncEnabled)
        {
            FleetCarrierSyncStatus = FleetCarrierSyncOffMessage;
        }
        else if (recovery.ApiKey is null)
        {
            FleetCarrierSyncStatus = "Save a Raven API key before Fleet Carrier cargo can sync.";
        }
        else
        {
            FleetCarrierSyncStatus = "Fleet Carrier cargo will sync from matching Market.json updates.";
        }
        ShipCargoPublishingStatus = GetShipCargoReadyStatus();
        OnPropertyChanged(nameof(HasCommanderProfile));
        OnPropertyChanged(nameof(HasStoredRavenApiKey));
        RaiseCommandStates();
        UpdateProjectEditorContext();
        UpdateSystemEditorContext();
    }

    /// <summary>
    /// Switches commander data and discards repair warnings and pending results from the previous commander.
    /// </summary>
    public async Task SetCommanderAsync(string? value)
    {
        string? normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (string.Equals(CommanderName, normalized, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        CancelDockingRefresh();
        recovery.SetCommander(normalized);
        OnPropertyChanged(nameof(CommanderName));
        OnPropertyChanged(nameof(CommanderStatus));
        RaiseCommandStates();
        NotifyContributionRecovery();
        ClearProjects();
        UpdateProjectEditorContext();
        UpdateSystemEditorContext();
        if (CommanderName is null)
        {
            StatusMessage = "No commander profile is active.";
            return;
        }

        if (IsEnabled)
        {
            await RefreshAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Applies journal context and expires repair warnings when docking moves to another known system.
    /// </summary>
    public void ApplyJournalEvents(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        string? journalCommanderName = null
    )
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        SystemEditor.ApplyJournalEvents(journalEvents);
        foreach (JournalEventEnvelope journalEvent in journalEvents)
        {
            ApplyShipIdentity(journalEvent);
        }

        if (recovery.ApplyJournalEvents(journalEvents, journalCommanderName))
        {
            UpdateConstructionDisplay();
            UpdateProjectSummary();
            UpdateProjectEditorContext();
            publishFleetCarrierCommand.RaiseCanExecuteChanged();
            syncFleetCarrierCargoCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Synchronizes journal events using their event-time dock and retries retained writes during idle polls.</summary>
    public async Task SynchronizeLiveProjectsAsync(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        bool allowPublishing,
        CargoInventoryState? cargoInventory = null,
        bool cargoActivity = false,
        bool preferShipCargoDiffForSquadron = true,
        CancellationToken cancellationToken = default
    )
    {
        IReadOnlyList<ColonizationDeliveryNotice> notices = await recovery.SynchronizeLiveEventsAsync(
            journalEvents,
            allowPublishing,
            cargoInventory,
            cargoActivity,
            preferShipCargoDiffForSquadron,
            cancellationToken
        );
        if (notices.Count > 0)
        {
            StatusMessage = ColonizationDeliveryMessages.Describe(notices);
        }
    }

    private void ScheduleDockingRefresh()
    {
        CancelDockingRefresh();
        dockingRefreshCancellation = new CancellationTokenSource();
        _ = RefreshAfterDockingAsync(dockingRefreshCancellation.Token);
    }

    private async Task RefreshAfterDockingAsync(CancellationToken cancellationToken)
    {
        try
        {
            await delayAsync(DockingRefreshDelay, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await RefreshAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A newer docking event, commander change, disable, or shutdown superseded it.
        }
    }

    private void CancelDockingRefresh()
    {
        CancellationTokenSource? cancellation = dockingRefreshCancellation;
        dockingRefreshCancellation = null;
        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        cancellation.Dispose();
    }

    /// <summary>
    /// Freeze ship cargo before CargoTransfer mutates the live projection when docked on a
    /// linked squadron fleet carrier.
    /// </summary>
    public void PrepareSquadronCargoTransferSnapshot(CargoInventoryState cargo)
    {
        recovery.PrepareSquadronCargoTransferSnapshot(cargo);
    }

    public async Task UpdateCargoAsync(CargoSnapshot? cargo, bool publishCurrentShipCargo = true)
    {
        if (cargo is null || SharedCargoSuppressed)
        {
            return;
        }

        shipCargo = cargo;
        UpdateCommodityPlan();
        if (publishCurrentShipCargo)
        {
            await PublishCurrentShipCargoAsync(cargo);
        }
    }

    private async Task PublishCurrentShipCargoAsync(CargoSnapshot cargo)
    {
        if (!ShipCargoPublishingEnabled)
        {
            return;
        }

        string? blockReason = GetShipCargoPublishingBlockReason();
        if (blockReason is not null)
        {
            ShipCargoPublishingStatus = blockReason;
            return;
        }

        var cargoCounts = cargo
            .Inventory.Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .GroupBy(item => item.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(item => Math.Max(0, item.Count)),
                StringComparer.OrdinalIgnoreCase
            );
        IsShipCargoPublishingBusy = true;
        ShipCargoPublishingStatus = "Publishing current ship cargo...";
        try
        {
            await client.PublishCurrentShipAsync(
                new ColonizationCurrentShip
                {
                    CommanderName = CommanderName!,
                    Name = currentShipName ?? currentShipType!,
                    Type = currentShipType!,
                    MaximumCargo = recovery.ShipCargoCapacity,
                    Cargo = cargoCounts,
                },
                recovery.ApiKey!,
                CancellationToken.None
            );
            ShipCargoPublishingStatus = $"Published {cargoCounts.Count:N0} ship cargo entries to Raven Colonial.";
        }
        catch (Exception exception)
            when (exception
                    is HttpRequestException
                        or InvalidDataException
                        or TaskCanceledException
                        or ArgumentException
            )
        {
            ShipCargoPublishingStatus = "Ship cargo was not published: " + exception.Message;
        }
        finally
        {
            IsShipCargoPublishingBusy = false;
        }
    }

    public async Task UpdateMarketAsync(MarketSnapshot? market)
    {
        if (market is null)
        {
            return;
        }

        recovery.UpdateMarket(market);
        UpdateCommodityPlan();
        publishFleetCarrierCommand.RaiseCanExecuteChanged();
        syncFleetCarrierCargoCommand.RaiseCanExecuteChanged();
        if (FleetCarrierCargoSyncEnabled)
        {
            await SyncFleetCarrierCargoAsync(force: false);
        }
    }

    public void UpdateStatus(EliteStatus? status)
    {
        if (status is null)
        {
            return;
        }

        latestStatus = status;
        recovery.UpdateStatus(status);
        SystemEditor.UpdateStatus(status);
        UpdateCommodityPlan();
    }

    public void UpdateMusicTrack(string? musicTrack)
    {
        CommodityOverlay.UpdateMusicTrack(musicTrack);
    }

    /// <summary>
    /// Updates the active system and prevents warnings from earlier systems from lingering or arriving late.
    /// </summary>
    public void UpdateSystemContext(string? systemName, GalacticCoordinate? position, long? systemAddress = null)
    {
        bool samePosition = position is GalacticCoordinate coordinate
            ? currentStarPosition.Count == 3
                && Math.Abs(currentStarPosition[0] - coordinate.X) <= 0.0000001d
                && Math.Abs(currentStarPosition[1] - coordinate.Y) <= 0.0000001d
                && Math.Abs(currentStarPosition[2] - coordinate.Z) <= 0.0000001d
            : currentStarPosition.Count == 0;
        if (!recovery.UpdateSystemContext(systemName, systemAddress, positionChanged: !samePosition))
        {
            return;
        }

        currentStarPosition = position is GalacticCoordinate nextCoordinate
            ? [nextCoordinate.X, nextCoordinate.Y, nextCoordinate.Z]
            : [];
        UpdateProjectEditorContext();
        UpdateSystemEditorContext();
    }

    public void ReportLinkFailure(string message)
    {
        StatusMessage = "Raven Colonial could not be opened: " + message;
    }

    public void Dispose()
    {
        CancelDockingRefresh();
    }

    /// <summary>Validates and persists a Raven key only against the captured commander profile.</summary>
    public async Task SaveRavenApiKeyAsync()
    {
        if (!CanSaveRavenApiKey() || commanderProfileStore is null || recovery.FrontierId is null)
        {
            return;
        }

        int version = recovery.ProfileVersion;
        IsFleetCarrierSyncBusy = true;
        try
        {
            await PersistRavenApiKeyAsync();
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or InvalidDataException
                        or HttpRequestException
                        or TaskCanceledException
                        or ArgumentException
            )
        {
            if (version == recovery.ProfileVersion)
            {
                RavenCredentialStatus = "The Raven API key was not saved: " + exception.Message;
            }
        }
        finally
        {
            IsFleetCarrierSyncBusy = false;
            RaiseCommandStates();
        }
    }

    /// <summary>Saves credentials to the initiating Frontier identity and applies them only if that profile is still current.</summary>
    private async Task PersistRavenApiKeyAsync()
    {
        int version = recovery.ProfileVersion;
        string? capturedFrontierId = recovery.FrontierId;
        bool capturedOdyssey = recovery.IsOdyssey;
        string? capturedCommander = CommanderName;
        string? normalized = string.IsNullOrWhiteSpace(RavenApiKey) ? null : RavenApiKey.Trim();
        string? validatedCommander = null;
        if (
            normalized is not null
            && !await TryValidateRavenApiKeyAsync(normalized, commander => validatedCommander = commander)
        )
        {
            return;
        }

        if (version != recovery.ProfileVersion)
        {
            return;
        }
        CommanderProfileStore store =
            commanderProfileStore ?? throw new InvalidOperationException("Commander profile storage is not available.");
        string frontierId =
            capturedFrontierId ?? throw new InvalidOperationException("No commander frontier id is available.");
        await store.SaveRavenColonialApiKeyAsync(
            frontierId,
            capturedCommander,
            capturedOdyssey,
            normalized,
            CancellationToken.None
        );
        if (version == recovery.ProfileVersion)
        {
            ApplySavedRavenApiKey(normalized, validatedCommander);
        }
    }

    private void ApplySavedRavenApiKey(string? normalized, string? validatedCommander)
    {
        recovery.UpdateApiKey(normalized);
        RavenApiKey = normalized ?? string.Empty;
        RavenCredentialStatus = normalized is null
            ? "The Raven API key was removed from this commander profile."
            : $"The Raven API key was validated for {validatedCommander} and saved.";
        OnPropertyChanged(nameof(HasStoredRavenApiKey));
        if (normalized is null && FleetCarrierCargoSyncEnabled)
        {
            FleetCarrierCargoSyncEnabled = false;
        }

        if (ShipCargoPublishingEnabled)
        {
            ShipCargoPublishingStatus = GetShipCargoReadyStatus();
        }

        UpdateProjectEditorContext();
        UpdateSystemEditorContext();
    }

    /// <summary>Checks key ownership against a captured commander and rejects results from a superseded profile.</summary>
    private async Task<bool> TryValidateRavenApiKeyAsync(string normalized, Action<string?> setValidatedCommander)
    {
        if (CommanderName is null)
        {
            RavenCredentialStatus = "Load the active commander before validating a Raven API key.";
            return false;
        }

        int version = recovery.ProfileVersion;
        string commander = CommanderName;
        RavenCredentialStatus = "Validating the Raven API key without saving it...";
        string? validatedCommander = await client.GetCommanderByApiKeyAsync(normalized, CancellationToken.None);
        if (version != recovery.ProfileVersion)
        {
            return false;
        }
        setValidatedCommander(validatedCommander);
        if (validatedCommander is null)
        {
            RavenCredentialStatus = "Raven rejected this API key. The saved key was not changed.";
            return false;
        }

        if (!string.Equals(validatedCommander, commander, StringComparison.OrdinalIgnoreCase))
        {
            RavenCredentialStatus =
                $"This key belongs to {validatedCommander}, not " + $"{CommanderName}. The saved key was not changed.";
            return false;
        }

        return true;
    }

    /// <summary>Registers a carrier and serializes its absolute cargo baseline before replaying queued transactions.</summary>
    public async Task PublishCurrentFleetCarrierAsync()
    {
        if (!CanPublishCurrentFleetCarrier())
        {
            FleetCarrierSyncStatus = GetFleetCarrierPublishBlockReason();
            return;
        }

        await recovery.PublishCurrentFleetCarrierAsync();
    }

    /// <summary>
    /// Seeds RavenColonial with a full CAPI cargo manifest once per linked carrier per
    /// session (EDMC parity). Later Frontier refreshes are ignored; journal deltas keep
    /// linked + workspace totals current between CAPI updates.
    /// </summary>
    public Task SeedLinkedCarrierCargoFromCapiAsync(FrontierAccountSnapshot? snapshot)
    {
        return recovery.SeedLinkedCarrierCargoFromCapiAsync(snapshot);
    }

    public async Task SyncFleetCarrierCargoAsync(bool force = true)
    {
        if (!CanSyncFleetCarrierCargo())
        {
            if (force)
            {
                FleetCarrierSyncStatus = GetFleetCarrierSyncBlockReason();
            }

            return;
        }

        await recovery.SyncFleetCarrierCargoAsync(force);
    }

    /// <summary>Lists retained deliveries for the current profile so uncertain server outcomes can be checked on Raven.</summary>
    public string PendingContributionSummary =>
        string.Join(
            Environment.NewLine,
            recovery
                .GetPendingContributions()
                .Select(item =>
                    $"{item.Commander}: {item.Cargo.Values.Sum(value => (long)value):N0} units for {item.BuildId}: {string.Join(", ", item.Cargo.Select(pair => $"{pair.Key} {pair.Value:N0}"))} ({(item.OutcomeUnknown ? "unconfirmed" : "waiting to retry")})."
                )
        );

    /// <summary>Indicates that the current profile has deliveries requiring an explicit reconciliation decision.</summary>
    public bool HasUncertainContributions => recovery.GetPendingContributions().Any(item => item.OutcomeUnknown);

    /// <summary>Offers individual uncertain deliveries for verification; checkbox choices are never persisted.</summary>
    public IReadOnlyList<ColonizationPendingContributionRowViewModel> UnconfirmedContributions =>
        unconfirmedContributions;

    /// <summary>Prevents recovery decisions from changing while a delivery upload or retry is active.</summary>
    public bool CanSelectUnconfirmedContributions => recovery.CanReconcileContributions;

    /// <summary>Retries checked deliveries after the commander verifies that Raven did not record them.</summary>
    public ICommand RetryUnconfirmedContributionsCommand { get; }

    /// <summary>Removes checked deliveries from local recovery after their Raven credit is confirmed.</summary>
    public ICommand DismissConfirmedContributionsCommand { get; }

    /// <summary>Retries only checked deliveries verified absent on Raven, retaining every unselected delivery unchanged.</summary>
    public async Task RetryUnconfirmedContributionsAsync()
    {
        if (!IsEnabled || !CanReconcileSelectedContributions())
        {
            return;
        }
        string[] selected = GetSelectedContributionIds();
        foreach (ColonizationPendingContributionRowViewModel row in unconfirmedContributions)
        {
            row.IsSelected = false;
        }
        IReadOnlyList<ColonizationDeliveryNotice>? notices = await recovery.RetryVerifiedContributionsAsync(selected);
        if (notices is not null)
        {
            StatusMessage =
                notices.Count > 0
                    ? ColonizationDeliveryMessages.Describe(notices)
                    : "Retried the selected construction deliveries.";
        }
    }

    /// <summary>Clears only checked deliveries verified credited on Raven without sending any server request.</summary>
    public void DismissConfirmedContributions()
    {
        if (!CanReconcileSelectedContributions())
        {
            return;
        }
        recovery.DismissVerifiedContributions(GetSelectedContributionIds());
    }

    /// <summary>Requires an explicit checkbox choice and excludes concurrent delivery or retry activity.</summary>
    private bool CanReconcileSelectedContributions() =>
        CanSelectUnconfirmedContributions && unconfirmedContributions.Any(row => row.IsSelected);

    private string[] GetSelectedContributionIds() =>
        unconfirmedContributions.Where(row => row.IsSelected).Select(row => row.EventId).ToArray();

    /// <summary>Preserves current checkbox choices while refreshing deliveries, clearing them when profile ownership changes.</summary>
    private void NotifyContributionRecovery()
    {
        string owner = recovery.RecoveryOwner;
        IReadOnlyList<ColonizationPendingContributionRowViewModel> previous =
            contributionRecoveryOwner == owner ? unconfirmedContributions : [];
        contributionRecoveryOwner = owner;
        unconfirmedContributions = recovery
            .GetPendingContributions()
            .Where(item => item.OutcomeUnknown)
            .Select(item =>
                previous.FirstOrDefault(row => row.EventId == item.EventId)
                ?? new ColonizationPendingContributionRowViewModel(item, RaiseContributionRecoveryCommandStates)
            )
            .ToArray();
        OnPropertyChanged(nameof(UnconfirmedContributions));
        OnPropertyChanged(nameof(CanSelectUnconfirmedContributions));
        OnPropertyChanged(nameof(PendingContributionSummary));
        OnPropertyChanged(nameof(HasUncertainContributions));
        RaiseContributionRecoveryCommandStates();
    }

    /// <summary>Updates both recovery buttons when selection or upload availability changes.</summary>
    private void RaiseContributionRecoveryCommandStates()
    {
        (RetryUnconfirmedContributionsCommand as AsyncCommand)?.RaiseCanExecuteChanged();
        (DismissConfirmedContributionsCommand as AsyncCommand)?.RaiseCanExecuteChanged();
    }

    /// <summary>Runs one requested workspace refresh after the current busy operation releases ownership.</summary>
    private async Task RefreshPendingAsync(CancellationToken cancellationToken = default)
    {
        if (!refreshPending)
        {
            return;
        }
        refreshPending = false;
        await RefreshAsync(cancellationToken);
    }

    /// <summary>Loads the active commander workspace and services refreshes requested while an obsolete load was busy.</summary>
    public Task RefreshAsync()
    {
        return RefreshAsync(CancellationToken.None);
    }

    /// <summary>Loads the active commander workspace and services refreshes requested while an obsolete load was busy.</summary>
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (!IsEnabled || CommanderName is null)
        {
            return;
        }
        if (IsBusy)
        {
            refreshPending = true;
            return;
        }
        int version = recovery.ProfileVersion;
        string commander = CommanderName;
        IsBusy = true;

        StatusMessage = "Fetching active projects from Raven Colonial...";
        try
        {
            if (Projects.Count == 0)
            {
                await RestoreLegacyProfileAsync(cancellationToken: cancellationToken);
            }
            if (version != recovery.ProfileVersion)
            {
                return;
            }
            ColonizationCommanderProjects result = await client.GetCommanderProjectsAsync(commander, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (
                version != recovery.ProfileVersion
                || !string.Equals(CommanderName, commander, StringComparison.OrdinalIgnoreCase)
            )
            {
                return;
            }

            hiddenProjectIds = result.HiddenProjectIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            primaryProjectId = result.PrimaryProjectId;
            recovery.ReplaceWorkspace(result.Projects, result.FleetCarriers);
            Projects = recovery.Projects.Select(CreateRow).ToArray();
            HasUnsavedProjectVisibility = false;
            UpdateProjectSummary();
            StatusMessage = Projects.Count switch
            {
                0 => "No active Raven Colonial projects were found for this commander.",
                1 => "Loaded 1 active Raven Colonial project.",
                _ => $"Loaded {Projects.Count:N0} active Raven Colonial projects.",
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is HttpRequestException or InvalidDataException or TaskCanceledException)
        {
            if (version == recovery.ProfileVersion)
            {
                StatusMessage = "Project refresh failed without changing your selection: " + exception.Message;
            }
        }
        finally
        {
            IsBusy = false;
            await RefreshPendingAsync(cancellationToken: cancellationToken);
        }
    }

    /// <summary>Restores legacy preferences only while the initiating commander profile remains active.</summary>
    private async Task RestoreLegacyProfileAsync(CancellationToken cancellationToken = default)
    {
        if (legacyProfileStore is null || recovery.FrontierId is null || CommanderName is null)
        {
            return;
        }

        int version = recovery.ProfileVersion;
        LegacyColonizationProfileLoadResult result = await legacyProfileStore.LoadAsync(
            recovery.FrontierId,
            cancellationToken
        );
        if (version != recovery.ProfileVersion)
        {
            return;
        }
        if (result.Error is not null)
        {
            StatusMessage = "The imported colonization cache could not be read: " + result.Error;
            return;
        }

        if (result.Snapshot is not { } snapshot)
        {
            return;
        }

        hiddenProjectIds = snapshot.HiddenProjectIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        primaryProjectId = snapshot.PrimaryProjectId;
        recovery.ReplaceWorkspace(snapshot.Projects, snapshot.FleetCarriers);
        Projects = recovery.Projects.Select(CreateRow).ToArray();
        HasUnsavedProjectVisibility = false;
        UpdateProjectSummary();
        string warning =
            result.Warnings.Count == 0 ? string.Empty : $" Ignored {result.Warnings.Count:N0} invalid cached item(s).";
        StatusMessage =
            $"Restored {Projects.Count:N0} imported colonization "
            + $"project(s) from {Path.GetFileName(result.Path)}.{warning}";
    }

    /// <summary>Saves visibility for a captured commander and prevents a stale response replacing another workspace.</summary>
    public async Task SaveProjectVisibilityAsync()
    {
        if (!IsEnabled || IsBusy || CommanderName is null || !HasUnsavedProjectVisibility)
        {
            return;
        }

        int version = recovery.ProfileVersion;
        IsBusy = true;
        StatusMessage = "Saving project visibility to Raven Colonial...";
        try
        {
            IReadOnlyList<string> saved = await client.SaveHiddenProjectIdsAsync(
                CommanderName,
                hiddenProjectIds,
                CancellationToken.None
            );
            if (version != recovery.ProfileVersion)
            {
                return;
            }
            hiddenProjectIds = saved.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (ColonizationProjectRowViewModel row in Projects)
            {
                row.UpdateShown(!hiddenProjectIds.Contains(row.Project.BuildId));
            }

            HasUnsavedProjectVisibility = false;
            UpdateProjectSummary();
            StatusMessage = "Project visibility saved to Raven Colonial.";
        }
        catch (Exception exception)
            when (exception is HttpRequestException or InvalidDataException or TaskCanceledException)
        {
            if (version != recovery.ProfileVersion)
            {
                return;
            }
            StatusMessage = "Project visibility was not saved: " + exception.Message;
        }
        finally
        {
            IsBusy = false;
            await RefreshPendingAsync(CancellationToken.None);
        }
    }

    private ColonizationProjectRowViewModel CreateRow(ColonizationProject project)
    {
        IReadOnlyList<ColonizationBuildCost> matchingBuilds = buildCatalog.FindByLayout(project.BuildType);
        ColonizationBuildCost? build =
            (matchingBuilds.Count > 0 ? matchingBuilds[0] : null) ?? buildCatalog.FindByBuildType(project.BuildType);
        string fleetCarrierType = project.IsFleetCarrierLoading ? "Fleet Carrier loading" : project.BuildType;
        string type = build is null ? fleetCarrierType : $"{build.DisplayName} ({project.BuildType})";
        return new ColonizationProjectRowViewModel(
            project,
            type,
            string.Equals(project.BuildId, primaryProjectId, StringComparison.OrdinalIgnoreCase),
            !hiddenProjectIds.Contains(project.BuildId),
            OnProjectShownChanged,
            TogglePrimaryProjectAsync
        );
    }

    /// <summary>Changes the captured commander's primary project without applying results to a replacement profile.</summary>
    public async Task TogglePrimaryProjectAsync(ColonizationProjectRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!IsEnabled || IsBusy || CommanderName is null)
        {
            return;
        }

        string? nextPrimaryId = row.IsPrimary ? null : row.Project.BuildId;
        int version = recovery.ProfileVersion;
        IsBusy = true;
        StatusMessage = nextPrimaryId is null
            ? "Clearing the primary Raven Colonial project..."
            : $"Setting {row.BuildName} as the primary Raven Colonial project...";
        try
        {
            await client.SetPrimaryProjectAsync(CommanderName, nextPrimaryId, CancellationToken.None);
            if (version != recovery.ProfileVersion)
            {
                return;
            }
            primaryProjectId = nextPrimaryId;
            Projects = recovery.Projects.Select(CreateRow).ToArray();
            StatusMessage = nextPrimaryId is null
                ? "The primary Raven Colonial project was cleared."
                : $"{row.BuildName} is now the primary Raven Colonial project.";
        }
        catch (Exception exception)
            when (exception
                    is HttpRequestException
                        or InvalidDataException
                        or TaskCanceledException
                        or ArgumentException
            )
        {
            if (version != recovery.ProfileVersion)
            {
                return;
            }
            StatusMessage = "The primary project was not changed: " + exception.Message;
        }
        finally
        {
            IsBusy = false;
            await RefreshPendingAsync(CancellationToken.None);
        }
    }

    /// <summary>Installs a successfully created project before optional cleanup and ignores obsolete callback results.</summary>
    private Task OnProjectCreatedAsync(ColonizationProject project)
    {
        return recovery.InstallCreatedProjectAsync(project);
    }

    private void OnRecoveryProjectsChanged()
    {
        Projects = recovery.Projects.Select(CreateRow).ToArray();
        UpdateProjectSummary();
    }

    private void UpdateProjectEditorContext()
    {
        ColonizationConstructionSnapshot snapshot = recovery.CreateConstructionSnapshot();
        ProjectEditor.UpdateContext(
            new ColonizationProjectEditorContext(
                IsEnabled,
                CommanderName,
                recovery.CurrentSystemName,
                currentStarPosition,
                snapshot.CurrentDock,
                snapshot.CurrentDepot,
                recovery.ApiKey,
                recovery.CurrentBodyId,
                recovery.CurrentBodyName
            )
        );
    }

    private void UpdateSystemEditorContext()
    {
        SystemEditor.UpdateContext(
            new ColonizationSystemEditorContext(
                IsEnabled,
                CommanderName,
                recovery.CurrentSystemName,
                recovery.CurrentSystemAddress,
                recovery.ApiKey
            )
        );
    }

    private void OnProjectShownChanged(ColonizationProjectRowViewModel row, bool isShown)
    {
        if (isShown)
        {
            hiddenProjectIds.Remove(row.Project.BuildId);
        }
        else
        {
            hiddenProjectIds.Add(row.Project.BuildId);
        }

        HasUnsavedProjectVisibility = true;
        UpdateProjectSummary();
    }

    private void UpdateProjectSummary()
    {
        ColonizationProjectTotals totals = ColonizationProjectCalculator.CalculateTotals(
            Projects.Select(row => row.Project),
            hiddenProjectIds,
            recovery.ShipCargoCapacity
        );
        string trips = totals.TripsInCurrentShip is long tripCount
            ? $" | {tripCount:N0} trips in current ship"
            : string.Empty;
        ProjectSummary = $"Cargo required: {totals.RemainingCargo:N0}" + trips;
        UpdateCommodityPlan();
    }

    private void UpdateConstructionDisplay()
    {
        ColonizationConstructionSnapshot snapshot = recovery.CreateConstructionSnapshot();
        if (snapshot.CurrentDock is null)
        {
            ConstructionTitle = "No construction depot active";
            ConstructionStatus = "Dock at a construction site and open Construction Services.";
            ConstructionResources = [];
            return;
        }

        ConstructionTitle = snapshot.CurrentDock.StationName;
        if (snapshot.CurrentDepot is null)
        {
            ConstructionStatus = snapshot.CurrentDock.IsConstructionSite
                ? "Open Construction Services to load current requirements."
                : "The current station is not a colonization construction site.";
            ConstructionResources = [];
            return;
        }

        ColonizationConstructionDepotSnapshot depot = snapshot.CurrentDepot;
        ConstructionStatus = depot.IsComplete
            ? "Construction complete."
            : (depot.IsFailed) switch
            {
                true => "Construction failed.",
                false => $"{depot.ReportedProgress:P1} complete | " + $"{depot.TotalRemaining:N0} cargo remaining",
            };
        ConstructionResources = depot
            .Resources.OrderByDescending(resource => resource.RemainingAmount)
            .ThenBy(resource => resource.LocalizedName)
            .Select(resource => new ColonizationResourceRowViewModel(
                resource.LocalizedName,
                resource.RemainingAmount,
                resource.ProvidedAmount,
                resource.RequiredAmount,
                resource.Payment
            ))
            .ToArray();
    }

    private void ClearProjects()
    {
        Projects = [];
        recovery.ClearWorkspace();
        hiddenProjectIds.Clear();
        primaryProjectId = null;
        HasUnsavedProjectVisibility = false;
        UpdateProjectSummary();
        syncFleetCarrierCargoCommand.RaiseCanExecuteChanged();
    }

    private void UpdateCommodityPlan()
    {
        OnPropertyChanged(nameof(LinkedFleetCarriers));
        OnPropertyChanged(nameof(DetectedSquadronCarrierMarketId));
        ColonizationConstructionSnapshot construction = recovery.CreateConstructionSnapshot();
        ColonizationDockingSnapshot? dock = construction.CurrentDock;
        MarketSnapshot? currentMarket = recovery.CurrentMarket;
        bool hasMarketSinceDocking =
            currentMarket is not null
            && dock?.Timestamp is not null
            && dock.MarketId == currentMarket.MarketId
            && currentMarket.Timestamp > dock.Timestamp;
        CommodityOverlay.Apply(
            ColonizationCommodityPlanner.Create(
                new ColonizationCommodityPlanRequest
                {
                    Projects = Projects.Select(row => row.Project),
                    HiddenBuildIds = hiddenProjectIds,
                    PrimaryBuildId = primaryProjectId,
                    CommanderName = CommanderName,
                    FleetCarriers = recovery.FleetCarriers,
                    ShipCargo = shipCargo,
                    Construction = construction,
                    Market = currentMarket,
                }
            ),
            latestStatus,
            hasMarketSinceDocking,
            construction.IsSquadronBankOpen
        );
    }

    private string GetShipCargoReadyStatus()
    {
        if (SharedCargoSuppressed)
        {
            return "Ship cargo is paused while multiple Elite windows are running because Cargo.json cannot be attributed safely.";
        }

        if (!ShipCargoPublishingEnabled)
        {
            return "Automatic ship cargo publishing is off.";
        }

        if (!IsEnabled)
        {
            return "Enable Raven Colonial before publishing ship cargo.";
        }

        return HasStoredRavenApiKey
            ? "Ship cargo will publish after Cargo.json changes."
            : "Save a Raven API key before ship cargo can publish.";
    }

    private bool CanSaveRavenApiKey()
    {
        string? normalized = string.IsNullOrWhiteSpace(RavenApiKey) ? null : RavenApiKey.Trim();
        return commanderProfileStore is not null
            && recovery.FrontierId is not null
            && !IsFleetCarrierSyncBusy
            && !string.Equals(normalized, recovery.ApiKey, StringComparison.Ordinal);
    }

    private bool CanPublishCurrentFleetCarrier()
    {
        return IsEnabled
            && HasStoredRavenApiKey
            && !IsFleetCarrierSyncBusy
            && recovery.CurrentDock is { MarketId: > 0, StationType: not null } dock
            && string.Equals(dock.StationType, FleetCarrierStationType, StringComparison.OrdinalIgnoreCase);
    }

    private string GetFleetCarrierPublishBlockReason()
    {
        if (!IsEnabled)
        {
            return "Enable Raven Colonial before publishing a Fleet Carrier.";
        }

        if (!HasStoredRavenApiKey)
        {
            return "Save a Raven API key before publishing a Fleet Carrier.";
        }

        return "Dock at the Fleet Carrier you want to publish and link.";
    }

    private bool CanSyncFleetCarrierCargo()
    {
        return !IsFleetCarrierSyncBusy && recovery.CanSyncFleetCarrierCargo;
    }

    private string GetFleetCarrierSyncBlockReason()
    {
        if (!IsEnabled)
        {
            return "Enable Raven Colonial before syncing Fleet Carrier cargo.";
        }

        if (!FleetCarrierCargoSyncEnabled)
        {
            return FleetCarrierSyncOffMessage;
        }

        if (!HasStoredRavenApiKey)
        {
            return "Save a Raven API key before syncing Fleet Carrier cargo.";
        }

        if (recovery.CurrentMarket is not { } currentMarket)
        {
            return "Open a Fleet Carrier commodity market in Elite first.";
        }

        if (!string.Equals(currentMarket.StationType, FleetCarrierStationType, StringComparison.OrdinalIgnoreCase))
        {
            return "The current market is not a Fleet Carrier market.";
        }

        if (!recovery.FleetCarriers.Any(carrier => carrier.MarketId == currentMarket.MarketId))
        {
            return "The current Fleet Carrier is not linked to this commander in Raven Colonial.";
        }

        return "Dock at the Fleet Carrier and reopen its commodity market before syncing.";
    }

    private string? GetShipCargoPublishingBlockReason()
    {
        if (!IsEnabled)
        {
            return "Enable Raven Colonial before publishing ship cargo.";
        }

        if (recovery.ApiKey is null)
        {
            return "Save a Raven API key before ship cargo can publish.";
        }

        if (CommanderName is null)
        {
            return "Load a commander profile before publishing ship cargo.";
        }

        if (!Projects.Any(project => project.IsShown))
        {
            return "Ship cargo was not published because no visible colonization projects are active.";
        }

        if (string.IsNullOrWhiteSpace(currentShipType))
        {
            return "Ship cargo is waiting for a Loadout journal event.";
        }

        return null;
    }

    private void ApplyShipIdentity(JournalEventEnvelope journalEvent)
    {
        JsonElement root = journalEvent.Payload;
        switch (journalEvent.EventName)
        {
            case "LoadGame":
            case "Loadout":
                currentShipType = GetJournalString(root, "Ship") ?? currentShipType;
                currentShipName =
                    GetJournalString(root, "ShipName") ?? GetJournalString(root, "ShipIdent") ?? currentShipName;
                break;

            case "ShipyardSwap":
                currentShipType = GetJournalString(root, "ShipType") ?? currentShipType;
                break;
        }
    }

    private static string? GetJournalString(JsonElement root, string propertyName)
    {
        return
            root.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;
    }

    private static string? CombineMessages(params string?[] messages)
    {
        string?[] present = messages.Where(message => !string.IsNullOrWhiteSpace(message)).ToArray();
        return present.Length == 0 ? null : string.Join(Environment.NewLine, present);
    }

    private void SaveOverlayPreferences(
        ColonizationOverlayPreferences updatedPreferences,
        [CallerMemberName] string? propertyName = null
    )
    {
        if (updatedPreferences == overlayPreferences)
        {
            return;
        }

        try
        {
            settingsStore.SaveOverlayPreferences(updatedPreferences);
            overlayPreferences = updatedPreferences;
            CommodityOverlay.ApplyPreferences(updatedPreferences);
            OnPropertyChanged(propertyName);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusMessage = "The construction overlay preference could not be saved: " + exception.Message;
        }
    }

    /// <summary>Refreshes workspace and recovery action availability after preferences or commander context change.</summary>
    private void RaiseCommandStates()
    {
        refreshCommand.RaiseCanExecuteChanged();
        saveProjectsCommand.RaiseCanExecuteChanged();
        saveRavenApiKeyCommand.RaiseCanExecuteChanged();
        publishFleetCarrierCommand.RaiseCanExecuteChanged();
        syncFleetCarrierCargoCommand.RaiseCanExecuteChanged();
        RaiseContributionRecoveryCommandStates();
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

    /// <summary>Reflects delivery recovery changes in workspace bindings, status lines, and the commodity overlay.</summary>
    private sealed class DeliveryObserver(ColonizationViewModel owner) : IColonizationDeliveryObserver
    {
        public void ProjectsChanged() => owner.OnRecoveryProjectsChanged();

        public void FleetCarriersChanged() => owner.UpdateCommodityPlan();

        public void PendingFleetCarrierCargoChanged(IEnumerable<string>? commodities) =>
            owner.CommodityOverlay.ApplyPendingFleetCarrierCargo(commodities);

        public void FleetCarrierStatusChanged(ColonizationDeliveryNotice notice) =>
            owner.FleetCarrierSyncStatus = ColonizationDeliveryMessages.Describe(notice);

        public void FleetCarrierSyncBusyChanged(bool busy) => owner.IsFleetCarrierSyncBusy = busy;

        public void StatusChanged(ColonizationDeliveryNotice notice) =>
            owner.StatusMessage = ColonizationDeliveryMessages.Describe(notice);

        public void PendingContributionsChanged() => owner.NotifyContributionRecovery();

        public void BuildSiteRepairWarningChanged() => owner.OnPropertyChanged(nameof(StatusMessage));

        public void CurrentBodyChanged() => owner.UpdateProjectEditorContext();

        public void DockingRefreshRequested() => owner.ScheduleDockingRefresh();
    }

    private sealed class AsyncCommand(Func<Task> execute, Func<bool> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => canExecute();

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

public sealed class ColonizationProjectRowViewModel : INotifyPropertyChanged
{
    private readonly Action<ColonizationProjectRowViewModel, bool> changed;
    private bool isShown;

    public ColonizationProjectRowViewModel(
        ColonizationProject project,
        string typeDescription,
        bool isPrimary,
        bool isShown,
        Action<ColonizationProjectRowViewModel, bool> changed,
        Func<ColonizationProjectRowViewModel, Task> togglePrimary
    )
    {
        Project = project;
        TypeDescription = typeDescription;
        IsPrimary = isPrimary;
        this.isShown = isShown;
        this.changed = changed;
        TogglePrimaryCommand = new RowAsyncCommand(() => togglePrimary(this));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ColonizationProject Project { get; }

    public string BuildName => Project.BuildName;

    public string SystemName => Project.SystemName;

    public string SystemAddressText => SystemAddressFormatter.Format(Project.SystemAddress);

    public string TypeDescription { get; }

    public bool IsPrimary { get; }

    public string PrimaryLabel => IsPrimary ? "PRIMARY" : string.Empty;

    public string PrimaryActionLabel => IsPrimary ? "Clear primary" : "Make primary";

    public ICommand TogglePrimaryCommand { get; }

    public string ProgressText =>
        Project.IsFleetCarrierLoading
            ? $"? of {Project.MaximumRequired:N0}"
            : Project.Progress switch
            {
                double progress => (progress * 100).ToString("0", CultureInfo.InvariantCulture)
                    + "% of "
                    + Project.MaximumRequired.ToString("N0", CultureInfo.CurrentCulture),
                null => "Progress unavailable",
            };

    public bool IsShown
    {
        get => isShown;
        set
        {
            if (value == isShown)
            {
                return;
            }

            isShown = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsShown)));
            changed(this, value);
        }
    }

    internal void UpdateShown(bool value)
    {
        if (value == isShown)
        {
            return;
        }

        isShown = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsShown)));
    }

    private sealed class RowAsyncCommand(Func<Task> execute) : ICommand
    {
        private bool isExecuting;

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => !isExecuting;

        public async void Execute(object? parameter)
        {
            if (isExecuting)
            {
                return;
            }

            isExecuting = true;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            try
            {
                await execute();
            }
            finally
            {
                isExecuting = false;
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}

public sealed record ColonizationResourceRowViewModel(
    string Name,
    int Remaining,
    int Provided,
    int Required,
    int Payment
)
{
    public string RemainingText => $"{Remaining:N0} remaining";

    public string ProgressText => $"{Provided:N0} / {Required:N0}";

    public string PaymentText => $"{Payment:N0} CR/t";
}
