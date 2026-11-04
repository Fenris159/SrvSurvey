using System.Numerics;
using SrvSurvey.Desktop.Platform.Overlay;
using Valve.VR;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class VrOverlayTransformTests
{
    [Fact]
    public void PlaceInFrontUsesCurrentHeadingAndPositionWithoutRetainingOldTiltOrOversizedWidth()
    {
        var calibration = new VrOverlayCalibration(20, new Vector3(0, 23, 20), new Vector3(15, -30, 12));
        VrOverlayCalibration placed = VrOverlayTransform.PlaceInFront(
            calibration,
            new VrOverlayHeadPose(new Vector3(2, 1, 3), MathF.PI / 2)
        );
        Assert.Equal(8, placed.Scale);
        Assert.Equal(10, placed.Position.X, 4);
        Assert.Equal(10, placed.Position.Y, 4);
        Assert.Equal(-30, placed.Position.Z, 4);
        Assert.Equal(0, placed.Rotation.X);
        Assert.Equal(90, placed.Rotation.Y, 4);
        Assert.Equal(0, placed.Rotation.Z);
    }

    [Fact]
    public void PlaceInFrontPreservesSmallerWidthsAndSupportsTheLegacyOpenVrRotationOffset()
    {
        var calibration = new VrOverlayCalibration(3, Vector3.Zero, Vector3.Zero);
        VrOverlayCalibration placed = VrOverlayTransform.PlaceInFront(
            calibration,
            new VrOverlayHeadPose(Vector3.Zero, 0, MathF.PI / 2)
        );
        Assert.Equal(3, placed.Scale);
        Assert.Equal(new Vector3(0, 0, 10), placed.Position);
        Assert.Equal(90, placed.Rotation.Y, 4);
    }

    [Fact]
    public void LegacyScaleAndCoordinateConversionArePreserved()
    {
        var calibration = new VrOverlayCalibration(20, new Vector3(10, 20, 30), Vector3.Zero);

        Matrix4x4 matrix = VrOverlayTransform.Create(calibration, 0, Matrix4x4.Identity);

        Assert.Equal(2, matrix.M11, 5);
        Assert.Equal(2, matrix.M22, 5);
        Assert.Equal(2, matrix.M33, 5);
        Assert.Equal(1, matrix.M41, 5);
        Assert.Equal(2, matrix.M42, 5);
        Assert.Equal(-3, matrix.M43, 5);
    }

    [Fact]
    public void HeadsetYawIsExtractedUsingTheLegacyMatrixAxes()
    {
        var matrix = new HmdMatrix34_t { m0 = 0, m8 = -1 };

        Assert.Equal(MathF.PI / 2, VrOverlayTransform.ExtractYaw(matrix), 5);
    }
}
