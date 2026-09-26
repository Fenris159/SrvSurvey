using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class ColonizationProjectEditorViewModel : INotifyPropertyChanged
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
    private ColonizationProjectEditorContext context = ColonizationProjectEditorContext.Unavailable;
    private IReadOnlyList<ColonizationBuildOptionViewModel> buildOptions = [];
    private IReadOnlyList<string> layouts = [];
    private IReadOnlyList<ColonizationSystemSiteOptionViewModel> systemSites = [];
    private ColonizationBuildLocation selectedLocation = ColonizationBuildLocation.Orbital;
    private ColonizationBuildOptionViewModel? selectedBuild;
    private string? selectedLayout;
    private ColonizationSystemSiteOptionViewModel? selectedSystemSite;
    private string projectName = string.Empty;
    private string architectName = string.Empty;
    private string notes = string.Empty;
    private const string InvalidBodyNumberMessage = "Body ID must be -1 for unknown or a non-negative integer.";

    private string bodyNumberText = "-1";
    private bool bodyNumberFollowsContext = true;
    private bool assigningBodyNumber;
    private bool bodyNameFollowsContext = true;
    private bool assigningBodyName;
    private bool projectNameMissing;
    private bool architectMissing;
    private bool architectFromRaven;
    private bool bodyIdMissing;
    private bool bodyIdFormatInvalid;
    private bool bodyNameMissing;
    private string bodyName = string.Empty;
    private string statusMessage = "A live construction depot is required before a project can be created.";
    private bool isPrepared;
    private bool isBusy;
    private bool interactionInFlight;
    private ColonizationProjectCreate? pendingProject;
    private string? pendingContextIdentity;
    private ColonizationProject? createdProject;
    private bool isSystemArchitect;

    public ColonizationProjectEditorViewModel(
        IRavenColonialClient client,
        ColonizationBuildCatalog buildCatalog,
        Func<ColonizationProject, Task> onCreated
    )
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.buildCatalog = buildCatalog ?? throw new ArgumentNullException(nameof(buildCatalog));
        this.onCreated = onCreated ?? throw new ArgumentNullException(nameof(onCreated));
        projectFactory = new ColonizationProjectFactory(this.buildCatalog);
        projectPublisher = new ColonizationProjectPublisher(this.client);
        Locations = Enum.GetValues<ColonizationBuildLocation>();
        prepareCommand = new AsyncCommand(PrepareAsync, () => CanPrepare);
        reviewCommand = new AsyncCommand(ReviewAsync, () => CanReview);
        confirmCommand = new AsyncCommand(ConfirmCreateAsync, () => IsConfirmationPending && !IsBusy);
        cancelReviewCommand = new DelegateCommand(CancelReview, () => IsConfirmationPending && !IsBusy);
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

    public bool CanPrepare =>
        !IsBusy
        && context.IsExternalDataEnabled
        && !string.IsNullOrWhiteSpace(context.CommanderName)
        && !string.IsNullOrWhiteSpace(context.SystemName)
        && context.StarPosition.Count == 3
        && context.Dock is { IsConstructionSite: true }
        && context.Depot is { IsComplete: false, IsFailed: false }
        && context.Dock.MarketId == context.Depot.MarketId
        && string.Equals(context.SystemName, context.Dock.SystemName, StringComparison.OrdinalIgnoreCase)
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

    public string PrepareButtonText => IsBusy ? "Loading project context..." : "Prepare new project";

    public string ReviewButtonText => IsBusy ? "Working..." : "Review project";

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
            if (ReferenceEquals(selectedBuild, value) || IsPlannedSiteSelected)
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
            if (string.Equals(selectedLayout, value, StringComparison.Ordinal) || IsPlannedSiteSelected)
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

            if (!isSystemArchitect && value?.Site is null)
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
                selectedSystemSite = isSystemArchitect
                    ? SystemSites.FirstOrDefault(option => option.Site is null)
                    : null;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsPlannedSiteSelected));
                OnPropertyChanged(nameof(IsBuildSelectionEnabled));
                RestoreManualSelection();
            }

            ClearConfirmation();
            RaiseCommandStates();
        }
    }

    public bool IsPlannedSiteSelected => SelectedSystemSite?.Site is not null;

    public bool IsBuildSelectionEnabled => isSystemArchitect && !IsPlannedSiteSelected;

    private bool CanReview =>
        IsPrepared && !IsBusy && !IsConfirmationPending && (isSystemArchitect || IsPlannedSiteSelected);

    public string ProjectName
    {
        get => projectName;
        set
        {
            if (SetField(ref projectName, value ?? string.Empty))
            {
                if (!string.IsNullOrWhiteSpace(projectName))
                {
                    SetRequiredFlag(ref projectNameMissing, false, nameof(IsProjectNameMissing));
                }

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
                if (!string.IsNullOrWhiteSpace(architectName))
                {
                    SetRequiredFlag(ref architectMissing, false, nameof(IsArchitectMissing));
                }

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
                if (!assigningBodyNumber)
                {
                    bodyNumberFollowsContext = false;
                }

                if (!string.IsNullOrWhiteSpace(bodyNumberText))
                {
                    SetRequiredFlag(ref bodyIdMissing, false, nameof(IsBodyIdMissing));
                }

                SetRequiredFlag(ref bodyIdFormatInvalid, false, nameof(IsBodyIdFormatInvalid));
                ClearConfirmation();
            }
        }
    }

    public bool IsProjectNameMissing => projectNameMissing;

    public bool IsArchitectMissing => architectMissing;

    public bool IsArchitectReadOnly => architectFromRaven;

    public bool ShowArchitectWarning => !architectFromRaven;

    public bool IsBodyIdMissing => bodyIdMissing;

    public bool IsBodyIdFormatInvalid => bodyIdFormatInvalid;

    public bool IsBodyIdInvalid => bodyIdMissing || bodyIdFormatInvalid;

    public bool IsBodyNameMissing => bodyNameMissing;

    public string BodyName
    {
        get => bodyName;
        set
        {
            if (SetField(ref bodyName, value ?? string.Empty))
            {
                if (!assigningBodyName)
                {
                    bodyNameFollowsContext = false;
                }

                if (!string.IsNullOrWhiteSpace(bodyName))
                {
                    SetRequiredFlag(ref bodyNameMissing, false, nameof(IsBodyNameMissing));
                }

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

    public string ConfirmationSummary =>
        pendingProject is null
            ? string.Empty
            : $"Publish {pendingProject.BuildName} in "
                + $"{pendingProject.SystemName} with "
                + $"{pendingProject.Commodities.Values.Sum():N0} cargo remaining?";

    public bool HasCreatedProject => createdProject is not null;

    public string CreatedProjectSummary =>
        createdProject is null ? string.Empty : $"Created {createdProject.BuildName} ({createdProject.BuildId}).";

    public string? CreatedProjectId => createdProject?.BuildId;

    public void ReportLinkFailure(string message)
    {
        StatusMessage = "The created Raven project could not be opened: " + message;
    }

    public void UpdateContext(ColonizationProjectEditorContext updatedContext)
    {
        ArgumentNullException.ThrowIfNull(updatedContext);
        string oldIdentity = GetContextIdentity(context);
        int? previousBodyId = context.CurrentBodyId;
        string? previousBodyName = context.CurrentBodyName;
        bool identityChanged = oldIdentity != GetContextIdentity(updatedContext);
        context = updatedContext;
        if (identityChanged)
        {
            IsPrepared = false;
            isSystemArchitect = false;
            bodyNumberFollowsContext = true;
            bodyNameFollowsContext = true;
            SetArchitectFromRaven(false);
            OnPropertyChanged(nameof(IsBuildSelectionEnabled));
            pendingProject = null;
            pendingContextIdentity = null;
            createdProject = null;
            OnPropertyChanged(nameof(IsConfirmationPending));
            OnPropertyChanged(nameof(ConfirmationSummary));
            OnPropertyChanged(nameof(HasCreatedProject));
            OnPropertyChanged(nameof(CreatedProjectSummary));
            OnPropertyChanged(nameof(CreatedProjectId));
        }
        else if (
            IsPrepared
            && bodyNumberFollowsContext
            && (
                previousBodyId != updatedContext.CurrentBodyId
                || !string.Equals(previousBodyName, updatedContext.CurrentBodyName, StringComparison.Ordinal)
            )
        )
        {
            ApplyAutomaticBody(SelectedSystemSite?.Site);
        }

        StatusMessage = CanPrepare
            ? "A compatible live construction depot is ready."
            : GetUnavailableReason(updatedContext);
        OnPropertyChanged(nameof(CanPrepare));
        RaiseCommandStates();
    }

    public async Task PrepareAsync()
    {
        if (!CanPrepare || interactionInFlight)
        {
            StatusMessage = GetUnavailableReason(context);
            return;
        }

        // Let the Prepare click finish before IsBusy disables that button.
        // Disabling the clicked button in the same turn can strand pointer capture,
        // so the window ignores later input until SrvSurvey is restarted.
        interactionInFlight = true;
        try
        {
            await Task.Yield();
            if (!CanPrepare)
            {
                StatusMessage = GetUnavailableReason(context);
                return;
            }

            await LoadPreparedContextAsync();
        }
        finally
        {
            interactionInFlight = false;
        }
    }

    private async Task LoadPreparedContextAsync()
    {
        IsBusy = true;
        StatusMessage = "Loading planned sites and architect from Raven Colonial...";
        try
        {
            Task<IReadOnlyList<ColonizationSystemSite>> sitesTask = client.GetSystemSitesAsync(context.SystemName!);
            Task<string?> architectTask = client.GetSystemArchitectAsync(context.SystemName!);
            await Task.WhenAll(sitesTask, architectTask);
            string? architect = await architectTask;
            isSystemArchitect = ColonizationSiteVisibility.CommanderIsArchitect(architect, context.CommanderName);
            ColonizationSystemSiteOptionViewModel[] planned = (await sitesTask)
                .Where(site => site.Status == ColonizationSystemSiteStatus.Plan)
                .Where(site => ColonizationSiteVisibility.CanViewerSeeSite(site, isSystemArchitect, buildCatalog))
                .OrderBy(site => site.Name)
                .Select(site => new ColonizationSystemSiteOptionViewModel(site))
                .ToArray();
            SystemSites = isSystemArchitect ? [ColonizationSystemSiteOptionViewModel.None, .. planned] : planned;
            selectedSystemSite = isSystemArchitect ? SystemSites[0] : null;
            OnPropertyChanged(nameof(SelectedSystemSite));
            OnPropertyChanged(nameof(IsPlannedSiteSelected));
            OnPropertyChanged(nameof(IsBuildSelectionEnabled));
            ProjectName = context.Dock!.DefaultProjectName;
            bool fromRaven = !string.IsNullOrWhiteSpace(architect);
            ArchitectName = fromRaven ? architect!.Trim() : context.CommanderName ?? string.Empty;
            SetArchitectFromRaven(fromRaven);
            Notes = string.Empty;
            bodyNumberFollowsContext = true;
            bodyNameFollowsContext = true;
            ApplyAutomaticBody(null);
            selectedLocation = context.Dock.StationName.StartsWith(
                ColonizationDockingSnapshot.PlanetaryConstructionSite,
                StringComparison.OrdinalIgnoreCase
            )
                ? ColonizationBuildLocation.Surface
                : ColonizationBuildLocation.Orbital;
            OnPropertyChanged(nameof(SelectedLocation));
            UpdateBuildOptions();
            IsPrepared = true;
            createdProject = null;
            OnPropertyChanged(nameof(HasCreatedProject));
            OnPropertyChanged(nameof(CreatedProjectSummary));
            OnPropertyChanged(nameof(CreatedProjectId));
            bool autoSelectedPlannedSite = false;
            if (planned.Length == 1)
            {
                SelectedSystemSite = SystemSites[isSystemArchitect ? 1 : 0];
                autoSelectedPlannedSite = IsPlannedSiteSelected;
            }

            StatusMessage = PlannedSitesStatus(planned.Length, autoSelectedPlannedSite, isSystemArchitect);
        }
        catch (Exception exception)
            when (exception is HttpRequestException or InvalidDataException or TaskCanceledException)
        {
            StatusMessage = "The new-project context could not be loaded: " + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string PlannedSitesStatus(int count, bool autoSelectedPlannedSite, bool isArchitect)
    {
        string kind = isArchitect ? "planned" : "orbital planned";
        return count switch
        {
            0 => isArchitect
                ? "No planned Raven site is available; choose a build layout manually."
                : "No orbital planned Raven sites are available to link. Only the system architect can start a build without a plan.",
            1 when autoSelectedPlannedSite => $"Loaded and selected the one {kind} Raven site.",
            1 => isArchitect
                ? "The planned Raven site could not be matched to the local build catalog; configure the build manually."
                : "The orbital planned Raven site could not be matched to the local build catalog. Only the system architect can start a build without a plan.",
            _ => isArchitect
                ? $"Loaded {count:N0} planned sites. Choose one or configure the build manually."
                : $"Loaded {count:N0} orbital planned sites. Choose one to link.",
        };
    }

    private string? GetPlannedSiteRequirementError()
    {
        if (isSystemArchitect || IsPlannedSiteSelected)
        {
            return null;
        }

        return SystemSites.Any(option => option.Site is not null)
            ? "Choose a planned Raven site to link. Only the system architect can start a build without a plan."
            : "Only the system architect can start a build project without a planned Raven site.";
    }

    public Task ReviewAsync()
    {
        if (!IsPrepared || IsBusy)
        {
            return Task.CompletedTask;
        }

        if (GetPlannedSiteRequirementError() is { } plannedSiteError)
        {
            StatusMessage = plannedSiteError;
            return Task.CompletedTask;
        }

        if (RejectIncompleteFields() is { } requiredMessage)
        {
            StatusMessage = requiredMessage;
            return Task.CompletedTask;
        }

        if (TryCreateProjectDraft() is not { } draft)
        {
            return Task.CompletedTask;
        }

        ColonizationProjectCreateResult result = projectFactory.Create(draft, context.Dock, context.Depot);
        if (!result.IsValid)
        {
            StatusMessage = string.Join(" ", result.Errors);
            return Task.CompletedTask;
        }

        ClearRequiredFieldFlags();
        pendingProject = result.Project;
        pendingContextIdentity = GetContextIdentity(context);
        StatusMessage = "Review the summary, then confirm to publish this project.";
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

        if (GetPlannedSiteRequirementError() is { } plannedSiteError)
        {
            ClearConfirmation();
            StatusMessage = plannedSiteError;
            return;
        }

        if (
            !CanPrepare || !string.Equals(pendingContextIdentity, GetContextIdentity(context), StringComparison.Ordinal)
        )
        {
            ClearConfirmation();
            StatusMessage = "The live construction context changed. Review the project again before publishing.";
            return;
        }

        if (RejectIncompleteFields() is { } requiredMessage)
        {
            ClearConfirmation();
            StatusMessage = requiredMessage;
            return;
        }

        if (TryCreateProjectDraft() is not { } draft)
        {
            ClearConfirmation();
            return;
        }

        ColonizationProjectCreateResult refreshed = projectFactory.Create(draft, context.Dock, context.Depot);
        if (!refreshed.IsValid || refreshed.Project is null)
        {
            ClearConfirmation();
            StatusMessage = string.Join(
                " ",
                refreshed.Errors.DefaultIfEmpty("The live construction requirements could not be refreshed.")
            );
            return;
        }

        pendingProject = refreshed.Project;
        OnPropertyChanged(nameof(ConfirmationSummary));

        if (interactionInFlight)
        {
            return;
        }

        // Same as Prepare: do not disable the Confirm button until its click has finished.
        interactionInFlight = true;
        try
        {
            await Task.Yield();
            if (pendingProject is null || IsBusy)
            {
                return;
            }

            await PublishPreparedProjectAsync();
        }
        finally
        {
            interactionInFlight = false;
        }
    }

    private async Task PublishPreparedProjectAsync()
    {
        if (pendingProject is not { } project)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Publishing the project to Raven Colonial...";
        try
        {
            ColonizationProjectPublishResult result = await projectPublisher.CreateAsync(project, context.RavenApiKey);
            ColonizationProject? created = result.Project;
            if (created is null)
            {
                StatusMessage = "Raven Colonial did not create the project. It may already exist.";
                return;
            }

            createdProject = created;
            pendingProject = null;
            pendingContextIdentity = null;
            IsPrepared = false;
            isSystemArchitect = false;
            SetArchitectFromRaven(false);
            OnPropertyChanged(nameof(IsBuildSelectionEnabled));
            OnPropertyChanged(nameof(IsConfirmationPending));
            OnPropertyChanged(nameof(ConfirmationSummary));
            OnPropertyChanged(nameof(HasCreatedProject));
            OnPropertyChanged(nameof(CreatedProjectSummary));
            OnPropertyChanged(nameof(CreatedProjectId));
            await onCreated(created);
            StatusMessage =
                result.Warning
                ?? (
                    result.PrimarySiteOrderStatus == ColonizationPrimarySiteOrderStatus.Restored
                        ? $"Created {created.BuildName} and restored the existing primary port to the first position."
                        : $"Created {created.BuildName}. It was added to the active project list."
                );
        }
        catch (Exception exception)
            when (exception
                    is HttpRequestException
                        or InvalidDataException
                        or InvalidOperationException
                        or TaskCanceledException
            )
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

        if (!buildCatalog.TryResolveSiteBuildType(site.BuildType, out ColonizationBuildCost? build) || build is null)
        {
            StatusMessage = $"The planned site layout '{site.BuildType}' is not in the local build catalog.";
            return false;
        }

        string resolvedLayout = ColonizationBuildCatalog.NormalizeSiteBuildTypeKey(site.BuildType);
        selectedLocation = build.Location;
        OnPropertyChanged(nameof(SelectedLocation));
        UpdateBuildOptions();
        selectedBuild = BuildOptions.First(option =>
            string.Equals(option.Build.BuildType, build.BuildType, StringComparison.OrdinalIgnoreCase)
        );
        OnPropertyChanged(nameof(SelectedBuild));
        UpdateLayouts();
        selectedLayout = Layouts.FirstOrDefault(layout =>
            string.Equals(layout, site.BuildType, StringComparison.OrdinalIgnoreCase)
            || string.Equals(layout, resolvedLayout, StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                ColonizationBuildCatalog.NormalizeSiteBuildTypeKey(layout),
                resolvedLayout,
                StringComparison.OrdinalIgnoreCase
            )
        );
        if (selectedLayout is null && Layouts.Count > 0)
        {
            selectedLayout = Layouts[0];
        }

        OnPropertyChanged(nameof(SelectedLayout));
        ApplyAutomaticBody(site);
        if (context.Dock?.IsPrimaryPortShip == true)
        {
            ProjectName = site.Name;
        }

        return true;
    }

    private void RestoreManualSelection()
    {
        ProjectName = context.Dock?.DefaultProjectName ?? string.Empty;
        ApplyAutomaticBody(null);
        selectedLocation =
            context.Dock?.StationName.StartsWith(
                ColonizationDockingSnapshot.PlanetaryConstructionSite,
                StringComparison.OrdinalIgnoreCase
            ) == true
                ? ColonizationBuildLocation.Surface
                : ColonizationBuildLocation.Orbital;
        OnPropertyChanged(nameof(SelectedLocation));
        UpdateBuildOptions();
    }

    private void UpdateBuildOptions()
    {
        BuildOptions = buildCatalog
            .ForLocation(SelectedLocation)
            .Select(build => new ColonizationBuildOptionViewModel(build))
            .ToArray();
        selectedBuild = BuildOptions.Count > 0 ? BuildOptions[0] : null;
        OnPropertyChanged(nameof(SelectedBuild));
        UpdateLayouts();
    }

    private void UpdateLayouts()
    {
        Layouts = SelectedBuild?.Build.Layouts ?? [];
        selectedLayout = Layouts.Count > 0 ? Layouts[0] : null;
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

    private string? RejectIncompleteFields()
    {
        bool projectMissing = string.IsNullOrWhiteSpace(ProjectName);
        bool architectNameMissing = string.IsNullOrWhiteSpace(ArchitectName);
        bool idMissing = string.IsNullOrWhiteSpace(BodyNumberText);
        bool idFormatInvalid =
            !idMissing
            && (
                !int.TryParse(
                    BodyNumberText.Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int bodyNumber
                )
                || bodyNumber < -1
            );
        bool nameMissing = string.IsNullOrWhiteSpace(BodyName);
        SetRequiredFlag(ref projectNameMissing, projectMissing, nameof(IsProjectNameMissing));
        SetRequiredFlag(ref architectMissing, architectNameMissing, nameof(IsArchitectMissing));
        SetRequiredFlag(ref bodyIdMissing, idMissing, nameof(IsBodyIdMissing));
        SetRequiredFlag(ref bodyIdFormatInvalid, idFormatInvalid, nameof(IsBodyIdFormatInvalid));
        SetRequiredFlag(ref bodyNameMissing, nameMissing, nameof(IsBodyNameMissing));
        if (projectMissing || architectNameMissing || idMissing || nameMissing)
        {
            return idFormatInvalid ? "This field is required. " + InvalidBodyNumberMessage : "This field is required.";
        }

        return idFormatInvalid ? InvalidBodyNumberMessage : null;
    }

    private void ClearRequiredFieldFlags()
    {
        SetRequiredFlag(ref projectNameMissing, false, nameof(IsProjectNameMissing));
        SetRequiredFlag(ref architectMissing, false, nameof(IsArchitectMissing));
        SetRequiredFlag(ref bodyIdMissing, false, nameof(IsBodyIdMissing));
        SetRequiredFlag(ref bodyIdFormatInvalid, false, nameof(IsBodyIdFormatInvalid));
        SetRequiredFlag(ref bodyNameMissing, false, nameof(IsBodyNameMissing));
    }

    private void SetArchitectFromRaven(bool fromRaven)
    {
        if (architectFromRaven == fromRaven)
        {
            return;
        }

        architectFromRaven = fromRaven;
        OnPropertyChanged(nameof(IsArchitectReadOnly));
        OnPropertyChanged(nameof(ShowArchitectWarning));
    }

    private void SetRequiredFlag(ref bool field, bool missing, string propertyName)
    {
        if (field != missing)
        {
            field = missing;
            OnPropertyChanged(propertyName);
        }

        if (propertyName is nameof(IsBodyIdMissing) or nameof(IsBodyIdFormatInvalid))
        {
            OnPropertyChanged(nameof(IsBodyIdInvalid));
        }
    }

    private ColonizationProjectDraft? TryCreateProjectDraft()
    {
        if (
            !int.TryParse(BodyNumberText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int bodyNumber)
            || bodyNumber < -1
        )
        {
            StatusMessage = InvalidBodyNumberMessage;
            return null;
        }

        return new ColonizationProjectDraft(
            context.CommanderName ?? string.Empty,
            context.SystemName ?? string.Empty,
            context.StarPosition,
            SelectedLayout ?? string.Empty,
            ProjectName,
            ArchitectName,
            Notes,
            bodyNumber,
            BodyName,
            SelectedSystemSite?.Site?.Id
        );
    }

    private void ApplyAutomaticBody(ColonizationSystemSite? site)
    {
        if (context.CurrentBodyId is >= 0)
        {
            AssignBodyNumber(context.CurrentBodyId.Value.ToString(CultureInfo.InvariantCulture));
            if (bodyNameFollowsContext)
            {
                AssignBodyName(context.CurrentBodyName ?? string.Empty);
            }

            return;
        }

        if (site?.BodyNumber is >= 0)
        {
            AssignBodyNumber(site.BodyNumber.ToString(CultureInfo.InvariantCulture));
            if (bodyNameFollowsContext)
            {
                AssignBodyName(site.BodyName?.Trim() ?? string.Empty);
            }

            return;
        }

        AssignBodyNumber("-1");
        if (bodyNameFollowsContext)
        {
            AssignBodyName(string.Empty);
        }
    }

    private void AssignBodyNumber(string value)
    {
        assigningBodyNumber = true;
        try
        {
            BodyNumberText = value;
            bodyNumberFollowsContext = true;
        }
        finally
        {
            assigningBodyNumber = false;
        }
    }

    private void AssignBodyName(string value)
    {
        assigningBodyName = true;
        try
        {
            BodyName = value;
            bodyNameFollowsContext = true;
        }
        finally
        {
            assigningBodyName = false;
        }
    }

    private static string GetContextIdentity(ColonizationProjectEditorContext value)
    {
        // Stable site identity only. Live depot progress/resource ticks must not wipe an
        // in-progress create form while the commander remains at the same construction site.
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
            value.Depot?.MarketId,
            value.Depot?.IsComplete,
            value.Depot?.IsFailed
        );
    }

    private static string GetUnavailableReason(ColonizationProjectEditorContext value)
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
            return "Dock at a colonization construction site first.";
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

        if (!string.Equals(value.SystemName, value.Dock.SystemName, StringComparison.OrdinalIgnoreCase))
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

    private sealed class DelegateCommand(Action execute, Func<bool> canExecute) : ICommand
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

public sealed record ColonizationProjectEditorContext(
    bool IsExternalDataEnabled,
    string? CommanderName,
    string? SystemName,
    IReadOnlyList<double> StarPosition,
    ColonizationDockingSnapshot? Dock,
    ColonizationConstructionDepotSnapshot? Depot,
    string? RavenApiKey = null,
    int? CurrentBodyId = null,
    string? CurrentBodyName = null
)
{
    public static ColonizationProjectEditorContext Unavailable { get; } = new(false, null, null, [], null, null, null);
}

public sealed record ColonizationBuildOptionViewModel(ColonizationBuildCost Build)
{
    public string DisplayName => $"Tier {Build.Tier}: {Build.DisplayName}";
}

public sealed record ColonizationSystemSiteOptionViewModel(ColonizationSystemSite? Site)
{
    public static ColonizationSystemSiteOptionViewModel None { get; } = new((ColonizationSystemSite?)null);

    public string DisplayName => Site is null ? "None - configure manually" : $"{Site.Name} ({Site.BuildType})";
}
