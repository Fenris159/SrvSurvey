namespace SrvSurvey.Desktop.ViewModels;

public sealed record SurfaceMiningSearchSnapshot(
    string Reference,
    double Radius,
    int ResultLimit,
    string[] Materials,
    string PadSize,
    long MinimumDemand,
    long MaximumDemand,
    string Status,
    SurfaceSellSnapshot[] Rows
)
{
    public DateTimeOffset? SavedAt { get; init; }
    public bool ForceIncludeReference { get; init; }
    public double MineSellRadius { get; init; } = 50;
    public int SurfaceSearchVersion { get; init; }
}

public sealed record SurfaceSellSnapshot(
    string Target,
    string Distance,
    AcquireStationViewModel[] Stations,
    SurfaceMiningSystemSnapshot[] Systems,
    double ReferenceDistanceLy,
    long BestViablePrice,
    long[] StationScores,
    SurfaceSellSystemDetails? Details
)
{
    public static SurfaceSellSnapshot From(SurfaceSellRowViewModel row) =>
        new(
            row.Target,
            row.Distance,
            row.Stations.ToArray(),
            row.Systems.Select(SurfaceMiningSystemSnapshot.From).ToArray(),
            row.ReferenceDistanceLy,
            row.BestViablePrice,
            row.StationScores.ToArray(),
            row.Details
        );

    public SurfaceSellRowViewModel Restore() =>
        new(
            Target,
            Distance,
            Stations,
            Systems.Select(system => system.Restore()).ToArray(),
            ReferenceDistanceLy,
            BestViablePrice,
            new SurfaceSellRowOptions(StationScores, Details)
        );
}

public sealed record SurfaceMiningSystemSnapshot(string System, double DistanceLy, SurfaceBodySnapshot[] Bodies)
{
    public static SurfaceMiningSystemSnapshot From(SurfaceMiningSystemRowViewModel row) =>
        new(row.System, row.DistanceLy, row.Bodies.Select(SurfaceBodySnapshot.From).ToArray());

    public SurfaceMiningSystemRowViewModel Restore() =>
        new(System, DistanceLy, Bodies.Select(body => body.Restore()).ToArray());
}

public sealed record SurfaceBodySnapshot(string[] Codes, string Details, string[] StationCodes)
{
    public static SurfaceBodySnapshot From(SurfaceBodyLine line) =>
        new(
            line.Codes.ToArray(),
            line.Details,
            line.Tags.Where(tag => tag.MatchesStation).Select(tag => tag.Code).ToArray()
        );

    public SurfaceBodyLine Restore() => new(Codes, Details, StationCodes.ToHashSet(StringComparer.OrdinalIgnoreCase));
}
