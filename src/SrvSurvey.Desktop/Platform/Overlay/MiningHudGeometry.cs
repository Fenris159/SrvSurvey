using Avalonia;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Platform.Overlay;

/// <summary>Shared local geometry for the calibration guides, label capture and bar sampling.</summary>
internal readonly struct MiningHudGeometry
{
    private readonly double xx;
    private readonly double xy;
    private readonly double yx;
    private readonly double yy;

    internal MiningHudGeometry(MiningDetectionSettings settings)
    {
        // The recorded mask already has this perspective tilt. Undo its basis before
        // applying the requested oval height and absolute rotation, preserving old calibrations.
        if (settings.RotationDegrees == MiningDetectionSettings.ReferenceRotationDegrees && settings.CircleAspectRatio == .65)
        {
            xx = yy = 1;
            xy = yx = 0;
            return;
        }
        var angle = settings.RotationDegrees * Math.PI / 180;
        var reference = MiningDetectionSettings.ReferenceRotationDegrees * Math.PI / 180;
        var c = Math.Cos(angle);
        var s = Math.Sin(angle);
        var cb = Math.Cos(reference);
        var sb = Math.Sin(reference);
        var height = settings.CircleAspectRatio / .65;
        xx = c * cb + s * height * sb;
        xy = c * sb - s * height * cb;
        yx = s * cb - c * height * sb;
        yy = s * sb + c * height * cb;
    }

    internal Vector Transform(double x, double y, double radius)
    {
        return new((x * xx + y * xy) * radius, (x * yx + y * yy) * radius);
    }

    internal Vector RingPoint(double angle, double radius)
    {
        var reference = MiningDetectionSettings.ReferenceRotationDegrees * Math.PI / 180;
        var x = Math.Cos(angle);
        var y = .65 * Math.Sin(angle);
        return Transform(x * Math.Cos(reference) - y * Math.Sin(reference),
            x * Math.Sin(reference) + y * Math.Cos(reference), radius);
    }
}
