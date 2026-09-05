namespace SrvSurvey.Core.Navigation;

/// <summary>Rhino cockpit offsets and rig proximity from legacy PR #1055.</summary>
public static class SurfaceMiningGeometry
{
    public const double PickupDistanceMeters = 5;
    public const double ExclusionDistanceMeters = 78;
    public const double RigRadiusMeters = 70;

    public static SurfaceCoordinate VehicleCenter(SurfaceCoordinate cockpit, double heading, double radius)
        => OffsetBehind(cockpit, heading, radius, 4);

    public static SurfaceCoordinate DeployedRig(SurfaceCoordinate cockpit, double heading, double radius)
        => OffsetBehind(cockpit, heading, radius, 7);

    private static SurfaceCoordinate OffsetBehind(SurfaceCoordinate origin, double heading, double radius, double meters)
    {
        if (!double.IsFinite(radius) || radius <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(radius));
        }

        if (!double.IsFinite(heading))
        {
            throw new ArgumentOutOfRangeException(nameof(heading));
        }

        var latitude = origin.Latitude * Math.PI / 180;
        var longitude = origin.Longitude * Math.PI / 180;
        var bearing = (heading + 180) * Math.PI / 180;
        var angle = meters / radius;
        var resultLatitude = Math.Asin(Math.Clamp(
            Math.Sin(latitude) * Math.Cos(angle)
            + Math.Cos(latitude) * Math.Sin(angle) * Math.Cos(bearing), -1, 1));
        var resultLongitude = longitude + Math.Atan2(
            Math.Sin(bearing) * Math.Sin(angle) * Math.Cos(latitude),
            Math.Cos(angle) - Math.Sin(latitude) * Math.Sin(resultLatitude));
        return new SurfaceCoordinate(resultLatitude * 180 / Math.PI,
            SurfaceNavigation.NormalizeDegrees(resultLongitude * 180 / Math.PI + 180) - 180);
    }
}
