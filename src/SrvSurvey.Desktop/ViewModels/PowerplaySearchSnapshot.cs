using SrvSurvey.Core.Mining;

namespace SrvSurvey.Desktop.ViewModels;

public sealed record MeritStationBlockSnapshot(
    string Icon,
    string Heading,
    string Commodity,
    string Price,
    string Demand,
    string Arrival,
    string Updated,
    MeritCommodityLineViewModel[] Commodities
)
{
    public static MeritStationBlockSnapshot From(MeritStationBlockViewModel block) =>
        new(
            block.Icon,
            block.Heading,
            block.Commodity,
            block.Price,
            block.Demand,
            block.Arrival,
            block.Updated,
            block.AllCommodities.ToArray()
        );

    public MeritStationBlockViewModel Restore() => new(this);
}

public sealed record MeritSystemSnapshot(
    string Name,
    string Distance,
    double? DistanceLy,
    string StateIcon,
    string StateText,
    string Power,
    PowerplayPowerLineViewModel[] PowerLines,
    MeritLineViewModel[] AllRings,
    MeritLineViewModel[] BestRings,
    MeritLineViewModel[] AllStations,
    MeritLineViewModel[] BestStations,
    MeritStationBlockSnapshot[] StationBlocks,
    string FactionState
);

public sealed record AcquireMinerSnapshot(
    string Name,
    MeritLineViewModel[] Rings,
    string State,
    PowerplayPowerLineViewModel[] PowerLines,
    string Connector
)
{
    public static AcquireMinerSnapshot From(AcquireMinerViewModel row) =>
        new(row.Name, row.AllRingLines.ToArray(), row.State, row.PowerLines.ToArray(), row.Connector);

    public AcquireMinerViewModel Restore() => new(Name, Rings, State, PowerLines, Connector);
}

public sealed record AcquireResultSnapshot(
    string Target,
    string State,
    string Distance,
    MeritStationBlockSnapshot[] Stations,
    AcquireMinerSnapshot[] Miners
)
{
    public static AcquireResultSnapshot From(AcquireResultRowViewModel row) =>
        new(
            row.Target,
            row.State,
            row.Distance,
            row.AllStationBlocks.Select(MeritStationBlockSnapshot.From).ToArray(),
            row.Miners.Select(AcquireMinerSnapshot.From).ToArray()
        );

    public AcquireResultRowViewModel Restore() =>
        new(
            Target,
            State,
            Distance,
            Stations.Select(station => station.Restore()).ToArray(),
            Miners.Select(miner => miner.Restore()).ToArray()
        );
}

public sealed record PowerplaySearchSnapshot(
    MiningSearchPreferences Options,
    string Objective,
    string[] MiningTypes,
    string[] Minerals,
    string[] States,
    string Status,
    MeritSystemSnapshot[] MeritRows,
    AcquireResultSnapshot[] AcquireRows,
    SurfaceMiningSearchSnapshot? PlanetaryRows
)
{
    public DateTimeOffset? SavedAt { get; init; }
}
