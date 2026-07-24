using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Journeys;

public sealed record JourneyDocument(
    string FileName,
    string FilePath,
    string FrontierId,
    string CommanderName,
    string Name,
    string Description,
    string StartingJournal,
    DateTimeOffset StartTime,
    DateTimeOffset? EndTime,
    DateTimeOffset Watermark,
    IReadOnlyList<JourneySystemVisit> VisitedSystems)
{
    public bool IsActive => EndTime is null;

    public JourneySystemVisit? CurrentSystem => VisitedSystems
        .LastOrDefault(visit => visit.Departed is null);
}

public sealed record JourneySystemVisit(
    JourneySystemReference StarSystem,
    DateTimeOffset Arrived,
    DateTimeOffset? Departed,
    JourneyCounts Counts,
    IReadOnlyDictionary<string, int>? LandedOn,
    IReadOnlySet<long>? CodexScanned,
    IReadOnlySet<int>? BodiesScanned,
    IReadOnlySet<string>? CodexNew,
    IReadOnlyDictionary<string, int>? SubCategories,
    IReadOnlyDictionary<string, int>? SurfaceSignals,
    IReadOnlyDictionary<string, int>? FssSignals)
{
    public bool HasCompletedFss => Counts.BodyCount > 0
        && Counts.BodyScans >= Counts.BodyCount;
}

public sealed record JourneySystemReference(
    string Name,
    long SystemAddress,
    GalacticCoordinate Position)
{
    public double DistanceTo(JourneySystemReference other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Position.DistanceTo(other.Position);
    }
}

public sealed record JourneyCounts(
    int BodyScans,
    int DetailedSurfaceScans,
    int NewCodexEntries,
    int Organisms,
    int Touchdowns,
    int BodyCount,
    int Screenshots,
    int Notes,
    int ExobiologyRewards,
    int ExplorationRewards,
    int Stars)
{
    public static JourneyCounts Empty { get; } = new(
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0);

    public static JourneyCounts operator +(
        JourneyCounts left,
        JourneyCounts right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return new JourneyCounts(
            left.BodyScans + right.BodyScans,
            left.DetailedSurfaceScans + right.DetailedSurfaceScans,
            left.NewCodexEntries + right.NewCodexEntries,
            left.Organisms + right.Organisms,
            left.Touchdowns + right.Touchdowns,
            left.BodyCount + right.BodyCount,
            left.Screenshots + right.Screenshots,
            left.Notes + right.Notes,
            left.ExobiologyRewards + right.ExobiologyRewards,
            left.ExplorationRewards + right.ExplorationRewards,
            left.Stars + right.Stars);
    }
}

public sealed record JourneyCreationRequest(
    string FrontierId,
    string CommanderName,
    string Name,
    string Description,
    string StartingJournal,
    DateTimeOffset StartingEventTimestamp);

public sealed record JourneyLoadResult(
    string Path,
    bool Exists,
    JourneyDocument? Journey,
    string? Error)
{
    public bool IsSuccess => Journey is not null;
}

public sealed record JourneyCatalogResult(
    IReadOnlyList<JourneyDocument> Journeys,
    IReadOnlyList<string> Errors);
