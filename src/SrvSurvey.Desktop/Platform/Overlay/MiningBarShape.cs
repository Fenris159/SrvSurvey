namespace SrvSurvey.Desktop.Platform.Overlay;

internal static class MiningBarShape
{
    // Binary shape from the recorded HUD: only geometry is retained, never reference RGB values.
    private static readonly string[] Mask =
    [
        "...........................................................",
        "...........................................................",
        "..............................................####.........",
        "..............................................####.........",
        "...........................................###.##..........",
        "........#.................................#####............",
        "......######...........................#######.............",
        "......########........................#####................",
        "........########..................########.................",
        "..........#########.###.##############.#...................",
        ".............########################......................",
        "................##.##############..........................",
        ".......................##..................................",
        "...........................................................",
        "...........................................................",
        "...........................................................",
        "...........................................................",
    ];
    private static readonly (double X, double Y, bool Filled)[] Samples = CreateSamples(false);
    private static readonly (double X, double Y, bool Filled)[] LowerSamples = CreateSamples(true);
    internal static bool IsOutsideRing(double dx, double dy, double radius, double scale, MiningHudGeometry geometry, double tilt)
    {
        // A bright circle rim can resemble the lower arc. Require most filled samples
        // to sit beyond the calibrated ellipse, with a small allowance for alignment error.
        var outside = 0;
        var filled = 0;
        foreach (var sample in Samples)
        {
            if (!sample.Filled) continue;
            filled++;
            var point = geometry.Transform(sample.X, sample.Y + sample.X * tilt, radius * scale);
            if (geometry.RingDistance(point.X + dx, point.Y + dy, radius) > 1.05) outside++;
        }
        return outside >= filled * .8;
    }
    internal static IReadOnlyList<(double X, double Y)> GuidePoints { get; } =
        Enumerable.Range(0, Mask[0].Length)
            .Where(x => Mask.Any(row => row[x] == '#'))
            .Select(x => ((x - 28) / 22d,
                (Enumerable.Range(0, Mask.Length).Where(y => Mask[y][x] == '#').Average() + 6) / 22d))
            .ToArray();
    private static (double X, double Y, bool Filled)[] CreateSamples(bool lowerOnly)
    {
        var samples = new List<(double, double, bool)>();
        for (var y = 0; y < Mask.Length; y += 2)
            for (var x = 0; x < Mask[y].Length; x += 2)
            {
                var nearBar = false;
                for (var ny = Math.Max(0, y - 3); ny <= Math.Min(Mask.Length - 1, y + 3); ny++)
                    for (var nx = Math.Max(0, x - 3); nx <= Math.Min(Mask[y].Length - 1, x + 3); nx++)
                        nearBar |= Mask[ny][nx] == '#';
                var belowBar = false;
                for (var ny = 0; ny < y; ny++) belowBar |= Mask[ny][x] == '#';
                if (lowerOnly && Mask[y][x] != '#' && !belowBar) continue;
                var rx = (x - 28) / 22d;
                var ry = (y + 6) / 22d;
                // The inner ring and its changing white progress arc are not bar background.
                if (!lowerOnly && Mask[y][x] != '#' && rx * rx + ry * ry / (.65 * .65) < 1.3) continue;
                if (nearBar) samples.Add(((x - 28) / 22d, (y + 6) / 22d, Mask[y][x] == '#'));
            }
        return samples.ToArray();
    }

    // Correlate the binary mask in each channel and either polarity: gray, colored and inverted bars.
    public static double Score(IFssPixelSource source, double x, double y, double radius, MiningHudGeometry geometry,
        double tilt = 0, bool lowerOnly = false)
    {
        Span<double> sum = stackalloc double[6];
        Span<double> square = stackalloc double[6];
        Span<double> filledSum = stackalloc double[6];
        sum.Clear(); square.Clear(); filledSum.Clear();
        var filled = 0;
        var samples = lowerOnly ? LowerSamples : Samples;
        foreach (var sample in samples)
        {
            var offset = geometry.Transform(sample.X, sample.Y + sample.X * tilt, radius);
            var px = (int)Math.Round(x + offset.X);
            var py = (int)Math.Round(y + offset.Y);
            if ((uint)px >= source.Width || (uint)py >= source.Height) return 0;
            var color = source.GetPixel(px, py);
            for (var c = 0; c < 6; c++)
            {
                double value = c switch
                {
                    0 => color.Red,
                    1 => color.Green,
                    2 => color.Blue,
                    3 => color.Red - color.Green,
                    4 => color.Green - color.Blue,
                    _ => color.Blue - color.Red,
                };
                sum[c] += value;
                square[c] += value * value;
                if (sample.Filled) filledSum[c] += value;
            }
            if (sample.Filled) filled++;
        }
        var count = samples.Length;
        var maskVariance = filled - filled * filled / (double)count;
        var score = 0d;
        for (var c = 0; c < 6; c++)
        {
            var variance = square[c] - sum[c] * sum[c] / count;
            if (variance < count * 25) continue;
            var covariance = filledSum[c] - filled * sum[c] / count;
            score = Math.Max(score, Math.Abs(covariance) / Math.Sqrt(maskVariance * variance));
        }
        return score;
    }
}
