using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Platform.Overlay;

public enum MiningBarState { Unknown, Absent, Present }

public sealed record MiningBarAnalysis(MiningBarState[] Slots, double OffsetX, double OffsetY)
{
    internal double[] BarScores { get; init; } = [];
    public static MiningBarAnalysis Unknown() => new(new MiningBarState[6], 0, 0);
}

/// <summary>Recognizes the six ellipse outlines, then tests local contrast along their lower bars.</summary>
public static class MiningBarDetector
{
    public static byte[][] CaptureReference(IFssPixelSource pixels, MiningDetectionSettings settings) =>
        MiningHudReference.Capture(pixels, settings.Normalize());

    public static MiningBarAnalysis Analyze(IFssPixelSource pixels, MiningDetectionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentNullException.ThrowIfNull(settings);
        settings = settings.Normalize();
        if (settings.LabelTemplates is null) return MiningBarAnalysis.Unknown();
        var image = new MiningHudReference.GrayImage(pixels, settings.CircleWidth);
        var matches = MiningHudReference.Locate(image, settings);
        if (matches is null) return MiningBarAnalysis.Unknown();
        var states = new MiningBarState[6];
        var scores = new double[6];
        for (var slot = 0; slot < 6; slot++)
        {
            var match = matches[slot];
            var radius = image.Radius * match.Scale;
            var best = ScoreBar(image, match.X, match.Y, radius, settings);
            states[slot] = double.IsNaN(best) ? MiningBarState.Unknown : best >= .82 ? MiningBarState.Present
                : best >= .7 ? MiningBarState.Unknown : MiningBarState.Absent;
            scores[slot] = best;
        }
        return new(states, matches[0].X - settings.Markers[0].X * image.Width,
            matches[0].Y - settings.Markers[0].Y * image.Height)
        { BarScores = scores };
    }

    internal static double ScoreBar(IFssPixelSource image, double x, double y, double radius, MiningDetectionSettings settings)
    {
        var geometry = new MiningHudGeometry(settings);
        var rim = MiningCircleMask.Locate(image, x, y, radius, geometry);
        if (rim.Confidence < 8) return double.NaN;
        // Mask the full rim thickness, not just its fitted centreline.
        var excludedRadius = rim.Radius + 1.5;
        var gap = radius * settings.BarGap;
        var best = 0d;
        for (var adjustment = -5; adjustment <= 5; adjustment++)
            for (var dx = -2; dx <= 2; dx++)
                foreach (var tilt in new[] { -.1, -.05, 0, .05, .1 })
                    foreach (var scale in new[] { .85, .925, 1, 1.075 })
                    {
                        var dy = gap + adjustment;
                        if (!MiningBarShape.IsOutsideRing(dx, dy, radius, scale, geometry, tilt)) continue;
                        if (!MiningBarShape.IsOutsideRing(
                            x + dx - rim.X, y + dy - rim.Y, excludedRadius, radius * scale / excludedRadius, geometry, tilt)) continue;
                        var lower = MiningBarShape.Score(image, x + dx, y + dy, radius * scale, geometry, tilt, lowerOnly: true);
                        if (lower > best && MiningBarShape.Score(image, x + dx, y + dy, radius * scale, geometry, tilt) >= .55)
                            best = lower;
                    }
        return best;
    }
}
/// <summary>Only emits transitions after a baseline, never treats lost visibility as a removed bar.</summary>
public sealed class MiningBarConfirmation
{
    private readonly MiningBarState[] stable = new MiningBarState[6];
    private readonly MiningBarState[] candidate = new MiningBarState[6];
    private readonly int[] counts = new int[6];
    public IReadOnlyList<MiningBarState> States => stable;
    public IReadOnlyList<int> Disappeared { get; private set; } = [];

    public int[] Apply(MiningBarAnalysis analysis)
    {
        var appeared = new List<int>();
        var disappeared = new List<int>();
        for (var i = 0; i < 6; i++)
        {
            var next = analysis.Slots[i];
            if (next == MiningBarState.Unknown)
            {
                counts[i] = 0;
                continue;
            }
            counts[i] = candidate[i] == next ? Math.Min(3, counts[i] + 1) : 1;
            candidate[i] = next;
            if (counts[i] < 3) continue;
            if (stable[i] == MiningBarState.Absent && next == MiningBarState.Present) appeared.Add(i + 1);
            if (stable[i] == MiningBarState.Present && next == MiningBarState.Absent) disappeared.Add(i + 1);
            stable[i] = next;
        }
        Disappeared = disappeared;
        return appeared.ToArray();
    }
}
