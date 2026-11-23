using System.Numerics;

namespace SrvSurvey.Desktop.Platform.Overlay;

public static class VrOverlayTransform
{
    private const float CalibrationSnapStepRadians = MathF.PI / 4f;
    private const float CalibrationSnapMaxYawRadians = MathF.PI / 2f;
    private const float CalibrationSnapHysteresisRadians = 30f * MathF.PI / 180f;

    /// <summary>Places only this panel at eye level, one metre along the current head heading.</summary>
    public static VrOverlayCalibration PlaceInFront(VrOverlayCalibration calibration, VrOverlayHeadPose pose)
    {
        var heading = Matrix4x4.CreateRotationY(pose.Yaw);
        Vector3 position = pose.Position + Vector3.Transform(new Vector3(0, 0, -1), heading);
        return new VrOverlayCalibration(
            Math.Min(calibration.Scale, 8),
            new Vector3(position.X * 10, position.Y * 10, -position.Z * 10),
            new Vector3(0, RadiansToDegrees(pose.PanelYaw ?? pose.Yaw), 0)
        );
    }

    /// <summary>Snaps heading yaw into five 45-degree front cockpit sectors (-90° to +90°) with hysteresis.</summary>
    public static float SnapCalibrationYaw(float rawYawRadians, float? currentSnappedYawRadians)
    {
        float normalized = NormalizeAngle(rawYawRadians);
        float clamped = Math.Clamp(normalized, -CalibrationSnapMaxYawRadians, CalibrationSnapMaxYawRadians);
        if (
            currentSnappedYawRadians is float current
            && MathF.Abs(NormalizeAngle(clamped - current)) < CalibrationSnapHysteresisRadians
        )
        {
            return current;
        }

        float slot = MathF.Round(clamped / CalibrationSnapStepRadians) * CalibrationSnapStepRadians;
        return Math.Clamp(slot, -CalibrationSnapMaxYawRadians, CalibrationSnapMaxYawRadians);
    }

    /// <summary>Places the headset calibration window centered along the requested heading, facing the eyes at its standard lower height.</summary>
    public static VrOverlayCalibration PlaceCalibrationControls(VrOverlayHeadPose pose, float? snappedYawRadians = null)
    {
        ArgumentNullException.ThrowIfNull(pose);
        const float scale = 8f;
        const float distanceMeters = 1f;
        const float heightOffsetMeters = -0.5f;
        float tiltRadians = MathF.Atan2(-heightOffsetMeters, distanceMeters);
        float targetYaw = snappedYawRadians ?? pose.Yaw;

        var heading = Matrix4x4.CreateRotationY(targetYaw);
        Vector3 positionMeters =
            pose.Position
            + Vector3.Transform(new Vector3(0, 0, -distanceMeters), heading)
            + new Vector3(0, heightOffsetMeters, 0);
        var position = new Vector3(positionMeters.X * 10f, positionMeters.Y * 10f, -positionMeters.Z * 10f);

        Vector3 rotation;
        if (pose.UsesLegacyOpenVrRotation)
        {
            float worldYaw = snappedYawRadians.HasValue ? targetYaw + pose.OpenVrYawOffset : pose.OpenVrWorldYaw;
            var desired = Quaternion.CreateFromYawPitchRoll(worldYaw, -tiltRadians, 0f);
            var inverse = Matrix4x4.CreateFromQuaternion(Quaternion.Inverse(desired));
            float pitch = MathF.Asin(Math.Clamp(-inverse.M32, -1f, 1f));
            float yaw = MathF.Atan2(inverse.M31, inverse.M33) + pose.OpenVrYawOffset;
            float roll = MathF.Atan2(inverse.M12, inverse.M22);
            rotation = new Vector3(RadiansToDegrees(pitch), RadiansToDegrees(yaw), RadiansToDegrees(roll));
        }
        else
        {
            float panelYaw = snappedYawRadians ?? (pose.PanelYaw ?? pose.Yaw);
            rotation = new Vector3(-RadiansToDegrees(tiltRadians), RadiansToDegrees(panelYaw), 0f);
        }

        return new VrOverlayCalibration(scale, position, rotation);
    }

    /// <summary>Moves a panel along its own plane in 10 cm calibration units during VR click-and-drag.</summary>
    public static VrOverlayCalibration ApplyPlaneDrag(
        VrOverlayCalibration calibration,
        float deltaRightUnits,
        float deltaUpUnits,
        bool usesLegacyOpenVrRotation
    )
    {
        ArgumentNullException.ThrowIfNull(calibration);
        Quaternion orientation = GetPanelOrientation(calibration, usesLegacyOpenVrRotation);
        var right = Vector3.Transform(Vector3.UnitX, orientation);
        var up = Vector3.Transform(Vector3.UnitY, orientation);
        Vector3 worldDelta = right * deltaRightUnits + up * deltaUpUnits;
        return ApplyWorldDelta(calibration, worldDelta);
    }

    /// <summary>Moves a panel along its depth normal (away from the viewer for positive steps) in 10 cm calibration units.</summary>
    public static VrOverlayCalibration ApplyDepthStep(
        VrOverlayCalibration calibration,
        float deltaDepthUnits,
        bool usesLegacyOpenVrRotation
    )
    {
        ArgumentNullException.ThrowIfNull(calibration);
        Quaternion orientation = GetPanelOrientation(calibration, usesLegacyOpenVrRotation);
        var awayNormal = Vector3.Transform(-Vector3.UnitZ, orientation);
        return ApplyWorldDelta(calibration, awayNormal * deltaDepthUnits);
    }

    private static Quaternion GetPanelOrientation(VrOverlayCalibration calibration, bool usesLegacyOpenVrRotation)
    {
        Vector3 radians = calibration.Rotation * (MathF.PI / 180f);
        var orientation = Quaternion.CreateFromYawPitchRoll(radians.Y, radians.X, radians.Z);
        return usesLegacyOpenVrRotation ? Quaternion.Inverse(orientation) : orientation;
    }

    private static VrOverlayCalibration ApplyWorldDelta(VrOverlayCalibration calibration, Vector3 worldDelta)
    {
        var position = new Vector3(
            Math.Clamp(calibration.Position.X + worldDelta.X, -1000f, 1000f),
            Math.Clamp(calibration.Position.Y + worldDelta.Y, -1000f, 1000f),
            Math.Clamp(calibration.Position.Z - worldDelta.Z, -1000f, 1000f)
        );
        return calibration with { Position = position };
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
        // OVRSharp.ToHmdMatrix34_t copies Matrix4x4's row-vector 3x3 block without transposing,
        // which negates rotation angles relative to column-vector OpenVR transforms.
        return new VrOverlayHeadPose(
            position,
            yaw - headsetYawOffset,
            headsetYawOffset - yaw,
            UsesLegacyOpenVrRotation: true,
            OpenVrWorldYaw: yaw,
            OpenVrYawOffset: headsetYawOffset
        );
    }

    public static float ExtractYaw(Valve.VR.HmdMatrix34_t matrix)
    {
        return MathF.Atan2(-matrix.m8, matrix.m0);
    }

    private static float DegreesToRadians(float degrees)
    {
        return MathF.PI / 180 * degrees;
    }

    private static float RadiansToDegrees(float radians)
    {
        return radians * 180f / MathF.PI;
    }

    private static float NormalizeAngle(float radians)
    {
        return MathF.Atan2(MathF.Sin(radians), MathF.Cos(radians));
    }
}

public sealed record VrOverlayHeadPose(
    Vector3 Position,
    float Yaw,
    float? PanelYaw = null,
    bool UsesLegacyOpenVrRotation = false,
    float OpenVrWorldYaw = 0f,
    float OpenVrYawOffset = 0f
);
