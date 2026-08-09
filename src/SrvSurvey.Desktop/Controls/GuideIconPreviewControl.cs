using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SrvSurvey.Core.Guardian;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Controls;

public sealed class GuideIconPreviewControl : Control
{
    public static readonly StyledProperty<GuideIconKind> KindProperty =
        AvaloniaProperty.Register<GuideIconPreviewControl, GuideIconKind>(
            nameof(Kind));
    public static readonly StyledProperty<string> SymbolProperty =
        AvaloniaProperty.Register<GuideIconPreviewControl, string>(
            nameof(Symbol),
            string.Empty);
    public static readonly StyledProperty<IBrush?> BackgroundBrushProperty =
        AvaloniaProperty.Register<GuideIconPreviewControl, IBrush?>(
            nameof(BackgroundBrush));
    public static readonly StyledProperty<IBrush?> PrimaryBrushProperty =
        AvaloniaProperty.Register<GuideIconPreviewControl, IBrush?>(
            nameof(PrimaryBrush));
    public static readonly StyledProperty<IBrush?> SecondaryBrushProperty =
        AvaloniaProperty.Register<GuideIconPreviewControl, IBrush?>(
            nameof(SecondaryBrush));
    public static readonly StyledProperty<IBrush?> MutedBrushProperty =
        AvaloniaProperty.Register<GuideIconPreviewControl, IBrush?>(
            nameof(MutedBrush));
    public static readonly StyledProperty<IBrush?> SuccessBrushProperty =
        AvaloniaProperty.Register<GuideIconPreviewControl, IBrush?>(
            nameof(SuccessBrush));
    public static readonly StyledProperty<IBrush?> WarningBrushProperty =
        AvaloniaProperty.Register<GuideIconPreviewControl, IBrush?>(
            nameof(WarningBrush));
    public static readonly StyledProperty<IBrush?> DangerBrushProperty =
        AvaloniaProperty.Register<GuideIconPreviewControl, IBrush?>(
            nameof(DangerBrush));
    public static readonly StyledProperty<IBrush?> GoldBrushProperty =
        AvaloniaProperty.Register<GuideIconPreviewControl, IBrush?>(
            nameof(GoldBrush));
    public static readonly StyledProperty<IBrush?> PipConfirmedBrushProperty =
        AvaloniaProperty.Register<GuideIconPreviewControl, IBrush?>(
            nameof(PipConfirmedBrush));
    public static readonly StyledProperty<IBrush?> PipConfirmedDimBrushProperty =
        AvaloniaProperty.Register<GuideIconPreviewControl, IBrush?>(
            nameof(PipConfirmedDimBrush));
    public static readonly StyledProperty<IBrush?> PipPotentialBrushProperty =
        AvaloniaProperty.Register<GuideIconPreviewControl, IBrush?>(
            nameof(PipPotentialBrush));
    public static readonly StyledProperty<IBrush?> PipPredictionBrushProperty =
        AvaloniaProperty.Register<GuideIconPreviewControl, IBrush?>(
            nameof(PipPredictionBrush));
    public static readonly StyledProperty<IBrush?> PipPredictionPotentialBrushProperty =
        AvaloniaProperty.Register<GuideIconPreviewControl, IBrush?>(
            nameof(PipPredictionPotentialBrush));
    public static readonly StyledProperty<IBrush?> PipHighlightBrushProperty =
        AvaloniaProperty.Register<GuideIconPreviewControl, IBrush?>(
            nameof(PipHighlightBrush));
    public static readonly StyledProperty<IBrush?> PipGlobalRegionalBrushProperty =
        AvaloniaProperty.Register<GuideIconPreviewControl, IBrush?>(
            nameof(PipGlobalRegionalBrush));
    public static readonly StyledProperty<IBrush?> PipGlobalRegionalPotentialBrushProperty =
        AvaloniaProperty.Register<GuideIconPreviewControl, IBrush?>(
            nameof(PipGlobalRegionalPotentialBrush));
    public static readonly StyledProperty<IBrush?> PipUnknownBrushProperty =
        AvaloniaProperty.Register<GuideIconPreviewControl, IBrush?>(
            nameof(PipUnknownBrush));
    public static readonly StyledProperty<IBrush?> PipUnknownGlyphBrushProperty =
        AvaloniaProperty.Register<GuideIconPreviewControl, IBrush?>(
            nameof(PipUnknownGlyphBrush));
    public static readonly StyledProperty<IBrush?> PipHatchBrushProperty =
        AvaloniaProperty.Register<GuideIconPreviewControl, IBrush?>(
            nameof(PipHatchBrush));
    public static readonly StyledProperty<IBrush?> PipEmptyBrushProperty =
        AvaloniaProperty.Register<GuideIconPreviewControl, IBrush?>(
            nameof(PipEmptyBrush));
    public static readonly StyledProperty<IBrush?> PipConfirmedEdgeBrushProperty =
        AvaloniaProperty.Register<GuideIconPreviewControl, IBrush?>(
            nameof(PipConfirmedEdgeBrush));
    public static readonly StyledProperty<IBrush?> PipConfirmedDimEdgeBrushProperty =
        AvaloniaProperty.Register<GuideIconPreviewControl, IBrush?>(
            nameof(PipConfirmedDimEdgeBrush));
    public static readonly StyledProperty<IBrush?> PipPredictionEdgeBrushProperty =
        AvaloniaProperty.Register<GuideIconPreviewControl, IBrush?>(
            nameof(PipPredictionEdgeBrush));
    public static readonly StyledProperty<IBrush?> PipHighlightEdgeBrushProperty =
        AvaloniaProperty.Register<GuideIconPreviewControl, IBrush?>(
            nameof(PipHighlightEdgeBrush));
    public static readonly StyledProperty<IBrush?> PipGlobalRegionalEdgeBrushProperty =
        AvaloniaProperty.Register<GuideIconPreviewControl, IBrush?>(
            nameof(PipGlobalRegionalEdgeBrush));
    public static readonly StyledProperty<IBrush?> PipUnknownEdgeBrushProperty =
        AvaloniaProperty.Register<GuideIconPreviewControl, IBrush?>(
            nameof(PipUnknownEdgeBrush));

    static GuideIconPreviewControl()
    {
        AffectsRender<GuideIconPreviewControl>(
            KindProperty,
            SymbolProperty,
            BackgroundBrushProperty,
            PrimaryBrushProperty,
            SecondaryBrushProperty,
            MutedBrushProperty,
            SuccessBrushProperty,
            WarningBrushProperty,
            DangerBrushProperty,
            GoldBrushProperty,
            PipConfirmedBrushProperty,
            PipConfirmedDimBrushProperty,
            PipPotentialBrushProperty,
            PipPredictionBrushProperty,
            PipPredictionPotentialBrushProperty,
            PipHighlightBrushProperty,
            PipGlobalRegionalBrushProperty,
            PipGlobalRegionalPotentialBrushProperty,
            PipUnknownBrushProperty,
            PipUnknownGlyphBrushProperty,
            PipHatchBrushProperty,
            PipEmptyBrushProperty,
            PipConfirmedEdgeBrushProperty,
            PipConfirmedDimEdgeBrushProperty,
            PipPredictionEdgeBrushProperty,
            PipHighlightEdgeBrushProperty,
            PipGlobalRegionalEdgeBrushProperty,
            PipUnknownEdgeBrushProperty);
    }

    public GuideIconKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public string Symbol
    {
        get => GetValue(SymbolProperty);
        set => SetValue(SymbolProperty, value);
    }

    public IBrush? BackgroundBrush
    {
        get => GetValue(BackgroundBrushProperty);
        set => SetValue(BackgroundBrushProperty, value);
    }

    public IBrush? PrimaryBrush
    {
        get => GetValue(PrimaryBrushProperty);
        set => SetValue(PrimaryBrushProperty, value);
    }

    public IBrush? SecondaryBrush
    {
        get => GetValue(SecondaryBrushProperty);
        set => SetValue(SecondaryBrushProperty, value);
    }

    public IBrush? MutedBrush
    {
        get => GetValue(MutedBrushProperty);
        set => SetValue(MutedBrushProperty, value);
    }

    public IBrush? SuccessBrush
    {
        get => GetValue(SuccessBrushProperty);
        set => SetValue(SuccessBrushProperty, value);
    }

    public IBrush? WarningBrush
    {
        get => GetValue(WarningBrushProperty);
        set => SetValue(WarningBrushProperty, value);
    }

    public IBrush? DangerBrush
    {
        get => GetValue(DangerBrushProperty);
        set => SetValue(DangerBrushProperty, value);
    }

    public IBrush? GoldBrush
    {
        get => GetValue(GoldBrushProperty);
        set => SetValue(GoldBrushProperty, value);
    }

    public IBrush? PipConfirmedBrush
    {
        get => GetValue(PipConfirmedBrushProperty);
        set => SetValue(PipConfirmedBrushProperty, value);
    }

    public IBrush? PipConfirmedDimBrush
    {
        get => GetValue(PipConfirmedDimBrushProperty);
        set => SetValue(PipConfirmedDimBrushProperty, value);
    }

    public IBrush? PipPotentialBrush
    {
        get => GetValue(PipPotentialBrushProperty);
        set => SetValue(PipPotentialBrushProperty, value);
    }

    public IBrush? PipPredictionBrush
    {
        get => GetValue(PipPredictionBrushProperty);
        set => SetValue(PipPredictionBrushProperty, value);
    }

    public IBrush? PipPredictionPotentialBrush
    {
        get => GetValue(PipPredictionPotentialBrushProperty);
        set => SetValue(PipPredictionPotentialBrushProperty, value);
    }

    public IBrush? PipHighlightBrush
    {
        get => GetValue(PipHighlightBrushProperty);
        set => SetValue(PipHighlightBrushProperty, value);
    }

    public IBrush? PipGlobalRegionalBrush
    {
        get => GetValue(PipGlobalRegionalBrushProperty);
        set => SetValue(PipGlobalRegionalBrushProperty, value);
    }

    public IBrush? PipGlobalRegionalPotentialBrush
    {
        get => GetValue(PipGlobalRegionalPotentialBrushProperty);
        set => SetValue(PipGlobalRegionalPotentialBrushProperty, value);
    }

    public IBrush? PipUnknownBrush
    {
        get => GetValue(PipUnknownBrushProperty);
        set => SetValue(PipUnknownBrushProperty, value);
    }

    public IBrush? PipUnknownGlyphBrush
    {
        get => GetValue(PipUnknownGlyphBrushProperty);
        set => SetValue(PipUnknownGlyphBrushProperty, value);
    }

    public IBrush? PipHatchBrush
    {
        get => GetValue(PipHatchBrushProperty);
        set => SetValue(PipHatchBrushProperty, value);
    }

    public IBrush? PipEmptyBrush
    {
        get => GetValue(PipEmptyBrushProperty);
        set => SetValue(PipEmptyBrushProperty, value);
    }

    public IBrush? PipConfirmedEdgeBrush
    {
        get => GetValue(PipConfirmedEdgeBrushProperty);
        set => SetValue(PipConfirmedEdgeBrushProperty, value);
    }

    public IBrush? PipConfirmedDimEdgeBrush
    {
        get => GetValue(PipConfirmedDimEdgeBrushProperty);
        set => SetValue(PipConfirmedDimEdgeBrushProperty, value);
    }

    public IBrush? PipPredictionEdgeBrush
    {
        get => GetValue(PipPredictionEdgeBrushProperty);
        set => SetValue(PipPredictionEdgeBrushProperty, value);
    }

    public IBrush? PipHighlightEdgeBrush
    {
        get => GetValue(PipHighlightEdgeBrushProperty);
        set => SetValue(PipHighlightEdgeBrushProperty, value);
    }

    public IBrush? PipGlobalRegionalEdgeBrush
    {
        get => GetValue(PipGlobalRegionalEdgeBrushProperty);
        set => SetValue(PipGlobalRegionalEdgeBrushProperty, value);
    }

    public IBrush? PipUnknownEdgeBrush
    {
        get => GetValue(PipUnknownEdgeBrushProperty);
        set => SetValue(PipUnknownEdgeBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var background = BackgroundBrush ?? Brushes.Black;
        var primary = PrimaryBrush ?? Brushes.Orange;
        var secondary = SecondaryBrush ?? Brushes.Cyan;
        var muted = MutedBrush ?? Brushes.Gray;
        var success = SuccessBrush ?? Brushes.LimeGreen;
        var warning = WarningBrush ?? Brushes.Gold;
        var danger = DangerBrush ?? Brushes.Red;
        var gold = GoldBrush ?? Brushes.Goldenrod;
        var pipConfirmed = PipConfirmedBrush ?? primary;
        var pipConfirmedDim = PipConfirmedDimBrush ?? muted;
        var pipPotential = PipPotentialBrush ?? muted;
        var pipPrediction = PipPredictionBrush ?? secondary;
        var pipPredictionPotential =
            PipPredictionPotentialBrush ?? muted;
        var pipHighlight = PipHighlightBrush ?? gold;
        var pipGlobalRegional = PipGlobalRegionalBrush ?? Brushes.White;
        var pipGlobalRegionalPotential =
            PipGlobalRegionalPotentialBrush ?? Brushes.Gray;
        var pipUnknown = PipUnknownBrush ?? muted;
        var pipUnknownGlyph = PipUnknownGlyphBrush ?? Brushes.LightGray;
        var pipHatch = PipHatchBrush ?? muted;
        var pipEmpty = PipEmptyBrush ?? Brushes.Transparent;
        var pipConfirmedEdge = PipConfirmedEdgeBrush ?? pipConfirmed;
        var pipConfirmedDimEdge = PipConfirmedDimEdgeBrush ?? pipConfirmedDim;
        var pipPredictionEdge = PipPredictionEdgeBrush ?? pipPrediction;
        var pipHighlightEdge = PipHighlightEdgeBrush ?? pipHighlight;
        var pipGlobalRegionalEdge =
            PipGlobalRegionalEdgeBrush ?? pipGlobalRegional;
        var pipUnknownEdge = PipUnknownEdgeBrush ?? pipUnknown;
        var bounds = new Rect(0.5, 0.5, Bounds.Width - 1, Bounds.Height - 1);
        context.DrawRectangle(background, new Pen(muted, 1), bounds, 8, 8);
        var center = bounds.Center;

        switch (Kind)
        {
            case GuideIconKind.Asset:
                break;
            case GuideIconKind.Glyph:
                DrawGlyph(
                    context,
                    center,
                    GetGlyphBrush(
                        Symbol,
                        new GlyphBrushPalette
                        {
                            Primary = primary,
                            Secondary = secondary,
                            Muted = muted,
                            Success = success,
                            Warning = warning,
                            Danger = danger,
                            Gold = gold,
                        }));
                break;
            case GuideIconKind.BiologyRewardKnown:
                DrawRewardPips(
                    context,
                    center,
                    pipConfirmedEdge,
                    pipConfirmed,
                    pipPotential,
                    pipEmpty,
                    pipHatch,
                    isPrediction: false);
                break;
            case GuideIconKind.BiologyRewardPredicted:
                DrawRewardPips(
                    context,
                    center,
                    pipPredictionEdge,
                    pipPrediction,
                    pipPredictionPotential,
                    pipEmpty,
                    pipHatch,
                    isPrediction: true);
                break;
            case GuideIconKind.BiologyRewardHighlighted:
                DrawRewardPips(
                    context,
                    center,
                    pipHighlightEdge,
                    pipHighlight,
                    pipHighlight,
                    pipEmpty,
                    pipHatch,
                    isPrediction: false);
                break;
            case GuideIconKind.BiologyRewardGlobalRegional:
                DrawRewardPips(
                    context,
                    center,
                    pipGlobalRegionalEdge,
                    pipGlobalRegional,
                    pipGlobalRegionalPotential,
                    pipEmpty,
                    pipHatch,
                    isPrediction: true,
                    reinforceEdge: true);
                break;
            case GuideIconKind.BiologyRewardDimmed:
                DrawRewardPips(
                    context,
                    center,
                    pipConfirmedDimEdge,
                    pipConfirmedDim,
                    pipPotential,
                    pipEmpty,
                    pipHatch,
                    isPrediction: false);
                break;
            case GuideIconKind.BiologyRewardUnknown:
                DrawUnknownPip(
                    context,
                    center,
                    pipUnknownEdge,
                    pipUnknownGlyph);
                break;
            case GuideIconKind.CanonnSignals:
                CanonnLogoControl.Draw(
                    context,
                    new Rect(
                        center.X - CanonnLogoControl.NativeSize / 2,
                        center.Y - CanonnLogoControl.NativeSize / 2,
                        CanonnLogoControl.NativeSize,
                        CanonnLogoControl.NativeSize));
                break;
            case GuideIconKind.DirectionalChevron:
                DirectionalChevronDrawing.Draw(
                    context,
                    new Point(center.X - 15, center.Y),
                    25,
                    -20,
                    isFar: false,
                    secondary,
                    2.5);
                DirectionalChevronDrawing.Draw(
                    context,
                    new Point(center.X + 15, center.Y),
                    25,
                    20,
                    isFar: true,
                    secondary,
                    2.5);
                break;
            case GuideIconKind.RadarCommander:
                DrawRadarCommander(context, center, secondary);
                break;
            case GuideIconKind.RadarShip:
                DrawTriangle(context, center, 12, warning, fill: true);
                break;
            case GuideIconKind.RadarSrv:
                context.DrawRectangle(
                    success,
                    new Pen(success, 1),
                    new Rect(center.X - 12, center.Y - 8, 24, 16),
                    4,
                    4);
                break;
            case GuideIconKind.RadarSample:
                context.DrawEllipse(null, new Pen(warning, 2), center, 21, 21);
                context.DrawEllipse(success, null, center, 4, 4);
                break;
            case GuideIconKind.RadarHistoricalScan:
                context.DrawEllipse(null, new Pen(danger, 2), center, 21, 21);
                context.DrawEllipse(muted, null, center, 4, 4);
                break;
            case GuideIconKind.RadarBookmark:
                context.DrawEllipse(null, new Pen(secondary, 2), center, 21, 21);
                context.DrawEllipse(secondary, null, center, 4, 4);
                break;
            case GuideIconKind.GroundTarget:
                DrawGroundTarget(context, center, secondary, warning, muted);
                break;
            case GuideIconKind.JumpRoute:
                DrawJumpRoute(context, center, primary, secondary, muted);
                break;
            case GuideIconKind.GuardianRelic:
                DrawGuardianLegacyIcon(
                    context,
                    center,
                    GuardianPoiType.Relic,
                    GuardianPoiStatus.Present);
                break;
            case GuideIconKind.GuardianArtifact:
                DrawGuardianArtifactPalette(context, center);
                break;
            case GuideIconKind.GuardianEmptyPuddle:
                DrawGuardianLegacyIcon(
                    context,
                    center,
                    GuardianPoiType.EmptyPuddle,
                    GuardianPoiStatus.Empty);
                break;
            case GuideIconKind.GuardianObelisk:
                DrawGuardianLegacyIcon(
                    context,
                    center,
                    GuardianPoiType.Obelisk,
                    GuardianPoiStatus.Unknown);
                break;
            case GuideIconKind.GuardianActiveObelisk:
                DrawGuardianActiveObelisk(context, center);
                break;
            case GuideIconKind.GuardianBrokenObelisk:
                DrawGuardianLegacyIcon(
                    context,
                    center,
                    GuardianPoiType.BrokenObelisk,
                    GuardianPoiStatus.Unknown);
                break;
            case GuideIconKind.GuardianPylon:
                DrawGuardianLegacyIcon(
                    context,
                    center,
                    GuardianPoiType.Pylon,
                    GuardianPoiStatus.Present);
                break;
            case GuideIconKind.GuardianComponent:
                DrawGuardianLegacyIcon(
                    context,
                    center,
                    GuardianPoiType.Component,
                    GuardianPoiStatus.Present);
                break;
            case GuideIconKind.GuardianCommander:
                context.DrawEllipse(background, new Pen(success, 2), center, 12, 12);
                context.DrawEllipse(success, null, center, 4, 4);
                break;
            case GuideIconKind.GuardianSiteHeading:
                context.DrawLine(
                    new Pen(
                        new SolidColorBrush(
                            GuardianLegacyMapDrawing.SiteHeading),
                        4,
                        dashStyle: DashStyle.Dash),
                    new Point(center.X, center.Y - 24),
                    new Point(center.X, center.Y + 24));
                break;
            case GuideIconKind.GuardianTowerHeading:
                context.DrawLine(
                    new Pen(
                        new SolidColorBrush(
                            GuardianLegacyMapDrawing.TowerHeading),
                        4),
                    new Point(center.X - 20, center.Y + 20),
                    new Point(center.X + 20, center.Y - 20));
                break;
            case GuideIconKind.GuardianSurveyNeeded:
                GuardianSurveyMarkerDrawing.Draw(
                    context,
                    center,
                    haloRadius: 20,
                    ringRadius: 18,
                    dotRadius: 1);
                break;
            case GuideIconKind.GuardianPoiStates:
                DrawGuardianPoiStates(context, center);
                break;
            case GuideIconKind.HumanLandingPad:
                DrawLandingPad(context, center, secondary);
                break;
            case GuideIconKind.HumanDoor:
                context.DrawRectangle(
                    warning,
                    new Pen(Brushes.White, 1),
                    new Rect(center.X - 18, center.Y - 5, 36, 10),
                    2,
                    2);
                break;
            case GuideIconKind.HumanTerminal:
                DrawTerminal(context, center, warning);
                break;
            case GuideIconKind.HumanMaterial:
                context.DrawEllipse(background, new Pen(Brushes.White, 2), center, 6, 6);
                break;
            case GuideIconKind.HumanCommander:
                DrawHumanCommander(context, center, success);
                break;
            case GuideIconKind.HumanShip:
                DrawVehicleLabel(context, center, "SHIP", secondary, isCircle: true);
                break;
            case GuideIconKind.HumanSrv:
                DrawVehicleLabel(context, center, "SRV", warning, isCircle: false);
                break;
            case GuideIconKind.HumanQuestTarget:
                context.DrawEllipse(null, new Pen(warning, 3), center, 22, 22);
                context.DrawEllipse(secondary, null, center, 3, 3);
                break;
            case GuideIconKind.HumanFloor:
                DrawCenteredText(context, center, "⌃⌃", Brushes.White, 24);
                break;
            case GuideIconKind.ConflictCheckpoint:
                DrawCheckpoint(context, center, warning);
                break;
            case GuideIconKind.ConflictPowerPost:
                DrawPowerPost(context, center, gold);
                break;
        }
    }

    private void DrawGlyph(DrawingContext context, Point center, IBrush brush)
    {
        var text = new FormattedText(
            Symbol,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            Symbol.Length == 1 ? 30 : 26,
            brush);
        context.DrawText(
            text,
            new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
    }

    private sealed class GlyphBrushPalette
    {
        public required IBrush Primary { get; init; }

        public required IBrush Secondary { get; init; }

        public required IBrush Muted { get; init; }

        public required IBrush Success { get; init; }

        public required IBrush Warning { get; init; }

        public required IBrush Danger { get; init; }

        public required IBrush Gold { get; init; }
    }

    private static IBrush GetGlyphBrush(string symbol, GlyphBrushPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        return symbol switch
        {
            "✓" => palette.Success,
            "⚠" => palette.Danger,
            "?" => palette.Muted,
            "⚑" or "⚐" or "☀" or "◆" => palette.Gold,
            "◇" => palette.Warning,
            "+" => palette.Secondary,
            "!" => palette.Danger,
            "■" or "►" => palette.Primary,
            _ => palette.Secondary,
        };
    }

    private static void DrawRewardPips(
        DrawingContext context,
        Point center,
        IBrush edge,
        IBrush filled,
        IBrush potential,
        IBrush empty,
        IBrush hatch,
        bool isPrediction,
        bool reinforceEdge = false)
    {
        var frame = new Rect(center.X - 12, center.Y - 25, 24, 50);
        var edgePen = new Pen(edge, 1.5);
        context.DrawRectangle(null, edgePen, frame, 3, 3);
        if (reinforceEdge)
        {
            context.DrawRectangle(null, edgePen, frame, 3, 3);
            context.DrawRectangle(null, edgePen, frame, 3, 3);
        }
        for (var index = 0; index < 4; index++)
        {
            var segment = new Rect(
                frame.X + 4,
                frame.Bottom - 5 - (index + 1) * 10,
                frame.Width - 8,
                8);
            var segmentBrush = GetRewardSegmentBrush(
                index,
                isPrediction,
                filled,
                potential,
                empty);
            context.DrawRectangle(segmentBrush, null, segment, 1, 1);
        }

        if (!isPrediction)
        {
            return;
        }

        var hatchPen = new Pen(hatch, 1);
        for (var x = frame.Left - frame.Height; x < frame.Right; x += 6)
        {
            var startX = Math.Max(x, frame.Left);
            var endX = Math.Min(x + frame.Height, frame.Right);
            if (startX >= endX)
            {
                continue;
            }

            context.DrawLine(
                hatchPen,
                new Point(startX, frame.Bottom - (startX - x)),
                new Point(endX, frame.Bottom - (endX - x)));
        }
    }

    private static IBrush GetRewardSegmentBrush(
        int index,
        bool isPrediction,
        IBrush filled,
        IBrush potential,
        IBrush empty)
    {
        var filledSegmentCount = isPrediction ? 2 : 3;
        if (index < filledSegmentCount)
        {
            return filled;
        }

        return isPrediction && index == filledSegmentCount
            ? potential
            : empty;
    }

    private static void DrawUnknownPip(
        DrawingContext context,
        Point center,
        IBrush frameBrush,
        IBrush glyphBrush)
    {
        var frame = new Rect(center.X - 12, center.Y - 25, 24, 50);
        context.DrawRectangle(null, new Pen(frameBrush, 1.5), frame, 3, 3);
        var text = new FormattedText(
            "?",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            24,
            glyphBrush);
        context.DrawText(
            text,
            new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
    }

    private static void DrawRadarCommander(
        DrawingContext context,
        Point center,
        IBrush brush)
    {
        context.DrawEllipse(null, new Pen(brush, 1.5), center, 20, 20);
        DrawTriangle(context, center, 10, brush, fill: true);
    }

    private static void DrawGroundTarget(
        DrawingContext context,
        Point center,
        IBrush accent,
        IBrush warning,
        IBrush muted)
    {
        var radarCenter = new Point(center.X, center.Y - 7);
        context.DrawEllipse(null, new Pen(muted, 1), radarCenter, 22, 22);
        RingedPointerDrawing.Draw(
            context,
            radarCenter,
            15,
            bearingDegrees: 0,
            accent,
            strokeThickness: 1.25);
        var target = new Point(radarCenter.X + 16, radarCenter.Y - 11);
        context.DrawLine(new Pen(accent, 2), radarCenter, target);
        context.DrawEllipse(accent, null, target, 3, 3);
        var origin = new Point(center.X - 22, center.Y + 24);
        context.DrawLine(new Pen(muted, 1), origin, new Point(origin.X + 44, origin.Y));
        context.DrawLine(new Pen(warning, 2), origin, new Point(origin.X + 34, origin.Y - 18));
    }

    private static void DrawGuardianLegacyIcon(
        DrawingContext context,
        Point center,
        GuardianPoiType type,
        GuardianPoiStatus status,
        bool isActiveObelisk = false)
    {
        var style = GuardianLegacyMapDrawing.GetPointStyle(
            type,
            status,
            isActiveObelisk);
        var stroke = new SolidColorBrush(style.Stroke);
        var fill = style.HasFill ? new SolidColorBrush(style.Fill) : null;
        var pen = new Pen(
            stroke,
            Math.Max(1.5, style.StrokeWidth),
            dashStyle: style.Pattern switch
            {
                GuardianLegacyStrokePattern.Dash => DashStyle.Dash,
                GuardianLegacyStrokePattern.Dot => DashStyle.Dot,
                _ => null,
            });
        if (type == GuardianPoiType.EmptyPuddle)
        {
            context.DrawEllipse(fill, pen, center, 12, 12);
            return;
        }

        var rotation = type switch
        {
            GuardianPoiType.Obelisk or GuardianPoiType.BrokenObelisk => 167.5,
            GuardianPoiType.Component => -45,
            _ => 0,
        };
        var points = GuardianLegacyMapDrawing.CreateGlyphPoints(
                type,
                new Point(),
                rotation)
            .Select(point => new Point(
                center.X + point.X * 3,
                center.Y + point.Y * 3))
            .ToArray();
        if (type == GuardianPoiType.Relic)
        {
            context.DrawGeometry(fill, pen, CreatePolygon(points));
        }
        else
        {
            DrawOpenPath(context, points, pen);
        }

        if (type == GuardianPoiType.Obelisk)
        {
            context.DrawLine(
                pen,
                center + GuardianLegacyMapDrawing.RotateClockwise(
                    new Point(0.6, 0),
                    rotation),
                center + GuardianLegacyMapDrawing.RotateClockwise(
                    new Point(-1.5, -3.6),
                    rotation));
            context.DrawLine(
                pen,
                center + GuardianLegacyMapDrawing.RotateClockwise(
                    new Point(0.6, 0),
                    rotation),
                center + GuardianLegacyMapDrawing.RotateClockwise(
                    new Point(4.5, -2.4),
                    rotation));
        }
        else if (type == GuardianPoiType.Pylon)
        {
            context.DrawLine(
                pen,
                center,
                new Point(center.X, center.Y + 9));
        }
        else if (type == GuardianPoiType.Component)
        {
            var materialCenters =
                GuardianLegacyMapDrawing.CreateComponentMaterialCenters(center);
            var materials = new[]
            {
                GuardianComponentMaterial.Cell,
                GuardianComponentMaterial.Conduit,
                GuardianComponentMaterial.Tech,
            };
            for (var index = 0; index < materialCenters.Count; index++)
            {
                var raw = materialCenters[index] - center;
                var dot = center + new Point(raw.X * 2.4, raw.Y * 2.4);
                var color = GuardianLegacyMapDrawing.GetComponentMaterialColor(
                    materials[index]);
                context.DrawEllipse(
                    new SolidColorBrush(color!.Value),
                    new Pen(Brushes.Black, 1),
                    dot,
                    3,
                    3);
            }
        }
    }

    private static void DrawGuardianActiveObelisk(
        DrawingContext context,
        Point center)
    {
        for (var step = 0; step < 6; step++)
        {
            var color = GuardianLegacyMapDrawing.Cyan;
            context.DrawGeometry(
                new SolidColorBrush(Color.FromArgb(
                    (byte)(18 + step * 22),
                    color.R,
                    color.G,
                    color.B)),
                null,
                CreatePolygon(GuardianLegacyMapDrawing.CreateWedge(
                    center,
                    26 - step * 3.5,
                    167.5)));
        }

        DrawGuardianLegacyIcon(
            context,
            center,
            GuardianPoiType.Obelisk,
            GuardianPoiStatus.Unknown,
            isActiveObelisk: true);
    }

    private static void DrawGuardianArtifactPalette(
        DrawingContext context,
        Point center)
    {
        var types = new[]
        {
            GuardianPoiType.Orb,
            GuardianPoiType.Casket,
            GuardianPoiType.Tablet,
            GuardianPoiType.Totem,
            GuardianPoiType.Urn,
        };
        for (var index = 0; index < types.Length; index++)
        {
            var style = GuardianLegacyMapDrawing.GetPointStyle(
                types[index],
                GuardianPoiStatus.Present);
            context.DrawEllipse(
                new SolidColorBrush(style.Fill),
                new Pen(new SolidColorBrush(style.Stroke), 2),
                new Point(center.X - 24 + index * 12, center.Y),
                5,
                8);
        }
    }

    private static void DrawGuardianPoiStates(
        DrawingContext context,
        Point center)
    {
        var unknown = new Point(center.X - 22, center.Y);
        GuardianSurveyMarkerDrawing.Draw(
            context,
            unknown,
            haloRadius: 9,
            ringRadius: 8,
            dotRadius: 0.7);
        var states = new[]
        {
            (GuardianPoiStatus.Absent, center.X - 7),
            (GuardianPoiStatus.Present, center.X + 8),
            (GuardianPoiStatus.Empty, center.X + 23),
        };
        foreach (var (status, x) in states)
        {
            var style = GuardianLegacyMapDrawing.GetPointStyle(
                GuardianPoiType.Orb,
                status);
            context.DrawEllipse(
                style.HasFill ? new SolidColorBrush(style.Fill) : null,
                new Pen(new SolidColorBrush(style.Stroke), 2),
                new Point(x, center.Y),
                6,
                6);
        }
    }

    private static void DrawOpenPath(
        DrawingContext context,
        Point[] points,
        Pen pen)
    {
        if (points.Length < 2)
        {
            return;
        }

        var geometry = new StreamGeometry();
        using var geometryContext = geometry.Open();
        geometryContext.BeginFigure(points[0], isFilled: false);
        for (var index = 1; index < points.Length; index++)
        {
            geometryContext.LineTo(points[index]);
        }

        geometryContext.EndFigure(isClosed: false);
        context.DrawGeometry(null, pen, geometry);
    }

    private static void DrawTerminal(
        DrawingContext context,
        Point center,
        IBrush brush)
    {
        var rect = new Rect(center.X - 15, center.Y - 15, 30, 30);
        context.DrawRectangle(null, new Pen(brush, 2), rect, 5, 5);
        context.DrawLine(
            new Pen(brush, 1.5),
            new Point(center.X - 8, center.Y),
            new Point(center.X + 8, center.Y));
    }

    private static void DrawHumanCommander(
        DrawingContext context,
        Point center,
        IBrush brush)
    {
        context.DrawEllipse(null, new Pen(brush, 2.5), center, 10, 10);
        context.DrawLine(
            new Pen(brush, 2.5),
            center,
            new Point(center.X, center.Y - 26));
    }

    private static void DrawCheckpoint(
        DrawingContext context,
        Point center,
        IBrush brush)
    {
        context.DrawEllipse(null, new Pen(brush, 2.5), center, 16, 16);
        var text = new FormattedText(
            "C",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            14,
            brush);
        context.DrawText(
            text,
            new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
    }

    private static void DrawJumpRoute(
        DrawingContext context,
        Point center,
        IBrush primary,
        IBrush secondary,
        IBrush muted)
    {
        var left = new Point(center.X - 24, center.Y);
        var middle = center;
        var right = new Point(center.X + 24, center.Y);
        context.DrawLine(new Pen(muted, 2), left, right);
        context.DrawEllipse(muted, null, left, 4, 4);
        context.DrawEllipse(primary, new Pen(secondary, 2), middle, 7, 7);
        context.DrawEllipse(null, new Pen(secondary, 2), right, 5, 5);
    }

    private static void DrawLandingPad(
        DrawingContext context,
        Point center,
        IBrush brush)
    {
        context.DrawRectangle(
            null,
            new Pen(brush, 2),
            new Rect(center.X - 18, center.Y - 25, 36, 50));
        DrawCenteredText(context, center, "2", brush, 18);
    }

    private static void DrawVehicleLabel(
        DrawingContext context,
        Point center,
        string text,
        IBrush brush,
        bool isCircle)
    {
        if (isCircle)
        {
            context.DrawEllipse(null, new Pen(brush, 2), center, 23, 23);
        }
        else
        {
            context.DrawRectangle(
                null,
                new Pen(brush, 2),
                new Rect(center.X - 22, center.Y - 18, 44, 36),
                4,
                4);
        }

        DrawCenteredText(context, center, text, brush, 10);
    }

    private static void DrawCenteredText(
        DrawingContext context,
        Point center,
        string value,
        IBrush brush,
        double size)
    {
        var text = new FormattedText(
            value,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            size,
            brush);
        context.DrawText(
            text,
            new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
    }

    private static void DrawPowerPost(
        DrawingContext context,
        Point center,
        IBrush brush)
    {
        var pen = new Pen(brush, 2.5);
        context.DrawEllipse(null, pen, center, 17, 17);
        context.DrawLine(pen, new Point(center.X + 5, center.Y - 14), new Point(center.X - 4, center.Y));
        context.DrawLine(pen, new Point(center.X - 4, center.Y), new Point(center.X + 5, center.Y));
        context.DrawLine(pen, new Point(center.X + 5, center.Y), new Point(center.X - 5, center.Y + 14));
    }

    private static void DrawTriangle(
        DrawingContext context,
        Point center,
        double radius,
        IBrush brush,
        bool fill)
    {
        var geometry = CreatePolygon(
        [
            new Point(center.X, center.Y - radius),
            new Point(center.X + radius * 0.75, center.Y + radius),
            new Point(center.X, center.Y + radius * 0.55),
            new Point(center.X - radius * 0.75, center.Y + radius),
        ]);
        context.DrawGeometry(fill ? brush : null, new Pen(brush, 2), geometry);
    }

    private static StreamGeometry CreatePolygon(IReadOnlyList<Point> points)
    {
        var geometry = new StreamGeometry();
        using var geometryContext = geometry.Open();
        geometryContext.BeginFigure(points[0], isFilled: true);
        for (var index = 1; index < points.Count; index++)
        {
            geometryContext.LineTo(points[index]);
        }

        geometryContext.EndFigure(isClosed: true);
        return geometry;
    }
}
