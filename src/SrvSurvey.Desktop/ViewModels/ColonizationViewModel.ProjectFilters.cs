namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class ColonizationViewModel
{
    private static readonly ColonizationProjectSystemOption AllProjectSystems = new(null, "All Systems");
    private static readonly ColonizationProjectBuildOption AllProjectBuilds = new(
        ColonizationProjectBuildFilter.All,
        "All Projects"
    );
    private ColonizationProjectSystemOption projectSystemFilter = AllProjectSystems;
    private ColonizationProjectBuildOption projectBuildFilter = AllProjectBuilds;

    public IReadOnlyList<ColonizationProjectSystemOption> ProjectSystemOptions { get; private set; } =
    [AllProjectSystems];

    public IReadOnlyList<ColonizationProjectBuildOption> ProjectBuildOptions { get; } =
    [
        AllProjectBuilds,
        new(ColonizationProjectBuildFilter.Building, "Building"),
        new(ColonizationProjectBuildFilter.Planning, "Planning"),
    ];

    public IReadOnlyList<ColonizationProjectRowViewModel> VisibleProjects { get; private set; } = [];

    public bool HasNoMatchingProjects => HasProjects && VisibleProjects.Count == 0;

    public ColonizationProjectSystemOption? ProjectSystemFilter
    {
        get => projectSystemFilter;
        set
        {
            if (value is not null && ProjectSystemOptions.Contains(value) && SetField(ref projectSystemFilter, value))
            {
                ApplyProjectFilters();
            }
        }
    }

    public ColonizationProjectBuildOption? ProjectBuildFilter
    {
        get => projectBuildFilter;
        set
        {
            if (value is not null && ProjectBuildOptions.Contains(value) && SetField(ref projectBuildFilter, value))
            {
                ApplyProjectFilters();
            }
        }
    }

    private void UpdateProjectFilters()
    {
        ProjectSystemOptions =
        [
            AllProjectSystems,
            .. Projects
                .Select(row => row.SystemName.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .Select(name => new ColonizationProjectSystemOption(name, name.Length == 0 ? "Unknown system" : name)),
        ];
        projectSystemFilter =
            ProjectSystemOptions.FirstOrDefault(option =>
                string.Equals(option.SystemName, projectSystemFilter.SystemName, StringComparison.OrdinalIgnoreCase)
            ) ?? AllProjectSystems;
        OnPropertyChanged(nameof(ProjectSystemOptions));
        OnPropertyChanged(nameof(ProjectSystemFilter));
        ApplyProjectFilters();
    }

    private void ResetProjectFilters()
    {
        projectSystemFilter = AllProjectSystems;
        projectBuildFilter = AllProjectBuilds;
        OnPropertyChanged(nameof(ProjectBuildFilter));
    }

    private void ApplyProjectFilters()
    {
        VisibleProjects = Projects
            .Where(row =>
                (
                    projectSystemFilter.SystemName is null
                    || string.Equals(
                        row.SystemName.Trim(),
                        projectSystemFilter.SystemName,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                && (
                    projectBuildFilter.Value switch
                    {
                        ColonizationProjectBuildFilter.Planning => row.Project.IsPlanned,
                        ColonizationProjectBuildFilter.Building => !row.Project.IsPlanned && !row.Project.IsComplete,
                        _ => true,
                    }
                )
            )
            .ToArray();
        OnPropertyChanged(nameof(VisibleProjects));
        OnPropertyChanged(nameof(HasNoMatchingProjects));
    }
}

public sealed record ColonizationProjectSystemOption(string? SystemName, string Label);

public enum ColonizationProjectBuildFilter
{
    All,
    Building,
    Planning,
}

public sealed record ColonizationProjectBuildOption(ColonizationProjectBuildFilter Value, string Label);
