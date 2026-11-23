using System.Numerics;

namespace SrvSurvey.Desktop.Platform.Overlay;

/// <summary>Desktop-mirrored seated OpenXR defaults with proportional scales and exact eye-facing angles.</summary>
internal static class VrSeatedCalibrationDefaults
{
    private const float PixelsPerScaleUnit = 75f;
    private const float MinimumScale = 0.8f;
    private const float MaximumScale = 8f;

    public static VrOverlayCalibration Get(string plotterName)
    {
        OverlayLayoutDefinition? definition = OverlayLayoutCatalog.Supported.FirstOrDefault(item =>
            string.Equals(item.Name, plotterName, StringComparison.Ordinal)
        );
        if (definition is null)
        {
            return new(4f, new Vector3(0f, 0f, 11f), Vector3.Zero);
        }

        float scale = MathF.Round(
            Math.Clamp(definition.PreviewSize.Width / PixelsPerScaleUnit, MinimumScale, MaximumScale),
            1
        );
        Vector3 position = GetCockpitPosition(definition.DefaultPlacement);
        Vector3 rotation = FaceOriginOpenXr(position);
        return new(scale, position, rotation);
    }

    private static Vector3 GetCockpitPosition(LegacyOverlayPlacement placement)
    {
        (float x, float z) = placement.Horizontal switch
        {
            LegacyHorizontalAnchor.Left => placement.HorizontalOffset > 100 ? (-3.5f, 10.5f) : (-6f, 9.5f),
            LegacyHorizontalAnchor.Right => placement.HorizontalOffset > 100 ? (3.5f, 10.5f) : (6f, 9.5f),
            _ when placement.HorizontalOffset >= 200 => (3.5f, 10.5f),
            _ when placement.HorizontalOffset <= -200 => (-3.5f, 10.5f),
            _ => (0f, 11f),
        };

        float y = placement.Vertical switch
        {
            LegacyVerticalAnchor.Top when placement.VerticalOffset >= 250 => -1.5f,
            LegacyVerticalAnchor.Top when placement.VerticalOffset >= 50 => 2.2f,
            LegacyVerticalAnchor.Top => 4.5f,
            LegacyVerticalAnchor.Bottom when placement.VerticalOffset >= 100 => -0.5f,
            LegacyVerticalAnchor.Bottom => -3f,
            _ => 0f,
        };

        return new(x, y, z);
    }

    private static Vector3 FaceOriginOpenXr(Vector3 position)
    {
        float horizontalDistance = MathF.Sqrt(position.X * position.X + position.Z * position.Z);
        float pitch = MathF.Round(MathF.Atan2(position.Y, horizontalDistance) * 180f / MathF.PI, 1) + 0f;
        float yaw = MathF.Round(MathF.Atan2(-position.X, position.Z) * 180f / MathF.PI, 1) + 0f;
        return new(pitch, yaw, 0f);
    }
}
