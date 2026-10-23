using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Navigation;

/// <summary>Presentation-only motion between authoritative surface snapshots. Never use for survey decisions.</summary>
public sealed class SurfaceVisualMotion
{
    private const double CorrectionSeconds = 0.08;
    private EliteStatus? status;
    private TimeSpan receivedAt;
    private double eastVelocity;
    private double northVelocity;
    private double correctionEast;
    private double correctionNorth;
    private double correctionHeading;
    private double predictionSeconds;

    public void Update(EliteStatus? next, TimeSpan now)
    {
        if (ReferenceEquals(status, next))
        {
            return;
        }

        SurfaceVisualPose? previousVisual = GetPose(now);
        EliteStatus? previous = status;
        double interval = (now - receivedAt).TotalSeconds;
        double previousEastVelocity = eastVelocity;
        double previousNorthVelocity = northVelocity;
        Reset(next, now);
        if (!CanPredict(previous, next, interval))
        {
            return;
        }

        var before = new SurfaceCoordinate(previous!.Latitude, previous.Longitude);
        var current = new SurfaceCoordinate(next!.Latitude, next.Longitude);
        (double east, double north) = GetOffset(before, current, (double)next.PlanetRadius);
        double speed = Math.Sqrt(east * east + north * north) / interval;
        if (speed > 1_500)
        {
            return;
        }

        double velocityChange = Math.Sqrt(
            Math.Pow(east / interval - previousEastVelocity, 2) + Math.Pow(north / interval - previousNorthVelocity, 2)
        );
        // Compare velocities at the previous position, then predict along the arrival direction at the current one.
        (double reverseEast, double reverseNorth) = GetOffset(current, before, (double)next.PlanetRadius);
        eastVelocity = -reverseEast / interval;
        northVelocity = -reverseNorth / interval;
        bool wasMoving = Math.Abs(previousEastVelocity) + Math.Abs(previousNorthVelocity) > 1;
        predictionSeconds = wasMoving && velocityChange > Math.Max(50, speed * 0.5) ? 0 : Math.Min(1, interval);
        if (previousVisual is { } visual)
        {
            (double correctionX, double correctionY) = GetOffset(current, visual.Location, (double)next.PlanetRadius);
            if (Math.Sqrt(correctionX * correctionX + correctionY * correctionY) <= 500)
            {
                correctionEast = correctionX;
                correctionNorth = correctionY;
            }

            double turn = SignedAngle(visual.Heading - next.NormalizedHeading);
            correctionHeading = Math.Abs(turn) <= 90 ? turn : 0;
        }
    }

    public SurfaceVisualPose? GetPose(TimeSpan now)
    {
        if (!HasValidPosition(status))
        {
            return null;
        }

        double age = Math.Max(0, (now - receivedAt).TotalSeconds);
        double correction = Math.Max(0, 1 - age / CorrectionSeconds);
        double ahead = Math.Min(age, predictionSeconds);
        var origin = new SurfaceCoordinate(status!.Latitude, status.Longitude);
        SurfaceCoordinate location = Move(
            origin,
            eastVelocity * ahead + correctionEast * correction,
            northVelocity * ahead + correctionNorth * correction,
            (double)status.PlanetRadius
        );
        return new SurfaceVisualPose(
            location,
            SurfaceNavigation.NormalizeDegrees(status.NormalizedHeading + correctionHeading * correction)
        );
    }

    public bool IsAnimating(TimeSpan now)
    {
        double age = (now - receivedAt).TotalSeconds;
        return age >= 0
            && (
                (age < predictionSeconds && Math.Abs(eastVelocity) + Math.Abs(northVelocity) > 0.01)
                || (
                    age < CorrectionSeconds
                    && Math.Abs(correctionEast) + Math.Abs(correctionNorth) + Math.Abs(correctionHeading) > 0.01
                )
            );
    }

    private void Reset(EliteStatus? next, TimeSpan now)
    {
        status = next;
        receivedAt = now;
        eastVelocity = 0;
        northVelocity = 0;
        correctionEast = 0;
        correctionNorth = 0;
        correctionHeading = 0;
        predictionSeconds = 0;
    }

    private static bool CanPredict(EliteStatus? previous, EliteStatus? next, double interval)
    {
        return IsFlying(previous)
            && IsFlying(next)
            && interval is >= 0.05 and <= 2.5
            && next!.Timestamp >= previous!.Timestamp
            && next.PlanetRadius == previous.PlanetRadius
            && string.Equals(next.BodyName, previous.BodyName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasValidPosition(EliteStatus? value)
    {
        return value is { HasLatitudeLongitude: true, PlanetRadius: > 0 }
            && double.IsFinite(value.Latitude)
            && value.Latitude is >= -90 and <= 90
            && double.IsFinite(value.Longitude)
            && value.Longitude is >= -180 and <= 180;
    }

    private static bool IsFlying(EliteStatus? value)
    {
        return HasValidPosition(value)
            && value
                is {
                    InMainShip: true,
                    InSrv: false,
                    OnFoot: false,
                    Docked: false,
                    Landed: false,
                    GuiFocus: <= GuiFocus.RolePanel
                }
            && (value.Flags & (StatusFlags.Supercruise | StatusFlags.FsdJump)) == 0
            && !string.IsNullOrWhiteSpace(value.BodyName);
    }

    public static (double East, double North) GetOffset(
        SurfaceCoordinate origin,
        SurfaceCoordinate target,
        double radius
    )
    {
        double distance = SurfaceNavigation.GetDistance(origin, target, radius);
        double bearing = SurfaceNavigation.GetBearing(origin, target) * Math.PI / 180;
        return (Math.Sin(bearing) * distance, Math.Cos(bearing) * distance);
    }

    private static SurfaceCoordinate Move(SurfaceCoordinate origin, double east, double north, double radius)
    {
        double distance = Math.Sqrt(east * east + north * north);
        if (distance == 0)
        {
            return origin;
        }

        double angular = distance / radius;
        double bearing = Math.Atan2(east, north);
        double latitude = origin.Latitude * Math.PI / 180;
        double nextLatitude = Math.Asin(
            Math.Clamp(
                Math.Sin(latitude) * Math.Cos(angular) + Math.Cos(latitude) * Math.Sin(angular) * Math.Cos(bearing),
                -1,
                1
            )
        );
        double longitudeDelta = Math.Atan2(
            Math.Sin(bearing) * Math.Sin(angular) * Math.Cos(latitude),
            Math.Cos(angular) - Math.Sin(latitude) * Math.Sin(nextLatitude)
        );
        return new SurfaceCoordinate(
            nextLatitude * 180 / Math.PI,
            SignedAngle(origin.Longitude + longitudeDelta * 180 / Math.PI)
        );
    }

    private static double SignedAngle(double angle) => SurfaceNavigation.NormalizeDegrees(angle + 180) - 180;
}

public readonly record struct SurfaceVisualPose(SurfaceCoordinate Location, double Heading);
