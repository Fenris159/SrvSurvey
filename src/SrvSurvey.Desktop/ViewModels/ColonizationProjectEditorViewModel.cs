using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class ColonizationProjectEditorViewModel
    : INotifyPropertyChanged
{
    private readonly IRavenColonialClient client;
    private readonly ColonizationBuildCatalog buildCatalog;
    private readonly ColonizationProjectFactory projectFactory;
    private readonly ColonizationProjectPublisher projectPublisher;
    private readonly Func<ColonizationProject, Task> onCreated;
    private readonly AsyncCommand prepareCommand;
    private readonly AsyncCommand reviewCommand;
    private readonly AsyncCommand confirmCommand;
    private readonly DelegateCommand cancelReviewCommand;
    private ColonizationProjectEditorContext context =
        ColonizationProjectEditorContext.Unavailable;
    private IReadOnlyList<ColonizationBuildOptionViewModel> buildOptions = [];
    private IReadOnlyList<string> layouts = [];
    private IReadOnlyList<ColonizationSystemSiteOptionViewModel> systemSites = [];
    private ColonizationBuildLocation selectedLocation =
        ColonizationBuildLocation.Orbital;
    private ColonizationBuildOptionViewModel? selectedBuild;
    private string? selectedLayout;
    private ColonizationSystemSiteOptionViewModel? selectedSystemSite;
    private string projectName = string.Empty;
    private string architectName = string.Empty;
    private string notes = string.Empty;
    private string bodyNumberText = "-1";
    private string bodyName = string.Empty;
    private string statusMessage =
        "A live construction depot is required before a project can be created.";
    private bool isPrepared;
    private bool isBusy;
    private ColonizationProjectCreate? pendingProject;
    private string? pendingContextIdentity;
    private ColonizationProject? createdProject;

    public ColonizationProjectEditorViewModel(
        IRavenColonialClient client,
        ColonizationBuildCatalog buildCatalog,
        Func<ColonizationProject, Task> onCreated)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.buildCatalog = buildCatalog
            ?? throw new ArgumentNullException(nameof(buildCatalog));
        this.onCreated = onCreated
            ?? throw new ArgumentNullException(nameof(onCreated));
        projectFactory = new ColonizationProjectFactory(this.buildCatalog);
        projectPublisher = new ColonizationProjectPublisher(this.client);
        Locations = Enum.GetValues<ColonizationBuildLocation>();
        prepareCommand = new AsyncCommand(PrepareAsync, () => CanPrepare);
        reviewCommand = new AsyncCommand(
            ReviewAsync,
            () => IsPrepared && !IsBusy && !IsConfirmationPending);
        confirmCommand = new AsyncCommand(
            ConfirmCreateAsync,
            () => IsConfirmationPending && !IsBusy);
        cancelReviewCommand = new DelegateCommand(
            CancelReview,
            () => IsConfirmationPending && !IsBusy);
        PrepareCommand = prepareCommand;
        ReviewCommand = reviewCommand;
        ConfirmCommand = confirmCommand;
        CancelReviewCommand = cancelReviewCommand;
        UpdateBuildOptions();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand PrepareCommand { get; }

    public ICommand ReviewCommand { get; }

    public ICommand ConfirmCommand { get; }

    public ICommand CancelReviewCommand { get; }

    public IReadOnlyList<ColonizationBuildLocation> Locations { get; }

    public bool CanPrepare => !IsBusy
        && context.IsExternalDataEnabled
        && !string.IsNullOrWhiteSpace(context.CommanderName)
        && !string.IsNullOrWhiteSpace(context.SystemName)
        && context.StarPosition.Count == 3
        && context.Dock is { IsConstructionSite: true }
        && context.Depot is { IsComplete: false, IsFailed: false }
        && context.Dock.MarketId == context.Depot.MarketId
        && string.Equals(
            context.SystemName,
            context.Dock.SystemName,
            StringComparison.OrdinalIgnoreCase)
        && context.Depot.Resources.Count > 0;

    public bool IsPrepared
    {
        get => isPrepared;
        private set
        {
            if (SetField(ref isPrepared, value))
            {
                OnPropertyChanged(nameof(IsEditorVisible));
                RaiseCommandStates();
            }
        }
    }

    public bool IsEditorVisible => IsPrepared;

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetField(ref isBusy, value))
            {
                OnPropertyChanged(nameof(PrepareButtonText));
                OnPropertyChanged(nameof(ReviewButtonText));
                RaiseCommandStates();
            }
        }
    }

    public string PrepareButtonText => IsBusy
        ? "Loading project context..."
        : "Prepare new project";

    public string ReviewButtonText => IsBusy
        ? "Working..."
        : "Review project";

    public IReadOnlyList<ColonizationBuildOptionViewModel> BuildOptions
    {
        get => buildOptions;
        private set => SetField(ref buildOptions, value);
    }

    public IReadOnlyList<string> Layouts
    {
        get => layouts;
        private set => SetField(ref layouts, value);
    }

    public IReadOnlyList<ColonizationSystemSiteOptionViewModel> SystemSites
    {
        get => systemSites;
        private set => SetField(ref systemSites, value);
    }

    public ColonizationBuildLocation SelectedLocation
    {
        get => selectedLocation;
        set
        {
            if (value == selectedLocation || IsPlannedSiteSelected)
            {
                return;
            }

            selectedLocation = value;
            OnPropertyChanged();
            UpdateBuildOptions();
            ClearConfirmation();
        }
    }

    public ColonizationBuildOptionViewModel? SelectedBuild
    {
        get => selectedBuild;
        set
        {
            if (ReferenceEquals(selectedBuild, value)
                || IsPlannedSiteSelected)
            {
                return;
            }

            selectedBuild = value;
            OnPropertyChanged();
            UpdateLayouts();
            ClearConfirmation();
        }
    }

    public string? SelectedLayout
    {
        get => selectedLayout;
        set
        {
            if (string.Equals(selectedLayout, value, StringComparison.Ordinal)
                || IsPlannedSiteSelected)
            {
                return;
            }

            selectedLayout = value;
            OnPropertyChanged();
            ClearConfirmation();
        }
    }

    public ColonizationSystemSiteOptionViewModel? SelectedSystemSite
    {
        get => selectedSystemSite;
        set
        {
            if (ReferenceEquals(selectedSystemSite, value))
            {
                return;
            }

            selectedSystemSite = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsPlannedSiteSelected));
            OnPropertyChanged(nameof(IsBuildSelectionEnabled));
            if (value?.Site is null)
            {
                RestoreManualSelection();
            }
            else if (!ApplyPlannedSite(value.Site))
            {
                selectedSystemSite = SystemSites.FirstOrDefault(option =>
                    option.Site is null);
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsPlannedSiteSelected));
                OnPropertyChanged(nameof(IsBuildSelectionEnabled));
                RestoreManualSelection();
            }

            ClearConfirmation();
        }
    }

    public bool IsPlannedSiteSelected => SelectedSystemSite?.Site is not null;

    public bool IsBuildSelectionEnabled => !IsPlannedSiteSelected;

    public string ProjectName
    {
        get => projectName;
        set
        {
            if (SetField(ref projectName, value ?? string.Empty))
            {
                ClearConfirmation();
            }
        }
    }

    public string ArchitectName
    {
        get => architectName;
        set
        {
            if (SetField(ref architectName, value ?? string.Empty))
            {
                ClearConfirmation();
            }
        }
    }

    public string Notes
    {
        get => notes;
        set
        {
            if (SetField(ref notes, value ?? string.Empty))
            {
                ClearConfirmation();
            }
        }
    }

    public string BodyNumberText
    {
        get => bodyNumberText;
        set
        {
            if (SetField(ref bodyNumberText, value ?? string.Empty))
            {
                ClearConfirmation();
            }
        }
    }

    public string BodyName
    {
        get => bodyName;
        set
        {
            if (SetField(ref bodyName, value ?? string.Empty))
            {
                ClearConfirmation();
            }
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public bool IsConfirmationPending => pendingProject is not null;

    public string ConfirmationSummary => pendingProject is null
        ? string.Empty
        : $"Publish {pendingProject.BuildName} in "
            + $"{pendingProject.SystemName} with "
            + $"{pendingProject.Commodities.Values.Sum():N0} cargo remaining?";

    public bool HasCreatedProject => createdProject is not null;

    public string CreatedProjectSummary => createdProject is null
        ? string.Empty
        : $"Created {createdProject.BuildName} ({createdProject.BuildId}).";

    public string? CreatedProjectId => createdProject?.BuildId;

    public void ReportLinkFailure(string message)
    {
        StatusMessage = "The created Raven project could not be opened: "
            + message;
    }

    public void UpdateContext(ColonizationProjectEditorContext updatedContext)
    {
        ArgumentNullException.ThrowIfNull(updatedContext);
        var oldIdentity = GetContextIdentity(context);
        context = updatedContext;
        if (oldIdentity != GetContextIdentity(updatedContext))
        {
            IsPrepared = false;
            pendingProject = null;
            pendingContextIdentity = null;
            createdProject = null;
            OnPropertyChanged(nameof(IsConfirmationPending));
            OnPropertyChanged(nameof(ConfirmationSummary));
            OnPropertyChanged(nameof(HasCreatedProject));
            OnPropertyChanged(nameof(CreatedProjectSummary));
            OnPropertyChanged(nameof(CreatedProjectId));
        }

        StatusMessage = CanPrepare
            ? "A compatible live construction depot is ready."
            : GetUnavailableReason(updatedContext);
        OnPropertyChanged(nameof(CanPrepare));
        RaiseCommandStates();
    }

    public async Task PrepareAsync()
    {
        if (!CanPrepare)
        {
            StatusMessage = GetUnavailableReason(context);
            return;
        }

        IsBusy = true;
        StatusMessage = "Loading planned sites and architect from Raven Colonial...";
        try
        {
            var sitesTask = client.GetSystemSitesAsync(context.SystemName!);
            var architectTask = client.GetSystemArchitectAsync(
                context.SystemName!);
            await Task.WhenAll(sitesTask, architectTask);
            var planned = (await sitesTask)
                .Where(site => site.Status == ColonizationSystemSiteStatus.Plan)
                .OrderBy(site => site.Name)
                .Select(site => new ColonizationSystemSiteOptionViewModel(site))
                .ToArray();
            SystemSites =
            [
                ColonizationSystemSiteOptionViewModel.None,
                .. planned,
            ];
            selectedSystemSite = SystemSites[0];
            OnPropertyChanged(nameof(SelectedSystemSite));
            OnPropertyChanged(nameof(IsPlannedSiteSelected));
            OnPropertyChanged(nameof(IsBuildSelectionEnabled));
            ProjectName = context.Dock!.DefaultProjectName;
            var architect = await architectTask;
            ArchitectName = string.IsNullOrWhiteSpace(architect)
                ? context.CommanderName!
                : architect;
            Notes = string.Empty;
            BodyNumberText = "-1";
            BodyName = string.Empty;
            selectedLocation = context.Dock.StationName.StartsWith(
                ColonizationDockingSnapshot.PlanetaryConstructionSite,
                StringComparison.OrdinalIgnoreCase)
                    ? ColonizationBuildLocation.Surface
                    : ColonizationBuildLocation.Orbital;
            OnPropertyChanged(nameof(SelectedLocation));
            UpdateBuildOptions();
            IsPrepared = true;
            createdProject = null;
            OnPropertyChanged(nameof(HasCreatedProject));
            OnPropertyChanged(nameof(CreatedProjectSummary));
            OnPropertyChanged(nameof(CreatedProjectId));
            var autoSelectedPlannedSite = false;
            if (planned.Length == 1)
            {
                SelectedSystemSite = SystemSites[1];
                autoSelectedPlannedSite = IsPlannedSiteSelected;
            }

            StatusMessage = planned.Length switch
            {
                0 => "No planned Raven site is available; choose a build layout manually.",
                1 when autoSelectedPlannedSite =>
                    "Loaded and selected the one planned Raven site.",
                1 => "The planned Raven site could not be matched to the local build catalog; configure the build manually.",
                _ => $"Loaded {planned.Length:N0} planned sites. Choose one or configure the build manually.",
            };
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or InvalidDataException
                or TaskCanceledException)
        {
            StatusMessage = "The new-project context could not be loaded: "
                + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task ReviewAsync()
    {
        if (!IsPrepared || IsBusy)
        {
            return Task.CompletedTask;
        }

        if (!int.TryParse(BodyNumberText, out var bodyNumber))
        {
            StatusMessage = "Body number must be -1 for unknown or a non-negative integer.";
            return Task.CompletedTask;
        }

        var result = projectFactory.Create(
            new ColonizationProjectDraft(
                context.CommanderName ?? string.Empty,
                context.SystemName ?? string.Empty,
                context.StarPosition,
                SelectedLayout ?? string.Empty,
                ProjectName,
                ArchitectName,
                Notes,
                bodyNumber,
                BodyName,
                SelectedSystemSite?.Site?.Id),
            context.Dock,
            context.Depot);
        if (!result.IsValid)
        {
            StatusMessage = string.Join(" ", result.Errors);
            return Task.CompletedTask;
        }

        pendingProject = result.Project;
        pendingContextIdentity = GetContextIdentity(context);
        StatusMessage =
            "Review the summary, then confirm to publish this project.";
        OnPropertyChanged(nameof(IsConfirmationPending));
        OnPropertyChanged(nameof(ConfirmationSummary));
        RaiseCommandStates();
        return Task.CompletedTask;
    }

    public async Task ConfirmCreateAsync()
    {
        if (pendingProject is null || IsBusy)
        {
            return;
        }

        if (!CanPrepare
            || !string.Equals(
                pendingContextIdentity,
                GetContextIdentity(context),
                StringComparison.Ordinal))
        {
            ClearConfirmation();
            StatusMessage = "The live construction context changed. Review the project again before publishing.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Publishing the project to Raven Colonial...";
        try
        {
            var result = await projectPublisher.CreateAsync(
                pendingProject,
                context.RavenApiKey);
            var created = result.Project;
            if (created is null)
            {
                StatusMessage = "Raven Colonial did not create the project. It may already exist.";
                return;
            }

            createdProject = created;
            pendingProject = null;
            pendingContextIdentity = null;
            IsPrepared = false;
            OnPropertyChanged(nameof(IsConfirmationPending));
            OnPropertyChanged(nameof(ConfirmationSummary));
            OnPropertyChanged(nameof(HasCreatedProject));
            OnPropertyChanged(nameof(CreatedProjectSummary));
            OnPropertyChanged(nameof(CreatedProjectId));
            await onCreated(created);
            StatusMessage = result.Warning
                ?? (result.PrimarySiteOrderStatus
                    == ColonizationPrimarySiteOrderStatus.Restored
                    ? $"Created {created.BuildName} and restored the existing primary port to the first position."
                    : $"Created {created.BuildName}. It was added to the active project list.");
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or InvalidDataException
                or InvalidOperationException
                or TaskCanceledException)
        {
            StatusMessage = "The project was not created: " + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void CancelReview()
    {
        pendingProject = null;
        pendingContextIdentity = null;
        StatusMessage = "Project review cancelled; nothing was published.";
        OnPropertyChanged(nameof(IsConfirmationPending));
        OnPropertyChanged(nameof(ConfirmationSummary));
        RaiseCommandStates();
    }

    private bool ApplyPlannedSite(ColonizationSystemSite? site)
    {
        if (site is null)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(site.BuildType))
        {
            StatusMessage = "The planned site does not identify a build layout.";
            return false;
        }

        var build = buildCatalog.FindByLayout(site.BuildType).FirstOrDefault();
        if (build is null)
        {
            StatusMessage = $"The planned site layout '{site.BuildType}' is not in the local build catalog.";
            return false;
        }

        selectedLocation = build.Location;
        OnPropertyChanged(nameof(SelectedLocation));
        UpdateBuildOptions();
        selectedBuild = BuildOptions.First(option =>
            string.Equals(
                option.Build.BuildType,
                build.BuildType,
                StringComparison.OrdinalIgnoreCase));
        OnPropertyChanged(nameof(SelectedBuild));
        UpdateLayouts();
        selectedLayout = Layouts.FirstOrDefault(layout => string.Equals(
            layout,
            site.BuildType,
            StringComparison.OrdinalIgnoreCase));
        OnPropertyChanged(nameof(SelectedLayout));
        BodyNumberText = site.BodyNumber.ToString();
        if (context.Dock?.IsPrimaryPortShip == true)
        {
            ProjectName = site.Name;
        }

        return true;
    }

    private void RestoreManualSelection()
    {
        ProjectName = context.Dock?.DefaultProjectName ?? string.Empty;
        BodyNumberText = "-1";
        BodyName = string.Empty;
        selectedLocation = context.Dock?.StationName.StartsWith(
            ColonizationDockingSnapshot.PlanetaryConstructionSite,
            StringComparison.OrdinalIgnoreCase) == true
                ? ColonizationBuildLocation.Surface
                : ColonizationBuildLocation.Orbital;
        OnPropertyChanged(nameof(SelectedLocation));
        UpdateBuildOptions();
    }

    private void UpdateBuildOptions()
    {
        BuildOptions = buildCatalog.ForLocation(SelectedLocation)
            .Select(build => new ColonizationBuildOptionViewModel(build))
            .ToArray();
        selectedBuild = BuildOptions.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedBuild));
        UpdateLayouts();
    }

    private void UpdateLayouts()
    {
        Layouts = SelectedBuild?.Build.Layouts ?? [];
        selectedLayout = Layouts.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedLayout));
    }

    private void ClearConfirmation()
    {
        if (pendingProject is null)
        {
            return;
        }

        pendingProject = null;
        pendingContextIdentity = null;
        OnPropertyChanged(nameof(IsConfirmationPending));
        OnPropertyChanged(nameof(ConfirmationSummary));
        RaiseCommandStates();
    }

    private static string GetContextIdentity(
        ColonizationProjectEditorContext value)
    {
        return string.Join(
            "|",
            value.IsExternalDataEnabled,
            value.CommanderName,
            value.SystemName,
            value.StarPosition.Count == 3 ? value.StarPosition[0] : null,
            value.StarPosition.Count == 3 ? value.StarPosition[1] : null,
            value.StarPosition.Count == 3 ? value.StarPosition[2] : null,
            value.Dock?.MarketId,
            value.Dock?.SystemAddress,
            value.Dock?.SystemName,
            value.Dock?.StationName,
            value.Dock?.FactionName,
            value.Depot?.MarketId,
            value.Depot?.Timestamp,
            value.Depot?.ReportedProgress,
            value.Depot?.IsComplete,
            value.Depot?.IsFailed,
            value.Depot?.TotalProvided,
            value.Depot?.TotalRequired,
            value.Depot is null
                ? null
                : string.Join(
                    ",",
                    value.Depot.Resources
                        .OrderBy(resource => resource.Name)
                        .Select(resource => string.Join(
                            ":",
                            resource.Name,
                            resource.RequiredAmount,
                            resource.ProvidedAmount,
                            resource.Payment))));
    }

    private static string GetUnavailableReason(
        ColonizationProjectEditorContext value)
    {
        if (!value.IsExternalDataEnabled)
        {
            return "Enable Raven Colonial before preparing a project.";
        }

        if (string.IsNullOrWhiteSpace(value.CommanderName))
        {
            return "An active commander profile is required.";
        }

        if (value.Dock is not { IsConstructionSite: true })
        {
            return "Dock at a colonisation construction site first.";
        }

        if (value.Depot is null)
        {
            return "Open Construction Services to load required commodities.";
        }

        if (value.Depot.MarketId != value.Dock.MarketId)
        {
            return "The loaded construction requirements are stale.";
        }

        if (value.Depot.IsComplete || value.Depot.IsFailed)
        {
            return "The current construction depot is no longer active.";
        }

        if (!string.Equals(
                value.SystemName,
                value.Dock.SystemName,
                StringComparison.OrdinalIgnoreCase))
        {
            return "The current system does not match the construction site.";
        }

        if (value.StarPosition.Count != 3)
        {
            return "The current system position is not available.";
        }

        return "The live construction context is incomplete.";
    }

    private void RaiseCommandStates()
    {
        prepareCommand.RaiseCanExecuteChanged();
        reviewCommand.RaiseCanExecuteChanged();
        confirmCommand.RaiseCanExecuteChanged();
        cancelReviewCommand.RaiseCanExecuteChanged();
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

    private sealed class DelegateCommand(
        Action execute,
        Func<bool> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => canExecute();

        public void Execute(object? parameter)
        {
            if (CanExecute(parameter))
            {
                execute();
            }
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
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

public sealed record ColonizationProjectEditorContext(
    bool IsExternalDataEnabled,
    string? CommanderName,
    string? SystemName,
    IReadOnlyList<double> StarPosition,
    ColonizationDockingSnapshot? Dock,
    ColonizationConstructionDepotSnapshot? Depot,
    string? RavenApiKey = null)
{
    public static ColonizationProjectEditorContext Unavailable { get; } = new(
        false,
        null,
        null,
        [],
        null,
        null,
        null);
}

public sealed record ColonizationBuildOptionViewModel(
    ColonizationBuildCost Build)
{
    public string DisplayName =>
        $"Tier {Build.Tier}: {Build.DisplayName}";
}

public sealed record ColonizationSystemSiteOptionViewModel(
    ColonizationSystemSite? Site)
{
    public static ColonizationSystemSiteOptionViewModel None { get; } = new(
        (ColonizationSystemSite?)null);

    public string DisplayName => Site is null
        ? "None - configure manually"
        : $"{Site.Name} ({Site.BuildType})";
}
