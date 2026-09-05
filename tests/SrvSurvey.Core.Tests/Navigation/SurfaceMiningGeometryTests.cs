using SrvSurvey.Core.Navigation;

namespace SrvSurvey.Core.Tests.Navigation;

public sealed class SurfaceMiningGeometryTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(85, 179.99999, 270)]
    [InlineData(-85, -179.99999, 90)]
    public void CockpitOffsetsRemainBehindVehicleAcrossLongitudeBoundary(
        double latitude, double longitude, double heading)
    {
        const double radius = 1_000_000;
        var cockpit = new SurfaceCoordinate(latitude, longitude);
        var center = SurfaceMiningGeometry.VehicleCenter(cockpit, heading, radius);
        var rig = SurfaceMiningGeometry.DeployedRig(cockpit, heading, radius);

        Assert.Equal(4, SurfaceNavigation.GetDistance(cockpit, center, radius), 4);
        Assert.Equal(7, SurfaceNavigation.GetDistance(cockpit, rig, radius), 4);
        Assert.Equal(SurfaceNavigation.NormalizeDegrees(heading + 180),
            SurfaceNavigation.GetBearing(cockpit, rig), 4);
        Assert.InRange(rig.Longitude, -180, 180);
        Assert.InRange(center.Longitude, -180, 180);
    }
}
