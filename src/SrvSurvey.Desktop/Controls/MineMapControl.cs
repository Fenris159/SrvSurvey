using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Navigation;

namespace SrvSurvey.Desktop.Controls;

public sealed class MineMapControl : Control
{
    public static readonly StyledProperty<MineMapSurvey?> SurveyProperty =
        AvaloniaProperty.Register<MineMapControl, MineMapSurvey?>(nameof(Survey));
    public static readonly StyledProperty<SurfaceCoordinate?> PlayerLocationProperty =
        AvaloniaProperty.Register<MineMapControl, SurfaceCoordinate?>(nameof(PlayerLocation));
    public static readonly StyledProperty<double> PlayerHeadingProperty =
        AvaloniaProperty.Register<MineMapControl, double>(nameof(PlayerHeading));
    public static readonly StyledProperty<double> ViewRadiusMetersProperty =
        AvaloniaProperty.Register<MineMapControl, double>(nameof(ViewRadiusMeters), 5_000);
    public static readonly StyledProperty<IReadOnlySet<string>?> VisibleMaterialsProperty =
        AvaloniaProperty.Register<MineMapControl, IReadOnlySet<string>?>(nameof(VisibleMaterials));
    public static readonly StyledProperty<IBrush?> GridBrushProperty =
        AvaloniaProperty.Register<MineMapControl, IBrush?>(nameof(GridBrush));
    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<MineMapControl, IBrush?>(nameof(AccentBrush));
    public static readonly StyledProperty<IBrush?> TextBrushProperty =
        AvaloniaProperty.Register<MineMapControl, IBrush?>(nameof(TextBrush));
    public static readonly StyledProperty<IBrush?> ZoneBrushProperty =
        AvaloniaProperty.Register<MineMapControl, IBrush?>(nameof(ZoneBrush));

    private static readonly Color[] MarkerColors =
    [
        Color.Parse("#2E9CCA"),
        Color.Parse("#E67E22"),
        Color.Parse("#C84C4C"),
        Color.Parse("#7CB342"),
        Color.Parse("#9575CD"),
        Color.Parse("#26A69A"),
        Color.Parse("#EC407A"),
        Color.Parse("#FFCA28"),
    ];

    static MineMapControl()
    {
        AffectsRender<MineMapControl>(
            SurveyProperty,
            PlayerLocationProperty,
            PlayerHeadingProperty,
            ViewRadiusMetersProperty,
            VisibleMaterialsProperty,
            GridBrushProperty,
            AccentBrushProperty,
            TextBrushProperty,
            ZoneBrushProperty);
    }

    public MineMapSurvey? Survey { get => GetValue(SurveyProperty); set => SetValue(SurveyProperty, value); }
    public SurfaceCoordinate? PlayerLocation { get => GetValue(PlayerLocationProperty); set => SetValue(PlayerLocationProperty, value); }
    public double PlayerHeading { get => GetValue(PlayerHeadingProperty); set => SetValue(PlayerHeadingProperty, value); }
    public double ViewRadiusMeters { get => GetValue(ViewRadiusMetersProperty); set => SetValue(ViewRadiusMetersProperty, value); }
    public IReadOnlySet<string>? VisibleMaterials { get => GetValue(VisibleMaterialsProperty); set => SetValue(VisibleMaterialsProperty, value); }
    public IBrush? GridBrush { get => GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public IBrush? AccentBrush { get => GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }
    public IBrush? TextBrush { get => GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
    public IBrush? ZoneBrush { get => GetValue(ZoneBrushProperty); set => SetValue(ZoneBrushProperty, value); }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var survey = Survey;
        var text = TextBrush ?? Brushes.White;
        if (survey is null)
        {
            DrawText(context, "No live mining map", new Point(8, Bounds.Height / 2), text, 14);
            return;
        }

        var radiusPixels = Math.Max(1, Math.Min(Bounds.Width, Bounds.Height) / 2 - 28);
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var viewRadius = double.IsFinite(ViewRadiusMeters) && ViewRadiusMeters > 0
            ? ViewRadiusMeters
            : 5_000;
        var scale = radiusPixels / viewRadius;
        var grid = GridBrush ?? Brushes.Gray;
        var accent = AccentBrush ?? Brushes.Cyan;
        var zone = ZoneBrush ?? Brushes.Gold;
        var gridPen = new Pen(grid, 1);

        using (context.PushClip(Bounds))
        {
            for (var kilometers = 1; kilometers <= 4; kilometers++)
            {
                var ringRadius = kilometers * 1000 * scale;
                if (ringRadius > radiusPixels) continue;
                context.DrawEllipse(null, gridPen, center, ringRadius, ringRadius);
                DrawText(
                    context,
                    kilometers.ToString(CultureInfo.InvariantCulture),
                    new Point(center.X + 5, center.Y - ringRadius + 3),
                    text,
                    10);
            }

            foreach (var heading in Enumerable.Range(0, 8).Select(index => index * 45))
            {
                var radians = heading * Math.PI / 180d;
                var edge = new Point(
                    center.X + Math.Sin(radians) * radiusPixels,
                    center.Y - Math.Cos(radians) * radiusPixels);
                context.DrawLine(gridPen, center, edge);
                var label = heading.ToString(CultureInfo.InvariantCulture) + "°";
                var labelPoint = new Point(
                    center.X + Math.Sin(radians) * (radiusPixels + 14) - (heading is 0 or 180 ? 8 : heading > 180 ? 22 : 0),
                    center.Y - Math.Cos(radians) * (radiusPixels + 14) - 6);
                DrawText(context, label, labelPoint, text, 10);
            }

            var locationRadius = MineMapService.LocationRadiusMeters * scale;
            context.DrawEllipse(null, new Pen(zone, 2), center, locationRadius, locationRadius);
            context.DrawEllipse(null, new Pen(accent, 2), center, 7, 7);
            context.DrawEllipse(accent, null, center, 2.5, 2.5);

            foreach (var marker in survey.Markers)
            {
                if (VisibleMaterials is { } visible && !visible.Contains(marker.Material)) continue;
                var point = ToPoint(survey, marker.Location, center, scale);
                if (Distance(center, point) > radiusPixels + 5) continue;
                var brush = new SolidColorBrush(ColorFor(marker.Material));
                context.DrawEllipse(brush, new Pen(Brushes.White, 0.75), point, 5, 5);
                DrawText(context, marker.Material, new Point(point.X + 8, point.Y - 8), text, 10);
            }

            if (PlayerLocation is { } player)
            {
                var point = ToPoint(survey, player, center, scale);
                if (Distance(center, point) <= radiusPixels + 8)
                {
                    DrawCommander(context, point, PlayerHeading, accent);
                }
            }
        }
    }

    public static Color ColorFor(string material)
    {
        var hash = 17;
        foreach (var character in material.ToUpperInvariant()) hash = unchecked(hash * 31 + character);
        return MarkerColors[(hash & int.MaxValue) % MarkerColors.Length];
    }

    private static Point ToPoint(
        MineMapSurvey survey,
        SurfaceCoordinate location,
        Point center,
        double scale)
    {
        var distance = SurfaceNavigation.GetDistance(
            survey.Center,
            location,
            survey.PlanetRadiusMeters);
        var bearing = SurfaceNavigation.GetBearing(survey.Center, location) * Math.PI / 180d;
        return new Point(
            center.X + Math.Sin(bearing) * distance * scale,
            center.Y - Math.Cos(bearing) * distance * scale);
    }

    private static void DrawCommander(
        DrawingContext context,
        Point position,
        double heading,
        IBrush brush)
    {
        var radians = heading * Math.PI / 180d;
        Point At(double angle, double distance) => new(
            position.X + Math.Sin(radians + angle) * distance,
            position.Y - Math.Cos(radians + angle) * distance);
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(At(0, 9), isFilled: true);
            path.LineTo(At(2.45, 7));
            path.LineTo(At(-2.45, 7));
            path.EndFigure(isClosed: true);
        }
        context.DrawGeometry(brush, new Pen(Brushes.White, 1), geometry);
    }

    private static void DrawText(
        DrawingContext context,
        string value,
        Point origin,
        IBrush brush,
        double size)
    {
        var formatted = new FormattedText(
            value,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Century Gothic, Segoe UI, sans-serif"),
            size,
            brush);
        context.DrawText(formatted, origin);
    }

    private static double Distance(Point first, Point second) => Math.Sqrt(
        Math.Pow(first.X - second.X, 2) + Math.Pow(first.Y - second.Y, 2));
}
