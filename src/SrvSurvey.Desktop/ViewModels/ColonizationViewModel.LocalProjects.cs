using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class ColonizationViewModel
{
    private ColonizationProjectScope projectScope = new();
    private bool selectCurrentProjectSystem;

    public bool ShowUnaffiliatedProjectsInCurrentSystem
    {
        get => projectScope.BrowseEnabled;
        set
        {
            if (value == projectScope.BrowseEnabled)
            {
                return;
            }
            projectScope.BrowseEnabled = value;
            SaveLocalProjects();
            OnPropertyChanged();
            selectCurrentProjectSystem = value;
            if (IsEnabled)
            {
                UpdateLocalProjectSystem();
                RefreshCurrentPlannedSystem();
            }
        }
    }

    private IEnumerable<string> EffectiveHiddenProjectIds =>
        hiddenProjectIds.Concat(hiddenPlannedProjectIds).Concat(projectScope.HiddenLocalIds);

    private bool IsProjectShown(ColonizationProject project) =>
        projectScope.IsLocal(project)
            ? projectScope.IsShown(project)
            : !hiddenProjectIds.Contains(project.BuildId) && !hiddenPlannedProjectIds.Contains(project.BuildId);

    private bool SaveLocalProjects()
    {
        if (CommanderName is not { } commander)
        {
            return true;
        }
        try
        {
            settingsStore.SaveLocalProjects(commander, projectScope.Capture());
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            plannedReadWarning = "Local project tracking could not be saved: " + exception.Message;
            StatusMessage = plannedReadWarning;
            return false;
        }
    }

    private void UpdateLocalProjectSystem()
    {
        projectScope.EnterSystem(recovery.CurrentSystemName);
        UpdateDockedLocalProject();
        recovery.ReplaceWorkspace(LiveWorkspaceProjects(recovery.Projects), recovery.FleetCarriers);
        RebuildProjectRows();
        UpdateProjectSummary();
    }

    private bool IsAffiliatedPlanningSystem(ColonizationSystemRecord system) =>
        projectScope.AffiliatedSystems.Contains(system.Name)
        || ColonizationSiteVisibility.CommanderIsArchitect(system.Architect, CommanderName)
        || localPlanningDrafts.ContainsKey(system.Name);

    private void PreserveExplicitLocalProjects()
    {
        IEnumerable<ColonizationProject> candidates = Projects
            .Select(row => row.Project)
            .Concat(
                plannedSystems.Values.SelectMany(system =>
                    ColonizationPlannedProjects.Create(system, buildCatalog, recovery.Projects, CommanderName, [])
                )
            );
        foreach (
            ColonizationProject project in candidates.DistinctBy(
                project => project.BuildId,
                StringComparer.OrdinalIgnoreCase
            )
        )
        {
            if (shoppingProjectIds.Contains(project.BuildId) && !projectScope.IsAffiliated(project))
            {
                projectScope.SetShown(project, true, FocusProjectId);
            }
            if (string.Equals(project.BuildId, localFocusProjectId, StringComparison.OrdinalIgnoreCase))
            {
                projectScope.RetainFocus(project);
            }
        }
    }

    private async Task LoadLocalSystemProjectsAsync(ColonizationSystemRecord snapshot, CancellationToken token)
    {
        var local = new List<ColonizationProject>(
            ColonizationPlannedProjects.Create(snapshot, buildCatalog, recovery.Projects, CommanderName, [])
        );
        foreach (
            ColonizationSystemSite site in snapshot.Sites.DistinctBy(site =>
                site.BuildId ?? site.MarketId?.ToString(System.Globalization.CultureInfo.InvariantCulture)
            )
        )
        {
            ColonizationProject? project = null;
            if (!string.IsNullOrWhiteSpace(site.BuildId))
            {
                project = await client.GetProjectAsync(site.BuildId, token);
            }
            else if (
                site.MarketId is > 0
                && (!site.HasExplicitStatus || site.Status != ColonizationSystemSiteStatus.Plan)
            )
            {
                project = await client.GetProjectAsync(snapshot.SystemAddress, site.MarketId.Value, token);
            }
            if (project is not null)
            {
                local.Add(project);
            }
        }
        token.ThrowIfCancellationRequested();
        authoritativePlanningPrefixes.Add(ColonizationPlannedProjects.BuildIdPrefix + snapshot.SystemAddress + ":");
        projectScope.SetCurrentProjects(local);
        ColonizationProject[] live = recovery
            .Projects.Where(project => !local.Any(fresh => fresh.BuildId == project.BuildId))
            .Concat(local.Where(project => !project.IsPlanned))
            .ToArray();
        recovery.ReplaceWorkspace(LiveWorkspaceProjects(live), recovery.FleetCarriers);
    }

    private void UpdateDockedLocalProject()
    {
        ColonizationDockingSnapshot? dock = recovery.CreateConstructionSnapshot().CurrentDock;
        projectScope.SetDockedProject(
            dock is { IsConstructionSite: true }
            && (recovery.CurrentSystemAddress is null || dock.SystemAddress == recovery.CurrentSystemAddress)
                ? recovery.Projects.FirstOrDefault(project =>
                    project.MarketId == dock.MarketId && project.SystemAddress == dock.SystemAddress
                )
                : null
        );
    }

    private IEnumerable<ColonizationProject> LiveWorkspaceProjects(IReadOnlyList<ColonizationProject> live) =>
        projectScope
            .Merge(live, [])
            .Where(project => !project.IsPlanned || live.Any(existing => existing.BuildId == project.BuildId));

    private async Task RefreshRetainedProjectsAsync(CancellationToken token)
    {
        ColonizationProjectScope scope = projectScope;
        var refreshed = new List<ColonizationProject>();
        foreach (ColonizationProject project in scope.GetRetainedLiveProjects())
        {
            try
            {
                refreshed.Add(await client.GetProjectAsync(project.BuildId, token) ?? project);
            }
            catch (Exception exception)
                when (exception is HttpRequestException or InvalidDataException or TaskCanceledException)
            {
                token.ThrowIfCancellationRequested();
                refreshed.Add(project);
                plannedReadWarning = "Tracked local projects could not be refreshed: " + exception.Message;
            }
        }
        token.ThrowIfCancellationRequested();
        scope.Merge(refreshed, []);
    }

    private void SelectLocalProjectSystem()
    {
        if (!selectCurrentProjectSystem || !ShowUnaffiliatedProjectsInCurrentSystem)
        {
            return;
        }
        ProjectSystemFilter = ProjectSystemOptions.FirstOrDefault(option =>
            string.Equals(option.SystemName, recovery.CurrentSystemName, StringComparison.OrdinalIgnoreCase)
        );
        ProjectBuildFilter = AllProjectBuilds;
        selectCurrentProjectSystem = false;
    }
}
