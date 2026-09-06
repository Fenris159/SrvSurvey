using Avalonia;

namespace SrvSurvey.Desktop.Configuration;

public sealed record MiningDetectionPoint(double X, double Y);

public sealed record MiningDetectionSettings
{
    public bool HasSameCalibration(MiningDetectionSettings other) => X == other.X && Y == other.Y
        && Width == other.Width && Height == other.Height && CircleWidth == other.CircleWidth
        && MotionMargin == other.MotionMargin && Markers.SequenceEqual(other.Markers)
        && (ReferenceEquals(LabelTemplates, other.LabelTemplates)
            || LabelTemplates is not null && other.LabelTemplates is not null
            && LabelTemplates.Length == other.LabelTemplates.Length
            && LabelTemplates.Zip(other.LabelTemplates).All(p => p.First.SequenceEqual(p.Second)));
    public bool Enabled { get; init; }
    public double X { get; init; } = 0.15;
    public double Y { get; init; } = 0.62;
    public double Width { get; init; } = 0.20;
    public double Height { get; init; } = 0.20;
    public double CircleWidth { get; init; } = 0.12;
    public double MotionMargin { get; init; } = 0.12;
    public byte[][]? LabelTemplates { get; init; }
    public MiningDetectionPoint[] Markers { get; init; } =
    [new(.30, .40), new(.48, .36), new(.66, .32),
     new(.30, .62), new(.48, .58), new(.66, .54)];

    public MiningDetectionSettings Normalize()
    {
        static double Safe(double value, double fallback, double min, double max) =>
            double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
        var defaults = new MiningDetectionSettings();
        var width = Safe(Width, defaults.Width, .05, .6);
        var height = Safe(Height, defaults.Height, .05, .6);
        return this with
        {
            Width = width,
            Height = height,
            X = Safe(X, defaults.X, 0, 1 - width),
            Y = Safe(Y, defaults.Y, 0, 1 - height),
            CircleWidth = Safe(CircleWidth, defaults.CircleWidth, .04, .30),
            MotionMargin = Safe(MotionMargin, defaults.MotionMargin, .02, .25),
            LabelTemplates = LabelTemplates is { Length: 6 } labels && labels.All(p => p is { Length: 224 })
                ? labels : null,
            Markers = Markers is { Length: 6 }
                ? Markers.Select((p, i) => p is null ? defaults.Markers[i] : new MiningDetectionPoint(
                    Safe(p.X, defaults.Markers[i].X, .05, .95),
                    Safe(p.Y, defaults.Markers[i].Y, .05, .95))).ToArray()
                : defaults.Markers,
        };
    }

    public PixelRect GetBounds(PixelRect viewport)
    {
        var value = Normalize();
        return new PixelRect(viewport.X + (int)Math.Round(value.X * viewport.Width),
            viewport.Y + (int)Math.Round(value.Y * viewport.Height),
            Math.Max(1, (int)Math.Round(value.Width * viewport.Width)),
            Math.Max(1, (int)Math.Round(value.Height * viewport.Height)));
    }

    public MiningDetectionSettings WithBounds(PixelRect bounds, PixelRect viewport) =>
        (this with
        {
            X = (bounds.X - viewport.X) / (double)viewport.Width,
            Y = (bounds.Y - viewport.Y) / (double)viewport.Height,
            Width = bounds.Width / (double)viewport.Width,
            Height = bounds.Height / (double)viewport.Height,
        }).Normalize();
}
