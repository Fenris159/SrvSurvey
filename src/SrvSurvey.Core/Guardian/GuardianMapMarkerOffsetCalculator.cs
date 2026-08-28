using SrvSurvey.Core.Navigation;

namespace SrvSurvey.Core.Guardian;

public static class GuardianMapMarkerOffsetCalculator
{
    public static GuardianMapPoint Calculate(
        GuardianSurfaceLocation alignmentOrigin,
        GuardianSurfaceLocation correctedOrigin,
        int siteHeading,
        double planetRadiusMeters)
    {
        if (siteHeading is < 0 or > 359)
        {
            throw new ArgumentOutOfRangeException(
                nameof(siteHeading),
                "The site heading must be between 0 and 359 degrees.");
        }

        var original = new SurfaceCoordinate(
            alignmentOrigin.Latitude,
            alignmentOrigin.Longitude);
        var corrected = new SurfaceCoordinate(
            correctedOrigin.Latitude,
            correctedOrigin.Longitude);
        var distance = SurfaceNavigation.GetDistance(
            original,
            corrected,
            planetRadiusMeters);
        if (distance == 0)
        {
            return default;
        }

        var bearing = SurfaceNavigation.GetBearing(original, corrected);
        var mapAngle = (bearing - siteHeading) * Math.PI / 180d;
        return new GuardianMapPoint(
            -Math.Sin(mapAngle) * distance,
            Math.Cos(mapAngle) * distance);
    }

    public static GuardianMapPoint ToSurfaceCoordinates(
        GuardianMapPoint markerOffset,
        int siteHeading)
    {
        if (siteHeading is < 0 or > 359)
        {
            throw new ArgumentOutOfRangeException(
                nameof(siteHeading),
                "The site heading must be between 0 and 359 degrees.");
        }

        var radians = siteHeading * Math.PI / 180d;
        return new GuardianMapPoint(
            (-markerOffset.X * Math.Cos(radians))
                + (markerOffset.Y * Math.Sin(radians)),
            (-markerOffset.X * Math.Sin(radians))
                - (markerOffset.Y * Math.Cos(radians)));
    }
}
