using Avalonia;

namespace SrvSurvey.Desktop.Platform.Overlay;

/// <summary>Locates the continuous upper rim so it cannot count as a deployment bar.</summary>
internal static class MiningCircleMask
{
    internal readonly record struct Rim(double X, double Y, double Radius, double Confidence);

    internal static Rim Locate(IFssPixelSource image, double x, double y, double radius, MiningHudGeometry geometry)
    {
        var best = new Rim(x, y, radius, 0);
        // The lower arc is deliberately excluded: that is where deployment bars live.
        var directions = Enumerable.Range(0, 19)
            .Select(i => geometry.RingPoint(-Math.PI + i * Math.PI / 18, 1)).ToArray();
        for (var dy = -2; dy <= 2; dy++)
            for (var dx = -2; dx <= 2; dx++)
                for (var step = 0; step <= 16; step++)
                {
                    var candidateRadius = radius * (.85 + step * .025);
                    var score = Score(image, x + dx, y + dy, candidateRadius, directions) - .01 * (dx * dx + dy * dy);
                    if (score > best.Confidence) best = new(x + dx, y + dy, candidateRadius, score);
                }
        return best;
    }

    private static double Score(IFssPixelSource image, double x, double y, double radius, Vector[] directions)
    {
        Span<double> sums = stackalloc double[3];
        Span<int> positive = stackalloc int[3];
        Span<int> negative = stackalloc int[3];
        sums.Clear(); positive.Clear(); negative.Clear();
        foreach (var direction in directions)
        {
            var center = Sample(image, x + direction.X * radius, y + direction.Y * radius);
            var inner = Sample(image, x + direction.X * (radius - 3), y + direction.Y * (radius - 3));
            var outer = Sample(image, x + direction.X * (radius + 3), y + direction.Y * (radius + 3));
            if (center is null || inner is null || outer is null) return 0;
            for (var channel = 0; channel < 3; channel++)
            {
                var ridge = Value(center.Value, channel) - (Value(inner.Value, channel) + Value(outer.Value, channel)) / 2;
                sums[channel] += ridge;
                if (ridge > 5) positive[channel]++;
                if (ridge < -5) negative[channel]++;
            }
        }
        var score = 0d;
        for (var channel = 0; channel < 3; channel++)
        {
            if (Math.Max(positive[channel], negative[channel]) < directions.Length * .7) continue;
            score = Math.Max(score, Math.Abs(sums[channel]) / directions.Length);
        }
        return score;
    }

    private static double Value(FssRgbPixel pixel, int channel) => channel switch
    { 0 => pixel.Red, 1 => pixel.Green, _ => pixel.Blue };

    private static FssRgbPixel? Sample(IFssPixelSource image, double x, double y)
    {
        var px = (int)Math.Round(x);
        var py = (int)Math.Round(y);
        return (uint)px < image.Width && (uint)py < image.Height ? image.GetPixel(px, py) : null;
    }
}
