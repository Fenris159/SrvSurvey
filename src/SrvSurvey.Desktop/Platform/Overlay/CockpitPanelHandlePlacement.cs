using Avalonia;

namespace SrvSurvey.Desktop.Platform.Overlay;

/// <summary>Docks the editor's control stack outside its frame while retaining a reachable display position.</summary>
internal static class CockpitPanelHandlePlacement
{
    private static readonly int[] EdgeOrder = [1, 3, 2, 0];

    internal static Point GetOrigin(IReadOnlyList<Point> outline, IReadOnlyList<Point> chrome, Size size, Rect display)
    {
        var interior = new Point(outline.Average(point => point.X), outline.Average(point => point.Y));
        Point? preferred = null;
        Point? best = null;
        double bestDistance = double.PositiveInfinity;
        foreach (int index in EdgeOrder)
        {
            Point end = outline[(index + 1) % outline.Count];
            Vector edge = end - outline[index];
            if (edge.Length < .001)
            {
                continue;
            }
            Vector along = edge / edge.Length;
            var outward = new Vector(along.Y, -along.X);
            if (Dot(outward, interior - end) > 0)
            {
                outward = -outward;
            }
            double chromeExtent = Math.Max(0, chrome.Max(point => Dot(outward, point - end)));
            double clearance = Support(outward, size) + chromeExtent + 8;
            Point origin =
                end + outward * clearance - along * Support(along, size) - new Vector(size.Width / 2, size.Height / 2);
            preferred ??= origin;
            double minimum = double.NegativeInfinity;
            double maximum = double.PositiveInfinity;
            if (
                !ConstrainAxis(origin.X, along.X, display.Left, display.Right - size.Width, ref minimum, ref maximum)
                || !ConstrainAxis(
                    origin.Y,
                    along.Y,
                    display.Top,
                    display.Bottom - size.Height,
                    ref minimum,
                    ref maximum
                )
            )
            {
                continue;
            }
            Point candidate = origin + along * Math.Clamp(0, minimum, maximum);
            Vector change = candidate - preferred.Value;
            double distance = Dot(change, change);
            if (distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }
        if (best is { } reachable)
        {
            return reachable;
        }
        // An oversized frame can leave no exterior space. Retain access to the controls.
        Point fallback = preferred ?? display.TopLeft;
        return new Point(
            Math.Clamp(fallback.X, display.Left, Math.Max(display.Left, display.Right - size.Width)),
            Math.Clamp(fallback.Y, display.Top, Math.Max(display.Top, display.Bottom - size.Height))
        );
    }

    private static bool ConstrainAxis(
        double origin,
        double direction,
        double minimum,
        double maximum,
        ref double low,
        ref double high
    )
    {
        if (maximum < minimum)
        {
            return false;
        }
        if (Math.Abs(direction) < .000001)
        {
            return origin >= minimum && origin <= maximum;
        }
        double first = (minimum - origin) / direction;
        double second = (maximum - origin) / direction;
        low = Math.Max(low, Math.Min(first, second));
        high = Math.Min(high, Math.Max(first, second));
        return low <= high;
    }

    private static double Support(Vector axis, Size size) =>
        (Math.Abs(axis.X) * size.Width + Math.Abs(axis.Y) * size.Height) / 2;

    private static double Dot(Vector first, Vector second) => first.X * second.X + first.Y * second.Y;
}
