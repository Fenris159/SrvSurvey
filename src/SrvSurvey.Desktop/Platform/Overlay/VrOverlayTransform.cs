using System.Numerics;

namespace SrvSurvey.Desktop.Platform.Overlay;

public static class VrOverlayTransform
{
    public static Matrix4x4 Create(
        VrOverlayCalibration calibration,
        float headsetYawOffset,
        Matrix4x4 headsetOrientationOffset)
    {
        ArgumentNullException.ThrowIfNull(calibration);
        calibration.Validate();
        var rotation = Matrix4x4.CreateFromYawPitchRoll(
            DegreesToRadians(calibration.Rotation.Y) - headsetYawOffset,
            DegreesToRadians(calibration.Rotation.X),
            DegreesToRadians(calibration.Rotation.Z));
        var position = new Vector3(
            calibration.Position.X / 10,
            calibration.Position.Y / 10,
            -calibration.Position.Z / 10);
        var rotatedPosition = Vector3.Transform(
            position,
            headsetOrientationOffset);
        var translation = Matrix4x4.CreateTranslation(rotatedPosition);
        var scale = Matrix4x4.CreateScale(calibration.Scale / 10);
        return Matrix4x4.Multiply(
            Matrix4x4.Multiply(rotation, scale),
            translation);
    }

    public static float ExtractYaw(Valve.VR.HmdMatrix34_t matrix)
    {
        return MathF.Atan2(-matrix.m8, matrix.m0);
    }

    private static float DegreesToRadians(float degrees)
    {
        return MathF.PI / 180 * degrees;
    }
}
