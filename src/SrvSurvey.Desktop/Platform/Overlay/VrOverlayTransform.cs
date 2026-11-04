using System.Numerics;

namespace SrvSurvey.Desktop.Platform.Overlay;

public static class VrOverlayTransform
{
    /// <summary>Places only this panel at eye level, one metre along the current head heading.</summary>
    public static VrOverlayCalibration PlaceInFront(VrOverlayCalibration calibration, VrOverlayHeadPose pose)
    {
        var heading = Matrix4x4.CreateRotationY(pose.Yaw);
        Vector3 position = pose.Position + Vector3.Transform(new Vector3(0, 0, -1), heading);
        return new VrOverlayCalibration(
            Math.Min(calibration.Scale, 8),
            new Vector3(position.X * 10, position.Y * 10, -position.Z * 10),
            new Vector3(0, (pose.PanelYaw ?? pose.Yaw) * 180 / MathF.PI, 0)
        );
    }

    public static Matrix4x4 Create(
        VrOverlayCalibration calibration,
        float headsetYawOffset,
        Matrix4x4 headsetOrientationOffset
    )
    {
        ArgumentNullException.ThrowIfNull(calibration);
        calibration.Validate();
        var rotation = Matrix4x4.CreateFromYawPitchRoll(
            DegreesToRadians(calibration.Rotation.Y) - headsetYawOffset,
            DegreesToRadians(calibration.Rotation.X),
            DegreesToRadians(calibration.Rotation.Z)
        );
        var position = new Vector3(
            calibration.Position.X / 10,
            calibration.Position.Y / 10,
            -calibration.Position.Z / 10
        );
        var rotatedPosition = Vector3.Transform(position, headsetOrientationOffset);
        var translation = Matrix4x4.CreateTranslation(rotatedPosition);
        var scale = Matrix4x4.CreateScale(calibration.Scale / 10);
        return Matrix4x4.Multiply(Matrix4x4.Multiply(rotation, scale), translation);
    }

    public static VrOverlayHeadPose FromOpenVrHeadPose(
        Valve.VR.HmdMatrix34_t matrix,
        float headsetYawOffset,
        Matrix4x4 headsetOrientationOffset
    )
    {
        var position = Vector3.Transform(
            new Vector3(matrix.m3, matrix.m7, matrix.m11),
            Matrix4x4.Transpose(headsetOrientationOffset)
        );
        float yaw = MathF.Atan2(matrix.m2, matrix.m10);
        // Legacy OpenVR rotates positions and panel orientations with opposite offset signs.
        return new VrOverlayHeadPose(position, yaw - headsetYawOffset, yaw + headsetYawOffset);
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

public sealed record VrOverlayHeadPose(Vector3 Position, float Yaw, float? PanelYaw = null);
