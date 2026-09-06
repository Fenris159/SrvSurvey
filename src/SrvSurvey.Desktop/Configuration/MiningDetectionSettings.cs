using Avalonia;

namespace SrvSurvey.Desktop.Configuration;

public sealed record MiningDetectionPoint(double X, double Y);

public sealed record MiningDetectionSettings
{
    public const double ReferenceRotationDegrees = -8;
    public bool HasSameCalibration(MiningDetectionSettings other) => X == other.X && Y == other.Y
        && BarColor == other.BarColor
        && Width == other.Width && Height == other.Height && CircleWidth == other.CircleWidth
        && RotationDegrees == other.RotationDegrees && CircleAspectRatio == other.CircleAspectRatio
        && BarGap == other.BarGap
        && MotionMargin == other.MotionMargin && Markers.SequenceEqual(other.Markers);
    public bool Enabled { get; init; }
    public uint BarColor { get; init; } = 0x00FF00;
    public double X { get; init; } = 0.15;
    public double Y { get; init; } = 0.62;
    public double Width { get; init; } = 0.20;
    public double Height { get; init; } = 0.20;
    public double CircleWidth { get; init; } = 0.12;
    public double RotationDegrees { get; init; } = ReferenceRotationDegrees;
    public double CircleAspectRatio { get; init; } = .65;
    public double BarGap { get; init; } = .14;
    public double MotionMargin { get; init; } = 0.12;
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
        var circleWidth = Safe(CircleWidth, defaults.CircleWidth, .005, .6);
        return this with
        {
            Width = width,
            BarColor = BarColor & 0xFFFFFF,
            Height = height,
            X = Safe(X, defaults.X, 0, 1 - width),
            Y = Safe(Y, defaults.Y, 0, 1 - height),
            CircleWidth = circleWidth,
            RotationDegrees = Safe(RotationDegrees, defaults.RotationDegrees, -60, 60),
            CircleAspectRatio = Safe(CircleAspectRatio, .65, .3, 1),
            BarGap = Safe(BarGap, defaults.BarGap, 0, .6),
            MotionMargin = Safe(MotionMargin, defaults.MotionMargin, 0, 120d / GetWorkingWidth(circleWidth)),
            Markers = Markers is { Length: 6 }
                ? Markers.Select((p, i) => p is null ? defaults.Markers[i] : new MiningDetectionPoint(
                    Safe(p.X, defaults.Markers[i].X, 0, 1),
                    Safe(p.Y, defaults.Markers[i].Y, 0, 1))).ToArray()
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

    public static int GetWorkingWidth(double circleWidth) => Math.Clamp((int)Math.Round(44 / circleWidth), 128, 800);

    public double GetMovementAllowance(double frameWidth) => frameWidth * Math.Min(MotionMargin, 120d / GetWorkingWidth(CircleWidth));

    public MiningDetectionSettings WithBounds(PixelRect bounds, PixelRect viewport)
    {
        var current = Normalize();
        var old = current.GetBounds(viewport);
        var radius = current.CircleWidth * old.Width / 2;
        var minimumWidth = current.Markers.Max(p => p.X) * old.Width + radius * 1.5;
        var minimumHeight = current.Markers.Max(p => p.Y) * old.Height + radius * 1.5 + 24;
        var resized = (current with
        {
            X = (bounds.X - viewport.X) / (double)viewport.Width,
            Y = (bounds.Y - viewport.Y) / (double)viewport.Height,
            Width = Math.Max(minimumWidth, bounds.Width) / viewport.Width,
            Height = Math.Max(minimumHeight, bounds.Height) / viewport.Height,
        }).Normalize();
        var next = resized.GetBounds(viewport);
        return (resized with
        {
            CircleWidth = current.CircleWidth * old.Width / next.Width,
            MotionMargin = current.MotionMargin * old.Width / next.Width,
            Markers = current.Markers.Select(p => new MiningDetectionPoint(
                p.X * old.Width / next.Width, p.Y * old.Height / next.Height)).ToArray(),
        }).Normalize();
    }
}
