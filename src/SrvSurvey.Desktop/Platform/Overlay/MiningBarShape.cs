using Avalonia;

namespace SrvSurvey.Desktop.Platform.Overlay;

internal static class MiningBarShape
{
    private const string EmptyRow = "...........................................................";

    // Binary shape from the recorded HUD: only geometry is retained, never reference RGB values.
    private static readonly string[] Mask =
    [
        EmptyRow,
        EmptyRow,
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
        EmptyRow,
        EmptyRow,
        EmptyRow,
        EmptyRow,
    ];
    private static readonly (double X, double Y, bool Filled)[] Samples = CreateSamples(false);
    private static readonly (double X, double Y, bool Filled)[] LowerSamples = CreateSamples(true);
    internal static (double X, double Y) Centroid { get; } =
        (Samples.Where(p => p.Filled).Average(p => p.X), Samples.Where(p => p.Filled).Average(p => p.Y));
    internal static IReadOnlyList<(double X, double Y)> GuidePoints { get; } =
        Enumerable
            .Range(0, Mask[0].Length)
            .Where(x => Mask.Any(row => row[x] == '#'))
            .Select(x =>
                ((x - 28) / 22d, (Enumerable.Range(0, Mask.Length).Where(y => Mask[y][x] == '#').Average() + 6) / 22d)
            )
            .ToArray();

    private static (double X, double Y, bool Filled)[] CreateSamples(bool lowerOnly)
    {
        var samples = new List<(double, double, bool)>();
        for (int y = 0; y < Mask.Length; y += 2)
        {
            for (int x = 0; x < Mask[y].Length; x += 2)
            {
                bool nearBar = IsNearBar(x, y);
                if (lowerOnly && Mask[y][x] != '#' && !IsBelowBar(x, y))
                {
                    continue;
                }

                double rx = (x - 28) / 22d;
                double ry = (y + 6) / 22d;
                // The inner ring and its changing white progress arc are not bar background.
                if (!lowerOnly && Mask[y][x] != '#' && rx * rx + ry * ry / (.65 * .65) < 1.3)
                {
                    continue;
                }

                if (nearBar)
                {
                    samples.Add(((x - 28) / 22d, (y + 6) / 22d, Mask[y][x] == '#'));
                }
            }
        }

        return samples.ToArray();
    }

    private static bool IsNearBar(int x, int y)
    {
        for (int ny = Math.Max(0, y - 3); ny <= Math.Min(Mask.Length - 1, y + 3); ny++)
        {
            for (int nx = Math.Max(0, x - 3); nx <= Math.Min(Mask[y].Length - 1, x + 3); nx++)
            {
                if (Mask[ny][nx] == '#')
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsBelowBar(int x, int y)
    {
        for (int ny = 0; ny < y; ny++)
        {
            if (Mask[ny][x] == '#')
            {
                return true;
            }
        }

        return false;
    }

    // Match bright chromatic pixels in the bar, not neutral rim brightness or dark gaps.
    public static double Score(
        IFssPixelSource source,
        double x,
        double y,
        double radius,
        MiningHudGeometry geometry,
        double tilt = 0,
        bool lowerOnly = false
    )
    {
        double sum = 0d;
        double square = 0d;
        double filledSum = 0d;
        int colored = 0;
        int filled = 0;
        (double X, double Y, bool Filled)[] samples = lowerOnly ? LowerSamples : Samples;
        foreach ((double X, double Y, bool Filled) sample in samples)
        {
            Vector offset = geometry.Transform(sample.X, sample.Y + sample.X * tilt, radius);
            int px = (int)Math.Round(x + offset.X);
            int py = (int)Math.Round(y + offset.Y);
            if ((uint)px >= source.Width || (uint)py >= source.Height)
            {
                return 0;
            }

            double value = ColoredBrightness(source.GetPixel(px, py));
            sum += value;
            square += value * value;
            if (sample.Filled)
            {
                filledSum += value;
                if (value > 0)
                {
                    colored++;
                }

                filled++;
            }
        }
        if (colored < filled * .6)
        {
            return 0;
        }

        int count = samples.Length;
        double maskVariance = filled - filled * filled / (double)count;
        double variance = square - sum * sum / count;
        if (variance < count * 25)
        {
            return 0;
        }

        double covariance = filledSum - filled * sum / count;
        return Math.Max(0, covariance / Math.Sqrt(maskVariance * variance));
    }

    internal static double ColoredBrightness(FssRgbPixel color)
    {
        byte maximum = Math.Max(color.Red, Math.Max(color.Green, color.Blue));
        byte minimum = Math.Min(color.Red, Math.Min(color.Green, color.Blue));
        int chroma = maximum - minimum;
        // Hue-independent: excludes black, white, gray and nearly neutral highlights.
        return maximum >= 96 && chroma >= 24 && chroma >= maximum * .2 ? chroma : 0;
    }
}
