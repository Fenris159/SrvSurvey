using Avalonia;
using Avalonia.Media;

namespace SrvSurvey.Desktop.Controls;

internal static class GuardianSurveyMarkerDrawing
{
    internal static readonly Color HaloColor =
        Color.FromArgb(160, 72, 61, 139);
    internal static readonly Color RingColor =
        Color.FromArgb(96, 0, 255, 255);

    private static readonly IBrush HaloBrush =
        new SolidColorBrush(HaloColor);
    private static readonly IBrush RingBrush =
        new SolidColorBrush(RingColor);

    public static void Draw(
        DrawingContext context,
        Point center,
        double haloRadius,
        double ringRadius,
        double dotRadius)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.DrawEllipse(
            HaloBrush,
            null,
            center,
            haloRadius,
            haloRadius);
        foreach (var dotCenter in CreateDotCenters(
                     center,
                     ringRadius,
                     dotRadius))
        {
            context.DrawEllipse(
                RingBrush,
                null,
                dotCenter,
                dotRadius,
                dotRadius);
        }
    }

    internal static IReadOnlyList<Point> CreateDotCenters(
        Point center,
        double ringRadius,
        double dotRadius)
    {
        if (!double.IsFinite(ringRadius) || ringRadius <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ringRadius));
        }

        if (!double.IsFinite(dotRadius) || dotRadius <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dotRadius));
        }

        var circumference = 2 * Math.PI * ringRadius;
        var dotCount = Math.Max(
            8,
            (int)Math.Round(circumference / (dotRadius * 4)));
        return Enumerable.Range(0, dotCount)
            .Select(index =>
            {
                var angle = index * 2 * Math.PI / dotCount;
                return new Point(
                    center.X + Math.Cos(angle) * ringRadius,
                    center.Y + Math.Sin(angle) * ringRadius);
            })
            .ToArray();
    }
}
