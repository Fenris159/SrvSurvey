using System.Collections.Specialized;
using System.ComponentModel;
using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class ColonizationViewModel
{
    private readonly Dictionary<string, ColonizationSystemRecord> plannedSystems = new(
        StringComparer.OrdinalIgnoreCase
    );
    private int plannedReadVersion;
    private int planningEditVersion;
    private readonly Dictionary<string, int> planningSystemEditVersions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ColonizationSystemRecord> localPlanningDrafts = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly Dictionary<string, ColonizationSystemRecord> latestPlanningSnapshots = new(
        StringComparer.OrdinalIgnoreCase
    );
    private string? plannedReadWarning;
    private readonly HashSet<string> hiddenPlannedProjectIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<ColonizationSystemSiteRowViewModel> observedSites = [];
    private readonly HashSet<string> authoritativePlanningPrefixes = new(StringComparer.Ordinal);

    private string ProjectLoadStatus()
    {
        string status = Projects.Count switch
        {
            0 => "No active Raven Colonial projects were found for this commander.",
            1 => "Loaded 1 active Raven Colonial project.",
            _ => $"Loaded {Projects.Count:N0} active Raven Colonial projects.",
        };
        int plannedCount = Projects.Count(row => row.Project.IsPlanned);
        if (plannedCount > 0)
        {
            status = $"Loaded {Projects.Count - plannedCount:N0} active projects and {plannedCount:N0} planned sites.";
        }
        return plannedReadWarning is null ? status : status + " " + plannedReadWarning;
    }

    private void SetPlannedProjectShown(ColonizationProject project, bool shown)
    {
        if (shown)
        {
            hiddenPlannedProjectIds.Remove(project.BuildId);
        }
        else
        {
            hiddenPlannedProjectIds.Add(project.BuildId);
        }
        if (CommanderName is not null)
        {
            try
            {
                settingsStore.SaveHiddenPlannedProjects(CommanderName, hiddenPlannedProjectIds);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                StatusMessage = "The planned project visibility could not be saved to disk.";
            }
        }
        UpdateProjectSummary();
    }

    private void InitializePlannedProjects()
    {
        SystemEditor.Sites.CollectionChanged += OnPlanningSitesChanged;
        SystemEditor.PropertyChanged += OnPlanningEditorChanged;
        SystemEditor.SystemPublished += OnPlanningSystemPublished;
    }

    private void StopPlannedProjects()
    {
        plannedReadVersion++;
        SystemEditor.Sites.CollectionChanged -= OnPlanningSitesChanged;
        SystemEditor.PropertyChanged -= OnPlanningEditorChanged;
        SystemEditor.SystemPublished -= OnPlanningSystemPublished;
        foreach (ColonizationSystemSiteRowViewModel row in observedSites)
        {
            row.PropertyChanged -= OnPlanningSiteChanged;
        }
        observedSites.Clear();
    }

    private void OnPlanningSitesChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (SystemEditor.IsReplacingSites)
        {
            return;
        }
        ObservePlanningSites();
        ApplyEditorPlans();
    }

    private void ObservePlanningSites()
    {
        foreach (ColonizationSystemSiteRowViewModel row in observedSites)
        {
            row.PropertyChanged -= OnPlanningSiteChanged;
        }
        observedSites.Clear();
        foreach (ColonizationSystemSiteRowViewModel row in SystemEditor.Sites)
        {
            observedSites.Add(row);
            row.PropertyChanged += OnPlanningSiteChanged;
        }
    }

    private void OnPlanningSiteChanged(object? sender, PropertyChangedEventArgs args) => ApplyEditorPlans();

    private void OnPlanningEditorChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SystemEditor.PlanningSnapshot))
        {
            ObservePlanningSites();
            ApplyEditorPlans();
        }
        else if (
            args.PropertyName == nameof(SystemEditor.HasLocalChanges)
            || (args.PropertyName == nameof(SystemEditor.IsBusy) && !SystemEditor.IsBusy)
        )
        {
            ApplyEditorPlans();
        }
    }

    private void ApplyEditorPlans()
    {
        if (
            disposed
            || !IsEnabled
            || SystemEditor.IsBusy
            || SystemEditor.IsReplacingSites
            || SystemEditor.PlanningSnapshot is not { } snapshot
            || SystemEditor.AuthoritativePlanningSnapshot is not { } authoritative
        )
        {
            return;
        }
        CancelCurrentPlannedSystemRead(clearPending: false);
        authoritative = KeepNewestPlanningSnapshot(authoritative);
        if (SystemEditor.HasLocalChanges)
        {
            planningSystemEditVersions[snapshot.Name] = ++planningEditVersion;
            localPlanningDrafts[snapshot.Name] = snapshot;
        }
        else
        {
            localPlanningDrafts.Remove(snapshot.Name);
            planningSystemEditVersions.Remove(snapshot.Name);
            snapshot = authoritative;
        }
        UpdatePlannedSystem(snapshot);
        SchedulePlannedSystemsSave();
        RebuildProjectRows();
        UpdateProjectSummary();
    }

    private void OnPlanningSystemPublished(string commander, ColonizationSystemRecord snapshot)
    {
        if (disposed || !IsEnabled || !string.Equals(CommanderName, commander, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        if (string.Equals(snapshot.Name, recovery.CurrentSystemName, StringComparison.OrdinalIgnoreCase))
        {
            CancelCurrentPlannedSystemRead(clearPending: false);
        }
        localPlanningDrafts.Remove(snapshot.Name);
        planningSystemEditVersions.Remove(snapshot.Name);
        UpdatePlannedSystem(KeepNewestPlanningSnapshot(snapshot));
        SchedulePlannedSystemsSave();
        RebuildProjectRows();
        UpdateProjectSummary();
    }

    private async Task RefreshPlannedProjectsAsync(
        IReadOnlyList<ColonizationProject> freshProjects,
        int startedEditVersion,
        CancellationToken token
    )
    {
        if (disposed || !IsEnabled || CommanderName is null)
        {
            return;
        }
        // Keep travel queued until this refresh finishes, including cancellation during discovery.
        CancelCurrentPlannedSystemRead(clearPending: false);
        int readVersion = ++plannedReadVersion;
        int profileVersion = recovery.ProfileVersion;
        (IReadOnlyList<ColonizationSystemRecord> discovered, string? warning, bool authoritativeDiscovery) =
            await ReadCommanderPlanningSystemsAsync(token);
        if (!IsCurrentPlannedRead(readVersion, profileVersion))
        {
            return;
        }
        var discoveredByName = discovered
            .GroupBy(system => system.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        string[] names = freshProjects
            .Select(project => project.SystemName)
            .Concat(
                plannedSystems
                    .Values.Where(system =>
                        !authoritativeDiscovery
                        || ColonizationSiteVisibility.CommanderIsArchitect(system.Architect, CommanderName)
                    )
                    .Select(system => system.Name)
            )
            .Concat(localPlanningDrafts.Keys)
            .Concat(discoveredByName.Keys)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var snapshots = new List<(ColonizationSystemRecord Snapshot, bool Authoritative)>();
        foreach (string systemName in names)
        {
            (ColonizationSystemRecord? snapshot, string? error) = await ReadPlannedSystemAsync(systemName, token);
            bool authoritative = snapshot is not null;
            if (!IsCurrentPlannedRead(readVersion, profileVersion))
            {
                return;
            }
            warning ??= error;
            if (snapshot is null && !plannedSystems.ContainsKey(systemName))
            {
                snapshot = discoveredByName.GetValueOrDefault(systemName);
            }
            if (snapshot is not null)
            {
                snapshots.Add((snapshot, authoritative));
            }
        }
        if (!IsCurrentPlannedRead(readVersion, profileVersion))
        {
            return;
        }
        token.ThrowIfCancellationRequested();
        plannedReadWarning = warning;
        ReconcilePlanningAffiliations(freshProjects, authoritativeDiscovery ? discoveredByName.Keys : null);
        foreach ((ColonizationSystemRecord snapshot, bool authoritative) in snapshots)
        {
            ApplyRefreshedPlannedSystem(snapshot, startedEditVersion, authoritative);
        }
        SavePlannedSystems();
    }

    private void ReconcilePlanningAffiliations(
        IReadOnlyList<ColonizationProject> freshProjects,
        IEnumerable<string>? affiliatedSystems
    )
    {
        PreserveExplicitLocalProjects();
        projectScope.SetAffiliations(freshProjects, affiliatedSystems);
        if (!SaveLocalProjects() || affiliatedSystems is null)
        {
            return;
        }
        foreach (
            string name in plannedSystems
                .Where(pair => !IsAffiliatedPlanningSystem(pair.Value))
                .Select(pair => pair.Key)
                .ToArray()
        )
        {
            plannedSystems.Remove(name);
            latestPlanningSnapshots.Remove(name);
        }
    }

    private void ApplyRefreshedPlannedSystem(
        ColonizationSystemRecord snapshot,
        int startedEditVersion,
        bool authoritative
    )
    {
        if (
            planningSystemEditVersions.TryGetValue(snapshot.Name, out int editedVersion)
            && editedVersion > startedEditVersion
        )
        {
            return;
        }
        if (authoritative)
        {
            snapshot = KeepNewestPlanningSnapshot(snapshot);
            if (CurrentEditorDraft(snapshot.Name) is null)
            {
                localPlanningDrafts.Remove(snapshot.Name);
            }
        }
        else if (latestPlanningSnapshots.TryGetValue(snapshot.Name, out ColonizationSystemRecord? latest))
        {
            snapshot = latest;
        }
        UpdatePlannedSystem(ResolvePlanningSnapshot(snapshot));
    }

    private ColonizationSystemRecord? CurrentEditorDraft(string systemName) =>
        SystemEditor.HasLocalChanges
        && SystemEditor.PlanningSnapshot is { } edited
        && string.Equals(edited.Name, systemName, StringComparison.OrdinalIgnoreCase)
            ? edited
            : null;

    private ColonizationSystemRecord ResolvePlanningSnapshot(ColonizationSystemRecord snapshot) =>
        CurrentEditorDraft(snapshot.Name) ?? localPlanningDrafts.GetValueOrDefault(snapshot.Name) ?? snapshot;

    private ColonizationSystemRecord KeepNewestPlanningSnapshot(ColonizationSystemRecord snapshot)
    {
        if (
            latestPlanningSnapshots.TryGetValue(snapshot.Name, out ColonizationSystemRecord? latest)
            && latest.Revision > snapshot.Revision
        )
        {
            return latest;
        }
        latestPlanningSnapshots[snapshot.Name] = snapshot;
        return snapshot;
    }

    private bool IsCurrentPlannedRead(int readVersion, int profileVersion) =>
        !disposed && IsEnabled && readVersion == plannedReadVersion && profileVersion == recovery.ProfileVersion;

    private async Task<(
        IReadOnlyList<ColonizationSystemRecord> Systems,
        string? Error,
        bool Authoritative
    )> ReadCommanderPlanningSystemsAsync(CancellationToken token)
    {
        string? key = recovery.ApiKey;
        string? commander = CommanderName;
        if (
            client is not IRavenColonialPlanningSystemReader reader
            || string.IsNullOrWhiteSpace(key)
            || commander is null
        )
        {
            return ([], null, false);
        }
        try
        {
            return (await reader.GetCommanderPlanningSystemsAsync(commander, key, token), null, true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is HttpRequestException or InvalidDataException or TaskCanceledException)
        {
            return ([], "Planned systems could not be refreshed: " + exception.Message, false);
        }
    }

    private async Task<(ColonizationSystemRecord? Snapshot, string? Error)> ReadPlannedSystemAsync(
        string systemName,
        CancellationToken token
    )
    {
        try
        {
            return (await client.GetSystemAsync(systemName, token), null);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is HttpRequestException or InvalidDataException or TaskCanceledException)
        {
            return (null, "Planned site requirements could not be refreshed: " + exception.Message);
        }
    }

    private void UpdatePlannedSystem(ColonizationSystemRecord snapshot)
    {
        authoritativePlanningPrefixes.Add(ColonizationPlannedProjects.BuildIdPrefix + snapshot.SystemAddress + ":");
        if (snapshot.Sites.Any(site => site.HasExplicitStatus && site.Status == ColonizationSystemSiteStatus.Plan))
        {
            plannedSystems[snapshot.Name] = snapshot;
        }
        else
        {
            plannedSystems.Remove(snapshot.Name);
        }
    }

    private void RebuildProjectRows()
    {
        if (!IsEnabled)
        {
            Projects = [];
            return;
        }
        UpdateDockedLocalProject();
        ColonizationProject[] plans = plannedSystems
            .Values.SelectMany(system =>
                ColonizationPlannedProjects.Create(
                    system,
                    buildCatalog,
                    recovery.Projects,
                    CommanderName,
                    string.Equals(system.Name, recovery.CurrentSystemName, StringComparison.OrdinalIgnoreCase)
                        ? currentStarPosition
                        : []
                )
            )
            .ToArray();
        IReadOnlyList<ColonizationProject> visible = projectScope.Merge(recovery.Projects, plans);
        RetireMissingPlannedFocus(visible.Where(project => project.IsPlanned).ToArray());
        Projects = visible.Select(CreateRow).ToArray();
    }

    private sealed class WorkspaceProjectReader(ColonizationViewModel owner, IRavenColonialProjectReader reader)
        : IRavenColonialProjectReader
    {
        public Task<ColonizationProjectPreviewData?> ReadProjectPreviewAsync(
            string buildId,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            ColonizationProject? plan = owner
                .Projects.Select(row => row.Project)
                .FirstOrDefault(project => project.IsPlanned && project.BuildId == buildId);
            return plan is null
                ? reader.ReadProjectPreviewAsync(buildId, cancellationToken)
                : Task.FromResult<ColonizationProjectPreviewData?>(new(plan, null, null));
        }
    }
}
