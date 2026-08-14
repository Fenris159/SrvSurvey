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

internal sealed record BoxelSurveyValueRequest(
    string PlanetClass,
    bool Terraformable,
    double MassEm,
    bool WasDiscovered,
    bool WasMapped,
    bool DssComplete,
    bool DssEfficiencyBonus,
    bool IsOdyssey);

internal static class BoxelSurveyValueCalculator
{
    public static (int Scan, int Current, int Mapped) Calculate(
        BoxelSurveyValueRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var shared = new ExplorationValueRequest
        {
            BodyClass = request.PlanetClass,
            IsTerraformable = request.Terraformable,
            Mass = request.MassEm,
            IsFirstDiscoverer = !request.WasDiscovered,
            IsFirstMapped = !request.WasMapped,
            IsOdyssey = request.IsOdyssey,
        };
        var scan = ExplorationValueCalculator.Calculate(
            CloneValueRequest(shared, isMapped: false, withEfficiencyBonus: false));
        var mapped = ExplorationValueCalculator.Calculate(
            CloneValueRequest(shared, isMapped: true, withEfficiencyBonus: true));
        var current = request.DssComplete
            ? ExplorationValueCalculator.Calculate(
                CloneValueRequest(
                    shared,
                    isMapped: true,
                    withEfficiencyBonus: request.DssEfficiencyBonus))
            : scan;
        return (scan, current, mapped);
    }

    private static ExplorationValueRequest CloneValueRequest(
        ExplorationValueRequest shared,
        bool isMapped,
        bool withEfficiencyBonus)
        => new()
        {
            BodyClass = shared.BodyClass,
            IsTerraformable = shared.IsTerraformable,
            Mass = shared.Mass,
            IsFirstDiscoverer = shared.IsFirstDiscoverer,
            IsMapped = isMapped,
            IsFirstMapped = shared.IsFirstMapped,
            IsOdyssey = shared.IsOdyssey,
            WithEfficiencyBonus = withEfficiencyBonus,
            IsFleetCarrierSale = shared.IsFleetCarrierSale,
        };
}
