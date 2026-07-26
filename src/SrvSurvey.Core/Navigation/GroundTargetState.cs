using System.Globalization;
using System.Text.RegularExpressions;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Navigation;

public sealed partial class GroundTargetState
{
    private SurfaceCoordinate? currentLocation;
    private double planetRadius;
    private double altitude;
    private int heading;

    public GroundTargetState(GroundTargetSnapshot? seed = null)
    {
        Reset(seed);
    }

    public bool IsActive { get; private set; }

    public SurfaceCoordinate Target { get; private set; }

    public SurfaceCoordinate? CurrentLocation => currentLocation;

    public GroundTargetSolution? Solution { get; private set; }

    public int Version { get; private set; }

    public void Reset(GroundTargetSnapshot? seed = null)
    {
        seed ??= GroundTargetSnapshot.Empty;
        IsActive = seed.IsActive;
        Target = seed.Target;
        Calculate();
        Version++;
    }

    public void UpdateStatus(EliteStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        try
        {
            currentLocation = status.HasLatitudeLongitude
                ? new SurfaceCoordinate(status.Latitude, status.Longitude)
                : null;
        }
        catch (ArgumentOutOfRangeException)
        {
            currentLocation = null;
        }

        var statusRadius = (double)status.PlanetRadius;
        planetRadius = double.IsFinite(statusRadius) && statusRadius > 0
            ? statusRadius
            : 0;
        altitude = status.Altitude;
        heading = status.NormalizedHeading;
        Calculate();
    }

    public void SetTarget(SurfaceCoordinate target)
    {
        Target = target;
        IsActive = true;
        Calculate();
        Version++;
    }

    public bool SetActive(bool value)
    {
        if (IsActive == value)
        {
            return false;
        }

        IsActive = value;
        Calculate();
        Version++;
        return true;
    }

    public bool TrySetTarget(
        string latitude,
        string longitude,
        out string? error)
    {
        if (!TryParseNumber(latitude, out var parsedLatitude)
            || !TryParseNumber(longitude, out var parsedLongitude))
        {
            error = "Enter valid decimal latitude and longitude values.";
            return false;
        }

        return TrySetTarget(
            new ParsedCoordinate(parsedLatitude, parsedLongitude),
            out error);
    }

    public bool TrySetTarget(string text, out string? error)
    {
        if (!TryParse(text, out var parsed))
        {
            error = "No latitude/longitude pair was found in the clipboard text.";
            return false;
        }

        return TrySetTarget(parsed, out error);
    }

    public bool TryUseCurrentLocation(out string? error)
    {
        if (currentLocation is null)
        {
            error = "The current status has no surface coordinates.";
            return false;
        }

        SetTarget(currentLocation.Value);
        error = null;
        return true;
    }

    public void Clear()
    {
        Target = default;
        IsActive = false;
        Solution = null;
        Version++;
    }

    public GroundTargetSnapshot CreateSnapshot()
    {
        return new GroundTargetSnapshot(IsActive, Target);
    }

    public static bool TryParse(string? text, out ParsedCoordinate coordinate)
    {
        coordinate = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var cardinal = CardinalPairRegex().Match(text);
        if (cardinal.Success
            && TryParseNumber(cardinal.Groups["latitude"].Value, out var latitude)
            && TryParseNumber(cardinal.Groups["longitude"].Value, out var longitude))
        {
            if (cardinal.Groups["northSouth"].Value.Equals(
                    "S",
                    StringComparison.OrdinalIgnoreCase))
            {
                latitude = -Math.Abs(latitude);
            }
            else
            {
                latitude = Math.Abs(latitude);
            }

            if (cardinal.Groups["eastWest"].Value.Equals(
                    "W",
                    StringComparison.OrdinalIgnoreCase))
            {
                longitude = -Math.Abs(longitude);
            }
            else
            {
                longitude = Math.Abs(longitude);
            }

            coordinate = new ParsedCoordinate(latitude, longitude);
            return true;
        }

        var legacy = LegacyPairRegex().Match(text);
        if (!legacy.Success
            || !TryParseNumber(legacy.Groups[1].Value, out latitude)
            || !TryParseNumber(legacy.Groups[2].Value, out longitude))
        {
            return false;
        }

        coordinate = new ParsedCoordinate(latitude, longitude);
        return true;
    }

    private bool TrySetTarget(ParsedCoordinate parsed, out string? error)
    {
        try
        {
            SetTarget(new SurfaceCoordinate(parsed.Latitude, parsed.Longitude));
            error = null;
            return true;
        }
        catch (ArgumentOutOfRangeException exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private void Calculate()
    {
        if (!IsActive || currentLocation is null || planetRadius <= 0)
        {
            Solution = null;
            return;
        }

        var distance = SurfaceNavigation.GetDistance(
            currentLocation.Value,
            Target,
            planetRadius);
        var bearing = SurfaceNavigation.GetBearing(currentLocation.Value, Target);
        var relativeBearing = SurfaceNavigation.NormalizeDegrees(bearing - heading);
        var attackAngle = distance == 0
            ? 0
            : Math.Atan(altitude / distance) * 180d / Math.PI;
        Solution = new GroundTargetSolution(
            distance,
            bearing,
            relativeBearing,
            attackAngle,
            GetApproachKind(attackAngle));
    }

    private static GroundTargetApproach GetApproachKind(double attackAngle)
    {
        return attackAngle switch
        {
            <= 5 => GroundTargetApproach.Level,
            <= 30 => GroundTargetApproach.Shallow,
            <= 50 => GroundTargetApproach.Ideal,
            <= 60 => GroundTargetApproach.Steep,
            _ => GroundTargetApproach.TooSteep,
        };
    }

    private static bool TryParseNumber(string value, out double number)
    {
        const NumberStyles styles = NumberStyles.Float;
        return double.TryParse(value, styles, CultureInfo.CurrentCulture, out number)
            || double.TryParse(value, styles, CultureInfo.InvariantCulture, out number);
    }

    [GeneratedRegex(
        @"([+\-.0-9]+)\s*[ ,|`/]\s*([+\-.0-9]+)",
        RegexOptions.Singleline)]
    private static partial Regex LegacyPairRegex();

    [GeneratedRegex(
        @"(?<latitude>[+\-.0-9]+)\s*°?\s*(?<northSouth>[NS])\s*[,|`/ ]*\s*(?<longitude>[+\-.0-9]+)\s*°?\s*(?<eastWest>[EW])",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex CardinalPairRegex();
}

public readonly record struct ParsedCoordinate(double Latitude, double Longitude);

public sealed record GroundTargetSnapshot(bool IsActive, SurfaceCoordinate Target)
{
    public static GroundTargetSnapshot Empty { get; } = new(false, default);
}

public sealed record GroundTargetSolution(
    double Distance,
    double Bearing,
    double RelativeBearing,
    double AttackAngle,
    GroundTargetApproach Approach);

public enum GroundTargetApproach
{
    Level,
    Shallow,
    Ideal,
    Steep,
    TooSteep,
}
