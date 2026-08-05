using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class ColonizationSystemEditorViewModel
    : INotifyPropertyChanged
{
    private const int MaximumBufferedJournalEvents = 2048;

    private readonly IRavenColonialClient client;
    private readonly AsyncCommand loadCommand;
    private readonly DelegateCommand requestBodyImportCommand;
    private readonly AsyncCommand confirmBodyImportCommand;
    private readonly DelegateCommand cancelBodyImportCommand;
    private readonly DelegateCommand addSiteCommand;
    private readonly DelegateCommand removeSiteCommand;
    private readonly AsyncCommand reviewCommand;
    private readonly AsyncCommand confirmPublishCommand;
    private readonly DelegateCommand cancelPublishCommand;
    private readonly Queue<JournalEventEnvelope> bufferedJournalEvents = [];
    private ColonizationSystemEditorContext context =
        ColonizationSystemEditorContext.Unavailable;
    private ColonizationSystemRecord? system;
    private List<ColonizationSystemSite> baseline = [];
    private ColonizationSystemSiteJournalTracker? journalTracker;
    private EliteStatus? latestStatus;
    private ColonizationSystemSiteReconciliationPlan? pendingPlan;
    private string? pendingContextIdentity;
    private ColonizationSystemSiteRowViewModel? selectedSite;
    private string newSiteName = string.Empty;
    private bool isBusy;
    private bool canEdit;
    private bool hasLocalChanges;
    private bool isBodyImportConfirmationPending;
    private bool captureUnknownSurfaceSites;
    private string statusMessage =
        "Load a live system to review its Raven Colonial sites.";
    private string reviewSummary = string.Empty;
    private IReadOnlyList<ColonizationSystemSiteConflict> conflicts = [];
    private IReadOnlyList<ColonizationSystemBodyOptionViewModel> bodies = [];

    public ColonizationSystemEditorViewModel(
        IRavenColonialClient client,
        ColonizationBuildCatalog buildCatalog)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        ArgumentNullException.ThrowIfNull(buildCatalog);
        BuildTypes = buildCatalog.Builds
            .SelectMany(build => build.Layouts)
            .Concat(
            [
                "installation?",
                "outpost?",
                "no_truss?",
                "orbis?",
                "dodec?",
                "settlement?",
                "aphrodite?",
            ])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        loadCommand = new AsyncCommand(LoadAsync, () => CanLoad);
        requestBodyImportCommand = new DelegateCommand(
            RequestBodyImport,
            () => NeedsBodyImport && !HasLocalChanges && !IsBusy);
        confirmBodyImportCommand = new AsyncCommand(
            ConfirmBodyImportAsync,
            () => IsBodyImportConfirmationPending && !IsBusy);
        cancelBodyImportCommand = new DelegateCommand(
            CancelBodyImport,
            () => IsBodyImportConfirmationPending && !IsBusy);
        addSiteCommand = new DelegateCommand(AddSite, () => CanAddSite);
        removeSiteCommand = new DelegateCommand(
            RemoveSelectedSite,
            () => CanEdit && SelectedSite is not null && !IsBusy);
        reviewCommand = new AsyncCommand(
            ReviewAsync,
            () => CanReview);
        confirmPublishCommand = new AsyncCommand(
            ConfirmPublishAsync,
            () => CanConfirmPublish);
        cancelPublishCommand = new DelegateCommand(
            CancelPublish,
            () => IsPublishConfirmationPending && !IsBusy);
        LoadCommand = loadCommand;
        RequestBodyImportCommand = requestBodyImportCommand;
        ConfirmBodyImportCommand = confirmBodyImportCommand;
        CancelBodyImportCommand = cancelBodyImportCommand;
        AddSiteCommand = addSiteCommand;
        RemoveSiteCommand = removeSiteCommand;
        ReviewCommand = reviewCommand;
        ConfirmPublishCommand = confirmPublishCommand;
        CancelPublishCommand = cancelPublishCommand;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand LoadCommand { get; }

    public ICommand RequestBodyImportCommand { get; }

    public ICommand ConfirmBodyImportCommand { get; }

    public ICommand CancelBodyImportCommand { get; }

    public ICommand AddSiteCommand { get; }

    public ICommand RemoveSiteCommand { get; }

    public ICommand ReviewCommand { get; }

    public ICommand ConfirmPublishCommand { get; }

    public ICommand CancelPublishCommand { get; }

    public ObservableCollection<ColonizationSystemSiteRowViewModel> Sites
    {
        get;
    } = [];

    public IReadOnlyList<string> BuildTypes { get; }

    public IReadOnlyList<ColonizationSystemSiteStatus> SiteStatuses { get; } =
        Enum.GetValues<ColonizationSystemSiteStatus>();

    public IReadOnlyList<ColonizationSystemBodyOptionViewModel> Bodies => bodies;

    public bool CanLoad => !IsBusy
        && context.IsExternalDataEnabled
        && !string.IsNullOrWhiteSpace(context.CommanderName)
        && (!string.IsNullOrWhiteSpace(context.SystemName)
            || context.SystemAddress is > 0);

    public bool IsLoaded => system is not null;

    public long? LoadedSystemAddress => system?.SystemAddress;

    public string SystemTitle => system is null
        ? context.SystemName ?? "No live system"
        : $"{system.Name} ({system.SystemAddress})";

    public string Architect => string.IsNullOrWhiteSpace(system?.Architect)
        ? "Unassigned"
        : system.Architect;

    public bool IsOpenSystem => system?.IsOpen == true;

    public bool CanEdit
    {
        get => canEdit;
        private set
        {
            if (SetField(ref canEdit, value))
            {
                OnPropertyChanged(nameof(CanAddSite));
                OnPropertyChanged(nameof(CanReview));
                RaiseCommandStates();
            }
        }
    }

    public bool NeedsBodyImport => system is not null
        && system.Bodies is null;

    public bool IsBodyImportConfirmationPending
    {
        get => isBodyImportConfirmationPending;
        private set
        {
            if (SetField(ref isBodyImportConfirmationPending, value))
            {
                RaiseCommandStates();
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
                OnPropertyChanged(nameof(CanLoad));
                OnPropertyChanged(nameof(CanAddSite));
                OnPropertyChanged(nameof(CanReview));
                OnPropertyChanged(nameof(CanConfirmPublish));
                RaiseCommandStates();
            }
        }
    }

    public bool HasLocalChanges
    {
        get => hasLocalChanges;
        private set
        {
            if (SetField(ref hasLocalChanges, value))
            {
                OnPropertyChanged(nameof(CanReview));
                RaiseCommandStates();
            }
        }
    }

    public ColonizationSystemSiteRowViewModel? SelectedSite
    {
        get => selectedSite;
        set
        {
            if (SetField(ref selectedSite, value))
            {
                removeSiteCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string NewSiteName
    {
        get => newSiteName;
        set
        {
            if (SetField(ref newSiteName, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(CanAddSite));
                addSiteCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanAddSite => CanEdit
        && !IsBusy
        && !string.IsNullOrWhiteSpace(NewSiteName);

    public bool CaptureUnknownSurfaceSites
    {
        get => captureUnknownSurfaceSites;
        set
        {
            if (SetField(ref captureUnknownSurfaceSites, value))
            {
                StatusMessage = value
                    ? "Surface discovery capture is active. Select settlements in Elite to add them locally."
                    : "Surface discovery capture is off.";
            }
        }
    }

    public int? ExpectedBodyCount => journalTracker?.ExpectedBodyCount;

    public int ScannedBodyCount => journalTracker?.ScannedBodyCount ?? 0;

    public bool IsBodyScanComplete =>
        journalTracker?.IsBodyScanComplete == true;

    public string ScanSummary => journalTracker is null
        ? "Journal scan context is not loaded."
        : (IsBodyScanComplete) switch
        {
            true => $"Body scan complete ({ScannedBodyCount:N0} scanned).",
            false => ExpectedBodyCount switch
            {
                int expected => $"Body scans: {ScannedBodyCount:N0} of {expected:N0}.",
                null => $"Body scans recorded: {ScannedBodyCount:N0}."
            }
        };

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public IReadOnlyList<ColonizationSystemSiteConflict> Conflicts
    {
        get => conflicts;
        private set
        {
            if (SetField(ref conflicts, value))
            {
                OnPropertyChanged(nameof(HasConflicts));
            }
        }
    }

    public bool HasConflicts => Conflicts.Count > 0;

    public string ReviewSummary
    {
        get => reviewSummary;
        private set => SetField(ref reviewSummary, value);
    }

    public bool IsPublishConfirmationPending => pendingPlan is not null;

    public bool CanReview => CanEdit
        && HasLocalChanges
        && !IsBusy
        && !IsBodyImportConfirmationPending;

    public bool CanConfirmPublish => pendingPlan?.CanPublish == true
        && !IsBusy
        && CanEdit
        && !string.IsNullOrWhiteSpace(context.RavenApiKey);

    public void UpdateContext(ColonizationSystemEditorContext updatedContext)
    {
        ArgumentNullException.ThrowIfNull(updatedContext);
        var changed = !string.Equals(
            GetLoadedContextIdentity(context),
            GetLoadedContextIdentity(updatedContext),
            StringComparison.Ordinal);
        context = updatedContext;
        if (changed)
        {
            ResetLoadedSystem();
        }

        StatusMessage = CanLoad
            ? (IsLoaded) switch
            {
                true => StatusMessage,
                false => "The live system is ready to load from Raven Colonial."
            }
            : GetUnavailableReason(context);
        OnPropertyChanged(nameof(CanLoad));
        OnPropertyChanged(nameof(SystemTitle));
        OnPropertyChanged(nameof(CanConfirmPublish));
        RaiseCommandStates();
    }

    public void ReportLinkFailure(string message)
    {
        StatusMessage = "The Raven system page could not be opened: "
            + message;
    }

    public void ApplyJournalEvents(
        IReadOnlyList<JournalEventEnvelope> journalEvents)
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        foreach (var journalEvent in journalEvents)
        {
            bufferedJournalEvents.Enqueue(journalEvent);
            while (bufferedJournalEvents.Count > MaximumBufferedJournalEvents)
            {
                bufferedJournalEvents.Dequeue();
            }
        }

        if (journalTracker is null || !CanEdit)
        {
            return;
        }

        var sites = SnapshotSites();
        var changed = journalTracker.ApplyJournalEvents(sites, journalEvents);
        RaiseScanProperties();
        if (changed > 0)
        {
            ReplaceRows(sites);
            MarkLocalChange(
                $"Journal data enriched {changed:N0} Raven site entr{(changed == 1 ? "y" : "ies")} locally.");
        }
    }

    public void UpdateStatus(EliteStatus? status)
    {
        latestStatus = status;
        if (journalTracker is null || !CanEdit)
        {
            return;
        }

        var sites = SnapshotSites();
        if (journalTracker.ApplyStatusDestination(
                sites,
                status,
                CaptureUnknownSurfaceSites))
        {
            ReplaceRows(sites);
            MarkLocalChange(
                "The selected Elite destination updated a Raven site locally.");
        }
    }

    public async Task LoadAsync()
    {
        if (!CanLoad)
        {
            StatusMessage = GetUnavailableReason(context);
            return;
        }

        IsBusy = true;
        ClearReview();
        StatusMessage = "Loading the current system from Raven Colonial...";
        try
        {
            var loaded = await client.GetSystemAsync(GetSystemIdentifier());
            ApplyLoadedSystem(loaded);
            StatusMessage = CanEdit
                ? (NeedsBodyImport) switch
                {
                    true => "Sites loaded read-only from Raven. Confirm a body import before using body-aware editing.",
                    false => $"Loaded {Sites.Count:N0} sites. Changes remain local until reviewed and confirmed."
                }
                : "This secured system can only be edited by its architect.";
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            StatusMessage = "The Raven system could not be loaded: "
                + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void RequestBodyImport()
    {
        if (!NeedsBodyImport || HasLocalChanges || IsBusy)
        {
            return;
        }

        ClearReview();
        IsBodyImportConfirmationPending = true;
        StatusMessage = "Confirm to ask Raven Colonial to import this system's body catalog. No local sites will be published.";
    }

    public async Task ConfirmBodyImportAsync()
    {
        if (!IsBodyImportConfirmationPending
            || system is null
            || IsBusy)
        {
            return;
        }

        if (!ContextMatchesLoadedSystem())
        {
            CancelBodyImport();
            StatusMessage = "The live system changed. Reload before importing bodies.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Importing the system body catalog into Raven Colonial...";
        try
        {
            var imported = await client.ImportSystemBodiesAsync(
                GetSystemIdentifier());
            ApplyLoadedSystem(imported);
            IsBodyImportConfirmationPending = false;
            StatusMessage = imported.Bodies is null
                ? "Raven Colonial completed the request but returned no body catalog."
                : $"Imported {imported.Bodies.Count:N0} system bodies. No site edits were published.";
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            StatusMessage = "The body catalog was not imported: "
                + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void AddSite()
    {
        if (!CanAddSite)
        {
            return;
        }

        var name = NewSiteName.Trim();
        if (Sites.Any(site => string.Equals(
                site.Name,
                name,
                StringComparison.OrdinalIgnoreCase)))
        {
            StatusMessage = $"A site named '{name}' already exists.";
            return;
        }

        var id = CreateLocalSiteId();
        var row = new ColonizationSystemSiteRowViewModel(
            new ColonizationSystemSite
            {
                Id = id,
                Name = name,
                BodyNumber = -1,
                Status = ColonizationSystemSiteStatus.Plan,
            });
        Subscribe(row);
        Sites.Insert(0, row);
        SelectedSite = row;
        NewSiteName = string.Empty;
        MarkLocalChange($"Added '{name}' locally.");
    }

    public void RemoveSelectedSite()
    {
        if (!CanEdit || SelectedSite is null || IsBusy)
        {
            return;
        }

        var removed = SelectedSite;
        Unsubscribe(removed);
        Sites.Remove(removed);
        SelectedSite = null;
        MarkLocalChange($"Removed '{removed.Name}' locally.");
    }

    public async Task ReviewAsync()
    {
        if (!CanReview)
        {
            return;
        }

        if (!TryValidateSites(out var edited, out var validationMessage))
        {
            StatusMessage = validationMessage;
            return;
        }

        IsBusy = true;
        ClearReview();
        StatusMessage = "Refreshing Raven data and checking for concurrent changes...";
        try
        {
            var latest = await client.GetSystemAsync(GetSystemIdentifier());
            if (!CanCommanderEdit(latest, context.CommanderName))
            {
                StatusMessage = "Raven secured this system after it was loaded. No changes can be published.";
                CanEdit = false;
                return;
            }

            var plan = ColonizationSystemSiteReconciler.CreatePlan(
                baseline,
                latest.Sites,
                edited);
            Conflicts = plan.Conflicts;
            if (plan.Conflicts.Count > 0)
            {
                ReviewSummary = $"Review found {plan.Conflicts.Count:N0} concurrent conflict(s). Reload and reapply those edits before publishing.";
                StatusMessage = "Concurrent Raven changes were preserved; nothing is ready to publish.";
                return;
            }

            if (!plan.HasChanges)
            {
                ReviewSummary = "The local workspace already matches Raven Colonial.";
                StatusMessage = "There are no Raven site changes to publish.";
                HasLocalChanges = false;
                return;
            }

            pendingPlan = plan;
            pendingContextIdentity = GetContextIdentity(context);
            ReviewSummary = $"Ready to publish {plan.Update.UpdatedSites.Count:N0} update(s) and {plan.Update.DeletedSiteIds.Count:N0} deletion(s). {plan.UnchangedCount:N0} site(s) remain unchanged.";
            StatusMessage = string.IsNullOrWhiteSpace(context.RavenApiKey)
                ? "Review passed, but a saved Raven API key is required before publishing."
                : "Review passed. Confirm once more to publish these Raven site changes.";
            RaiseReviewProperties();
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            StatusMessage = "The Raven site review could not be completed: "
                + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ConfirmPublishAsync()
    {
        if (!CanConfirmPublish || pendingPlan is null)
        {
            return;
        }

        if (!string.Equals(
                pendingContextIdentity,
                GetContextIdentity(context),
                StringComparison.Ordinal)
            || !ContextMatchesLoadedSystem())
        {
            CancelPublish();
            StatusMessage = "The commander or live system changed. Review the edits again before publishing.";
            return;
        }

        var plan = pendingPlan;
        IsBusy = true;
        StatusMessage = "Publishing the confirmed site changes to Raven Colonial...";
        try
        {
            var updated = await client.UpdateSystemSitesAsync(
                GetSystemIdentifier(),
                plan.Update,
                context.RavenApiKey!);
            ApplyLoadedSystem(updated);
            StatusMessage = $"Raven Colonial accepted the update. Revision {updated.Revision:N0} now has {updated.Sites.Count:N0} sites.";
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            StatusMessage = "The confirmed Raven update was not published: "
                + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyLoadedSystem(ColonizationSystemRecord loaded)
    {
        ValidateLoadedSystem(loaded);
        system = CloneSystem(loaded);
        bodies = system.Bodies?
            .OrderBy(body => body.Number)
            .Select(body => new ColonizationSystemBodyOptionViewModel(
                body.Number,
                body.Name))
            .ToArray() ?? [];
        baseline = loaded.Sites.Select(CloneSite).ToList();
        CanEdit = CanCommanderEdit(loaded, context.CommanderName);
        journalTracker = new ColonizationSystemSiteJournalTracker(
            loaded.SystemAddress,
            loaded.Name,
            loaded.Bodies?.Select(body => body.Number));
        var editableSites = loaded.Sites.Select(CloneSite).ToList();
        if (CanEdit)
        {
            journalTracker.ApplyJournalEvents(
                editableSites,
                bufferedJournalEvents);
            journalTracker.ApplyStatusDestination(
                editableSites,
                latestStatus,
                CaptureUnknownSurfaceSites);
        }

        ReplaceRows(editableSites);
        HasLocalChanges = !SiteListsEqual(baseline, editableSites);
        IsBodyImportConfirmationPending = false;
        ClearReview();
        OnPropertyChanged(nameof(IsLoaded));
        OnPropertyChanged(nameof(LoadedSystemAddress));
        OnPropertyChanged(nameof(SystemTitle));
        OnPropertyChanged(nameof(Architect));
        OnPropertyChanged(nameof(IsOpenSystem));
        OnPropertyChanged(nameof(NeedsBodyImport));
        OnPropertyChanged(nameof(Bodies));
        RaiseScanProperties();
    }

    private void ResetLoadedSystem()
    {
        system = null;
        bodies = [];
        baseline = [];
        journalTracker = null;
        CanEdit = false;
        HasLocalChanges = false;
        IsBodyImportConfirmationPending = false;
        CaptureUnknownSurfaceSites = false;
        ReplaceRows([]);
        ClearReview();
        OnPropertyChanged(nameof(IsLoaded));
        OnPropertyChanged(nameof(LoadedSystemAddress));
        OnPropertyChanged(nameof(SystemTitle));
        OnPropertyChanged(nameof(Architect));
        OnPropertyChanged(nameof(IsOpenSystem));
        OnPropertyChanged(nameof(NeedsBodyImport));
        OnPropertyChanged(nameof(Bodies));
        RaiseScanProperties();
    }

    private void ReplaceRows(IEnumerable<ColonizationSystemSite> sites)
    {
        foreach (var row in Sites)
        {
            Unsubscribe(row);
        }

        Sites.Clear();
        foreach (var site in sites)
        {
            var row = new ColonizationSystemSiteRowViewModel(site);
            Subscribe(row);
            Sites.Add(row);
        }

        SelectedSite = null;
    }

    private List<ColonizationSystemSite> SnapshotSites()
    {
        return Sites.Select(row => row.ToSite()).ToList();
    }

    private bool TryValidateSites(
        out List<ColonizationSystemSite> sites,
        out string message)
    {
        sites = SnapshotSites();
        message = string.Empty;
        var missingName = sites.FirstOrDefault(site =>
            string.IsNullOrWhiteSpace(site.Name));
        if (missingName is not null)
        {
            message = "Every Raven site requires a name.";
            return false;
        }

        var duplicate = sites
            .GroupBy(site => site.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            message = $"The site name '{duplicate.Key}' is duplicated.";
            return false;
        }

        var knownBodies = system?.Bodies?.Select(body => body.Number).ToHashSet();
        var invalidBody = sites.FirstOrDefault(site =>
            site.BodyNumber < -1
            || (site.BodyNumber >= 0
                && knownBodies is { Count: > 0 }
                && !knownBodies.Contains(site.BodyNumber)));
        if (invalidBody is not null)
        {
            message = $"'{invalidBody.Name}' uses body number {invalidBody.BodyNumber}, which is not in the imported body catalog.";
            return false;
        }

        sites = sites
            .Select(site => site with
            {
                Id = site.Id.Trim(),
                Name = site.Name.Trim(),
                BuildType = NormalizeOptional(site.BuildType),
                BuildId = NormalizeOptional(site.BuildId),
            })
            .ToList();
        return true;
    }

    private void SitePropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (sender is ColonizationSystemSiteRowViewModel row)
        {
            MarkLocalChange($"Edited '{row.Name}' locally.");
        }
    }

    private void MarkLocalChange(string message)
    {
        HasLocalChanges = true;
        ClearReview();
        StatusMessage = message + " Review and confirmation are required to publish.";
    }

    private void Subscribe(ColonizationSystemSiteRowViewModel row)
    {
        row.PropertyChanged += SitePropertyChanged;
    }

    private void Unsubscribe(ColonizationSystemSiteRowViewModel row)
    {
        row.PropertyChanged -= SitePropertyChanged;
    }

    private void RequestBodyImportCommandState()
    {
        requestBodyImportCommand.RaiseCanExecuteChanged();
        confirmBodyImportCommand.RaiseCanExecuteChanged();
        cancelBodyImportCommand.RaiseCanExecuteChanged();
    }

    private void CancelBodyImport()
    {
        IsBodyImportConfirmationPending = false;
        StatusMessage = "Body import cancelled; Raven Colonial was not changed.";
    }

    private void CancelPublish()
    {
        ClearReview();
        StatusMessage = "Publish cancelled; all edits remain local.";
    }

    private void ClearReview()
    {
        pendingPlan = null;
        pendingContextIdentity = null;
        Conflicts = [];
        ReviewSummary = string.Empty;
        RaiseReviewProperties();
    }

    private void RaiseReviewProperties()
    {
        OnPropertyChanged(nameof(IsPublishConfirmationPending));
        OnPropertyChanged(nameof(CanConfirmPublish));
        reviewCommand.RaiseCanExecuteChanged();
        confirmPublishCommand.RaiseCanExecuteChanged();
        cancelPublishCommand.RaiseCanExecuteChanged();
    }

    private void RaiseScanProperties()
    {
        OnPropertyChanged(nameof(ExpectedBodyCount));
        OnPropertyChanged(nameof(ScannedBodyCount));
        OnPropertyChanged(nameof(IsBodyScanComplete));
        OnPropertyChanged(nameof(ScanSummary));
    }

    private void RaiseCommandStates()
    {
        loadCommand.RaiseCanExecuteChanged();
        RequestBodyImportCommandState();
        addSiteCommand.RaiseCanExecuteChanged();
        removeSiteCommand.RaiseCanExecuteChanged();
        RaiseReviewProperties();
    }

    private string GetSystemIdentifier()
    {
        return context.SystemAddress is > 0
            ? context.SystemAddress.Value.ToString()
            : context.SystemName!.Trim();
    }

    private bool ContextMatchesLoadedSystem()
    {
        if (system is null)
        {
            return false;
        }

        return context.SystemAddress is > 0
            ? context.SystemAddress == system.SystemAddress
            : string.Equals(
                context.SystemName,
                system.Name,
                StringComparison.OrdinalIgnoreCase);
    }

    private string CreateLocalSiteId()
    {
        var value = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string id;
        do
        {
            id = $"y{value++}";
        }
        while (Sites.Any(site => string.Equals(
            site.Id,
            id,
            StringComparison.Ordinal)));
        return id;
    }

    private static bool CanCommanderEdit(
        ColonizationSystemRecord record,
        string? commanderName)
    {
        return string.IsNullOrWhiteSpace(record.Architect)
            || record.IsOpen
            || string.Equals(
                record.Architect,
                commanderName,
                StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateLoadedSystem(ColonizationSystemRecord loaded)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        if (loaded.SystemAddress <= 0 || string.IsNullOrWhiteSpace(loaded.Name))
        {
            throw new InvalidDataException(
                "Raven Colonial returned an incomplete system identity.");
        }

        var duplicateId = loaded.Sites
            .Where(site => !string.IsNullOrWhiteSpace(site.Id))
            .GroupBy(site => site.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        var duplicateName = loaded.Sites
            .GroupBy(site => site.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (loaded.Sites.Any(site => string.IsNullOrWhiteSpace(site.Name))
            || duplicateId is not null
            || duplicateName is not null)
        {
            throw new InvalidDataException(
                "Raven Colonial returned unnamed or duplicate system sites.");
        }
    }

    private static string GetContextIdentity(
        ColonizationSystemEditorContext value)
    {
        return string.Join(
            "|",
            value.IsExternalDataEnabled,
            value.CommanderName,
            value.SystemName,
            value.SystemAddress);
    }

    private static string GetLoadedContextIdentity(
        ColonizationSystemEditorContext value)
    {
        return GetContextIdentity(value);
    }

    private static string GetUnavailableReason(
        ColonizationSystemEditorContext value)
    {
        if (!value.IsExternalDataEnabled)
        {
            return "Enable Raven Colonial before loading a system.";
        }

        if (string.IsNullOrWhiteSpace(value.CommanderName))
        {
            return "An active commander profile is required.";
        }

        if (string.IsNullOrWhiteSpace(value.SystemName)
            && value.SystemAddress is not > 0)
        {
            return "Enter a live Elite system before loading Raven sites.";
        }

        return "The Raven system context is incomplete.";
    }

    private static bool IsExpectedFailure(Exception exception)
    {
        return exception is HttpRequestException
            or InvalidDataException
            or TaskCanceledException;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static ColonizationSystemRecord CloneSystem(
        ColonizationSystemRecord record)
    {
        return record with
        {
            Sites = record.Sites.Select(CloneSite).ToList(),
            Bodies = record.Bodies?.Select(CloneBody).ToList(),
            ExtensionData = CloneJsonMap(record.ExtensionData),
        };
    }

    private static ColonizationSystemBody CloneBody(
        ColonizationSystemBody body)
    {
        return body with
        {
            Parents = [.. body.Parents],
            Features = [.. body.Features],
            ExtensionData = CloneJsonMap(body.ExtensionData),
        };
    }

    private static ColonizationSystemSite CloneSite(
        ColonizationSystemSite site)
    {
        return site with { ExtensionData = CloneJsonMap(site.ExtensionData) };
    }

    private static Dictionary<string, JsonElement> CloneJsonMap(
        IReadOnlyDictionary<string, JsonElement> source)
    {
        return source.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Clone(),
            StringComparer.Ordinal);
    }

    private static bool SiteListsEqual(
        List<ColonizationSystemSite> left,
        List<ColonizationSystemSite> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        return left.All(site => right.Any(candidate =>
            string.Equals(site.Id, candidate.Id, StringComparison.Ordinal)
            && string.Equals(site.Name, candidate.Name, StringComparison.Ordinal)
            && site.BodyNumber == candidate.BodyNumber
            && string.Equals(
                site.BuildType,
                candidate.BuildType,
                StringComparison.Ordinal)
            && string.Equals(
                site.BuildId,
                candidate.BuildId,
                StringComparison.Ordinal)
            && site.MarketId == candidate.MarketId
            && site.Status == candidate.Status));
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

public sealed class ColonizationSystemSiteRowViewModel
    : INotifyPropertyChanged
{
    private readonly IReadOnlyDictionary<string, JsonElement> extensionData;
    private string id;
    private string name;
    private int bodyNumber;
    private string? buildType;
    private string? buildId;
    private long? marketId;
    private ColonizationSystemSiteStatus status;

    public ColonizationSystemSiteRowViewModel(ColonizationSystemSite site)
    {
        ArgumentNullException.ThrowIfNull(site);
        id = site.Id;
        name = site.Name;
        bodyNumber = site.BodyNumber;
        buildType = site.BuildType;
        buildId = site.BuildId;
        marketId = site.MarketId;
        status = site.Status;
        extensionData = site.ExtensionData.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Clone(),
            StringComparer.Ordinal);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<ColonizationSystemSiteStatus> AllowedStatuses { get; }
        = Enum.GetValues<ColonizationSystemSiteStatus>();

    public string Id
    {
        get => id;
        set => SetField(ref id, value ?? string.Empty);
    }

    public string Name
    {
        get => name;
        set => SetField(ref name, value ?? string.Empty);
    }

    public int BodyNumber
    {
        get => bodyNumber;
        set => SetField(ref bodyNumber, value);
    }

    public string? BuildType
    {
        get => buildType;
        set => SetField(ref buildType, value);
    }

    public string? BuildId
    {
        get => buildId;
        set => SetField(ref buildId, value);
    }

    public long? MarketId
    {
        get => marketId;
        set => SetField(ref marketId, value);
    }

    public ColonizationSystemSiteStatus Status
    {
        get => status;
        set => SetField(ref status, value);
    }

    public ColonizationSystemSite ToSite()
    {
        return new ColonizationSystemSite
        {
            Id = Id,
            Name = Name,
            BodyNumber = BodyNumber,
            BuildType = BuildType,
            BuildId = BuildId,
            MarketId = MarketId,
            Status = Status,
            ExtensionData = extensionData.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Clone(),
                StringComparer.Ordinal),
        };
    }

    private void SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record ColonizationSystemBodyOptionViewModel(
    int Number,
    string Name)
{
    public string Label => $"{Number}: {Name}";
}

public sealed record ColonizationSystemEditorContext(
    bool IsExternalDataEnabled,
    string? CommanderName,
    string? SystemName,
    long? SystemAddress,
    string? RavenApiKey)
{
    public static ColonizationSystemEditorContext Unavailable { get; } = new(
        false,
        null,
        null,
        null,
        null);
}
