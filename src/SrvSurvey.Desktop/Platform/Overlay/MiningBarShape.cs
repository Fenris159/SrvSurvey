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
    internal static (double X, double Y) Centroid { get; } =
        (Samples.Where(p => p.Filled).Average(p => p.X), Samples.Where(p => p.Filled).Average(p => p.Y));
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

    // Match bright chromatic pixels in the bar, not neutral rim brightness or dark gaps.
    public static double Score(IFssPixelSource source, double x, double y, double radius, MiningHudGeometry geometry,
        double tilt = 0, bool lowerOnly = false)
    {
        var sum = 0d;
        var square = 0d;
        var filledSum = 0d;
        var colored = 0;
        var filled = 0;
        var samples = lowerOnly ? LowerSamples : Samples;
        foreach (var sample in samples)
        {
            var offset = geometry.Transform(sample.X, sample.Y + sample.X * tilt, radius);
            var px = (int)Math.Round(x + offset.X);
            var py = (int)Math.Round(y + offset.Y);
            if ((uint)px >= source.Width || (uint)py >= source.Height) return 0;
            var value = ColoredBrightness(source.GetPixel(px, py));
            sum += value;
            square += value * value;
            if (sample.Filled)
            {
                filledSum += value;
                if (value > 0) colored++;
                filled++;
            }
        }
        if (colored < filled * .6) return 0;
        var count = samples.Length;
        var maskVariance = filled - filled * filled / (double)count;
        var variance = square - sum * sum / count;
        if (variance < count * 25) return 0;
        var covariance = filledSum - filled * sum / count;
        return Math.Max(0, covariance / Math.Sqrt(maskVariance * variance));
    }

    internal static double ColoredBrightness(FssRgbPixel color)
    {
        var maximum = Math.Max(color.Red, Math.Max(color.Green, color.Blue));
        var minimum = Math.Min(color.Red, Math.Min(color.Green, color.Blue));
        var chroma = maximum - minimum;
        // Hue-independent: excludes black, white, gray and nearly neutral highlights.
        return maximum >= 96 && chroma >= 24 && chroma >= maximum * .2 ? chroma : 0;
    }
}
