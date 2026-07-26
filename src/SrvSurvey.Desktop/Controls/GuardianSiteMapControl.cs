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
                    scale));
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
            context.DrawEllipse(
                null,
                new Pen(accent, 1.5, dashStyle: DashStyle.Dot),
                center,
                7,
                7);
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
            center);
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
        Point location)
    {
        var brush = GetPointBrush(point);
        var isTarget = string.Equals(
            point.Name,
            TargetPointName,
            StringComparison.OrdinalIgnoreCase);
        var pen = new Pen(
            isTarget ? AccentBrush ?? Brushes.Cyan : brush,
            point.IsActiveObelisk || isTarget ? 2.5 : 1.5);
        if (point.IsActiveObelisk || isTarget)
        {
            context.DrawEllipse(null, pen, location, isTarget ? 11 : 8, isTarget ? 11 : 8);
        }

        switch (point.Type)
        {
            case GuardianPoiType.Obelisk:
                DrawTriangle(context, location, 4.5, pen, point.IsScannedObelisk);
                context.DrawLine(
                    pen,
                    new Point(location.X, location.Y - 4),
                    new Point(location.X, location.Y + 5));
                break;

            case GuardianPoiType.BrokenObelisk:
                context.DrawLine(
                    pen,
                    new Point(location.X - 4, location.Y - 4),
                    new Point(location.X + 4, location.Y + 4));
                context.DrawLine(
                    pen,
                    new Point(location.X + 4, location.Y - 4),
                    new Point(location.X - 4, location.Y + 4));
                break;

            case GuardianPoiType.Pylon:
                DrawDiamond(context, location, 5, pen);
                break;

            case GuardianPoiType.Component:
                context.DrawRectangle(
                    point.Status == GuardianPoiStatus.Present ? brush : null,
                    pen,
                    new Rect(location.X - 3.5, location.Y - 3.5, 7, 7));
                DrawComponentMaterials(
                    context,
                    location,
                    point.ComponentMaterials);
                break;

            case GuardianPoiType.DestructiblePanel:
                var materialBrush = GetComponentMaterialBrush(
                    point.ComponentMaterials.FirstOrDefault());
                context.DrawRectangle(
                    materialBrush
                        ?? (point.Status == GuardianPoiStatus.Present
                            ? brush
                            : null),
                    pen,
                    new Rect(location.X - 3.5, location.Y - 3.5, 7, 7));
                break;

            case GuardianPoiType.Relic:
                DrawTriangle(
                    context,
                    location,
                    6,
                    pen,
                    point.Status == GuardianPoiStatus.Present);
                break;

            default:
                context.DrawEllipse(
                    point.Status is GuardianPoiStatus.Present
                        or GuardianPoiStatus.Empty
                            ? brush
                            : null,
                    pen,
                    location,
                    4,
                    4);
                break;
        }
    }

    private static void DrawComponentMaterials(
        DrawingContext context,
        Point location,
        IReadOnlyList<GuardianComponentMaterial> materials)
    {
        var offsets = new[]
        {
            new Point(0, -8),
            new Point(-7, 5),
            new Point(7, 5),
        };
        for (var index = 0; index < offsets.Length && index < materials.Count;
             index++)
        {
            var brush = GetComponentMaterialBrush(materials[index]);
            if (brush is null)
            {
                continue;
            }

            var center = location + offsets[index];
            context.DrawEllipse(
                brush,
                new Pen(Brushes.Black, 1),
                center,
                3,
                3);
        }
    }

    private static IBrush? GetComponentMaterialBrush(
        GuardianComponentMaterial material)
    {
        return material switch
        {
            GuardianComponentMaterial.Cell => Brushes.Lime,
            GuardianComponentMaterial.Conduit => Brushes.Cyan,
            GuardianComponentMaterial.Tech => Brushes.OrangeRed,
            _ => null,
        };
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

    private IBrush GetPointBrush(GuardianProjectedPoint point)
    {
        if (point.IsScannedObelisk)
        {
            return PresentBrush ?? Brushes.LimeGreen;
        }

        if (point.IsActiveObelisk)
        {
            return AccentBrush ?? Brushes.Cyan;
        }

        return point.Status switch
        {
            GuardianPoiStatus.Present => PresentBrush ?? Brushes.LimeGreen,
            GuardianPoiStatus.Absent => AbsentBrush ?? Brushes.OrangeRed,
            GuardianPoiStatus.Empty => EmptyBrush ?? Brushes.Goldenrod,
            _ => MutedBrush ?? Brushes.Gray,
        };
    }

    private static void DrawTriangle(
        DrawingContext context,
        Point center,
        double radius,
        Pen pen,
        bool fill)
    {
        var points = new[]
        {
            new Point(center.X, center.Y - radius),
            new Point(center.X + radius, center.Y + radius),
            new Point(center.X - radius, center.Y + radius),
        };
        var geometry = CreatePolygon(points);
        context.DrawGeometry(fill ? pen.Brush : null, pen, geometry);
    }

    private static void DrawDiamond(
        DrawingContext context,
        Point center,
        double radius,
        Pen pen)
    {
        var geometry = CreatePolygon(
        [
            new Point(center.X, center.Y - radius),
            new Point(center.X + radius, center.Y),
            new Point(center.X, center.Y + radius),
            new Point(center.X - radius, center.Y),
        ]);
        context.DrawGeometry(null, pen, geometry);
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
