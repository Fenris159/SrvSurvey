using System.Numerics;

namespace SrvSurvey.Desktop.Platform.Overlay;

/// <summary>Seated, head-origin starting points from the Quest 3 / VDXR field report (#264).</summary>
internal static class VrSeatedCalibrationDefaults
{
    public static VrOverlayCalibration Get(string plotterName) =>
        plotterName switch
        {
            "PlotJumpInfo" => new(8, new Vector3(0, 4.5f, 11), new Vector3(-20, 0, 0)),
            "PlotBioStatus" => new(5, new Vector3(0, 3.5f, 10), new Vector3(-15, 0, 0)),
            "PlotSysStatus" => new(3.5f, new Vector3(-5.5f, -3, 9), new Vector3(15, -25, 0)),
            "PlotBioSystem" => new(5, new Vector3(-6.5f, 0.5f, 9), new Vector3(0, -35, 0)),
            _ => new(5, new Vector3(0, 0, 10), Vector3.Zero),
        };
}
