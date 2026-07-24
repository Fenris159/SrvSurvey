namespace SrvSurvey.Core.Search;

public sealed class SphereLimitState
{
    public const double DefaultRadius = 100;
    public const double MinimumRadius = 1;
    public const double MaximumRadius = 1000;

    public SphereLimitState(SphereLimitSnapshot? seed = null)
    {
        Reset(seed);
    }

    public bool IsActive { get; private set; }

    public string? CenterSystemName { get; private set; }

    public GalacticCoordinate? Center { get; private set; }

    public double Radius { get; private set; }

    public int Version { get; private set; }

    public void Reset(SphereLimitSnapshot? seed = null)
    {
        seed ??= SphereLimitSnapshot.Empty;
        CenterSystemName = string.IsNullOrWhiteSpace(seed.CenterSystemName)
            ? null
            : seed.CenterSystemName.Trim();
        Center = seed.Center;
        Radius = IsValidRadius(seed.Radius)
            ? seed.Radius
            : DefaultRadius;
        IsActive = seed.Active
            && Center is not null
            && CenterSystemName is not null;
        Version++;
    }

    public bool TryEnable(
        StarSystemReference? centerSystem,
        double radius,
        out string? error)
    {
        if (centerSystem is null
            || string.IsNullOrWhiteSpace(centerSystem.Name))
        {
            error = "Choose a valid center system before enabling the limit.";
            return false;
        }

        if (!IsValidRadius(radius))
        {
            error = $"Radius must be between {MinimumRadius:N0} and "
                + $"{MaximumRadius:N0} light-years.";
            return false;
        }

        CenterSystemName = centerSystem.Name.Trim();
        Center = centerSystem.Position;
        Radius = radius;
        IsActive = true;
        Version++;
        error = null;
        return true;
    }

    public void Disable()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        Version++;
    }

    public double? DistanceFrom(GalacticCoordinate? position)
    {
        return Center is { } center && position is { } target
            ? center.DistanceTo(target)
            : null;
    }

    public SphereLimitEvaluation? Evaluate(
        string targetSystemName,
        GalacticCoordinate targetPosition)
    {
        if (!IsActive || Center is not { } center)
        {
            return null;
        }

        var distance = center.DistanceTo(targetPosition);
        return new SphereLimitEvaluation(
            targetSystemName,
            targetPosition,
            distance,
            distance < Radius);
    }

    public SphereLimitSnapshot CreateSnapshot()
    {
        return new SphereLimitSnapshot(
            IsActive,
            CenterSystemName,
            Center,
            Radius);
    }

    public static bool IsValidRadius(double radius)
    {
        return double.IsFinite(radius)
            && radius is >= MinimumRadius and <= MaximumRadius;
    }
}

public sealed record SphereLimitSnapshot(
    bool Active,
    string? CenterSystemName,
    GalacticCoordinate? Center,
    double Radius)
{
    public static SphereLimitSnapshot Empty { get; } = new(
        false,
        null,
        null,
        SphereLimitState.DefaultRadius);
}

public sealed record SphereLimitEvaluation(
    string TargetSystemName,
    GalacticCoordinate TargetPosition,
    double Distance,
    bool IsInside);
