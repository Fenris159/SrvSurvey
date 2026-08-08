using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Controls;

public sealed class BiologyRewardBandControl : Control
{
    public static readonly StyledProperty<long> MinimumRewardProperty =
        AvaloniaProperty.Register<BiologyRewardBandControl, long>(
            nameof(MinimumReward));
    public static readonly StyledProperty<long> MaximumRewardProperty =
        AvaloniaProperty.Register<BiologyRewardBandControl, long>(
            nameof(MaximumReward),
            -1);
    public static readonly StyledProperty<double> BucketOneMillionsProperty =
        AvaloniaProperty.Register<BiologyRewardBandControl, double>(
            nameof(BucketOneMillions),
            3);
    public static readonly StyledProperty<double> BucketTwoMillionsProperty =
        AvaloniaProperty.Register<BiologyRewardBandControl, double>(
            nameof(BucketTwoMillions),
            7);
    public static readonly StyledProperty<double> BucketThreeMillionsProperty =
        AvaloniaProperty.Register<BiologyRewardBandControl, double>(
            nameof(BucketThreeMillions),
            12);
    public static readonly StyledProperty<bool> IsPredictionProperty =
        AvaloniaProperty.Register<BiologyRewardBandControl, bool>(
            nameof(IsPrediction));
    public static readonly StyledProperty<bool> IsHighlightedProperty =
        AvaloniaProperty.Register<BiologyRewardBandControl, bool>(
            nameof(IsHighlighted));
    public static readonly StyledProperty<IBrush?> FilledBrushProperty =
        AvaloniaProperty.Register<BiologyRewardBandControl, IBrush?>(
            nameof(FilledBrush));
    public static readonly StyledProperty<IBrush?> PotentialBrushProperty =
        AvaloniaProperty.Register<BiologyRewardBandControl, IBrush?>(
            nameof(PotentialBrush));
    public static readonly StyledProperty<IBrush?> DimmedFilledBrushProperty =
        AvaloniaProperty.Register<BiologyRewardBandControl, IBrush?>(
            nameof(DimmedFilledBrush));
    public static readonly StyledProperty<IBrush?> PredictionFilledBrushProperty =
        AvaloniaProperty.Register<BiologyRewardBandControl, IBrush?>(
            nameof(PredictionFilledBrush));
    public static readonly StyledProperty<IBrush?> PredictionPotentialBrushProperty =
        AvaloniaProperty.Register<BiologyRewardBandControl, IBrush?>(
            nameof(PredictionPotentialBrush));
    public static readonly StyledProperty<IBrush?> HighlightBrushProperty =
        AvaloniaProperty.Register<BiologyRewardBandControl, IBrush?>(
            nameof(HighlightBrush));
    public static readonly StyledProperty<IBrush?> DimmedHighlightBrushProperty =
        AvaloniaProperty.Register<BiologyRewardBandControl, IBrush?>(
            nameof(DimmedHighlightBrush));
    public static readonly StyledProperty<IBrush?> EdgeBrushProperty =
        AvaloniaProperty.Register<BiologyRewardBandControl, IBrush?>(
            nameof(EdgeBrush));
    public static readonly StyledProperty<IBrush?> PredictionBrushProperty =
        AvaloniaProperty.Register<BiologyRewardBandControl, IBrush?>(
            nameof(PredictionBrush));
    public static readonly StyledProperty<IBrush?> UnknownBrushProperty =
        AvaloniaProperty.Register<BiologyRewardBandControl, IBrush?>(
            nameof(UnknownBrush));
    public static readonly StyledProperty<IBrush?> HatchBrushProperty =
        AvaloniaProperty.Register<BiologyRewardBandControl, IBrush?>(
            nameof(HatchBrush));
    public static readonly StyledProperty<IBrush?> EmptyBrushProperty =
        AvaloniaProperty.Register<BiologyRewardBandControl, IBrush?>(
            nameof(EmptyBrush));
    public static readonly StyledProperty<bool> IsDimmedProperty =
        AvaloniaProperty.Register<BiologyRewardBandControl, bool>(
            nameof(IsDimmed));

    // Immutable brush is free-threaded; a static SolidColorBrush would pin to the
    // first UI thread that touches it and break parallel Avalonia rendering tests.
    private static readonly IBrush DefaultEmptyBrush =
        new ImmutableSolidColorBrush(Color.FromArgb(40, 255, 255, 255));

    static BiologyRewardBandControl()
    {
        AffectsRender<BiologyRewardBandControl>(
            MinimumRewardProperty,
            MaximumRewardProperty,
            BucketOneMillionsProperty,
            BucketTwoMillionsProperty,
            BucketThreeMillionsProperty,
            IsPredictionProperty,
            IsHighlightedProperty,
            FilledBrushProperty,
            PotentialBrushProperty,
            DimmedFilledBrushProperty,
            PredictionFilledBrushProperty,
            PredictionPotentialBrushProperty,
            HighlightBrushProperty,
            DimmedHighlightBrushProperty,
            EdgeBrushProperty,
            PredictionBrushProperty,
            UnknownBrushProperty,
            HatchBrushProperty,
            EmptyBrushProperty,
            IsDimmedProperty);
    }

    public BiologyRewardBandControl()
    {
        // Keep stroke anti-alias and hatch diagonals from painting outside
        // the pip rectangle; prediction hatch is also push-clipped below.
        ClipToBounds = true;
    }

    public long MinimumReward
    {
        get => GetValue(MinimumRewardProperty);
        set => SetValue(MinimumRewardProperty, value);
    }

    public long MaximumReward
    {
        get => GetValue(MaximumRewardProperty);
        set => SetValue(MaximumRewardProperty, value);
    }

    public double BucketOneMillions
    {
        get => GetValue(BucketOneMillionsProperty);
        set => SetValue(BucketOneMillionsProperty, value);
    }

    public double BucketTwoMillions
    {
        get => GetValue(BucketTwoMillionsProperty);
        set => SetValue(BucketTwoMillionsProperty, value);
    }

    public double BucketThreeMillions
    {
        get => GetValue(BucketThreeMillionsProperty);
        set => SetValue(BucketThreeMillionsProperty, value);
    }

    public bool IsPrediction
    {
        get => GetValue(IsPredictionProperty);
        set => SetValue(IsPredictionProperty, value);
    }

    public bool IsHighlighted
    {
        get => GetValue(IsHighlightedProperty);
        set => SetValue(IsHighlightedProperty, value);
    }

    public IBrush? FilledBrush
    {
        get => GetValue(FilledBrushProperty);
        set => SetValue(FilledBrushProperty, value);
    }

    public IBrush? PotentialBrush
    {
        get => GetValue(PotentialBrushProperty);
        set => SetValue(PotentialBrushProperty, value);
    }

    public IBrush? DimmedFilledBrush
    {
        get => GetValue(DimmedFilledBrushProperty);
        set => SetValue(DimmedFilledBrushProperty, value);
    }

    public IBrush? PredictionFilledBrush
    {
        get => GetValue(PredictionFilledBrushProperty);
        set => SetValue(PredictionFilledBrushProperty, value);
    }

    public IBrush? PredictionPotentialBrush
    {
        get => GetValue(PredictionPotentialBrushProperty);
        set => SetValue(PredictionPotentialBrushProperty, value);
    }

    public IBrush? HighlightBrush
    {
        get => GetValue(HighlightBrushProperty);
        set => SetValue(HighlightBrushProperty, value);
    }

    public IBrush? DimmedHighlightBrush
    {
        get => GetValue(DimmedHighlightBrushProperty);
        set => SetValue(DimmedHighlightBrushProperty, value);
    }

    public IBrush? EdgeBrush
    {
        get => GetValue(EdgeBrushProperty);
        set => SetValue(EdgeBrushProperty, value);
    }

    public IBrush? PredictionBrush
    {
        get => GetValue(PredictionBrushProperty);
        set => SetValue(PredictionBrushProperty, value);
    }

    public IBrush? UnknownBrush
    {
        get => GetValue(UnknownBrushProperty);
        set => SetValue(UnknownBrushProperty, value);
    }

    public IBrush? HatchBrush
    {
        get => GetValue(HatchBrushProperty);
        set => SetValue(HatchBrushProperty, value);
    }

    public IBrush? EmptyBrush
    {
        get => GetValue(EmptyBrushProperty);
        set => SetValue(EmptyBrushProperty, value);
    }

    public bool IsDimmed
    {
        get => GetValue(IsDimmedProperty);
        set => SetValue(IsDimmedProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var brushes = ResolveBandBrushes();
        var state = BiologyRewardBandScale.Calculate(
            MinimumReward,
            MaximumReward,
            BiologyRewardThresholds.Normalize(
                BucketOneMillions,
                BucketTwoMillions,
                BucketThreeMillions));
        var edge = state.IsUnknown
            ? brushes.Unknown
            : EdgeBrush ?? brushes.Filled;
        var outer = new Rect(0.5, 0.5, Bounds.Width - 1, Bounds.Height - 1);
        context.DrawRectangle(Brushes.Transparent, new Pen(edge, 1), outer, 2, 2);

        if (state.IsUnknown)
        {
            DrawUnknownMarker(context, brushes.Prediction);
            return;
        }

        DrawSegments(context, state, brushes.Filled, brushes.Potential);
        if (IsPrediction)
        {
            DrawPredictionHatch(context, brushes.Hatch);
        }
    }

    private readonly record struct BandBrushes(
        IBrush Unknown,
        IBrush Filled,
        IBrush Potential,
        IBrush Prediction,
        IBrush Hatch);

    private BandBrushes ResolveBandBrushes()
    {
        return new BandBrushes(
            UnknownBrush ?? Brushes.Gray,
            ResolveFilledBrush(),
            ResolvePotentialBrush(),
            PredictionBrush ?? Brushes.LightGray,
            HatchBrush ?? PredictionBrush ?? Brushes.LightGray);
    }

    private IBrush ResolveFilledBrush()
    {
        if (IsHighlighted)
        {
            return IsDimmed
                ? DimmedHighlightBrush ?? Brushes.DarkGoldenrod
                : HighlightBrush ?? Brushes.Gold;
        }

        if (IsPrediction)
        {
            return PredictionFilledBrush ?? Brushes.Cyan;
        }

        return IsDimmed
            ? DimmedFilledBrush ?? Brushes.DarkOrange
            : FilledBrush ?? Brushes.Orange;
    }

    private IBrush ResolvePotentialBrush()
    {
        if (IsHighlighted)
        {
            return DimmedHighlightBrush ?? Brushes.DarkGoldenrod;
        }

        return IsPrediction
            ? PredictionPotentialBrush ?? Brushes.DarkCyan
            : PotentialBrush ?? Brushes.DarkOrange;
    }

    private void DrawUnknownMarker(DrawingContext context, IBrush prediction)
    {
        var text = new FormattedText(
            "?",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            Math.Max(9, Bounds.Height * 0.48),
            prediction);
        context.DrawText(
            text,
            new Point(
                (Bounds.Width - text.Width) / 2,
                (Bounds.Height - text.Height) / 2));
    }

    private void DrawSegments(
        DrawingContext context,
        BiologyRewardBandState state,
        IBrush filled,
        IBrush potential)
    {
        const double gap = 1;
        var segmentHeight = (Bounds.Height - 3 - gap * 3) / 4;
        for (var index = 0; index < state.Segments.Count; index++)
        {
            var segment = state.Segments[index];
            var y = Bounds.Height - 1.5 - segmentHeight
                - index * (segmentHeight + gap);
            var rect = new Rect(2, y, Bounds.Width - 4, segmentHeight);
            DrawSegment(context, segment, rect, filled, potential);
        }
    }

    private void DrawSegment(
        DrawingContext context,
        BiologyRewardBandSegment segment,
        Rect rect,
        IBrush filled,
        IBrush potential)
    {
        if (segment == BiologyRewardBandSegment.Filled)
        {
            context.DrawRectangle(filled, null, rect, 1, 1);
            return;
        }

        if (segment == BiologyRewardBandSegment.Potential)
        {
            context.DrawRectangle(potential, null, rect, 1, 1);
            return;
        }

        // Leave empty slots visible as recessed gaps so 1/2/3-bar
        // illustrations still show the full four-slot structure.
        context.DrawRectangle(
            EmptyBrush ?? DefaultEmptyBrush,
            null,
            rect,
            1,
            1);
    }

    private void DrawPredictionHatch(DrawingContext context, IBrush hatch)
    {
        // Clip strictly inside the border so diagonals never spill past the
        // pip frame (visible when IsPrediction paints the hatch overlay).
        var inset = 1.5;
        var clip = new Rect(
            inset,
            inset,
            Math.Max(0, Bounds.Width - inset * 2),
            Math.Max(0, Bounds.Height - inset * 2));
        if (clip.Width <= 0 || clip.Height <= 0)
        {
            return;
        }

        using (context.PushClip(clip))
        {
            var hatchPen = new Pen(hatch, 0.75);
            for (var x = -Bounds.Height; x < Bounds.Width; x += 4)
            {
                context.DrawLine(
                    hatchPen,
                    new Point(x, Bounds.Height - 1),
                    new Point(x + Bounds.Height, 1));
            }
        }
    }
}

public static class BiologyRewardBandScale
{
    public static BiologyRewardBandState Calculate(
        long minimumReward,
        long maximumReward,
        BiologyRewardThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(thresholds);
        if (minimumReward <= 0)
        {
            return new BiologyRewardBandState(true, []);
        }

        var buckets = new[]
        {
            0L,
            ToCredits(thresholds.BucketOneMillions),
            ToCredits(thresholds.BucketTwoMillions),
            ToCredits(thresholds.BucketThreeMillions),
        };
        var segments = buckets.Select(bucket => minimumReward > bucket
                ? BiologyRewardBandSegment.Filled
                : (maximumReward > bucket) switch
                {
                    true => BiologyRewardBandSegment.Potential,
                    false => BiologyRewardBandSegment.Empty
                })
            .ToArray();
        return new BiologyRewardBandState(false, segments);
    }

    private static long ToCredits(double millions)
    {
        return checked((long)(millions * 1_000_000));
    }
}

public sealed record BiologyRewardBandState(
    bool IsUnknown,
    IReadOnlyList<BiologyRewardBandSegment> Segments);

public enum BiologyRewardBandSegment
{
    Empty,
    Potential,
    Filled,
}
