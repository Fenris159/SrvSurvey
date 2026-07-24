using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class ColonizationViewModel : INotifyPropertyChanged
{
    private readonly IRavenColonialClient client;
    private readonly ColonizationBuildCatalog buildCatalog;
    private readonly ColonizationSettingsStore settingsStore;
    private readonly ColonizationConstructionState constructionState = new();
    private readonly AsyncCommand refreshCommand;
    private readonly AsyncCommand saveProjectsCommand;
    private IReadOnlyList<ColonizationProjectRowViewModel> projects = [];
    private IReadOnlyList<ColonizationResourceRowViewModel> constructionResources =
        [];
    private HashSet<string> hiddenProjectIds = new(
        StringComparer.OrdinalIgnoreCase);
    private string? commanderName;
    private string? primaryProjectId;
    private bool isEnabled;
    private bool isBusy;
    private bool hasUnsavedProjectVisibility;
    private string statusMessage;
    private string projectSummary = "No projects loaded.";
    private string constructionTitle = "No construction depot active";
    private string constructionStatus =
        "Dock at a construction site and open Construction Services.";

    public ColonizationViewModel(
        ColonizationSettingsStore settingsStore,
        IRavenColonialClient? client = null,
        ColonizationBuildCatalog? buildCatalog = null)
    {
        this.settingsStore = settingsStore
            ?? throw new ArgumentNullException(nameof(settingsStore));
        this.client = client ?? new RavenColonialClient();
        this.buildCatalog = buildCatalog
            ?? ColonizationBuildCatalog.LoadEmbedded();
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
        RefreshCommand = refreshCommand;
        SaveProjectsCommand = saveProjectsCommand;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand RefreshCommand { get; }

    public ICommand SaveProjectsCommand { get; }

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
        }
    }

    public void ReportLinkFailure(string message)
    {
        StatusMessage = "Raven Colonial could not be opened: " + message;
    }

    public async Task RefreshAsync()
    {
        if (!IsEnabled || IsBusy || CommanderName is null)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Fetching active projects from Raven Colonial...";
        try
        {
            var result = await client.GetCommanderProjectsAsync(CommanderName);
            hiddenProjectIds = result.HiddenProjectIds.ToHashSet(
                StringComparer.OrdinalIgnoreCase);
            primaryProjectId = result.PrimaryProjectId;
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
        hiddenProjectIds.Clear();
        primaryProjectId = null;
        HasUnsavedProjectVisibility = false;
        UpdateProjectSummary();
    }

    private void RaiseCommandStates()
    {
        refreshCommand.RaiseCanExecuteChanged();
        saveProjectsCommand.RaiseCanExecuteChanged();
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
