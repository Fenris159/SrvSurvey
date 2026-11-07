using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Exploration.History;

public enum ExplorationDataStatus
{
    Unsold,
    Sold,
    Lost,
}

[Flags]
public enum ExplorationNotables
{
    None = 0,
    EarthLikeWorld = 1,
    WaterWorld = 2,
    AmmoniaWorld = 4,
    TerraformableWorld = 8,
    GreenGasGiant = 16,
    StellarPhenomena = 32,
    NeutronStar = 64,
    BlackHole = 128,
}

public sealed record ExplorationHistoryQuery(
    ExplorationDataStatus? Status = null,
    string SearchText = "",
    int Offset = 0,
    int PageSize = 50,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ThroughUtc = null,
    ExplorationHistoryAnchor? SelectionAnchor = null
);

public sealed record ExplorationHistorySummary
{
    public long UnsoldValue { get; init; }
    public long SoldValue { get; init; }
    public long LostValue { get; init; }
    public long SaleEarnings { get; init; }
    public int UnsoldSystems { get; init; }
    public int SoldSystems { get; init; }
    public int LostSystems { get; init; }
    public int SourceFiles { get; init; }
    public bool HasCommanderHistory { get; init; }
    public bool IsComplete { get; init; }
    public string? Notice { get; init; }
}

/// <summary>A retained original observation identifies its collection independently of replayed database IDs and dates.</summary>
public sealed record ExplorationHistoryAnchor(string SourceName, long Offset, string Hash);

public sealed record ExplorationHistoryRow
{
    public long Id { get; init; }
    public ExplorationHistoryAnchor? Anchor { get; init; }
    public string SystemName { get; init; } = string.Empty;
    public long? SystemAddress { get; init; }
    public double? X { get; init; }
    public double? Y { get; init; }
    public double? Z { get; init; }
    public DateTimeOffset? FirstCollected { get; init; }
    public DateTimeOffset? LastCollected { get; init; }
    public DateTimeOffset? SettledAt { get; init; }
    public ExplorationDataStatus Status { get; init; }
    public long EstimatedValue { get; init; }
    public int ScannedBodies { get; init; }
    public int MappedBodies { get; init; }
    public ExplorationNotables Notables { get; init; }
}

public sealed record ExplorationHistoryPage(
    IReadOnlyList<ExplorationHistoryRow> Rows,
    int TotalRows,
    int Offset,
    int PageSize,
    ExplorationHistorySummary Summary,
    long? SelectedRowId = null
);

public sealed record ExplorationHistoryDetails(
    long RowId,
    IReadOnlyList<JournalEventEnvelope> Events,
    string? Notice = null
);

public sealed record ExplorationHistoryProgress(int ProcessedFiles, int TotalFiles, string CurrentFile);
