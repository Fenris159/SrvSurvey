using System.Collections.Frozen;
using System.Numerics;

namespace SrvSurvey.Desktop.Platform.Overlay;

/// <summary>Desktop-mirrored seated OpenXR defaults with proportional scales and exact eye-facing angles.</summary>
internal static class VrSeatedCalibrationDefaults
{
    private const float PixelsPerScaleUnit = 75f;
    private const float MinimumScale = 0.8f;
    private const float MaximumScale = 8f;

    // Pre-RC77.27 bundled floor-origin presets copied into user plotters.json before the seated-cockpit migration.
    private static readonly FrozenDictionary<string, VrOverlayCalibration> LegacyBundledDefaults = new Dictionary<
        string,
        VrOverlayCalibration
    >(StringComparer.Ordinal)
    {
        ["PlotAdjustVR"] = new(
            10.090002f,
            new(-0.42953613f, 14.988647f, 33.43891f),
            new(-0.13900007f, 0f, -0.98910034f)
        ),
        ["PlotBioStatus"] = new(10f, new(-0.37305868f, 19.789331f, 18.2856f), new(-13.458961f, 0f, 0f)),
        ["PlotBioSystem"] = new(10f, new(-31.349808f, 14.00925f, 27.757381f), new(-2.5088782f, -56.153645f, 3.569419f)),
        ["PlotBodyInfo"] = new(10f, new(0.56273437f, 0.40408444f, 17.688816f), new(37.76997f, -8.517466f, -3.6345477f)),
        ["PlotBuildCommodities"] = new(
            10f,
            new(17.642536f, 18.340712f, 29.530416f),
            new(-5.4038405f, 35.560196f, -2.9844203f)
        ),
        ["PlotFlightWarning"] = new(10f, new(-0.10232024f, 17.130922f, 35.47194f), Vector3.Zero),
        ["PlotFloatie"] = new(10f, new(0.3508407f, -4.058254f, 35.360367f), Vector3.Zero),
        ["PlotFootCombat"] = new(
            10f,
            new(-30.857403f, 25.342245f, 26.802038f),
            new(-18.774181f, -49.25817f, 19.909042f)
        ),
        ["PlotFSS"] = new(10f, new(0.50730133f, 4.138785f, 15.860139f), new(39.654785f, -2.1141796f, -0.051382415f)),
        ["PlotFSSInfo"] = new(10f, new(-20.298681f, 13.641001f, 28.996878f), new(-0.08262f, -41.752735f, 0.88688004f)),
        ["PlotGalMap"] = new(10f, new(-11.4784f, 22.122263f, 17.58605f), new(-14.281838f, -34.070545f, 10.871888f)),
        ["PlotGrounded"] = new(
            5.6049976f,
            new(12.371634f, 2.2900984f, 17.55092f),
            new(39.696003f, 27.936098f, 17.480618f)
        ),
        ["PlotGuardians"] = new(10f, new(-8f, 10f, 45f), Vector3.Zero),
        ["PlotGuardianStatus"] = new(10f, new(-8f, 10f, 45f), Vector3.Zero),
        ["PlotGuardianSystem"] = new(10f, new(-8f, 10f, 45f), Vector3.Zero),
        ["PlotHumanSite"] = new(10f, new(-20.51811f, 15.310758f, 18.06471f), new(-2.5494804f, -54.370968f, 0f)),
        ["PlotJumpInfo"] = new(20f, new(0.06868008f, 23.358482f, 19.957216f), new(-20.24308f, 0f, 0f)),
        ["PlotRouteBio"] = new(10f, new(18f, 18f, 32f), new(0f, 32f, 0f)),
        ["PlotMassacre"] = new(10f, new(-8f, 10f, 45f), Vector3.Zero),
        ["PlotMiniTrack"] = new(10f, new(21.321388f, 18.282234f, 38.582367f), new(0.83643997f, 19.883081f, 0f)),
        ["PlotPriorScans"] = new(10f, new(-17.692802f, 17.214205f, 31.163607f), Vector3.Zero),
        ["PlotPulse"] = new(10f, new(-7.199409f, 10f, 47.523716f), Vector3.Zero),
        ["PlotRamTah"] = new(10f, new(-8f, 10f, 45f), Vector3.Zero),
        ["PlotSphericalSearch"] = new(
            9.573f,
            new(11.936403f, 24.9184f, 19.843262f),
            new(-14.5932865f, 33.037697f, -8.326503f)
        ),
        ["PlotStationInfo"] = new(
            5.827999f,
            new(-19.598705f, 15.827987f, 22.313263f),
            new(-3.0998404f, -53.595703f, -1.0533398f)
        ),
        ["PlotSysStatus"] = new(3.1119983f, new(-9.165022f, 4.2676287f, 17.66371f), new(5.0753016f, -10.793489f, 0f)),
        ["PlotTrackTarget"] = new(
            10f,
            new(14.991257f, 33.09991f, 40.56384f),
            new(-18.465744f, -2.6104999f, -0.89873964f)
        ),
        ["PlotQuestMini"] = new(10f, new(-8f, 10f, 45f), Vector3.Zero),
    }.ToFrozenDictionary(StringComparer.Ordinal);

    // Distinct seated cockpit slots for panels whose desktop defaults share a corner within a category or map view.
    private static readonly FrozenDictionary<string, Vector3> SeatedPositionOverrides = new Dictionary<string, Vector3>(
        StringComparer.Ordinal
    )
    {
        ["PlotColonizationWishList"] = new(-3.5f, -1.5f, 10.5f),
        ["PlotExplorationPois"] = new(6f, 2.2f, 9.5f),
        ["PlotFleetCarrierRoute"] = new(3.5f, 0f, 10.5f),
        ["PlotFSS"] = new(0f, 2.2f, 11f),
        ["PlotFSSInfo"] = new(-6f, 0f, 9.5f),
        ["PlotGalMap"] = new(-3.5f, 2.2f, 10.5f),
        ["PlotMassacre"] = new(6f, 2.2f, 9.5f),
        ["PlotPriorScans"] = new(-6f, 2.2f, 9.5f),
        ["PlotRouteBio"] = new(6f, -0.5f, 9.5f),
        ["PlotStationInfo"] = new(-6f, 2.2f, 9.5f),
    }.ToFrozenDictionary(StringComparer.Ordinal);

    public static bool IsLegacyBundledDefault(string plotterName, VrOverlayCalibration calibration) =>
        LegacyBundledDefaults.TryGetValue(plotterName, out VrOverlayCalibration? previousDefault)
        && calibration == previousDefault;

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
        Vector3 position = SeatedPositionOverrides.TryGetValue(definition.Name, out Vector3 overridePosition)
            ? overridePosition
            : GetCockpitPosition(definition.DefaultPlacement);
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
