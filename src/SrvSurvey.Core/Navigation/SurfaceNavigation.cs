namespace SrvSurvey.Core.Navigation;

public static class SurfaceNavigation
{
    public static double GetDistance(
        SurfaceCoordinate first,
        SurfaceCoordinate second,
        double radius)
    {
        if (!double.IsFinite(radius) || radius <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(radius),
                "The body radius must be positive.");
        }

        if (first == second)
        {
            return 0;
        }

        var firstLatitude = DegreesToRadians(first.Latitude);
        var secondLatitude = DegreesToRadians(second.Latitude);
        var longitudeDelta = DegreesToRadians(
            second.Longitude - first.Longitude);
        var cosine = (Math.Sin(firstLatitude) * Math.Sin(secondLatitude))
            + (Math.Cos(firstLatitude)
                * Math.Cos(secondLatitude)
                * Math.Cos(longitudeDelta));
        return Math.Acos(Math.Clamp(cosine, -1, 1)) * radius;
    }

    public static double GetBearing(
        SurfaceCoordinate origin,
        SurfaceCoordinate target)
    {
        if (origin == target)
        {
            return 0;
        }

        var originLatitude = DegreesToRadians(origin.Latitude);
        var targetLatitude = DegreesToRadians(target.Latitude);
        var longitudeDelta = DegreesToRadians(
            target.Longitude - origin.Longitude);
        var y = Math.Sin(longitudeDelta) * Math.Cos(targetLatitude);
        var x = (Math.Cos(originLatitude) * Math.Sin(targetLatitude))
            - (Math.Sin(originLatitude)
                * Math.Cos(targetLatitude)
                * Math.Cos(longitudeDelta));
        return NormalizeDegrees(RadiansToDegrees(Math.Atan2(y, x)));
    }

    public static double NormalizeDegrees(double degrees)
    {
        return ((degrees % 360) + 360) % 360;
    }

    private static double DegreesToRadians(double degrees)
    {
        return degrees * Math.PI / 180d;
    }

    private static double RadiansToDegrees(double radians)
    {
        return radians * 180d / Math.PI;
    }
}

public readonly record struct SurfaceCoordinate
{
    public SurfaceCoordinate(double latitude, double longitude)
    {
        if (!double.IsFinite(latitude) || latitude is < -90 or > 90)
        {
            throw new ArgumentOutOfRangeException(
                nameof(latitude),
                "Latitude must be between -90 and 90 degrees.");
        }

        if (!double.IsFinite(longitude) || longitude is < -180 or > 180)
        {
            throw new ArgumentOutOfRangeException(
                nameof(longitude),
                "Longitude must be between -180 and 180 degrees.");
        }

        Latitude = latitude;
        Longitude = longitude;
    }

    public double Latitude { get; }

    public double Longitude { get; }
}
