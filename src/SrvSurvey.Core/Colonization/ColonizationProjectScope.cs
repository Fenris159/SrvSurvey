namespace SrvSurvey.Core.Colonization;

/// <summary>Separates Raven affiliation, temporary system browsing, and explicitly retained local projects.</summary>
public sealed class ColonizationProjectScope
{
    private readonly Dictionary<string, ColonizationLocalProject> retained = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ColonizationProject> current = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> temporaryBuilds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> unverifiedBuilds = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> affiliatedBuilds = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> affiliatedSystems = new(StringComparer.OrdinalIgnoreCase);
    private bool hasProjectAffiliations;
    private bool hasSystemAffiliations;
    private ColonizationProject? dockedProject;

    public bool BrowseEnabled { get; set; }
    public string? CurrentSystem { get; private set; }
    public bool HasSystemAffiliations => hasSystemAffiliations;
    public IReadOnlySet<string> AffiliatedSystems => affiliatedSystems;

    /// <summary>Restores only deliberately retained projects; visited-system results never survive a restart.</summary>
    public ColonizationProjectScope(ColonizationProjectScopeState? state = null)
    {
        if (state is null)
        {
            return;
        }
        BrowseEnabled = state.BrowseEnabled;
        hasProjectAffiliations = state.HasProjectAffiliations;
        hasSystemAffiliations = state.HasSystemAffiliations;
        affiliatedBuilds.UnionWith(state.AffiliatedBuildIds);
        affiliatedSystems.UnionWith(state.AffiliatedSystemNames);
        foreach (ColonizationLocalProject entry in state.RetainedProjects)
        {
            retained[entry.Project.BuildId] = entry with
            {
                Project = entry.Project with { IsPlanned = entry.IsPlanned },
            };
        }
    }

    public bool IsAffiliated(ColonizationProject project) =>
        affiliatedBuilds.Contains(project.BuildId)
        || project.IsPlanned && affiliatedSystems.Contains(project.SystemName);

    public bool IsLocal(ColonizationProject project) =>
        !IsAffiliated(project)
        && (
            retained.ContainsKey(project.BuildId)
            || current.ContainsKey(project.BuildId)
            || dockedProject?.BuildId == project.BuildId
        );

    public bool IsShown(ColonizationProject project) =>
        retained.TryGetValue(project.BuildId, out ColonizationLocalProject? entry) && entry.IsShown;

    public void AddAffiliatedProject(ColonizationProject project)
    {
        affiliatedBuilds.Add(project.BuildId);
        affiliatedSystems.Add(project.SystemName);
        retained.Remove(project.BuildId);
    }

    public IEnumerable<string> HiddenLocalIds =>
        CurrentProjects()
            .Concat(retained.Values.Select(entry => entry.Project))
            .Where(project => !IsAffiliated(project) && !IsShown(project))
            .Select(project => project.BuildId)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>Commits successful commander results; an unavailable planning endpoint cannot erase saved affiliations.</summary>
    public void SetAffiliations(IEnumerable<ColonizationProject> projects, IEnumerable<string>? systems)
    {
        ColonizationProject[] live = projects.ToArray();
        affiliatedBuilds = live.Select(project => project.BuildId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        hasProjectAffiliations = true;
        if (systems is not null)
        {
            affiliatedSystems = systems
                .Concat(live.Select(project => project.SystemName))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            hasSystemAffiliations = true;
        }
        else
        {
            affiliatedSystems.UnionWith(live.Select(project => project.SystemName));
        }
        foreach (
            string id in retained.Where(pair => IsAffiliated(pair.Value.Project)).Select(pair => pair.Key).ToArray()
        )
        {
            retained.Remove(id);
        }
    }

    /// <summary>Expires unretained observations on travel or when browsing is disabled.</summary>
    public void EnterSystem(string? systemName)
    {
        if (!string.Equals(CurrentSystem, systemName, StringComparison.OrdinalIgnoreCase))
        {
            dockedProject = null;
        }
        if (!BrowseEnabled || !string.Equals(CurrentSystem, systemName, StringComparison.OrdinalIgnoreCase))
        {
            current.Clear();
        }
        CurrentSystem = systemName;
    }

    /// <summary>Reveals a docked site's needs without retaining it or requiring system browsing.</summary>
    public void SetDockedProject(ColonizationProject? project)
    {
        if (project is not null)
        {
            NoteTemporaryProject(project);
        }
        dockedProject = project;
    }

    private void NoteTemporaryProject(ColonizationProject project)
    {
        if (!unverifiedBuilds.Contains(project.BuildId))
        {
            temporaryBuilds.Add(project.BuildId);
        }
    }

    private IEnumerable<ColonizationProject> CurrentProjects() =>
        dockedProject is null ? current.Values : current.Values.Append(dockedProject);

    /// <summary>Replaces the current system's read-only observations, deduplicating commander-affiliated projects.</summary>
    public void SetCurrentProjects(IEnumerable<ColonizationProject> projects)
    {
        current.Clear();
        if (!BrowseEnabled)
        {
            return;
        }
        foreach (
            ColonizationProject project in projects.Where(project =>
                string.Equals(project.SystemName, CurrentSystem, StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            NoteTemporaryProject(project);
            current[project.BuildId] = project;
            if (retained.TryGetValue(project.BuildId, out ColonizationLocalProject? entry))
            {
                retained[project.BuildId] = entry with { Project = project, IsPlanned = project.IsPlanned };
            }
        }
        foreach (
            string id in retained
                .Where(pair =>
                    pair.Value.IsPlanned
                    && string.Equals(pair.Value.Project.SystemName, CurrentSystem, StringComparison.OrdinalIgnoreCase)
                    && !current.ContainsKey(pair.Key)
                )
                .Select(pair => pair.Key)
                .ToArray()
        )
        {
            retained.Remove(id);
        }
    }

    /// <summary>Records an explicit Show choice without changing Raven membership or visibility preferences.</summary>
    public void SetShown(ColonizationProject project, bool shown, string? focusId)
    {
        retained[project.BuildId] = new(project, project.IsPlanned, shown);
        ReleaseUnselected(focusId);
    }

    public void RetainFocus(ColonizationProject project)
    {
        if (!retained.ContainsKey(project.BuildId))
        {
            retained[project.BuildId] = new(project, project.IsPlanned, false);
        }
    }

    public void ReleaseUnselected(string? focusId)
    {
        foreach (
            string id in retained
                .Where(pair =>
                    !pair.Value.IsShown && !string.Equals(pair.Key, focusId, StringComparison.OrdinalIgnoreCase)
                )
                .Select(pair => pair.Key)
                .ToArray()
        )
        {
            retained.Remove(id);
        }
    }

    /// <summary>Merges workspace rows while preferring live delivery updates over retained snapshots.</summary>
    public IReadOnlyList<ColonizationProject> Merge(
        IEnumerable<ColonizationProject> liveProjects,
        IEnumerable<ColonizationProject> plans
    )
    {
        var result = new Dictionary<string, ColonizationProject>(StringComparer.OrdinalIgnoreCase);
        foreach (
            ColonizationProject project in CurrentProjects()
                .Concat(retained.Values.Select(entry => entry.Project))
                .Concat(plans)
        )
        {
            result[project.BuildId] = project;
        }
        foreach (ColonizationProject project in liveProjects)
        {
            if (
                (!hasProjectAffiliations && !temporaryBuilds.Contains(project.BuildId))
                || IsAffiliated(project)
                || IsLocal(project)
            )
            {
                result[project.BuildId] = project;
                if (!hasProjectAffiliations && !IsLocal(project))
                {
                    unverifiedBuilds.Add(project.BuildId);
                }
            }
            if (retained.TryGetValue(project.BuildId, out ColonizationLocalProject? entry))
            {
                retained[project.BuildId] = entry with { Project = project, IsPlanned = project.IsPlanned };
            }
        }
        return result.Values.ToArray();
    }

    public IReadOnlyList<ColonizationProject> GetRetainedLiveProjects() =>
        retained.Values.Where(entry => !entry.IsPlanned).Select(entry => entry.Project).ToArray();

    public ColonizationProjectScopeState Capture() =>
        new()
        {
            BrowseEnabled = BrowseEnabled,
            HasProjectAffiliations = hasProjectAffiliations,
            HasSystemAffiliations = hasSystemAffiliations,
            AffiliatedBuildIds = affiliatedBuilds.ToArray(),
            AffiliatedSystemNames = affiliatedSystems.ToArray(),
            RetainedProjects = retained.Values.ToArray(),
        };
}

public sealed record ColonizationLocalProject(ColonizationProject Project, bool IsPlanned, bool IsShown);

public sealed record ColonizationProjectScopeState
{
    public bool BrowseEnabled { get; init; }
    public bool HasProjectAffiliations { get; init; }
    public bool HasSystemAffiliations { get; init; }
    public string[] AffiliatedBuildIds { get; init; } = [];
    public string[] AffiliatedSystemNames { get; init; } = [];
    public ColonizationLocalProject[] RetainedProjects { get; init; } = [];
}
