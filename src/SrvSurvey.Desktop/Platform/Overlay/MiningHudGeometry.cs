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

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "S1244",
        Justification = "Only the exact recorded calibration can use the identity transform; any explicit user adjustment must use the calculated transform."
    )]
    internal MiningHudGeometry(MiningDetectionSettings settings)
    {
        // The recorded mask already has this perspective tilt. Undo its basis before
        // applying the requested oval height and absolute rotation, preserving old calibrations.
        if (
            settings.RotationDegrees.Equals(MiningDetectionSettings.ReferenceRotationDegrees)
            && settings.CircleAspectRatio.Equals(.65)
        )
        {
            xx = yy = 1;
            xy = yx = 0;
            return;
        }
        double angle = settings.RotationDegrees * Math.PI / 180;
        double reference = MiningDetectionSettings.ReferenceRotationDegrees * Math.PI / 180;
        double c = Math.Cos(angle);
        double s = Math.Sin(angle);
        double cb = Math.Cos(reference);
        double sb = Math.Sin(reference);
        double height = settings.CircleAspectRatio / .65;
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
        double reference = MiningDetectionSettings.ReferenceRotationDegrees * Math.PI / 180;
        double x = Math.Cos(angle);
        double y = .65 * Math.Sin(angle);
        return Transform(
            x * Math.Cos(reference) - y * Math.Sin(reference),
            x * Math.Sin(reference) + y * Math.Cos(reference),
            radius
        );
    }

    internal double RingDistance(double x, double y, double radius)
    {
        double determinant = xx * yy - xy * yx;
        double rx = (yy * x - xy * y) / determinant / radius;
        double ry = (-yx * x + xx * y) / determinant / radius;
        double reference = MiningDetectionSettings.ReferenceRotationDegrees * Math.PI / 180;
        double horizontal = rx * Math.Cos(reference) + ry * Math.Sin(reference);
        double vertical = -rx * Math.Sin(reference) + ry * Math.Cos(reference);
        return Math.Sqrt(horizontal * horizontal + vertical * vertical / (.65 * .65));
    }
}
