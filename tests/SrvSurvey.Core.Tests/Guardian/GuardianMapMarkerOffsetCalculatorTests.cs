using SrvSurvey.Core.Guardian;

namespace SrvSurvey.Core.Tests.Guardian;

public sealed class GuardianMapMarkerOffsetCalculatorTests
{
    [Fact]
    public void CalculatesOppositeMapTranslationFromCorrectedOrigin()
    {
        var offset = GuardianMapMarkerOffsetCalculator.Calculate(
            new GuardianSurfaceLocation(0, 0),
            new GuardianSurfaceLocation(0, 90),
            siteHeading: 0,
            planetRadiusMeters: 100);

        Assert.Equal(-Math.PI * 50, offset.X, precision: 8);
        Assert.Equal(0, offset.Y, precision: 8);
    }

    [Fact]
    public void RotatesStoredMapOffsetIntoSurfaceCoordinatesForTargeting()
    {
        var surfaceOffset = GuardianMapMarkerOffsetCalculator
            .ToSurfaceCoordinates(
                new GuardianMapPoint(0, 10),
                siteHeading: 90);

        Assert.Equal(10, surfaceOffset.X, precision: 8);
        Assert.Equal(0, surfaceOffset.Y, precision: 8);
    }

    [Fact]
    public void RecoversAlignmentOriginFromPortableOffset()
    {
        const double radius = 1_000_000;
        var original = new GuardianSurfaceLocation(10, 20);
        var corrected = new GuardianSurfaceLocation(10.01, 20.02);
        var offset = GuardianMapMarkerOffsetCalculator.Calculate(
            original,
            corrected,
            siteHeading: 37,
            radius);

        var recovered = GuardianMapMarkerOffsetCalculator.RecoverAlignmentOrigin(
            corrected,
            offset,
            siteHeading: 37,
            radius);

        Assert.Equal(original.Latitude, recovered.Latitude, precision: 5);
        Assert.Equal(original.Longitude, recovered.Longitude, precision: 5);
    }
}
