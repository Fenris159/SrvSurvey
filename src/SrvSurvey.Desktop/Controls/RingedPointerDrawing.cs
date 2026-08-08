using Avalonia;
using Avalonia.Media;

namespace SrvSurvey.Desktop.Controls;

internal static class RingedPointerDrawing
{
    public static void Draw(
        DrawingContext context,
        Point center,
        double size,
        double bearingDegrees,
        IBrush brush,
        double strokeThickness)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(brush);
        if (!double.IsFinite(size) || size <= 0)
        {
            return;
        }

        var maximumThickness = Math.Max(0.5, size / 4);
        var thickness = double.IsFinite(strokeThickness)
            ? Math.Clamp(strokeThickness, 0.5, maximumThickness)
            : 1.5;
        var radius = Math.Max(1, (size - thickness) / 2);
        var angle = double.IsFinite(bearingDegrees) ? bearingDegrees : 0;
        var radians = angle * Math.PI / 180d;

        context.DrawEllipse(
            null,
            new Pen(brush, thickness),
            center,
            radius,
            radius);

        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(
                Rotate(center, 0, -radius * 1.08, radians),
                isFilled: true);
            geometryContext.LineTo(Rotate(
                center,
                radius * 0.52,
                radius * 0.62,
                radians));
            geometryContext.LineTo(Rotate(
                center,
                0,
                radius * 0.3,
                radians));
            geometryContext.LineTo(Rotate(
                center,
                -radius * 0.52,
                radius * 0.62,
                radians));
            geometryContext.EndFigure(isClosed: true);
        }

        context.DrawGeometry(
            brush,
            new Pen(brush, Math.Max(0.5, thickness / 2)),
            geometry);
    }

    private static Point Rotate(
        Point center,
        double x,
        double y,
        double radians)
    {
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        return new Point(
            center.X + x * cosine - y * sine,
            center.Y + x * sine + y * cosine);
    }
}
