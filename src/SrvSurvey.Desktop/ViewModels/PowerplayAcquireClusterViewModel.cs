using System.Windows.Input;

namespace SrvSurvey.Desktop.ViewModels;

/// <summary>
/// A display projection of independently scored Acquire sell systems. Shared mining
/// systems are shown once without changing either sell system's station quotes.
/// </summary>
public sealed class PowerplayAcquireClusterViewModel : WorkspaceObservable
{
    private readonly PowerplayAcquireMiningNode[] miningSystems;
    private IReadOnlyList<PowerplayAcquireMiningNode> visibleMiningSystems = [];
    private bool hideIrrelevant;
    private string selectedSellSystem = "";
    private Action<PowerplayAcquireClusterViewModel>? onSelected;

    private PowerplayAcquireClusterViewModel(
        IReadOnlyList<SurfaceSellRowViewModel> rows,
        ICommand? sellDistanceSortCommand,
        ICommand? bestStationSortCommand,
        string sellDistanceSortIndicator,
        string bestStationSortIndicator
    )
    {
        PrimaryRow = rows[0];
        SellNodes = rows.Select(row => new PowerplayAcquireSellNode(
                row,
                () => Select(row),
                sellDistanceSortCommand,
                bestStationSortCommand,
                sellDistanceSortIndicator,
                bestStationSortIndicator
            ))
            .ToArray();
        miningSystems = BuildMiningSystems(rows);
        Select(PrimaryRow);
    }

    public SurfaceSellRowViewModel PrimaryRow { get; }
    public bool IsShared => SellNodes.Count > 1;
    public bool IsSingle => !IsShared;
    public IReadOnlyList<PowerplayAcquireSellNode> SellNodes { get; }
    public IReadOnlyList<PowerplayAcquireMiningNode> MiningSystems => miningSystems;
    public IReadOnlyList<PowerplayAcquireMiningNode> VisibleMiningSystems => visibleMiningSystems;
    public string SelectedSellSystem => selectedSellSystem;

    public void SetHideIrrelevant(bool hide)
    {
        hideIrrelevant = hide;
        foreach (PowerplayAcquireMiningNode system in miningSystems)
        {
            system.SetHideIrrelevant(hide);
        }
    }

    public static IReadOnlyList<PowerplayAcquireClusterViewModel> Group(
        IReadOnlyList<SurfaceSellRowViewModel> rows,
        ICommand? sellDistanceSortCommand = null,
        ICommand? bestStationSortCommand = null,
        string sellDistanceSortIndicator = "",
        string bestStationSortIndicator = ""
    )
    {
        int[] parents = Enumerable.Range(0, rows.Count).ToArray();
        var firstByMiningSystem = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < rows.Count; index++)
        {
            foreach (string system in rows[index].Systems.Select(item => item.System))
            {
                if (firstByMiningSystem.TryGetValue(system, out int first))
                {
                    Join(parents, first, index);
                }
                else
                {
                    firstByMiningSystem.Add(system, index);
                }
            }
        }

        PowerplayAcquireClusterViewModel[] clusters = rows.Select(
                (row, index) => (Row: row, Index: index, Root: Root(parents, index))
            )
            .GroupBy(item => item.Root)
            .OrderBy(group => group.Min(item => item.Index))
            .Select(group => new PowerplayAcquireClusterViewModel(
                group.OrderBy(item => item.Index).Select(item => item.Row).ToArray(),
                sellDistanceSortCommand,
                bestStationSortCommand,
                sellDistanceSortIndicator,
                bestStationSortIndicator
            ))
            .ToArray();
        foreach (PowerplayAcquireClusterViewModel cluster in clusters)
        {
            cluster.onSelected = selected =>
            {
                foreach (
                    PowerplayAcquireClusterViewModel other in clusters.Where(other => !ReferenceEquals(other, selected))
                )
                {
                    other.Deselect();
                }
            };
        }

        foreach (PowerplayAcquireClusterViewModel cluster in clusters.Skip(1))
        {
            cluster.Deselect();
        }

        return clusters;
    }

    private static int Root(int[] parents, int index)
    {
        while (parents[index] != index)
        {
            parents[index] = parents[parents[index]];
            index = parents[index];
        }

        return index;
    }

    private static void Join(int[] parents, int first, int second) =>
        parents[Root(parents, second)] = Root(parents, first);

    private static PowerplayAcquireMiningNode[] BuildMiningSystems(IReadOnlyList<SurfaceSellRowViewModel> rows) =>
        rows.SelectMany((row, index) => row.Systems.Select(system => (Row: row, Index: index, System: system)))
            .GroupBy(item => item.System.System, StringComparer.OrdinalIgnoreCase)
            .Select(group => new PowerplayAcquireMiningNode(group.Key, group.ToArray()))
            .OrderBy(system => system.SellPosition)
            .ThenBy(system => system.DistanceLy)
            .ThenBy(system => system.System, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private void Select(SurfaceSellRowViewModel row)
    {
        selectedSellSystem = row.Target;
        foreach (PowerplayAcquireSellNode sell in SellNodes)
        {
            sell.IsSelected = ReferenceEquals(sell.Row, row);
        }

        foreach (PowerplayAcquireMiningNode system in miningSystems)
        {
            system.Select(row.Target, hideIrrelevant);
        }

        visibleMiningSystems = miningSystems
            .OrderByDescending(system => system.ConnectsTo(row.Target))
            .ThenBy(system => system.ConnectsTo(row.Target) ? system.DistanceLy : system.SellPosition)
            .ThenBy(system => system.System, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Changed(nameof(VisibleMiningSystems));
        Changed(nameof(SelectedSellSystem));
        onSelected?.Invoke(this);
    }

    private void Deselect()
    {
        if (selectedSellSystem.Length == 0)
        {
            return;
        }

        selectedSellSystem = "";
        foreach (PowerplayAcquireSellNode sell in SellNodes)
        {
            sell.IsSelected = false;
        }

        foreach (PowerplayAcquireMiningNode system in miningSystems)
        {
            system.Select("", hideIrrelevant);
        }

        visibleMiningSystems = miningSystems;
        Changed(nameof(VisibleMiningSystems));
        Changed(nameof(SelectedSellSystem));
    }
}

public sealed class PowerplayAcquireSellNode(
    SurfaceSellRowViewModel row,
    Action select,
    ICommand? sellDistanceSortCommand,
    ICommand? bestStationSortCommand,
    string sellDistanceSortIndicator,
    string bestStationSortIndicator
) : WorkspaceObservable
{
    private bool isSelected;

    public SurfaceSellRowViewModel Row { get; } = row;
    public ICommand SelectCommand { get; } = new WorkspaceCommand(select);
    public string SelectionGlyph => IsSelected ? "▶" : "▷";
    public ICommand? SellDistanceSortCommand { get; } = sellDistanceSortCommand;
    public ICommand? BestStationSortCommand { get; } = bestStationSortCommand;
    public string SellDistanceSortIndicator { get; } = sellDistanceSortIndicator;
    public string BestStationSortIndicator { get; } = bestStationSortIndicator;
    public string Title =>
        string.Join(
            " • ",
            new[] { Row.Target, Row.PowerState, Row.FactionState }.Where(value => !string.IsNullOrWhiteSpace(value))
        );

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (Set(ref isSelected, value))
            {
                Changed(nameof(SelectionGlyph));
            }
        }
    }
}

public sealed class PowerplayAcquireMiningNode : WorkspaceObservable
{
    private readonly IReadOnlyList<(
        SurfaceSellRowViewModel Row,
        int Index,
        SurfaceMiningSystemRowViewModel System
    )> sources;
    private bool isExpanded;
    private SurfaceBodyLine[] bodies = [];
    private IReadOnlyList<SurfaceBodyLine> visibleBodies = [];
    private double emphasisOpacity = 1;

    internal PowerplayAcquireMiningNode(
        string system,
        IReadOnlyList<(SurfaceSellRowViewModel Row, int Index, SurfaceMiningSystemRowViewModel System)> sources
    )
    {
        System = system;
        this.sources = sources;
        SellPosition = sources.Average(source => source.Index);
        DistanceLy = sources.Min(source => source.System.DistanceLy);
        SellCount = sources.Select(source => source.Row.Target).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        ToggleCommand = new WorkspaceCommand(() => IsExpanded = !IsExpanded);
    }

    public string System { get; }
    public double SellPosition { get; }
    public double DistanceLy { get; }
    public int SellCount { get; }
    public IReadOnlyList<SurfaceBodyLine> Bodies => bodies;
    public IReadOnlyList<SurfaceBodyLine> VisibleBodies => visibleBodies;
    public bool HasAdditionalBodies => bodies.Length > 1;
    public string Chevron => IsExpanded ? "▾" : "▸";
    public double EmphasisOpacity => emphasisOpacity;
    public ICommand ToggleCommand { get; }

    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            if (HasAdditionalBodies && Set(ref isExpanded, value))
            {
                RefreshVisibleBodies();
                Changed(nameof(Chevron));
            }
        }
    }

    public bool ConnectsTo(string sellSystem) =>
        sources.Any(source => source.Row.Target.Equals(sellSystem, StringComparison.OrdinalIgnoreCase));

    internal void Select(string sellSystem, bool hideIrrelevant)
    {
        emphasisOpacity = ConnectsTo(sellSystem) ? 1 : 0.28;
        Changed(nameof(EmphasisOpacity));
        bodies = sources
            .SelectMany(source => source.System.Bodies.Select(body => (source.Row.Target, Body: body)))
            .GroupBy(item => item.Body.Details, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                string[] codes = group
                    .SelectMany(item => item.Body.Codes)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                string[] matching = group
                    .Where(item => item.Target.Equals(sellSystem, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(item => item.Body.Tags)
                    .Where(tag => tag.MatchesStation)
                    .Select(tag => tag.Code)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var line = new SurfaceBodyLine(codes, group.Key, matching.ToHashSet(StringComparer.OrdinalIgnoreCase));
                foreach (SurfaceBodyTag tag in line.Tags)
                {
                    tag.SetHideIrrelevant(hideIrrelevant);
                }
                return line;
            })
            .ToArray();
        Changed(nameof(Bodies));
        Changed(nameof(HasAdditionalBodies));
        RefreshVisibleBodies();
    }

    internal void SetHideIrrelevant(bool hide)
    {
        foreach (SurfaceBodyTag tag in bodies.SelectMany(body => body.Tags))
        {
            tag.SetHideIrrelevant(hide);
        }
    }

    private void RefreshVisibleBodies()
    {
        visibleBodies = isExpanded ? bodies : bodies.Take(1).ToArray();
        Changed(nameof(VisibleBodies));
    }
}
