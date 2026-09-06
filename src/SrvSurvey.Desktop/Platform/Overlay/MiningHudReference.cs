using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Platform.Overlay;

/// <summary>Calibrated label shapes keep the six otherwise identical circles in their own slots.</summary>
internal static class MiningHudReference
{
    internal const int TemplateWidth = 16;
    internal const int TemplateHeight = 14;
    internal const int TemplateLength = TemplateWidth * TemplateHeight;
    private static readonly double[] Scales = [.8, .9, 1, 1.1, 1.2, 1.3];
    internal readonly record struct Match(double X, double Y, double Scale, double Score);

    internal static byte[][] Capture(IFssPixelSource source, MiningDetectionSettings settings)
    {
        var image = new GrayImage(source, settings.CircleWidth);
        var geometry = new MiningHudGeometry(settings);
        var result = new byte[6][];
        for (var slot = 0; slot < 6; slot++)
        {
            var p = settings.Markers[slot];
            var samples = new byte[TemplateLength];
            for (var y = 0; y < TemplateHeight; y++)
                for (var x = 0; x < TemplateWidth; x++)
                {
                    var offset = geometry.Transform(OffsetX(x), OffsetY(y), image.Radius);
                    var value = image.Sample(p.X * image.Width + offset.X, p.Y * image.Height + offset.Y);
                    if (value < 0) throw new InvalidOperationException("Keep all six circles and their labels inside the calibration frame.");
                    samples[y * TemplateWidth + x] = (byte)Math.Round(value);
                }
            if (samples.Max() - samples.Min() < 20)
                throw new InvalidOperationException("The HUD labels need more contrast or more accurate alignment.");
            result[slot] = samples;
        }
        return result;
    }

    internal static Match[]? Locate(GrayImage image, MiningDetectionSettings settings)
    {
        if (settings.LabelTemplates is not { Length: 6 } templates) return null;
        var margin = settings.GetMovementAllowance(image.Width);
        var geometry = new MiningHudGeometry(settings);
        var matches = new List<Match>[6];
        for (var slot = 0; slot < 6; slot++)
        {
            matches[slot] = FindMatches(image, templates[slot], settings.Markers[slot], margin, geometry);
            if (matches[slot].Count == 0) return null;
        }

        // The outer top labels establish a common pose. Every other label must agree with it;
        // a good isolated match cannot move a rig to a neighboring row or column.
        Match[]? best = null;
        var bestScore = .78 * 6;
        var origin = settings.Markers[0];
        var far = settings.Markers[2];
        var referenceDx = (far.X - origin.X) * image.Width;
        if (referenceDx < image.Radius * 2) return null;
        foreach (var first in matches[0])
            foreach (var third in matches[2])
            {
                var scale = (third.X - first.X) / referenceDx;
                if (scale < .78 || scale > 1.35) continue;
                var shear = (third.Y - first.Y - (far.Y - origin.Y) * image.Height * scale) / referenceDx;
                if (Math.Abs(shear) > .22) continue;
                var group = new Match[6];
                var score = 0d;
                for (var slot = 0; slot < 6; slot++)
                {
                    var marker = settings.Markers[slot];
                    var dx = (marker.X - origin.X) * image.Width;
                    var x = first.X + dx * scale;
                    var y = first.Y + (marker.Y - origin.Y) * image.Height * scale + dx * shear;
                    var nearby = matches[slot].Where(m => Math.Abs(m.X - x) <= 7 && Math.Abs(m.Y - y) <= 7
                        && Math.Abs(m.Scale - scale) <= .22).OrderByDescending(m => m.Score).FirstOrDefault();
                    if (nearby.Score < .7) break;
                    var adjustment = geometry.Transform(28 / 22d, 24 / 22d, image.Radius * (scale - nearby.Scale));
                    group[slot] = nearby with
                    {
                        X = nearby.X + adjustment.X,
                        Y = nearby.Y + adjustment.Y,
                        Scale = scale,
                    };
                    score += nearby.Score;
                }
                if (score <= bestScore) continue;
                bestScore = score;
                best = group;
            }
        return best;
    }

    private static List<Match> FindMatches(GrayImage image, byte[] template, MiningDetectionPoint marker, double margin,
        MiningHudGeometry geometry)
    {
        var candidates = new List<Match>();
        var fullTemplates = new Dictionary<double, ((double X, double Y, double Value)[] Samples, double Variance)>();
        foreach (var scale in Scales)
        {
            var samples = new List<(double X, double Y, double Value)>();
            for (var y = 0; y < TemplateHeight; y++)
                for (var x = 0; x < TemplateWidth; x++)
                {
                    var offset = geometry.Transform(OffsetX(x), OffsetY(y), image.Radius * scale);
                    samples.Add((offset.X, offset.Y, template[y * TemplateWidth + x]));
                }
            var mean = samples.Average(p => p.Value);
            var centered = samples.Select(p => (p.X, p.Y, Value: p.Value - mean)).ToArray();
            var variance = centered.Sum(p => p.Value * p.Value);
            if (variance < centered.Length * 16) continue;
            fullTemplates[scale] = (centered, variance);
            var sparse = samples.Where((_, i) => i % TemplateWidth % 2 == 0 && i / TemplateWidth % 2 == 0).ToArray();
            var sparseMean = sparse.Average(p => p.Value);
            var sparseCentered = sparse.Select(p => (p.X, p.Y, Value: p.Value - sparseMean)).ToArray();
            var sparseVariance = sparseCentered.Sum(p => p.Value * p.Value);
            for (var y = (int)Math.Max(0, marker.Y * image.Height - margin); y <= Math.Min(image.Height - 1, marker.Y * image.Height + margin); y++)
                for (var x = (int)Math.Max(0, marker.X * image.Width - margin); x <= Math.Min(image.Width - 1, marker.X * image.Width + margin); x++)
                {
                    var score = Correlate(image, sparseCentered, sparseVariance, x, y);
                    if (score >= .55) candidates.Add(new(x, y, scale, score));
                }
        }
        var seeds = new List<Match>();
        foreach (var candidate in candidates.OrderByDescending(c => c.Score))
        {
            if (seeds.Any(p => Math.Abs(p.X - candidate.X) < 3 && Math.Abs(p.Y - candidate.Y) < 3
                && p.Scale == candidate.Scale)) continue;
            seeds.Add(candidate);
            if (seeds.Count == 100) break;
        }
        candidates.Clear();
        foreach (var seed in seeds)
        {
            var full = fullTemplates[seed.Scale];
            for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    var score = Correlate(image, full.Samples, full.Variance, seed.X + dx, seed.Y + dy);
                    if (score >= .65) candidates.Add(new(seed.X + dx, seed.Y + dy, seed.Scale, score));
                }
        }
        var peaks = new List<Match>();
        foreach (var candidate in candidates.OrderByDescending(c => c.Score))
        {
            if (peaks.Any(p => Math.Abs(p.X - candidate.X) < 5 && Math.Abs(p.Y - candidate.Y) < 5)) continue;
            peaks.Add(candidate);
            if (peaks.Count == 12) break;
        }
        return peaks;
    }

    private static double Correlate(GrayImage image, (double X, double Y, double Value)[] samples,
        double templateVariance, double x, double y)
    {
        var sum = 0d;
        var square = 0d;
        var cross = 0d;
        foreach (var sample in samples)
        {
            var value = image.Sample(x + sample.X, y + sample.Y);
            if (value < 0) return 0;
            sum += value;
            square += value * value;
            cross += value * sample.Value;
        }
        var variance = square - sum * sum / samples.Length;
        return variance < samples.Length * 16 ? 0 : Math.Abs(cross) / Math.Sqrt(variance * templateVariance);
    }

    private static double OffsetX(int x) => (-28 + x) / 22d;
    private static double OffsetY(int y) => (-24 + y) / 22d;

    internal sealed class GrayImage : IFssPixelSource
    {
        private readonly double[] pixels;
        private readonly FssRgbPixel[] colors;
        public int Width { get; }
        public int Height { get; }
        public double Radius { get; }
        internal GrayImage(IFssPixelSource source, double circleWidth)
        {
            Width = MiningDetectionSettings.GetWorkingWidth(circleWidth);
            Height = Math.Max(1, (int)Math.Round(source.Height * Width / (double)source.Width));
            Radius = Width * circleWidth / 2;
            pixels = new double[Width * Height];
            colors = new FssRgbPixel[Width * Height];
            for (var y = 0; y < Height; y++)
                for (var x = 0; x < Width; x++)
                {
                    var p = source.GetPixel(Math.Min(source.Width - 1, (int)(x * source.Width / (double)Width)),
                        Math.Min(source.Height - 1, (int)(y * source.Height / (double)Height)));
                    pixels[y * Width + x] = (p.Red + p.Green + p.Blue) / 3d;
                    colors[y * Width + x] = p;
                }
        }
        internal double Sample(double x, double y)
        {
            var ix = (int)Math.Floor(x);
            var iy = (int)Math.Floor(y);
            if (ix < 0 || iy < 0 || ix + 1 >= Width || iy + 1 >= Height) return -1;
            var fx = x - ix;
            var fy = y - iy;
            return (pixels[iy * Width + ix] * (1 - fx) + pixels[iy * Width + ix + 1] * fx) * (1 - fy)
                + (pixels[(iy + 1) * Width + ix] * (1 - fx) + pixels[(iy + 1) * Width + ix + 1] * fx) * fy;
        }
        public FssRgbPixel GetPixel(int x, int y)
        {
            return colors[y * Width + x];
        }
    }
}
