using System.Globalization;
using System.Windows.Input;
using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Desktop.ViewModels;

/// <summary>A read-only Nexus snapshot with a route-wide or individual-system cargo/build scope.</summary>
public sealed class ColonizationNexusDetailViewModel : WorkspaceObservable
{
    private ColonizationNexusScope selectedScope;
    private ColonizationNexusScope? selectedSystem;

    public ColonizationNexusDetailViewModel(
        ColonizationNexusPlan plan,
        ColonizationBuildCatalog catalog,
        Action<ColonizationProject> openProject,
        long selectedSystemAddress = 0,
        Action<long>? openReport = null,
        long selectedHubAddress = 0
    )
    {
        Nexus = plan.Nexus;
        Cargo = plan.Cargo;
        HubCount = plan.Systems.Count(system => system.IsHub);
        CompletedSystems = plan.CompletedSystems;
        RouteSummary = $"{plan.CompletedSystems:N0} of {plan.Systems.Count:N0} systems complete";
        HasIncompleteRequirements = plan.HasIncompleteRequirements;
        Members = new[] { Nexus.Owner }
            .Concat(Nexus.Commanders)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name =>
                string.Equals(name, Nexus.Owner, StringComparison.OrdinalIgnoreCase) ? $"{name} (owner)" : name
            )
            .ToArray();
        Systems = plan
            .Systems.Select(
                (system, index) =>
                    new ColonizationNexusScope(
                        system.System.Name,
                        system.Cargo,
                        system.Needs,
                        system
                            .System.Builds.Select(build => new ColonizationNexusBuildRow(
                                build,
                                system.System.Name,
                                catalog,
                                openProject,
                                system.System.SystemAddress
                            ))
                            .ToArray(),
                        () => SelectedScope = Systems[index],
                        openReport is null ? null : () => openReport(system.System.SystemAddress)
                    )
                    {
                        SystemAddress = system.System.SystemAddress,
                        SystemName = system.System.Name,
                        Kind = system.IsHub ? "Hub" : "System",
                        Nickname = system.System.Nickname,
                    }
            )
            .ToArray();
        Carriers = CreateCarriers(plan, Systems);
        var all = new ColonizationNexusScope(
            "All systems",
            plan.Cargo,
            plan.Needs,
            Systems.SelectMany(system => system.Builds).DistinctBy(build => build.Project.BuildId).ToArray(),
            () => SelectedScope = Scopes[0],
            openReport is null ? null : () => openReport(0)
        );
        Scopes = new[] { all }.Concat(Systems).ToArray();
        selectedScope = Scopes.FirstOrDefault(scope => scope.SystemAddress == selectedSystemAddress) ?? all;
        selectedSystem =
            Systems.FirstOrDefault(system => system.SystemAddress == selectedHubAddress)
            ?? Systems.FirstOrDefault(system => system.SystemAddress == selectedSystemAddress)
            ?? (Systems.Count > 0 ? Systems[0] : null);
    }

    public ColonizationNexus Nexus { get; }
    public ColonizationNexusCargo Cargo { get; }
    public bool HasNotes => !string.IsNullOrWhiteSpace(Nexus.Notes);
    public string Sharing => Nexus.Open ? "Public Nexus" : "Private Nexus";
    public string RouteSummary { get; }
    public int HubCount { get; }
    public int CompletedSystems { get; }
    public double RouteCompletionPercent => Systems.Count > 0 ? CompletedSystems * 100d / Systems.Count : 0;
    public bool HasIncompleteRequirements { get; }
    public IReadOnlyList<string> Members { get; }
    public IReadOnlyList<ColonizationNexusCarrierRow> Carriers { get; }
    public bool HasNoCarriers => Carriers.Count == 0;
    public bool HasSelectedSystem => SelectedSystem is not null;
    public IReadOnlyList<ColonizationNexusScope> Systems { get; } = [];
    public IReadOnlyList<ColonizationNexusScope> Scopes { get; } = [];
    public ICommand OpenAllReportCommand => Scopes[0].OpenReportCommand;
    public string CargoSummary =>
        $"{Cargo.Delivered:N0} t delivered · {Cargo.Remaining:N0} t remaining · {Cargo.Ready:N0} t on Nexus carriers";
    public string ProgressLabel =>
        Cargo.Total > 0 ? $"{Cargo.DeliveredPercent:N2}% delivered" : "Requirements not reported";

    public ColonizationNexusScope SelectedScope
    {
        get => selectedScope;
        set
        {
            if (Scopes.Contains(value) && Set(ref selectedScope, value) && Systems.Contains(value))
            {
                SelectedSystem = value;
            }
        }
    }

    public ColonizationNexusScope? SelectedSystem
    {
        get => selectedSystem;
        set
        {
            if (value is not null && Systems.Contains(value) && Set(ref selectedSystem, value))
            {
                SelectedScope = value;
                Changed(nameof(HasSelectedSystem));
            }
        }
    }

    private static ColonizationNexusCarrierRow[] CreateCarriers(
        ColonizationNexusPlan plan,
        IReadOnlyList<ColonizationNexusScope> systems
    )
    {
        var builds = systems.ToDictionary(system => system.SystemAddress, system => system.Builds);
        return plan
            .Nexus.FleetCarriers.Select(carrier => new ColonizationNexusCarrierRow(
                carrier,
                plan.Systems.Where(system => system.System.FleetCarrierIds.Contains(carrier.MarketId))
                    .Select(system => new ColonizationNexusSystemLink(system.System.Name)
                    {
                        Builds = builds[system.System.SystemAddress]
                            .Where(build => !build.Project.IsComplete)
                            .ToArray(),
                    })
                    .ToArray()
            ))
            .ToArray();
    }

    internal static string BuildName(ColonizationBuildCatalog catalog, string type)
    {
        IReadOnlyList<ColonizationBuildCost> layouts = catalog.FindByLayout(type);
        return (layouts.Count > 0 ? layouts[0] : catalog.FindByBuildType(type))?.DisplayName ?? type;
    }
}

public sealed class ColonizationNexusScope
{
    public ColonizationNexusScope(
        string name,
        ColonizationNexusCargo cargo,
        IReadOnlyList<ColonizationNexusNeed> needs,
        IReadOnlyList<ColonizationNexusBuildRow> builds,
        Action select,
        Action? openReport = null
    )
    {
        Name = name;
        Cargo = cargo;
        Needs = needs
            .Select(need => new ColonizationNexusNeedRow(
                ColonizationShopping.DisplayName(need.Commodity),
                need.Required,
                need.Ready,
                need.Missing
            ))
            .ToArray();
        Builds = builds;
        SelectCommand = new WorkspaceCommand(select);
        OpenReportCommand = new WorkspaceCommand(() => openReport?.Invoke(), () => openReport is not null);
    }

    public string Name { get; }
    public long SystemAddress { get; init; }
    public string SystemName { get; init; } = string.Empty;
    public Uri? SystemUri =>
        SystemAddress > 0
            ? new Uri(RavenColonialClient.WebsiteUri, $"#sys={SystemAddress.ToString(CultureInfo.InvariantCulture)}")
            : null;
    public string Kind { get; init; } = "Route";
    public string? Nickname { get; init; }
    public ColonizationNexusCargo Cargo { get; }
    public IReadOnlyList<ColonizationNexusNeedRow> Needs { get; }
    public IReadOnlyList<ColonizationNexusBuildRow> Builds { get; }
    public ICommand SelectCommand { get; }
    public ICommand OpenReportCommand { get; }
    public bool HasNoBuilds => Builds.Count == 0;
    public string ProgressLabel
    {
        get
        {
            if (Cargo.IsComplete)
            {
                return "Complete";
            }
            return Cargo.Total > 0 ? $"{Cargo.DeliveredPercent:N2}% delivered" : "Requirements not reported";
        }
    }
    public string CargoSummary =>
        $"{Cargo.Remaining:N0} t remaining · {Cargo.Ready:N0} t ready · {Math.Max(0, Cargo.Remaining - Cargo.Ready):N0} t still to load";
    public string EmptyNeedsMessage
    {
        get
        {
            if (Needs.Count > 0)
            {
                return string.Empty;
            }
            return Cargo.IsComplete
                ? "All reported requirements have been delivered."
                : "No commodity requirements were reported for this selection.";
        }
    }
}

public sealed record ColonizationNexusNeedRow(string Commodity, long Required, long Ready, long Missing);

public sealed class ColonizationNexusBuildRow
{
    public ColonizationNexusBuildRow(
        ColonizationProject project,
        string system,
        ColonizationBuildCatalog catalog,
        Action<ColonizationProject> open,
        long systemAddress = 0
    )
    {
        Project = project;
        System = system;
        SystemAddress = project.SystemAddress > 0 ? project.SystemAddress : systemAddress;
        Type = ColonizationNexusDetailViewModel.BuildName(catalog, project.BuildType);
        OpenCommand = new WorkspaceCommand(() => open(project));
    }

    public ColonizationProject Project { get; }
    public string System { get; }
    public long SystemAddress { get; }
    public Uri? SystemUri =>
        SystemAddress > 0
            ? new Uri(RavenColonialClient.WebsiteUri, $"#sys={SystemAddress.ToString(CultureInfo.InvariantCulture)}")
            : null;
    public Uri BuildUri => new(RavenColonialClient.WebsiteUri, $"#build={Uri.EscapeDataString(Project.BuildId)}");
    public string Type { get; }
    public ICommand OpenCommand { get; }
    public double DeliveredPercent => (Project.Progress ?? 0) * 100;
    public string ProgressLabel
    {
        get
        {
            if (Project.IsComplete)
            {
                return "Complete";
            }
            return Project.Progress.HasValue
                ? $"{DeliveredPercent:N2}% delivered · {Project.RemainingRequired:N0} t remaining"
                : "Progress not reported";
        }
    }
    public string Commanders => string.Join(", ", (Project.Commanders ?? []).Keys);
}

public sealed class ColonizationNexusCarrierRow(
    ColonizationFleetCarrier carrier,
    IReadOnlyList<ColonizationNexusSystemLink> systems
)
{
    public string Name =>
        string.IsNullOrWhiteSpace(carrier.DisplayName) || carrier.DisplayName == carrier.Name
            ? carrier.Name
            : $"{carrier.DisplayName} ({carrier.Name})";
    public string Owner => carrier.Owner ?? "Owner not reported";
    public string Location => carrier.SystemName ?? "Location not reported";
    public bool HasLocation => !string.IsNullOrWhiteSpace(carrier.SystemName);
    public Uri? SystemUri =>
        HasLocation
            ? new Uri(RavenColonialClient.WebsiteUri, $"#sys={Uri.EscapeDataString(carrier.SystemName!)}")
            : null;
    public IReadOnlyList<ColonizationNexusSystemLink> SystemLinks { get; } = systems;
    public bool HasNoAssignments => SystemLinks.Count == 0;
    public string Assignments =>
        SystemLinks.Count > 0 ? string.Join(", ", SystemLinks.Select(system => system.Name)) : "No system assignments";
    public string Updated =>
        carrier.LastRefresh is { } time ? $"Cargo updated {time.LocalDateTime:g}" : "Cargo update time not reported";
}

public sealed record ColonizationNexusSystemLink(string Name)
{
    public IReadOnlyList<ColonizationNexusBuildRow> Builds { get; init; } = [];
    public Uri SystemUri => new(RavenColonialClient.WebsiteUri, $"#sys={Uri.EscapeDataString(Name)}");
}
