using System.Windows.Input;
using SrvSurvey.Core.Mining;

namespace SrvSurvey.Desktop.ViewModels;

/// <summary>Groups ring Acquire targets that share mining systems without merging their station markets.</summary>
public sealed class PowerplayRingAcquireClusterViewModel : WorkspaceObservable
{
    private readonly PowerplayRingAcquireMiningNode[] miningSystems;
    private IReadOnlyList<PowerplayRingAcquireMiningNode> visibleMiningSystems = [];
    private string selectedSellSystem = "";
    private Action<PowerplayRingAcquireClusterViewModel>? onSelected;

    private PowerplayRingAcquireClusterViewModel(
        IReadOnlyList<AcquireResultRowViewModel> rows,
        ICommand sellDistanceSortCommand,
        ICommand bestStationSortCommand,
        string sellDistanceSortIndicator,
        string bestStationSortIndicator
    )
    {
        SellNodes = rows.Select(row => new PowerplayRingAcquireSellNode(
                row,
                () => Select(row),
                sellDistanceSortCommand,
                bestStationSortCommand,
                sellDistanceSortIndicator,
                bestStationSortIndicator
            ))
            .ToArray();
        miningSystems = BuildMiningSystems(rows);
        Select(rows[0]);
    }

    public IReadOnlyList<PowerplayRingAcquireSellNode> SellNodes { get; }
    public IReadOnlyList<PowerplayRingAcquireMiningNode> VisibleMiningSystems => visibleMiningSystems;
    public string SelectedSellSystem => selectedSellSystem;

    public static IReadOnlyList<PowerplayRingAcquireClusterViewModel> Group(
        IReadOnlyList<AcquireResultRowViewModel> rows,
        ICommand sellDistanceSortCommand,
        ICommand bestStationSortCommand,
        string sellDistanceSortIndicator,
        string bestStationSortIndicator
    )
    {
        int[] parents = Enumerable.Range(0, rows.Count).ToArray();
        var firstByMiningSystem = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < rows.Count; index++)
        {
            foreach (string system in rows[index].Miners.Select(miner => miner.Name))
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

        PowerplayRingAcquireClusterViewModel[] clusters = rows.Select(
                (row, index) => (Row: row, Index: index, Root: Root(parents, index))
            )
            .GroupBy(item => item.Root)
            .OrderBy(group => group.Min(item => item.Index))
            .Select(group => new PowerplayRingAcquireClusterViewModel(
                group.OrderBy(item => item.Index).Select(item => item.Row).ToArray(),
                sellDistanceSortCommand,
                bestStationSortCommand,
                sellDistanceSortIndicator,
                bestStationSortIndicator
            ))
            .ToArray();
        foreach (PowerplayRingAcquireClusterViewModel cluster in clusters)
        {
            cluster.onSelected = selected =>
            {
                foreach (
                    PowerplayRingAcquireClusterViewModel other in clusters.Where(other =>
                        !ReferenceEquals(other, selected)
                    )
                )
                {
                    other.Deselect();
                }
            };
        }

        foreach (PowerplayRingAcquireClusterViewModel cluster in clusters.Skip(1))
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

    private static PowerplayRingAcquireMiningNode[] BuildMiningSystems(IReadOnlyList<AcquireResultRowViewModel> rows) =>
        rows.SelectMany((row, index) => row.Miners.Select(miner => (Row: row, Index: index, Miner: miner)))
            .GroupBy(item => item.Miner.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => new PowerplayRingAcquireMiningNode(group.Key, group.ToArray()))
            .OrderBy(system => system.SellPosition)
            .ThenBy(system => system.System, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private void Select(AcquireResultRowViewModel row)
    {
        selectedSellSystem = row.Target;
        foreach (PowerplayRingAcquireSellNode sell in SellNodes)
        {
            sell.IsSelected = ReferenceEquals(sell.Row, row);
        }

        foreach (PowerplayRingAcquireMiningNode system in miningSystems)
        {
            system.Select(row.Target);
        }

        visibleMiningSystems = miningSystems
            .OrderByDescending(system => system.ConnectsTo(row.Target))
            .ThenBy(system => system.SellPosition)
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
        foreach (PowerplayRingAcquireSellNode sell in SellNodes)
        {
            sell.IsSelected = false;
        }

        foreach (PowerplayRingAcquireMiningNode system in miningSystems)
        {
            system.Select("");
        }

        visibleMiningSystems = miningSystems;
        Changed(nameof(VisibleMiningSystems));
        Changed(nameof(SelectedSellSystem));
    }
}

public sealed class PowerplayRingAcquireSellNode : WorkspaceObservable
{
    private readonly PowerplayRingAcquireStationNode[] stations;
    private IReadOnlyList<PowerplayRingAcquireStationNode> visibleStations;
    private bool isSelected;
    private bool showAllStations;

    public PowerplayRingAcquireSellNode(
        AcquireResultRowViewModel row,
        Action select,
        ICommand sellDistanceSortCommand,
        ICommand bestStationSortCommand,
        string sellDistanceSortIndicator,
        string bestStationSortIndicator
    )
    {
        Row = row;
        var available = row
            .Miners.SelectMany(miner => miner.AllRingLines)
            .Select(line => line.EffectiveCommodityCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        stations = row
            .AllStationBlocks.Select(block => new PowerplayRingAcquireStationNode(block, available))
            .ToArray();
        visibleStations = stations.Take(1).ToArray();
        SelectCommand = new WorkspaceCommand(select);
        ToggleStationsCommand = new WorkspaceCommand(ToggleStations);
        SellDistanceSortCommand = sellDistanceSortCommand;
        BestStationSortCommand = bestStationSortCommand;
        SellDistanceSortIndicator = sellDistanceSortIndicator;
        BestStationSortIndicator = bestStationSortIndicator;
    }

    public AcquireResultRowViewModel Row { get; }
    public string Title =>
        string.Join(
            " • ",
            new[] { Row.Target, Row.State, Row.FactionState }.Where(value => !string.IsNullOrWhiteSpace(value))
        );
    public string SelectionGlyph => IsSelected ? "▶" : "▷";
    public ICommand SelectCommand { get; }
    public ICommand ToggleStationsCommand { get; }
    public ICommand SellDistanceSortCommand { get; }
    public ICommand BestStationSortCommand { get; }
    public string SellDistanceSortIndicator { get; }
    public string BestStationSortIndicator { get; }
    public IReadOnlyList<PowerplayRingAcquireStationNode> VisibleStations => visibleStations;
    public bool CanToggleStations => stations.Length > 1;
    public string StationToggleLabel => showAllStations ? "Show less stations" : "Show all stations";

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

    private void ToggleStations()
    {
        showAllStations = !showAllStations;
        visibleStations = showAllStations ? stations : stations.Take(1).ToArray();
        Changed(nameof(VisibleStations));
        Changed(nameof(StationToggleLabel));
    }
}

public sealed class PowerplayRingAcquireStationNode : WorkspaceObservable
{
    private readonly AcquireQuoteViewModel[] quotes;
    private IReadOnlyList<AcquireQuoteViewModel> visibleQuotes;
    private bool showAll;

    public PowerplayRingAcquireStationNode(MeritStationBlockViewModel block, IReadOnlySet<string> available)
    {
        Heading = block.Heading;
        Arrival = block.Arrival;
        Updated = block.Updated;
        quotes = new[]
        {
            new AcquireQuoteViewModel(
                block.PrimaryQuote.Code,
                block.PrimaryQuote.Price,
                block.PrimaryQuote.Demand,
                !available.Contains(block.PrimaryQuote.Code),
                true
            ),
        }
            .Concat(
                block.AllCommodities.Select(quote => new AcquireQuoteViewModel(
                    quote.Code,
                    quote.Price,
                    quote.Demand,
                    !available.Contains(quote.Code),
                    true
                ))
            )
            .ToArray();
        visibleQuotes = quotes.Take(7).ToArray();
        ToggleCommand = new WorkspaceCommand(() =>
        {
            showAll = !showAll;
            visibleQuotes = showAll ? quotes : quotes.Take(7).ToArray();
            Changed(nameof(VisibleQuotes));
            Changed(nameof(ToggleLabel));
        });
    }

    public string Heading { get; }
    public string Arrival { get; }
    public string Updated { get; }
    public bool HasUpdated => Updated.Length > 0;
    public IReadOnlyList<AcquireQuoteViewModel> VisibleQuotes => visibleQuotes;
    public bool CanToggle => quotes.Length > 7;
    public string ToggleLabel => showAll ? "Show less" : "Show all";
    public ICommand ToggleCommand { get; }
}

public sealed class PowerplayRingAcquireMiningNode : WorkspaceObservable
{
    private readonly IReadOnlyList<(AcquireResultRowViewModel Row, int Index, AcquireMinerViewModel Miner)> sources;
    private readonly MeritLineViewModel[] ringLines;
    private MeritLineViewModel[] orderedRingLines;
    private IReadOnlyList<MeritLineViewModel> visibleRingLines;
    private bool isExpanded;
    private double emphasisOpacity = 1;

    internal PowerplayRingAcquireMiningNode(
        string system,
        IReadOnlyList<(AcquireResultRowViewModel Row, int Index, AcquireMinerViewModel Miner)> sources
    )
    {
        System = system;
        this.sources = sources;
        SellPosition = sources.Average(source => source.Index);
        ringLines = sources
            .SelectMany(source => source.Miner.AllRingLines)
            .DistinctBy(line => line.Text, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        orderedRingLines = ringLines;
        visibleRingLines = orderedRingLines.Take(1).ToArray();
        ToggleCommand = new WorkspaceCommand(() => IsExpanded = !IsExpanded);
    }

    public string System { get; }
    public double SellPosition { get; }
    public IReadOnlyList<MeritLineViewModel> VisibleRingLines => visibleRingLines;
    public bool HasAdditionalSignals => ringLines.Length > 1;
    public string Chevron => IsExpanded ? "▾" : "▸";
    public double EmphasisOpacity => emphasisOpacity;
    public ICommand ToggleCommand { get; }

    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            if (HasAdditionalSignals && Set(ref isExpanded, value))
            {
                RefreshVisibleRingLines();
                Changed(nameof(Chevron));
            }
        }
    }

    public bool ConnectsTo(string sellSystem) =>
        sources.Any(source => source.Row.Target.Equals(sellSystem, StringComparison.OrdinalIgnoreCase));

    internal void Select(string sellSystem)
    {
        emphasisOpacity = ConnectsTo(sellSystem) ? 1 : 0.28;
        Changed(nameof(EmphasisOpacity));
        string[] stationCodes = sources
            .Where(source => source.Row.Target.Equals(sellSystem, StringComparison.OrdinalIgnoreCase))
            .SelectMany(source => source.Row.AllStationBlocks)
            .SelectMany(block =>
                new[] { MiningCommodityCode.Abbreviate(block.Commodity) }.Concat(
                    block.AllCommodities.Select(quote => quote.Code)
                )
            )
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        orderedRingLines = ringLines
            .OrderBy(line =>
            {
                string code = line.EffectiveCommodityCode;
                int rank = Array.FindIndex(
                    stationCodes,
                    candidate => candidate.Equals(code, StringComparison.OrdinalIgnoreCase)
                );
                return rank < 0 ? int.MaxValue : rank;
            })
            .ToArray();
        RefreshVisibleRingLines();
    }

    private void RefreshVisibleRingLines()
    {
        visibleRingLines = isExpanded ? orderedRingLines : orderedRingLines.Take(1).ToArray();
        Changed(nameof(VisibleRingLines));
    }
}
