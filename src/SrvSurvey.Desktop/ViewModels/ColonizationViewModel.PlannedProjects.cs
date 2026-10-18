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
    }

    private void StopPlannedProjects()
    {
        plannedReadVersion++;
        SystemEditor.Sites.CollectionChanged -= OnPlanningSitesChanged;
        SystemEditor.PropertyChanged -= OnPlanningEditorChanged;
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
        else if (args.PropertyName == nameof(SystemEditor.IsBusy) && !SystemEditor.IsBusy)
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
        )
        {
            return;
        }
        plannedReadVersion++;
        UpdatePlannedSystem(snapshot);
        SchedulePlannedSystemsSave();
        RebuildProjectRows();
        UpdateProjectSummary();
    }

    private async Task RefreshPlannedProjectsAsync(
        CancellationToken token,
        IReadOnlyList<ColonizationProject>? freshProjects = null
    )
    {
        if (disposed || !IsEnabled || CommanderName is null)
        {
            return;
        }
        CancelCurrentPlannedSystemRead();
        int readVersion = ++plannedReadVersion;
        int profileVersion = recovery.ProfileVersion;
        (IReadOnlyList<ColonizationSystemRecord> discovered, string? warning) = await ReadCommanderPlanningSystemsAsync(
            token
        );
        if (!IsCurrentPlannedRead(readVersion, profileVersion))
        {
            return;
        }
        var discoveredByName = discovered
            .GroupBy(system => system.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        string[] names = (freshProjects ?? recovery.Projects)
            .Select(project => project.SystemName)
            .Concat(plannedSystems.Keys)
            .Concat(discoveredByName.Keys)
            .Append(recovery.CurrentSystemName ?? string.Empty)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var snapshots = new List<ColonizationSystemRecord>();
        foreach (string systemName in names)
        {
            (ColonizationSystemRecord? snapshot, string? error) = await ReadPlannedSystemAsync(systemName, token);
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
                snapshots.Add(ResolvePlanningSnapshot(snapshot));
            }
        }
        if (!IsCurrentPlannedRead(readVersion, profileVersion))
        {
            return;
        }
        token.ThrowIfCancellationRequested();
        plannedReadWarning = warning;
        foreach (ColonizationSystemRecord snapshot in snapshots)
        {
            UpdatePlannedSystem(snapshot);
        }
        SavePlannedSystems();
        RebuildProjectRows();
        UpdateProjectSummary();
    }

    private ColonizationSystemRecord ResolvePlanningSnapshot(ColonizationSystemRecord snapshot) =>
        SystemEditor.HasLocalChanges
        && SystemEditor.PlanningSnapshot is { } edited
        && string.Equals(edited.Name, snapshot.Name, StringComparison.OrdinalIgnoreCase)
            ? edited
            : snapshot;

    private bool IsCurrentPlannedRead(int readVersion, int profileVersion) =>
        !disposed && IsEnabled && readVersion == plannedReadVersion && profileVersion == recovery.ProfileVersion;

    private async Task<(
        IReadOnlyList<ColonizationSystemRecord> Systems,
        string? Error
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
            return ([], null);
        }
        try
        {
            return (await reader.GetCommanderPlanningSystemsAsync(commander, key, token), null);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is HttpRequestException or InvalidDataException or TaskCanceledException)
        {
            return ([], "Planned systems could not be refreshed: " + exception.Message);
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
        IEnumerable<ColonizationProject> plans = plannedSystems.Values.SelectMany(system =>
            ColonizationPlannedProjects.Create(
                system,
                buildCatalog,
                recovery.Projects,
                CommanderName,
                string.Equals(system.Name, recovery.CurrentSystemName, StringComparison.OrdinalIgnoreCase)
                    ? currentStarPosition
                    : []
            )
        );
        Projects = recovery.Projects.Concat(plans).Select(CreateRow).ToArray();
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
