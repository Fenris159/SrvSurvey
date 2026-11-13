using System.Numerics;
using Avalonia;

namespace SrvSurvey.Desktop.Platform.Overlay;

/// <summary>A desktop panel's orientation and relative distance in the cockpit prototype.</summary>
public sealed record CockpitPanelPose(double Yaw = 0, double Pitch = 0, double Roll = 0, double Depth = 1)
{
    /// <summary>Rejects unsupported angles and distances rather than creating an invalid perspective transform.</summary>
    public bool IsValid =>
        double.IsFinite(Yaw)
        && Math.Abs(Yaw) <= 85
        && double.IsFinite(Pitch)
        && Math.Abs(Pitch) <= 85
        && double.IsFinite(Roll)
        && Math.Abs(Roll) <= 180
        && double.IsFinite(Depth)
        && Depth is >= 0.25 and <= 5;
}

/// <summary>Projects a saved desktop rectangle through a calibrated cockpit-local graphics transform.</summary>
public static class CockpitPanelProjection
{
    // The captured camera uses reversed depth. Distance is deliberately relative in this prototype.
    private const float NearDepth = 0.025f;
    private const float VerticalFocalScale = 1.8358436f;

    /// <summary>Projects editor content through the same viewport, physical rectangle and lens as the live presenter.</summary>
    internal static Matrix? CreateEditorPreview(
        Rect physicalPanel,
        Size contentSize,
        PixelRect viewport,
        double viewportScaling,
        CockpitPanelPose pose,
        CockpitProjectionLens? lens = null
    )
    {
        if (
            !double.IsFinite(viewportScaling)
            || viewportScaling <= 0
            || contentSize.Width <= 0
            || contentSize.Height <= 0
        )
        {
            return null;
        }
        var panel = new Rect(
            (physicalPanel.X - viewport.X) / viewportScaling,
            (physicalPanel.Y - viewport.Y) / viewportScaling,
            physicalPanel.Width / viewportScaling,
            physicalPanel.Height / viewportScaling
        );
        Matrix? projection = Create(
            panel,
            new Size(viewport.Width / viewportScaling, viewport.Height / viewportScaling),
            pose,
            Matrix4x4.Identity,
            lens
        );
        double scaleX = panel.Width / contentSize.Width;
        double scaleY = panel.Height / contentSize.Height;
        return projection is { } matrix && scaleX > 0 && scaleY > 0
            ? Matrix.CreateScale(scaleX, scaleY)
                * matrix
                * Matrix.CreateTranslation(-panel.X, -panel.Y)
                * Matrix.CreateScale(1 / scaleX, 1 / scaleY)
            : null;
    }

    /// <summary>Creates a true projective transform, returning null when a panel crosses behind the camera.</summary>
    public static Matrix? Create(
        Rect panel,
        Size viewport,
        CockpitPanelPose pose,
        Matrix4x4 cameraChange,
        CockpitProjectionLens? lens = null
    )
    {
        ArgumentNullException.ThrowIfNull(pose);
        if (
            !pose.IsValid
            || lens is { IsValid: false }
            || viewport.Width <= 0
            || viewport.Height <= 0
            || panel.Width <= 0
            || panel.Height <= 0
        )
        {
            return null;
        }

        float depth = (float)pose.Depth;
        float focalX = lens?.HorizontalFocalScale ?? VerticalFocalScale * (float)(viewport.Height / viewport.Width);
        float focalY = lens?.VerticalFocalScale ?? VerticalFocalScale;
        var orientation = Quaternion.CreateFromYawPitchRoll(Radians(pose.Yaw), Radians(pose.Pitch), Radians(pose.Roll));
        var center = new Vector3(
            (float)(2 * panel.Center.X / viewport.Width - 1) * depth / focalX,
            (float)(1 - 2 * panel.Center.Y / viewport.Height) * depth / focalY,
            depth
        );
        var horizontal = Vector3.Transform(new Vector3(2 / ((float)viewport.Width * focalX), 0, 0), orientation);
        var vertical = Vector3.Transform(new Vector3(0, -2 / ((float)viewport.Height * focalY), 0), orientation);
        Vector3 origin = center - horizontal * (float)(panel.Width / 2) - vertical * (float)(panel.Height / 2);
        var projection = new Matrix4x4(
            focalX,
            0,
            0,
            0,
            0,
            focalY,
            0,
            0,
            0,
            0,
            0,
            1,
            0,
            0,
            lens?.NearDepth ?? NearDepth,
            0
        );
        Matrix4x4 transform = projection * cameraChange;
        var start = Vector4.Transform(new Vector4(origin, 1), transform);
        var across = Vector4.Transform(new Vector4(horizontal, 0), transform);
        var down = Vector4.Transform(new Vector4(vertical, 0), transform);
        if (
            !InFront(start)
            || !InFront(start + across * (float)panel.Width)
            || !InFront(start + down * (float)panel.Height)
            || !InFront(start + across * (float)panel.Width + down * (float)panel.Height)
        )
        {
            return null;
        }

        double halfWidth = viewport.Width / 2;
        double halfHeight = viewport.Height / 2;
        return new Matrix(
            halfWidth * (across.X + across.W),
            halfHeight * (across.W - across.Y),
            across.W,
            halfWidth * (down.X + down.W),
            halfHeight * (down.W - down.Y),
            down.W,
            halfWidth * (start.X + start.W),
            halfHeight * (start.W - start.Y),
            start.W
        );
    }

    private static bool InFront(Vector4 point) =>
        float.IsFinite(point.X)
        && float.IsFinite(point.Y)
        && float.IsFinite(point.Z)
        && float.IsFinite(point.W)
        && point.W > 0.001f;

    private static float Radians(double value) => (float)(value * Math.PI / 180);
}

/// <summary>The actual game's perspective parameters captured when the user centers the cockpit view.</summary>
public sealed record CockpitProjectionLens(float HorizontalFocalScale, float VerticalFocalScale, float NearDepth)
{
    public bool IsValid =>
        float.IsFinite(HorizontalFocalScale)
        && HorizontalFocalScale is > 0.05f and < 100
        && float.IsFinite(VerticalFocalScale)
        && VerticalFocalScale is > 0.05f and < 100
        && float.IsFinite(NearDepth)
        && NearDepth is > 0.000001f and < 100;

    /// <summary>Removes the rigid model rotation to recover focal scales and reversed near depth.</summary>
    public static CockpitProjectionLens? FromRigidProjection(Matrix4x4 transform)
    {
        float forward = new Vector3(transform.M14, transform.M24, transform.M34).Length();
        var lens = new CockpitProjectionLens(
            new Vector3(transform.M11, transform.M21, transform.M31).Length() / forward,
            new Vector3(transform.M12, transform.M22, transform.M32).Length() / forward,
            Math.Abs(transform.M43) / forward
        );
        return lens.IsValid ? lens : null;
    }
}
