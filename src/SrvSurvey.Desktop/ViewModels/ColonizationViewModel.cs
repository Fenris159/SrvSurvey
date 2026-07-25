using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class ColonizationViewModel : INotifyPropertyChanged
{
    private readonly IRavenColonialClient client;
    private readonly ColonizationBuildCatalog buildCatalog;
    private readonly ColonizationSettingsStore settingsStore;
    private readonly CommanderProfileStore? commanderProfileStore;
    private readonly LegacyColonizationProfileStore? legacyProfileStore;
    private ColonizationOverlayPreferences overlayPreferences;
    private readonly ColonizationConstructionState constructionState = new();
    private readonly AsyncCommand refreshCommand;
    private readonly AsyncCommand saveProjectsCommand;
    private readonly AsyncCommand saveRavenApiKeyCommand;
    private readonly AsyncCommand syncFleetCarrierCargoCommand;
    private IReadOnlyList<ColonizationProjectRowViewModel> projects = [];
    private IReadOnlyList<ColonizationResourceRowViewModel> constructionResources =
        [];
    private HashSet<string> hiddenProjectIds = new(
        StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<ColonizationFleetCarrier> fleetCarriers = [];
    private CargoSnapshot? shipCargo;
    private MarketSnapshot? currentMarket;
    private EliteStatus? latestStatus;
    private string? commanderName;
    private string? currentSystemName;
    private IReadOnlyList<double> currentStarPosition = [];
    private string? primaryProjectId;
    private bool isEnabled;
    private bool isBusy;
    private bool hasUnsavedProjectVisibility;
    private bool fleetCarrierCargoSyncEnabled;
    private bool isFleetCarrierSyncBusy;
    private string ravenApiKey = string.Empty;
    private string? storedRavenApiKey;
    private string? profileFrontierId;
    private bool profileIsOdyssey = true;
    private (long MarketId, DateTimeOffset Timestamp)? lastSyncedMarket;
    private string ravenCredentialStatus =
        "Load a commander profile to configure a Raven API key.";
    private string fleetCarrierSyncStatus =
        "Automatic Fleet Carrier cargo sync is off.";
    private string statusMessage;
    private string projectSummary = "No projects loaded.";
    private string constructionTitle = "No construction depot active";
    private string constructionStatus =
        "Dock at a construction site and open Construction Services.";

    public ColonizationViewModel(
        ColonizationSettingsStore settingsStore,
        IRavenColonialClient? client = null,
        ColonizationBuildCatalog? buildCatalog = null,
        CommanderProfileStore? commanderProfileStore = null,
        LegacyColonizationProfileStore? legacyProfileStore = null)
    {
        this.settingsStore = settingsStore
            ?? throw new ArgumentNullException(nameof(settingsStore));
        this.client = client ?? new RavenColonialClient();
        this.commanderProfileStore = commanderProfileStore;
        this.legacyProfileStore = legacyProfileStore;
        this.buildCatalog = buildCatalog
            ?? ColonizationBuildCatalog.LoadEmbedded();
        overlayPreferences = settingsStore.LoadOverlayPreferences();
        fleetCarrierCargoSyncEnabled =
            settingsStore.LoadFleetCarrierCargoSyncEnabled();
        isEnabled = settingsStore.LoadEnabled();
        statusMessage = isEnabled
            ? "Raven Colonial access is enabled. Waiting for a commander profile."
            : "Raven Colonial access is off. No project data will be fetched or published.";
        refreshCommand = new AsyncCommand(
            RefreshAsync,
            () => IsEnabled && !IsBusy && CommanderName is not null);
        saveProjectsCommand = new AsyncCommand(
            SaveProjectVisibilityAsync,
            () => IsEnabled
                && !IsBusy
                && HasUnsavedProjectVisibility
                && CommanderName is not null);
        saveRavenApiKeyCommand = new AsyncCommand(
            SaveRavenApiKeyAsync,
            CanSaveRavenApiKey);
        syncFleetCarrierCargoCommand = new AsyncCommand(
            () => SyncFleetCarrierCargoAsync(force: true),
            CanSyncFleetCarrierCargo);
        RefreshCommand = refreshCommand;
        SaveProjectsCommand = saveProjectsCommand;
        SaveRavenApiKeyCommand = saveRavenApiKeyCommand;
        SyncFleetCarrierCargoCommand = syncFleetCarrierCargoCommand;
        ProjectEditor = new ColonizationProjectEditorViewModel(
            this.client,
            this.buildCatalog,
            OnProjectCreatedAsync);
        CommodityOverlay = new ColonizationCommodityOverlayViewModel();
        CommodityOverlay.ApplyPreferences(overlayPreferences);
        UpdateProjectEditorContext();
        UpdateCommodityPlan();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand RefreshCommand { get; }

    public ICommand SaveProjectsCommand { get; }

    public ICommand SaveRavenApiKeyCommand { get; }

    public ICommand SyncFleetCarrierCargoCommand { get; }

    public ColonizationProjectEditorViewModel ProjectEditor { get; }

    public ColonizationCommodityOverlayViewModel CommodityOverlay { get; }

    public bool AutoShowCommodityOverlay
    {
        get => overlayPreferences.AutoShow;
        set => SaveOverlayPreferences(
            overlayPreferences with { AutoShow = value });
    }

    public bool ShowCommodityOverlayOnRightPanel
    {
        get => overlayPreferences.ShowOnRightPanel;
        set => SaveOverlayPreferences(
            overlayPreferences with { ShowOnRightPanel = value });
    }

    public bool ShowFleetCarrierCargo
    {
        get => overlayPreferences.ShowFleetCarrierCargo;
        set => SaveOverlayPreferences(
            overlayPreferences with { ShowFleetCarrierCargo = value });
    }

    public bool ShowFleetCarrierDelta
    {
        get => overlayPreferences.ShowFleetCarrierDelta;
        set => SaveOverlayPreferences(
            overlayPreferences with { ShowFleetCarrierDelta = value });
    }

    public bool InlineFleetCarrierCargo
    {
        get => overlayPreferences.InlineFleetCarrierCargo;
        set => SaveOverlayPreferences(
            overlayPreferences with { InlineFleetCarrierCargo = value });
    }

    public bool CollapseCoveredCommodityGroups
    {
        get => overlayPreferences.CollapseCoveredGroups;
        set => SaveOverlayPreferences(
            overlayPreferences with { CollapseCoveredGroups = value });
    }

    public bool HighlightAlmostCoveredFleetCarrierLoads
    {
        get => overlayPreferences.HighlightAlmostCoveredFleetCarrierLoads;
        set => SaveOverlayPreferences(
            overlayPreferences with
            {
                HighlightAlmostCoveredFleetCarrierLoads = value,
            });
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

    public bool HasCommanderProfile => profileFrontierId is not null;

    public bool HasStoredRavenApiKey =>
        !string.IsNullOrWhiteSpace(storedRavenApiKey);

    public string RavenCredentialStatus
    {
        get => ravenCredentialStatus;
        private set => SetField(ref ravenCredentialStatus, value);
    }

    public bool FleetCarrierCargoSyncEnabled
    {
        get => fleetCarrierCargoSyncEnabled;
        set
        {
            if (value == fleetCarrierCargoSyncEnabled)
            {
                return;
            }

            try
            {
                settingsStore.SaveFleetCarrierCargoSyncEnabled(value);
                fleetCarrierCargoSyncEnabled = value;
                OnPropertyChanged();
                FleetCarrierSyncStatus = value
                    ? HasStoredRavenApiKey
                        ? "Fleet Carrier cargo will sync from matching Market.json updates."
                        : "Save a Raven API key before Fleet Carrier cargo can sync."
                    : "Automatic Fleet Carrier cargo sync is off.";
                syncFleetCarrierCargoCommand.RaiseCanExecuteChanged();
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or InvalidOperationException)
            {
                FleetCarrierSyncStatus =
                    "The Fleet Carrier sync preference could not be saved: "
                    + exception.Message;
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
                RaiseCommandStates();
            }
        }
    }

    public string FleetCarrierSyncButtonText =>
        IsFleetCarrierSyncBusy ? "Syncing..." : "Sync current market";

    public string FleetCarrierSyncStatus
    {
        get => fleetCarrierSyncStatus;
        private set => SetField(ref fleetCarrierSyncStatus, value);
    }

    public bool IsEnabled
    {
        get => isEnabled;
        set
        {
            if (value == isEnabled)
            {
                return;
            }

            try
            {
                settingsStore.SaveEnabled(value);
                isEnabled = value;
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
                    ClearProjects();
                    StatusMessage = "Raven Colonial access is off. No project data will be fetched or published.";
                }

                UpdateProjectEditorContext();
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or InvalidOperationException)
            {
                StatusMessage =
                    "The Raven Colonial preference could not be saved: "
                    + exception.Message;
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

    public string? CommanderName
    {
        get => commanderName;
        private set
        {
            if (SetField(ref commanderName, value))
            {
                OnPropertyChanged(nameof(CommanderStatus));
                RaiseCommandStates();
            }
        }
    }

    public string CommanderStatus => CommanderName is null
        ? "No commander profile is active."
        : $"Commander: {CommanderName}";

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

    public string StatusMessage
    {
        get => statusMessage;
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

    public IReadOnlyList<ColonizationResourceRowViewModel>
        ConstructionResources
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

    public void SetCommanderProfile(
        string? frontierId,
        bool isOdyssey,
        string? apiKey)
    {
        profileFrontierId = string.IsNullOrWhiteSpace(frontierId)
            ? null
            : frontierId.Trim();
        profileIsOdyssey = isOdyssey;
        storedRavenApiKey = string.IsNullOrWhiteSpace(apiKey)
            ? null
            : apiKey.Trim();
        RavenApiKey = storedRavenApiKey ?? string.Empty;
        lastSyncedMarket = null;
        RavenCredentialStatus = profileFrontierId is null
            ? "Load a commander profile to configure a Raven API key."
            : storedRavenApiKey is null
                ? "No Raven API key is saved for this commander."
                : "A Raven API key is saved for this commander.";
        FleetCarrierSyncStatus = FleetCarrierCargoSyncEnabled
            ? storedRavenApiKey is null
                ? "Save a Raven API key before Fleet Carrier cargo can sync."
                : "Fleet Carrier cargo will sync from matching Market.json updates."
            : "Automatic Fleet Carrier cargo sync is off.";
        OnPropertyChanged(nameof(HasCommanderProfile));
        OnPropertyChanged(nameof(HasStoredRavenApiKey));
        RaiseCommandStates();
    }

    public async Task SetCommanderAsync(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
        if (string.Equals(
                CommanderName,
                normalized,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        CommanderName = normalized;
        ClearProjects();
        UpdateProjectEditorContext();
        if (CommanderName is null)
        {
            StatusMessage = "No commander profile is active.";
            return;
        }

        if (IsEnabled)
        {
            await RefreshAsync();
        }
    }

    public void ApplyJournalEvents(
        IReadOnlyList<JournalEventEnvelope> journalEvents)
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        var before = constructionState.Version;
        foreach (var journalEvent in journalEvents)
        {
            constructionState.Apply(journalEvent);
        }

        if (constructionState.Version != before)
        {
            UpdateConstructionDisplay();
            UpdateProjectSummary();
            UpdateProjectEditorContext();
            syncFleetCarrierCargoCommand.RaiseCanExecuteChanged();
        }
    }

    public void UpdateCargo(CargoSnapshot? cargo)
    {
        if (cargo is null)
        {
            return;
        }

        shipCargo = cargo;
        UpdateCommodityPlan();
    }

    public async Task UpdateMarketAsync(MarketSnapshot? market)
    {
        if (market is null)
        {
            return;
        }

        currentMarket = market;
        UpdateCommodityPlan();
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
        UpdateCommodityPlan();
    }

    public void UpdateSystemContext(
        string? systemName,
        GalacticCoordinate? position)
    {
        currentSystemName = string.IsNullOrWhiteSpace(systemName)
            ? null
            : systemName.Trim();
        currentStarPosition = position is GalacticCoordinate coordinate
            ? [coordinate.X, coordinate.Y, coordinate.Z]
            : [];
        UpdateProjectEditorContext();
    }

    public void ReportLinkFailure(string message)
    {
        StatusMessage = "Raven Colonial could not be opened: " + message;
    }

    public async Task SaveRavenApiKeyAsync()
    {
        if (!CanSaveRavenApiKey()
            || commanderProfileStore is null
            || profileFrontierId is null)
        {
            return;
        }

        IsFleetCarrierSyncBusy = true;
        try
        {
            var normalized = string.IsNullOrWhiteSpace(RavenApiKey)
                ? null
                : RavenApiKey.Trim();
            await commanderProfileStore.SaveRavenColonialApiKeyAsync(
                profileFrontierId,
                CommanderName,
                profileIsOdyssey,
                normalized);
            storedRavenApiKey = normalized;
            RavenApiKey = normalized ?? string.Empty;
            RavenCredentialStatus = normalized is null
                ? "The Raven API key was removed from this commander profile."
                : "The Raven API key was saved to this commander profile.";
            OnPropertyChanged(nameof(HasStoredRavenApiKey));
            if (normalized is null && FleetCarrierCargoSyncEnabled)
            {
                FleetCarrierCargoSyncEnabled = false;
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            RavenCredentialStatus =
                "The Raven API key was not saved: " + exception.Message;
        }
        finally
        {
            IsFleetCarrierSyncBusy = false;
            RaiseCommandStates();
        }
    }

    public async Task SyncFleetCarrierCargoAsync(bool force = true)
    {
        if (!CanSyncFleetCarrierCargo()
            || currentMarket is null
            || storedRavenApiKey is null)
        {
            if (force)
            {
                FleetCarrierSyncStatus = GetFleetCarrierSyncBlockReason();
            }

            return;
        }

        var identity = (currentMarket.MarketId, currentMarket.Timestamp);
        if (!force && lastSyncedMarket == identity)
        {
            return;
        }

        var localCarrier = fleetCarriers.First(carrier =>
            carrier.MarketId == currentMarket.MarketId);
        IsFleetCarrierSyncBusy = true;
        FleetCarrierSyncStatus =
            $"Checking {GetCarrierName(localCarrier)} market cargo...";
        try
        {
            var serverCarrier = await client.GetFleetCarrierAsync(
                currentMarket.MarketId);
            if (serverCarrier is null)
            {
                FleetCarrierSyncStatus =
                    "Raven Colonial does not have this Fleet Carrier.";
                return;
            }

            var replacements =
                ColonizationFleetCarrierCargoSynchronizer
                    .CreateMarketReplacement(currentMarket, serverCarrier);
            if (replacements.Count == 0)
            {
                ReplaceLocalFleetCarrier(serverCarrier);
                lastSyncedMarket = identity;
                FleetCarrierSyncStatus =
                    $"{GetCarrierName(serverCarrier)} cargo is already current.";
                return;
            }

            CommodityOverlay.ApplyPendingFleetCarrierCargo(
                replacements.Keys);
            FleetCarrierSyncStatus =
                $"Updating {replacements.Count:N0} cargo entries for "
                + GetCarrierName(serverCarrier)
                + "...";
            var updatedCargo = await client.ReplaceFleetCarrierCargoAsync(
                currentMarket.MarketId,
                replacements,
                storedRavenApiKey);
            ReplaceLocalFleetCarrier(serverCarrier with
            {
                Cargo = updatedCargo.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.OrdinalIgnoreCase),
            });
            lastSyncedMarket = identity;
            FleetCarrierSyncStatus =
                $"Updated {replacements.Count:N0} cargo entries for "
                + GetCarrierName(serverCarrier)
                + ".";
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or InvalidDataException
                or TaskCanceledException
                or ArgumentException)
        {
            FleetCarrierSyncStatus =
                "Fleet Carrier cargo was not updated: " + exception.Message;
        }
        finally
        {
            CommodityOverlay.ApplyPendingFleetCarrierCargo(null);
            IsFleetCarrierSyncBusy = false;
        }
    }

    public async Task RefreshAsync()
    {
        if (!IsEnabled || IsBusy || CommanderName is null)
        {
            return;
        }

        if (Projects.Count == 0)
        {
            await RestoreLegacyProfileAsync();
        }

        IsBusy = true;
        StatusMessage = "Fetching active projects from Raven Colonial...";
        try
        {
            var result = await client.GetCommanderProjectsAsync(CommanderName);
            hiddenProjectIds = result.HiddenProjectIds.ToHashSet(
                StringComparer.OrdinalIgnoreCase);
            primaryProjectId = result.PrimaryProjectId;
            fleetCarriers = result.FleetCarriers;
            Projects = result.Projects
                .OrderBy(project => project.SystemName)
                .ThenBy(project => project.BuildName)
                .Select(CreateRow)
                .ToArray();
            HasUnsavedProjectVisibility = false;
            UpdateProjectSummary();
            StatusMessage = Projects.Count switch
            {
                0 => "No active Raven Colonial projects were found for this commander.",
                1 => "Loaded 1 active Raven Colonial project.",
                _ => $"Loaded {Projects.Count:N0} active Raven Colonial projects.",
            };
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or InvalidDataException
                or TaskCanceledException)
        {
            StatusMessage = "Project refresh failed without changing your selection: "
                + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RestoreLegacyProfileAsync()
    {
        if (legacyProfileStore is null
            || profileFrontierId is null
            || CommanderName is null)
        {
            return;
        }

        var result = await legacyProfileStore.LoadAsync(profileFrontierId);
        if (result.Error is not null)
        {
            StatusMessage = "The imported colonisation cache could not be read: "
                + result.Error;
            return;
        }

        if (result.Snapshot is not { } snapshot)
        {
            return;
        }

        hiddenProjectIds = snapshot.HiddenProjectIds.ToHashSet(
            StringComparer.OrdinalIgnoreCase);
        primaryProjectId = snapshot.PrimaryProjectId;
        fleetCarriers = snapshot.FleetCarriers;
        Projects = snapshot.Projects
            .OrderBy(project => project.SystemName)
            .ThenBy(project => project.BuildName)
            .Select(CreateRow)
            .ToArray();
        HasUnsavedProjectVisibility = false;
        UpdateProjectSummary();
        var warning = result.Warnings.Count == 0
            ? string.Empty
            : $" Ignored {result.Warnings.Count:N0} invalid cached item(s).";
        StatusMessage = $"Restored {Projects.Count:N0} imported colonisation "
            + $"project(s) from {Path.GetFileName(result.Path)}.{warning}";
    }

    public async Task SaveProjectVisibilityAsync()
    {
        if (!IsEnabled
            || IsBusy
            || CommanderName is null
            || !HasUnsavedProjectVisibility)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Saving project visibility to Raven Colonial...";
        try
        {
            var saved = await client.SaveHiddenProjectIdsAsync(
                CommanderName,
                hiddenProjectIds);
            hiddenProjectIds = saved.ToHashSet(
                StringComparer.OrdinalIgnoreCase);
            foreach (var row in Projects)
            {
                row.UpdateShown(!hiddenProjectIds.Contains(row.Project.BuildId));
            }

            HasUnsavedProjectVisibility = false;
            UpdateProjectSummary();
            StatusMessage = "Project visibility saved to Raven Colonial.";
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or InvalidDataException
                or TaskCanceledException)
        {
            StatusMessage = "Project visibility was not saved: "
                + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private ColonizationProjectRowViewModel CreateRow(
        ColonizationProject project)
    {
        var build = buildCatalog.FindByLayout(project.BuildType).FirstOrDefault()
            ?? buildCatalog.FindByBuildType(project.BuildType);
        var type = project.IsFleetCarrierLoading
            ? "Fleet Carrier loading"
            : build is null
                ? project.BuildType
                : $"{build.DisplayName} ({project.BuildType})";
        return new ColonizationProjectRowViewModel(
            project,
            type,
            string.Equals(
                project.BuildId,
                primaryProjectId,
                StringComparison.OrdinalIgnoreCase),
            !hiddenProjectIds.Contains(project.BuildId),
            OnProjectShownChanged);
    }

    private Task OnProjectCreatedAsync(ColonizationProject project)
    {
        Projects = Projects
            .Where(row => !string.Equals(
                row.Project.BuildId,
                project.BuildId,
                StringComparison.OrdinalIgnoreCase))
            .Select(row => row.Project)
            .Append(project)
            .OrderBy(candidate => candidate.SystemName)
            .ThenBy(candidate => candidate.BuildName)
            .Select(CreateRow)
            .ToArray();
        UpdateProjectSummary();
        return Task.CompletedTask;
    }

    private void UpdateProjectEditorContext()
    {
        var snapshot = constructionState.CreateSnapshot();
        ProjectEditor.UpdateContext(new ColonizationProjectEditorContext(
            IsEnabled,
            CommanderName,
            currentSystemName,
            currentStarPosition,
            snapshot.CurrentDock,
            snapshot.CurrentDepot));
    }

    private void OnProjectShownChanged(
        ColonizationProjectRowViewModel row,
        bool isShown)
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
        var totals = ColonizationProjectCalculator.CalculateTotals(
            Projects.Select(row => row.Project),
            hiddenProjectIds,
            constructionState.ShipCargoCapacity);
        var trips = totals.TripsInCurrentShip is long tripCount
            ? $" | {tripCount:N0} trips in current ship"
            : string.Empty;
        ProjectSummary = $"Cargo required: {totals.RemainingCargo:N0}"
            + trips;
        UpdateCommodityPlan();
    }

    private void UpdateConstructionDisplay()
    {
        var snapshot = constructionState.CreateSnapshot();
        if (snapshot.CurrentDock is null)
        {
            ConstructionTitle = "No construction depot active";
            ConstructionStatus =
                "Dock at a construction site and open Construction Services.";
            ConstructionResources = [];
            return;
        }

        ConstructionTitle = snapshot.CurrentDock.StationName;
        if (snapshot.CurrentDepot is null)
        {
            ConstructionStatus = snapshot.CurrentDock.IsConstructionSite
                ? "Open Construction Services to load current requirements."
                : "The current station is not a colonisation construction site.";
            ConstructionResources = [];
            return;
        }

        var depot = snapshot.CurrentDepot;
        ConstructionStatus = depot.IsComplete
            ? "Construction complete."
            : depot.IsFailed
                ? "Construction failed."
                : $"{depot.ReportedProgress:P1} complete | "
                    + $"{depot.TotalRemaining:N0} cargo remaining";
        ConstructionResources = depot.Resources
            .OrderByDescending(resource => resource.RemainingAmount)
            .ThenBy(resource => resource.LocalizedName)
            .Select(resource => new ColonizationResourceRowViewModel(
                resource.LocalizedName,
                resource.RemainingAmount,
                resource.ProvidedAmount,
                resource.RequiredAmount,
                resource.Payment))
            .ToArray();
    }

    private void ClearProjects()
    {
        Projects = [];
        fleetCarriers = [];
        hiddenProjectIds.Clear();
        primaryProjectId = null;
        HasUnsavedProjectVisibility = false;
        UpdateProjectSummary();
        syncFleetCarrierCargoCommand.RaiseCanExecuteChanged();
    }

    private void UpdateCommodityPlan()
    {
        CommodityOverlay.Apply(
            ColonizationCommodityPlanner.Create(
                Projects.Select(row => row.Project),
                hiddenProjectIds,
                primaryProjectId,
                CommanderName,
                fleetCarriers,
                shipCargo,
                constructionState.CreateSnapshot(),
                currentMarket),
            latestStatus);
    }

    private bool CanSaveRavenApiKey()
    {
        var normalized = string.IsNullOrWhiteSpace(RavenApiKey)
            ? null
            : RavenApiKey.Trim();
        return commanderProfileStore is not null
            && profileFrontierId is not null
            && !IsFleetCarrierSyncBusy
            && !string.Equals(
                normalized,
                storedRavenApiKey,
                StringComparison.Ordinal);
    }

    private bool CanSyncFleetCarrierCargo()
    {
        if (!IsEnabled
            || !FleetCarrierCargoSyncEnabled
            || !HasStoredRavenApiKey
            || IsFleetCarrierSyncBusy
            || currentMarket is null
            || !string.Equals(
                currentMarket.StationType,
                "FleetCarrier",
                StringComparison.OrdinalIgnoreCase)
            || !fleetCarriers.Any(carrier =>
                carrier.MarketId == currentMarket.MarketId))
        {
            return false;
        }

        var dock = constructionState.CurrentDock;
        return dock?.Timestamp is not null
            && dock.MarketId == currentMarket.MarketId
            && currentMarket.Timestamp > dock.Timestamp;
    }

    private string GetFleetCarrierSyncBlockReason()
    {
        if (!IsEnabled)
        {
            return "Enable Raven Colonial before syncing Fleet Carrier cargo.";
        }

        if (!FleetCarrierCargoSyncEnabled)
        {
            return "Automatic Fleet Carrier cargo sync is off.";
        }

        if (!HasStoredRavenApiKey)
        {
            return "Save a Raven API key before syncing Fleet Carrier cargo.";
        }

        if (currentMarket is null)
        {
            return "Open a Fleet Carrier commodity market in Elite first.";
        }

        if (!string.Equals(
                currentMarket.StationType,
                "FleetCarrier",
                StringComparison.OrdinalIgnoreCase))
        {
            return "The current market is not a Fleet Carrier market.";
        }

        if (!fleetCarriers.Any(carrier =>
                carrier.MarketId == currentMarket.MarketId))
        {
            return "The current Fleet Carrier is not linked to this commander in Raven Colonial.";
        }

        return "Dock at the Fleet Carrier and reopen its commodity market before syncing.";
    }

    private void ReplaceLocalFleetCarrier(
        ColonizationFleetCarrier updatedCarrier)
    {
        fleetCarriers = fleetCarriers
            .Where(carrier => carrier.MarketId != updatedCarrier.MarketId)
            .Append(updatedCarrier)
            .ToArray();
        UpdateCommodityPlan();
    }

    private static string GetCarrierName(ColonizationFleetCarrier carrier)
    {
        return string.IsNullOrWhiteSpace(carrier.DisplayName)
            ? carrier.Name
            : carrier.DisplayName;
    }

    private void SaveOverlayPreferences(
        ColonizationOverlayPreferences updatedPreferences,
        [CallerMemberName] string? propertyName = null)
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
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            StatusMessage =
                "The construction overlay preference could not be saved: "
                + exception.Message;
        }
    }

    private void RaiseCommandStates()
    {
        refreshCommand.RaiseCanExecuteChanged();
        saveProjectsCommand.RaiseCanExecuteChanged();
        saveRavenApiKeyCommand.RaiseCanExecuteChanged();
        syncFleetCarrierCargoCommand.RaiseCanExecuteChanged();
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

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }

    private sealed class AsyncCommand(
        Func<Task> execute,
        Func<bool> canExecute) : ICommand
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

public sealed class ColonizationProjectRowViewModel
    : INotifyPropertyChanged
{
    private readonly Action<ColonizationProjectRowViewModel, bool> changed;
    private bool isShown;

    public ColonizationProjectRowViewModel(
        ColonizationProject project,
        string typeDescription,
        bool isPrimary,
        bool isShown,
        Action<ColonizationProjectRowViewModel, bool> changed)
    {
        Project = project;
        TypeDescription = typeDescription;
        IsPrimary = isPrimary;
        this.isShown = isShown;
        this.changed = changed;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ColonizationProject Project { get; }

    public string BuildName => Project.BuildName;

    public string SystemName => Project.SystemName;

    public string TypeDescription { get; }

    public bool IsPrimary { get; }

    public string PrimaryLabel => IsPrimary ? "PRIMARY" : string.Empty;

    public string ProgressText => Project.IsFleetCarrierLoading
        ? $"? of {Project.MaximumRequired:N0}"
        : Project.Progress is double progress
            ? $"{progress:P0} of {Project.MaximumRequired:N0}"
            : "Progress unavailable";

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
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(IsShown)));
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
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(nameof(IsShown)));
    }
}

public sealed record ColonizationResourceRowViewModel(
    string Name,
    int Remaining,
    int Provided,
    int Required,
    int Payment)
{
    public string RemainingText => $"{Remaining:N0} remaining";

    public string ProgressText => $"{Provided:N0} / {Required:N0}";

    public string PaymentText => $"{Payment:N0} CR/t";
}
