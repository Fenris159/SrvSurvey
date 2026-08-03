using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SrvSurvey.Core.Guardian;
using SrvSurvey.Desktop.Localization;

namespace SrvSurvey.Desktop.Controls;

public sealed class GuardianSiteMapControl : Control
{
    public static readonly StyledProperty<GuardianSiteMapProjection?> ProjectionProperty =
        AvaloniaProperty.Register<GuardianSiteMapControl, GuardianSiteMapProjection?>(
            nameof(Projection));
    public static readonly StyledProperty<GuardianSiteProximitySnapshot?>
        ProximityProperty = AvaloniaProperty.Register<
            GuardianSiteMapControl,
            GuardianSiteProximitySnapshot?>(nameof(Proximity));
    public static readonly StyledProperty<double> MapScaleProperty =
        AvaloniaProperty.Register<GuardianSiteMapControl, double>(
            nameof(MapScale),
            double.NaN);
    public static readonly StyledProperty<double> CommanderHeadingProperty =
        AvaloniaProperty.Register<GuardianSiteMapControl, double>(
            nameof(CommanderHeading));
    public static readonly StyledProperty<string?> TargetPointNameProperty =
        AvaloniaProperty.Register<GuardianSiteMapControl, string?>(
            nameof(TargetPointName));
    public static readonly StyledProperty<IBrush?> MapBackgroundProperty =
        AvaloniaProperty.Register<GuardianSiteMapControl, IBrush?>(
            nameof(MapBackground));
    public static readonly StyledProperty<IBrush?> GridBrushProperty =
        AvaloniaProperty.Register<GuardianSiteMapControl, IBrush?>(
            nameof(GridBrush));
    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<GuardianSiteMapControl, IBrush?>(
            nameof(AccentBrush));
    public static readonly StyledProperty<IBrush?> MutedBrushProperty =
        AvaloniaProperty.Register<GuardianSiteMapControl, IBrush?>(
            nameof(MutedBrush));
    public static readonly StyledProperty<IBrush?> PresentBrushProperty =
        AvaloniaProperty.Register<GuardianSiteMapControl, IBrush?>(
            nameof(PresentBrush));
    public static readonly StyledProperty<IBrush?> AbsentBrushProperty =
        AvaloniaProperty.Register<GuardianSiteMapControl, IBrush?>(
            nameof(AbsentBrush));
    public static readonly StyledProperty<IBrush?> EmptyBrushProperty =
        AvaloniaProperty.Register<GuardianSiteMapControl, IBrush?>(
            nameof(EmptyBrush));
    public static readonly StyledProperty<bool> ShowLegendProperty =
        AvaloniaProperty.Register<GuardianSiteMapControl, bool>(
            nameof(ShowLegend),
            true);

    static GuardianSiteMapControl()
    {
        AffectsRender<GuardianSiteMapControl>(
            ProjectionProperty,
            ProximityProperty,
            MapScaleProperty,
            CommanderHeadingProperty,
            TargetPointNameProperty,
            MapBackgroundProperty,
            GridBrushProperty,
            AccentBrushProperty,
            MutedBrushProperty,
            PresentBrushProperty,
            AbsentBrushProperty,
            EmptyBrushProperty,
            ShowLegendProperty);
    }

    public GuardianSiteMapProjection? Projection
    {
        get => GetValue(ProjectionProperty);
        set => SetValue(ProjectionProperty, value);
    }

    public GuardianSiteProximitySnapshot? Proximity
    {
        get => GetValue(ProximityProperty);
        set => SetValue(ProximityProperty, value);
    }

    public double MapScale
    {
        get => GetValue(MapScaleProperty);
        set => SetValue(MapScaleProperty, value);
    }

    public double CommanderHeading
    {
        get => GetValue(CommanderHeadingProperty);
        set => SetValue(CommanderHeadingProperty, value);
    }

    public string? TargetPointName
    {
        get => GetValue(TargetPointNameProperty);
        set => SetValue(TargetPointNameProperty, value);
    }

    public IBrush? MapBackground
    {
        get => GetValue(MapBackgroundProperty);
        set => SetValue(MapBackgroundProperty, value);
    }

    public IBrush? GridBrush
    {
        get => GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    public IBrush? AccentBrush
    {
        get => GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public IBrush? MutedBrush
    {
        get => GetValue(MutedBrushProperty);
        set => SetValue(MutedBrushProperty, value);
    }

    public IBrush? PresentBrush
    {
        get => GetValue(PresentBrushProperty);
        set => SetValue(PresentBrushProperty, value);
    }

    public IBrush? AbsentBrush
    {
        get => GetValue(AbsentBrushProperty);
        set => SetValue(AbsentBrushProperty, value);
    }

    public IBrush? EmptyBrush
    {
        get => GetValue(EmptyBrushProperty);
        set => SetValue(EmptyBrushProperty, value);
    }

    public bool ShowLegend
    {
        get => GetValue(ShowLegendProperty);
        set => SetValue(ShowLegendProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = new Rect(Bounds.Size);
        context.DrawRectangle(
            MapBackground ?? Brushes.Transparent,
            new Pen(GridBrush ?? Brushes.Gray, 1),
            bounds,
            8,
            8);
        if (Projection is not { } projection
            || bounds.Width <= 0
            || bounds.Height <= 0)
        {
            return;
        }

        var grid = GridBrush ?? Brushes.Gray;
        var accent = AccentBrush ?? Brushes.Cyan;
        var viewportCenter = bounds.Center;
        var radius = Math.Max(
            1,
            Math.Min(bounds.Width, bounds.Height) / 2 - 30);
        var fittedScale = radius / projection.MaximumDistance;
        var scale = double.IsFinite(MapScale) && MapScale > 0
            ? Math.Clamp(MapScale, 0.1, 20)
            : fittedScale;
        var mapOrigin = TransformMapPoint(
            0,
            0,
            Proximity,
            CommanderHeading,
            viewportCenter,
            scale);
        var gridExtent = Math.Max(bounds.Width, bounds.Height) / scale * 2;
        var gridPen = new Pen(grid, 1, dashStyle: DashStyle.Dash);
        context.DrawLine(
            gridPen,
            TransformMapPoint(
                0,
                -gridExtent,
                Proximity,
                CommanderHeading,
                viewportCenter,
                scale),
            TransformMapPoint(
                0,
                gridExtent,
                Proximity,
                CommanderHeading,
                viewportCenter,
                scale));
        context.DrawLine(
            gridPen,
            TransformMapPoint(
                -gridExtent,
                0,
                Proximity,
                CommanderHeading,
                viewportCenter,
                scale),
            TransformMapPoint(
                gridExtent,
                0,
                Proximity,
                CommanderHeading,
                viewportCenter,
                scale));
        for (var ring = 1; ring <= 4; ring++)
        {
            var ringRadius = projection.MaximumDistance * scale * ring / 4;
            context.DrawEllipse(
                null,
                gridPen,
                mapOrigin,
                ringRadius,
                ringRadius);
        }

        context.DrawEllipse(accent, null, mapOrigin, 3, 3);
        DrawHeadingLines(
            context,
            projection,
            mapOrigin,
            gridExtent * scale,
            CommanderHeading);
        foreach (var point in projection.Points)
        {
            DrawPoint(
                context,
                point,
                TransformMapPoint(
                    point.X,
                    point.Y,
                    Proximity,
                    CommanderHeading,
                    viewportCenter,
                    scale),
                projection,
                Math.Max(bounds.Width, bounds.Height));
        }

        foreach (var group in projection.Groups)
        {
            DrawGroup(
                context,
                group,
                TransformMapPoint(
                    group.X,
                    group.Y,
                    Proximity,
                    CommanderHeading,
                    viewportCenter,
                    scale));
        }

        if (Proximity is not null)
        {
            DrawCommander(context, viewportCenter);
        }

        if (ShowLegend)
        {
            DrawLegend(context, projection);
        }
    }

    public static IReadOnlyList<string> CreateLegendLabels(
        GuardianSiteMapProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return CreateLegendEntries(projection)
            .Select(entry => entry.Label)
            .ToArray();
    }

    public static Point TransformMapPoint(
        double x,
        double y,
        GuardianSiteProximitySnapshot? proximity,
        double commanderHeading,
        Point viewportCenter,
        double scale)
    {
        if (!double.IsFinite(scale) || scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scale));
        }

        var relativeX = x - (proximity?.MapX ?? 0);
        var relativeY = y - (proximity?.MapY ?? 0);
        var heading = double.IsFinite(commanderHeading)
            ? commanderHeading
            : 0;
        var radians = heading * Math.PI / 180;
        var rotatedX = (relativeX * Math.Cos(radians))
            + (relativeY * Math.Sin(radians));
        var rotatedY = (-relativeX * Math.Sin(radians))
            + (relativeY * Math.Cos(radians));
        return new Point(
            viewportCenter.X + rotatedX * scale,
            viewportCenter.Y + rotatedY * scale);
    }

    private void DrawCommander(
        DrawingContext context,
        Point location)
    {
        var brush = PresentBrush ?? Brushes.LimeGreen;
        var pen = new Pen(brush, 2);
        context.DrawEllipse(MapBackground, pen, location, 7, 7);
        context.DrawEllipse(brush, null, location, 2.5, 2.5);
    }

    private void DrawLegend(
        DrawingContext context,
        GuardianSiteMapProjection projection)
    {
        var entries = CreateLegendEntries(projection);
        const double rowHeight = 17;
        const double width = 156;
        var height = 28 + entries.Count * rowHeight;
        var panel = new Rect(12, 12, width, height);
        context.DrawRectangle(
            MapBackground ?? Brushes.Black,
            new Pen(GridBrush ?? Brushes.Gray, 1),
            panel,
            5,
            5);
        context.DrawText(
            CreateLegendText("Legend", FontWeight.Bold),
            new Point(22, 18));
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var center = new Point(28, 45 + index * rowHeight);
            DrawLegendSymbol(context, center, entry);
            context.DrawText(
                CreateLegendText(entry.Label, FontWeight.Normal),
                new Point(42, center.Y - 7));
        }
    }

    private void DrawLegendSymbol(
        DrawingContext context,
        Point center,
        GuardianMapLegendEntry entry)
    {
        var accent = AccentBrush ?? Brushes.Cyan;
        if (entry.Kind == GuardianMapLegendKind.SiteHeading)
        {
            context.DrawLine(
                new Pen(accent, 2),
                new Point(center.X - 6, center.Y + 5),
                new Point(center.X + 5, center.Y - 6));
            return;
        }

        if (entry.Kind == GuardianMapLegendKind.TowerHeading)
        {
            context.DrawLine(
                new Pen(EmptyBrush ?? Brushes.Goldenrod, 2),
                new Point(center.X - 6, center.Y + 5),
                new Point(center.X + 5, center.Y - 6));
            return;
        }

        if (entry.Kind == GuardianMapLegendKind.SurveyNeeded)
        {
            GuardianSurveyMarkerDrawing.Draw(
                context,
                center,
                haloRadius: 8,
                ringRadius: 7,
                dotRadius: 0.6);
            return;
        }

        DrawPoint(
            context,
            new GuardianProjectedPoint(
                entry.Label,
                entry.Type,
                0,
                0,
                0,
                0,
                0,
                entry.Status,
                false,
                false,
                string.Empty,
                []),
            center,
            new GuardianSiteMapProjection(
                "Alpha",
                [],
                [],
                1,
                IsRuins: true,
                SiteHeading: 0,
                RelicTowerHeading: 45),
            headingLength: 0);
    }

    private FormattedText CreateLegendText(
        string text,
        FontWeight weight)
    {
        return new FormattedText(
            LocalizationCatalog.Translate(text),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter", FontStyle.Normal, weight),
            11,
            MutedBrush ?? Brushes.Wheat);
    }

    private static IReadOnlyList<GuardianMapLegendEntry> CreateLegendEntries(
        GuardianSiteMapProjection projection)
    {
        var entries = new List<GuardianMapLegendEntry>
        {
            new("Relic tower", GuardianPoiType.Relic),
            new("Orb", GuardianPoiType.Orb),
            new("Casket", GuardianPoiType.Casket),
            new("Tablet", GuardianPoiType.Tablet),
            new("Totem", GuardianPoiType.Totem),
            new("Urn", GuardianPoiType.Urn),
            new("Empty puddle", GuardianPoiType.EmptyPuddle, GuardianPoiStatus.Empty),
            new("Obelisk", GuardianPoiType.Obelisk),
        };
        if (projection.Points.Any(point => point.Type == GuardianPoiType.Pylon))
        {
            entries.Add(new GuardianMapLegendEntry(
                "Energy pylon",
                GuardianPoiType.Pylon));
        }

        if (projection.Points.Any(point => point.Type
                is GuardianPoiType.Component
                    or GuardianPoiType.DestructiblePanel))
        {
            entries.Add(new GuardianMapLegendEntry(
                "Component tower",
                GuardianPoiType.Component));
        }

        entries.Add(new GuardianMapLegendEntry(
            "Site heading",
            GuardianPoiType.Unknown,
            Kind: GuardianMapLegendKind.SiteHeading));
        entries.Add(new GuardianMapLegendEntry(
            "Tower heading",
            GuardianPoiType.Unknown,
            Kind: GuardianMapLegendKind.TowerHeading));
        entries.Add(new GuardianMapLegendEntry(
            "Survey needed",
            GuardianPoiType.Unknown,
            Kind: GuardianMapLegendKind.SurveyNeeded));
        return entries;
    }

    private void DrawPoint(
        DrawingContext context,
        GuardianProjectedPoint point,
        Point location,
        GuardianSiteMapProjection projection,
        double headingLength)
    {
        var style = GuardianLegacyMapDrawing.GetPointStyle(
            point.Type,
            point.Status,
            point.IsActiveObelisk);
        var pen = CreatePen(style);
        var fill = style.HasFill ? new SolidColorBrush(style.Fill) : null;
        var rotation = GuardianLegacyMapDrawing.GetGlyphRotation(
            point,
            projection,
            CommanderHeading);
        if (RequiresSurveyMarker(point))
        {
            var (haloRadius, ringRadius) = GetSurveyMarkerRadii(point.Type);
            GuardianSurveyMarkerDrawing.Draw(
                context,
                location,
                haloRadius,
                ringRadius,
                dotRadius: 0.75);
        }

        if (point.Type == GuardianPoiType.Relic
            && point.Status == GuardianPoiStatus.Unknown
            && projection.IsRuins)
        {
            context.DrawEllipse(
                null,
                new Pen(
                    new SolidColorBrush(GuardianLegacyMapDrawing.Cyan),
                    4,
                    dashStyle: DashStyle.Dash),
                location,
                8,
                8);
        }

        if (point.Type == GuardianPoiType.Relic
            && point.HasIndividualRelicHeading
            && headingLength > 0)
        {
            var (start, end) = GuardianLegacyMapDrawing.CreateHeadingLine(
                location,
                headingLength,
                rotation);
            context.DrawLine(
                new Pen(
                    new SolidColorBrush(
                        GuardianLegacyMapDrawing.IndividualTowerHeading),
                    10),
                start,
                end);
        }

        var isTarget = string.Equals(
            point.Name,
            TargetPointName,
            StringComparison.OrdinalIgnoreCase);
        var isNearest = Proximity?.NearestPoint is
        { Distance: <= 75 } nearest
            && string.Equals(
                nearest.Point.Name,
                point.Name,
                StringComparison.OrdinalIgnoreCase);
        if (isTarget || isNearest)
        {
            var highlightRadius = point.Type == GuardianPoiType.Obelisk
                ? 8
                : 13;
            context.DrawEllipse(
                null,
                new Pen(
                    new SolidColorBrush(GuardianLegacyMapDrawing.Target),
                    point.Type == GuardianPoiType.Obelisk ? 2 : 4,
                    dashStyle: DashStyle.Dot),
                location,
                highlightRadius,
                highlightRadius);
        }

        if (point.Type == GuardianPoiType.Obelisk
            && point.IsActiveObelisk)
        {
            DrawActiveObeliskEffect(context, point, location, rotation);
        }

        switch (point.Type)
        {
            case GuardianPoiType.Obelisk:
            case GuardianPoiType.BrokenObelisk:
                DrawPolyline(
                    context,
                    GuardianLegacyMapDrawing.CreateGlyphPoints(
                        point.Type,
                        location,
                        rotation),
                    pen);
                if (point.Type == GuardianPoiType.BrokenObelisk)
                {
                    break;
                }

                context.DrawLine(
                    pen,
                    location + GuardianLegacyMapDrawing.RotateClockwise(
                        new Point(0.2, 0),
                        rotation),
                    location + GuardianLegacyMapDrawing.RotateClockwise(
                        new Point(-0.5, -1.2),
                        rotation));
                context.DrawLine(
                    pen,
                    location + GuardianLegacyMapDrawing.RotateClockwise(
                        new Point(0.2, 0),
                        rotation),
                    location + GuardianLegacyMapDrawing.RotateClockwise(
                        new Point(1.5, -0.8),
                        rotation));
                break;

            case GuardianPoiType.Pylon:
                DrawPolyline(
                    context,
                    GuardianLegacyMapDrawing.CreateGlyphPoints(
                        point.Type,
                        location,
                        rotation),
                    pen);
                context.DrawLine(
                    pen,
                    location,
                    location + GuardianLegacyMapDrawing.RotateClockwise(
                        new Point(0, 3),
                        rotation));
                break;

            case GuardianPoiType.Component:
                DrawPolyline(
                    context,
                    GuardianLegacyMapDrawing.CreateGlyphPoints(
                        point.Type,
                        location,
                        rotation),
                    pen);
                DrawComponentMaterials(
                    context,
                    location,
                    point.ComponentMaterials);
                break;

            case GuardianPoiType.DestructiblePanel:
                var materialColor = GuardianLegacyMapDrawing
                    .GetComponentMaterialColor(
                    point.ComponentMaterials.FirstOrDefault());
                context.DrawRectangle(
                    materialColor is { } known
                        ? new SolidColorBrush(known)
                        : null,
                    materialColor is not null
                        ? new Pen(Brushes.Black, 1)
                        : pen,
                    new Rect(location.X - 2, location.Y - 2, 4, 4));
                break;

            case GuardianPoiType.Relic:
                context.DrawGeometry(
                    fill,
                    pen,
                    CreatePolygon(
                        GuardianLegacyMapDrawing.CreateGlyphPoints(
                            point.Type,
                            location,
                            rotation)));
                break;

            default:
                var radius = GuardianLegacyMapDrawing.GetPuddleRadius(
                    projection,
                    point);
                context.DrawEllipse(
                    fill,
                    pen,
                    location,
                    radius,
                    radius);
                break;
        }
    }

    private static void DrawHeadingLines(
        DrawingContext context,
        GuardianSiteMapProjection projection,
        Point mapOrigin,
        double length,
        double commanderHeading)
    {
        if (projection.SiteHeading < 0)
        {
            return;
        }

        var (siteStart, siteEnd) = GuardianLegacyMapDrawing.CreateHeadingLine(
            mapOrigin,
            length,
            -commanderHeading);
        context.DrawLine(
            new Pen(
                new SolidColorBrush(GuardianLegacyMapDrawing.SiteHeading),
                4,
                dashStyle: DashStyle.Dash),
            siteStart,
            siteEnd);

        if (projection.RelicTowerHeading < 0)
        {
            return;
        }

        var towerRotation = projection.RelicTowerHeading
            - projection.SiteHeading
            - commanderHeading;
        var (towerStart, towerEnd) =
            GuardianLegacyMapDrawing.CreateHeadingLine(
                mapOrigin,
                length,
                towerRotation);
        context.DrawLine(
            new Pen(
                new SolidColorBrush(GuardianLegacyMapDrawing.TowerHeading),
                4),
            towerStart,
            towerEnd);
    }

    private static void DrawActiveObeliskEffect(
        DrawingContext context,
        GuardianProjectedPoint point,
        Point location,
        double rotation)
    {
        var color = GuardianLegacyMapDrawing.GetActiveObeliskEffectColor(point);
        for (var step = 0; step < 6; step++)
        {
            var radius = 15 - step * 2.2;
            var alpha = (byte)(18 + step * 22);
            context.DrawGeometry(
                new SolidColorBrush(Color.FromArgb(
                    alpha,
                    color.R,
                    color.G,
                    color.B)),
                null,
                CreatePolygon(GuardianLegacyMapDrawing.CreateWedge(
                    location,
                    radius,
                    rotation)));
        }
    }

    private static Pen CreatePen(GuardianLegacyPointStyle style)
    {
        var dash = style.Pattern switch
        {
            GuardianLegacyStrokePattern.Dash => DashStyle.Dash,
            GuardianLegacyStrokePattern.Dot => DashStyle.Dot,
            _ => null,
        };
        return new Pen(
            new SolidColorBrush(style.Stroke),
            style.StrokeWidth,
            dashStyle: dash);
    }

    private static void DrawPolyline(
        DrawingContext context,
        IReadOnlyList<Point> points,
        Pen pen)
    {
        if (points.Count < 2)
        {
            return;
        }

        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(points[0], isFilled: false);
            for (var index = 1; index < points.Count; index++)
            {
                geometryContext.LineTo(points[index]);
            }

            geometryContext.EndFigure(isClosed: false);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    internal static bool RequiresSurveyMarker(GuardianProjectedPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);
        return point.Status == GuardianPoiStatus.Unknown
            && point.Type is not GuardianPoiType.Obelisk
                and not GuardianPoiType.BrokenObelisk
                and not GuardianPoiType.EmptyPuddle;
    }

    internal static (double HaloRadius, double RingRadius)
        GetSurveyMarkerRadii(GuardianPoiType type)
    {
        return type == GuardianPoiType.Relic
            ? (13, 12)
            : (10, 9);
    }

    private static void DrawComponentMaterials(
        DrawingContext context,
        Point location,
        IReadOnlyList<GuardianComponentMaterial> materials)
    {
        var centers = GuardianLegacyMapDrawing.CreateComponentMaterialCenters(
            location);
        for (var index = 0; index < centers.Count && index < materials.Count;
             index++)
        {
            var color = GuardianLegacyMapDrawing.GetComponentMaterialColor(
                materials[index]);
            if (color is null)
            {
                continue;
            }

            context.DrawEllipse(
                new SolidColorBrush(color.Value),
                new Pen(Brushes.Black, 1),
                centers[index],
                2,
                2);
        }
    }

    private void DrawGroup(
        DrawingContext context,
        GuardianProjectedGroup group,
        Point location)
    {
        var brush = AccentBrush ?? Brushes.Cyan;
        var text = new FormattedText(
            group.Name,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter", FontStyle.Normal, FontWeight.SemiBold),
            12,
            brush);
        context.DrawEllipse(
            MapBackground,
            new Pen(brush, 1),
            location,
            9,
            9);
        context.DrawText(
            text,
            new Point(
                location.X - text.Width / 2,
                location.Y - text.Height / 2));
    }

    private static StreamGeometry CreatePolygon(IReadOnlyList<Point> points)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(points[0], isFilled: true);
            for (var index = 1; index < points.Count; index++)
            {
                context.LineTo(points[index]);
            }

            context.EndFigure(isClosed: true);
        }

        return geometry;
    }

    private sealed record GuardianMapLegendEntry(
        string Label,
        GuardianPoiType Type,
        GuardianPoiStatus Status = GuardianPoiStatus.Present,
        GuardianMapLegendKind Kind = GuardianMapLegendKind.Point);

    private enum GuardianMapLegendKind
    {
        Point,
        SiteHeading,
        TowerHeading,
        SurveyNeeded,
    }
}
