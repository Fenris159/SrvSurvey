using System.Numerics;
using SrvSurvey.Desktop.Platform.Overlay;
using Valve.VR;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class VrOverlayTransformTests
{
    [Fact]
    public void LegacyScaleAndCoordinateConversionArePreserved()
    {
        var calibration = new VrOverlayCalibration(
            20,
            new Vector3(10, 20, 30),
            Vector3.Zero);

        var matrix = VrOverlayTransform.Create(
            calibration,
            0,
            Matrix4x4.Identity);

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
        var matrix = new HmdMatrix34_t
        {
            m0 = 0,
            m8 = -1,
        };

        Assert.Equal(MathF.PI / 2, VrOverlayTransform.ExtractYaw(matrix), 5);
    }
}
