using SrvSurvey.Core.Exploration;

namespace SrvSurvey.Core.Search;

public sealed record BoxelSurveyStatsCatalog(
    string FrontierId,
    int SchemaVersion,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<BoxelSurveyIndexEntry> Index)
{
    public const int CurrentSchemaVersion = 1;

    public static BoxelSurveyStatsCatalog Empty(string frontierId)
        => new(frontierId, CurrentSchemaVersion, DateTimeOffset.MinValue, []);
}

public sealed record BoxelSurveyIndexEntry(
    string Prefix,
    char MassCode,
    long? BoxelId64,
    DateTimeOffset? LastVisited,
    int VisitedSystemCount,
    int ImpliedPopulation,
    int FssCompleteCount,
    int NavBeaconCount,
    double? MinHeliumPercent,
    double? MaxHeliumPercent,
    long CurrentValue,
    long MappedPotentialValue);

public sealed record BoxelSurveyBoxelDocument(
    string Prefix,
    long? BoxelId64,
    DateTimeOffset? LastVisited,
    double? MinHeliumPercent,
    double? MaxHeliumPercent,
    IReadOnlyList<BoxelSurveySystemContribution> Systems);

public sealed record BoxelSurveySystemContribution(
    string GeneratedName,
    long SystemAddress,
    int N2,
    DateTimeOffset? LastVisited,
    int FssDiscoveryBodyCount,
    bool AllBodiesFound,
    bool NavBeaconScanned,
    double? MinHeliumPercent,
    double? MaxHeliumPercent,
    long ScanValue,
    long CurrentValue,
    long MappedPotentialValue,
    IReadOnlyList<BoxelSurveyBodyContribution> Bodies);

public sealed record BoxelSurveyBodyContribution(
    int BodyId,
    BoxelPlanetClass Class,
    bool Terraformable,
    bool Landable,
    bool Atmospheric,
    double MassEm,
    double? HeliumPercent,
    int ScanValue,
    int CurrentValue,
    int MappedPotentialValue,
    bool WasDiscovered = false,
    bool WasMapped = false,
    bool DssComplete = false,
    bool DssEfficiencyBonus = false);

public sealed record BoxelSurveyClassCounts(
    int Count,
    int Terraformable,
    int Landable,
    int Atmospheric)
{
    public static BoxelSurveyClassCounts Zero { get; } = new(0, 0, 0, 0);

    public BoxelSurveyClassCounts Add(BoxelSurveyClassCounts other)
        => new(
            Count + other.Count,
            Terraformable + other.Terraformable,
            Landable + other.Landable,
            Atmospheric + other.Atmospheric);

    public BoxelSurveyClassCounts AddBody(
        bool terraformable,
        bool landable,
        bool atmospheric)
        => new(
            Count + 1,
            Terraformable + (terraformable ? 1 : 0),
            Landable + (landable ? 1 : 0),
            Atmospheric + (atmospheric ? 1 : 0));
}

public sealed record BoxelSurveyBoxelSnapshot(
    string Prefix,
    char MassCode,
    long? BoxelId64,
    DateTimeOffset? LastVisited,
    int Visited,
    int ImpliedPopulation,
    int FssCompleteCount,
    int NavBeaconCount,
    int FssDiscoveryBodyCountSum,
    double? MinHeliumPercent,
    double? MaxHeliumPercent,
    long ScanValue,
    long CurrentValue,
    long MappedPotentialValue,
    int OtherTerraformableCount,
    IReadOnlyDictionary<BoxelPlanetClass, BoxelSurveyClassCounts> Classes,
    IReadOnlyList<BoxelSurveySystemContribution> Systems)
{
    public static BoxelSurveyBoxelSnapshot Empty { get; } = new(
        string.Empty,
        BoxelAddress.MinimumMassCode,
        null,
        null,
        0,
        0,
        0,
        0,
        0,
        null,
        null,
        0,
        0,
        0,
        0,
        new Dictionary<BoxelPlanetClass, BoxelSurveyClassCounts>(),
        []);

    public double? BodyAverage => Visited <= 0
        ? null
        : FssDiscoveryBodyCountSum / (double)Visited;

    public double? ValuePerSystem => Visited <= 0
        ? null
        : CurrentValue / (double)Visited;

    public BoxelSurveyClassCounts CountsOf(BoxelPlanetClass classified)
        => Classes.TryGetValue(classified, out var counts)
            ? counts
            : BoxelSurveyClassCounts.Zero;

    public BoxelSurveyIndexEntry ToIndexEntry()
        => new(
            Prefix,
            MassCode,
            BoxelId64,
            LastVisited,
            Visited,
            ImpliedPopulation,
            FssCompleteCount,
            NavBeaconCount,
            MinHeliumPercent,
            MaxHeliumPercent,
            CurrentValue,
            MappedPotentialValue);
}

internal static class BoxelSurveyValueCalculator
{
    public static (int Scan, int Current, int Mapped) Calculate(
        string planetClass,
        bool terraformable,
        double massEm,
        bool wasDiscovered,
        bool wasMapped,
        bool dssComplete,
        bool dssEfficiencyBonus,
        bool isOdyssey)
    {
        var scan = ExplorationValueCalculator.Calculate(
            new ExplorationValueRequest
            {
                BodyClass = planetClass,
                IsTerraformable = terraformable,
                Mass = massEm,
                IsFirstDiscoverer = !wasDiscovered,
                IsMapped = false,
                IsFirstMapped = !wasMapped,
                IsOdyssey = isOdyssey,
                WithEfficiencyBonus = false,
            });
        var mapped = ExplorationValueCalculator.Calculate(
            new ExplorationValueRequest
            {
                BodyClass = planetClass,
                IsTerraformable = terraformable,
                Mass = massEm,
                IsFirstDiscoverer = !wasDiscovered,
                IsMapped = true,
                IsFirstMapped = !wasMapped,
                IsOdyssey = isOdyssey,
                WithEfficiencyBonus = true,
            });
        var current = dssComplete
            ? ExplorationValueCalculator.Calculate(
                new ExplorationValueRequest
                {
                    BodyClass = planetClass,
                    IsTerraformable = terraformable,
                    Mass = massEm,
                    IsFirstDiscoverer = !wasDiscovered,
                    IsMapped = true,
                    IsFirstMapped = !wasMapped,
                    IsOdyssey = isOdyssey,
                    WithEfficiencyBonus = dssEfficiencyBonus,
                })
            : scan;
        return (scan, current, mapped);
    }
}
