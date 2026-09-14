using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SrvSurvey.Core.Settlements;
using SrvSurvey.Desktop.Localization;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Controls;

public sealed class HumanSiteMapControl : Control
{
    public static readonly StyledProperty<HumanSiteMapProjection?> ProjectionProperty = AvaloniaProperty.Register<
        HumanSiteMapControl,
        HumanSiteMapProjection?
    >(nameof(Projection));
    public static readonly StyledProperty<HumanSiteMapPoint?> CommanderOffsetProperty = AvaloniaProperty.Register<
        HumanSiteMapControl,
        HumanSiteMapPoint?
    >(nameof(CommanderOffset));
    public static readonly StyledProperty<HumanSiteMapPoint?> ShipOffsetProperty = AvaloniaProperty.Register<
        HumanSiteMapControl,
        HumanSiteMapPoint?
    >(nameof(ShipOffset));
    public static readonly StyledProperty<HumanSiteMapPoint?> SrvOffsetProperty = AvaloniaProperty.Register<
        HumanSiteMapControl,
        HumanSiteMapPoint?
    >(nameof(SrvOffset));
    public static readonly StyledProperty<bool> HasShipDepartedProperty = AvaloniaProperty.Register<
        HumanSiteMapControl,
        bool
    >(nameof(HasShipDeparted));
    public static readonly StyledProperty<bool> ShowShipDismissalBoundaryProperty = AvaloniaProperty.Register<
        HumanSiteMapControl,
        bool
    >(nameof(ShowShipDismissalBoundary));
    public static readonly StyledProperty<IReadOnlyList<HumanSiteMapPoint>?> ProcessedTerminalOffsetsProperty =
        AvaloniaProperty.Register<HumanSiteMapControl, IReadOnlyList<HumanSiteMapPoint>?>(
            nameof(ProcessedTerminalOffsets)
        );
    public static readonly StyledProperty<IReadOnlyList<HumanSiteCollectedMaterial>?> CollectedMaterialsProperty =
        AvaloniaProperty.Register<HumanSiteMapControl, IReadOnlyList<HumanSiteCollectedMaterial>?>(
            nameof(CollectedMaterials)
        );
    public static readonly StyledProperty<IReadOnlyList<HumanSiteQuestMarker>?> QuestMarkersProperty =
        AvaloniaProperty.Register<HumanSiteMapControl, IReadOnlyList<HumanSiteQuestMarker>?>(nameof(QuestMarkers));
    public static readonly StyledProperty<IReadOnlyList<HumanSiteQuestRoute>?> QuestRoutesProperty =
        AvaloniaProperty.Register<HumanSiteMapControl, IReadOnlyList<HumanSiteQuestRoute>?>(nameof(QuestRoutes));
    public static readonly StyledProperty<double> CommanderHeadingProperty = AvaloniaProperty.Register<
        HumanSiteMapControl,
        double
    >(nameof(CommanderHeading));
    public static readonly StyledProperty<double> ScaleMultiplierProperty = AvaloniaProperty.Register<
        HumanSiteMapControl,
        double
    >(nameof(ScaleMultiplier), 1);
    public static readonly StyledProperty<bool> ShowOriginWarningProperty = AvaloniaProperty.Register<
        HumanSiteMapControl,
        bool
    >(nameof(ShowOriginWarning));
    public static readonly StyledProperty<IBrush?> MapBackgroundProperty = AvaloniaProperty.Register<
        HumanSiteMapControl,
        IBrush?
    >(nameof(MapBackground));
    public static readonly StyledProperty<IBrush?> GridBrushProperty = AvaloniaProperty.Register<
        HumanSiteMapControl,
        IBrush?
    >(nameof(GridBrush));
    public static readonly StyledProperty<IBrush?> AccentBrushProperty = AvaloniaProperty.Register<
        HumanSiteMapControl,
        IBrush?
    >(nameof(AccentBrush));
    public static readonly StyledProperty<IBrush?> MutedBrushProperty = AvaloniaProperty.Register<
        HumanSiteMapControl,
        IBrush?
    >(nameof(MutedBrush));
    public static readonly StyledProperty<IBrush?> SurfaceBrushProperty = AvaloniaProperty.Register<
        HumanSiteMapControl,
        IBrush?
    >(nameof(SurfaceBrush));
    public static readonly StyledProperty<IBrush?> SuccessBrushProperty = AvaloniaProperty.Register<
        HumanSiteMapControl,
        IBrush?
    >(nameof(SuccessBrush));
    public static readonly StyledProperty<IBrush?> WarningBrushProperty = AvaloniaProperty.Register<
        HumanSiteMapControl,
        IBrush?
    >(nameof(WarningBrush));
    public static readonly StyledProperty<IBrush?> DangerBrushProperty = AvaloniaProperty.Register<
        HumanSiteMapControl,
        IBrush?
    >(nameof(DangerBrush));
    public static readonly StyledProperty<IBrush?> TextBrushProperty = AvaloniaProperty.Register<
        HumanSiteMapControl,
        IBrush?
    >(nameof(TextBrush));
    public static readonly StyledProperty<IBrush?> CheckpointBrushProperty = AvaloniaProperty.Register<
        HumanSiteMapControl,
        IBrush?
    >(nameof(CheckpointBrush));
    public static readonly StyledProperty<IBrush?> LocalCheckpointBrushProperty = AvaloniaProperty.Register<
        HumanSiteMapControl,
        IBrush?
    >(nameof(LocalCheckpointBrush));
    public static readonly StyledProperty<IBrush?> PowerPostBrushProperty = AvaloniaProperty.Register<
        HumanSiteMapControl,
        IBrush?
    >(nameof(PowerPostBrush));
    public static readonly StyledProperty<IBrush?> ProcessedBrushProperty = AvaloniaProperty.Register<
        HumanSiteMapControl,
        IBrush?
    >(nameof(ProcessedBrush));
    public static readonly StyledProperty<IBrush?> QuestBrushProperty = AvaloniaProperty.Register<
        HumanSiteMapControl,
        IBrush?
    >(nameof(QuestBrush));

    static HumanSiteMapControl()
    {
        AffectsRender<HumanSiteMapControl>(
            ProjectionProperty,
            CommanderOffsetProperty,
            ShipOffsetProperty,
            SrvOffsetProperty,
            HasShipDepartedProperty,
            ShowShipDismissalBoundaryProperty,
            ProcessedTerminalOffsetsProperty,
            CollectedMaterialsProperty,
            QuestMarkersProperty,
            QuestRoutesProperty,
            CommanderHeadingProperty,
            ScaleMultiplierProperty,
            ShowOriginWarningProperty,
            MapBackgroundProperty,
            GridBrushProperty,
            AccentBrushProperty,
            MutedBrushProperty,
            SurfaceBrushProperty,
            SuccessBrushProperty,
            WarningBrushProperty,
            DangerBrushProperty,
            TextBrushProperty,
            CheckpointBrushProperty,
            LocalCheckpointBrushProperty,
            PowerPostBrushProperty,
            ProcessedBrushProperty,
            QuestBrushProperty
        );
    }

    public HumanSiteMapProjection? Projection
    {
        get => GetValue(ProjectionProperty);
        set => SetValue(ProjectionProperty, value);
    }

    public HumanSiteMapPoint? CommanderOffset
    {
        get => GetValue(CommanderOffsetProperty);
        set => SetValue(CommanderOffsetProperty, value);
    }

    public HumanSiteMapPoint? ShipOffset
    {
        get => GetValue(ShipOffsetProperty);
        set => SetValue(ShipOffsetProperty, value);
    }

    public HumanSiteMapPoint? SrvOffset
    {
        get => GetValue(SrvOffsetProperty);
        set => SetValue(SrvOffsetProperty, value);
    }

    public bool HasShipDeparted
    {
        get => GetValue(HasShipDepartedProperty);
        set => SetValue(HasShipDepartedProperty, value);
    }

    public bool ShowShipDismissalBoundary
    {
        get => GetValue(ShowShipDismissalBoundaryProperty);
        set => SetValue(ShowShipDismissalBoundaryProperty, value);
    }

    public IReadOnlyList<HumanSiteMapPoint>? ProcessedTerminalOffsets
    {
        get => GetValue(ProcessedTerminalOffsetsProperty);
        set => SetValue(ProcessedTerminalOffsetsProperty, value);
    }

    public IReadOnlyList<HumanSiteCollectedMaterial>? CollectedMaterials
    {
        get => GetValue(CollectedMaterialsProperty);
        set => SetValue(CollectedMaterialsProperty, value);
    }

    public IReadOnlyList<HumanSiteQuestMarker>? QuestMarkers
    {
        get => GetValue(QuestMarkersProperty);
        set => SetValue(QuestMarkersProperty, value);
    }

    public IReadOnlyList<HumanSiteQuestRoute>? QuestRoutes
    {
        get => GetValue(QuestRoutesProperty);
        set => SetValue(QuestRoutesProperty, value);
    }

    public double CommanderHeading
    {
        get => GetValue(CommanderHeadingProperty);
        set => SetValue(CommanderHeadingProperty, value);
    }

    public double ScaleMultiplier
    {
        get => GetValue(ScaleMultiplierProperty);
        set => SetValue(ScaleMultiplierProperty, value);
    }

    public bool ShowOriginWarning
    {
        get => GetValue(ShowOriginWarningProperty);
        set => SetValue(ShowOriginWarningProperty, value);
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

    public IBrush? SurfaceBrush
    {
        get => GetValue(SurfaceBrushProperty);
        set => SetValue(SurfaceBrushProperty, value);
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

    public IBrush? TextBrush
    {
        get => GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
    }

    public IBrush? CheckpointBrush
    {
        get => GetValue(CheckpointBrushProperty);
        set => SetValue(CheckpointBrushProperty, value);
    }

    public IBrush? LocalCheckpointBrush
    {
        get => GetValue(LocalCheckpointBrushProperty);
        set => SetValue(LocalCheckpointBrushProperty, value);
    }

    public IBrush? PowerPostBrush
    {
        get => GetValue(PowerPostBrushProperty);
        set => SetValue(PowerPostBrushProperty, value);
    }

    public IBrush? ProcessedBrush
    {
        get => GetValue(ProcessedBrushProperty);
        set => SetValue(ProcessedBrushProperty, value);
    }

    public IBrush? QuestBrush
    {
        get => GetValue(QuestBrushProperty);
        set => SetValue(QuestBrushProperty, value);
    }

    public HumanSiteMapControl()
    {
        ClipToBounds = true;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = new Rect(Bounds.Size);
        IBrush background = MapBackground ?? Brushes.Transparent;
        IBrush grid = GridBrush ?? Brushes.DimGray;
        context.DrawRectangle(background, new Pen(grid, 1), bounds, 8, 8);
        if (
            Projection is not { } projection
            || CommanderOffset is not { } commander
            || bounds.Width <= 0
            || bounds.Height <= 0
        )
        {
            return;
        }

        Point center = bounds.Center;
        double scale = double.IsFinite(ScaleMultiplier) ? Math.Clamp(ScaleMultiplier, 0.1, 15) : 1;
        // Keep settlement geometry inside the map frame so out-of-range
        // markers disappear at the border instead of painting over UI.
        using (context.PushClip(bounds))
        {
            DrawSiteContents(context, projection, bounds, center, commander, scale, grid);
        }
    }

    private void DrawSiteContents(
        DrawingContext context,
        HumanSiteMapProjection projection,
        Rect bounds,
        Point center,
        HumanSiteMapPoint commander,
        double scale,
        IBrush grid
    )
    {
        DrawSiteGrid(context, bounds, center, commander, scale, grid);
        DrawBuildings(context, projection, center, commander, scale);
        DrawOuterLimit(context, center, commander, scale);
        foreach (HumanSiteProjectedPoint pad in projection.LandingPads)
        {
            DrawLandingPad(context, pad, center, commander, scale);
        }

        foreach (HumanSiteProjectedPoint door in projection.SecureDoors)
        {
            DrawDoor(context, door, center, commander, scale);
        }

        foreach (HumanSiteProjectedPoint point in projection.NamedPoints)
        {
            DrawNamedPoint(context, point, center, commander, scale);
        }

        for (int index = 0; index < projection.DataTerminals.Count; index++)
        {
            DrawTerminal(
                context,
                projection.DataTerminals[index],
                ProcessedTerminalOffsets?.Contains(projection.DataTerminals[index].Offset) == true,
                center,
                commander,
                scale
            );
        }

        foreach (HumanSiteProjectedPoint point in projection.ConflictZonePoints)
        {
            DrawConflictZonePoint(context, point, center, commander, scale);
        }

        DrawVehicleMarkers(context, center, commander, scale);
        DrawQuestRoutes(context, center, commander, scale);
        DrawQuestMarkers(context, bounds, center, commander, scale);
        DrawCollectedMaterials(context, center, commander, scale);
        DrawCommander(context, center);
        if (ShowOriginWarning)
        {
            DrawOriginWarning(context, bounds, center, commander, scale);
        }
    }

    public static Point TransformMapPoint(
        HumanSiteMapPoint point,
        HumanSiteMapPoint commander,
        double commanderHeading,
        Point viewportCenter,
        double scale
    )
    {
        double x = point.X - commander.X;
        double y = point.Y - commander.Y;
        double radians = commanderHeading * Math.PI / 180;
        double rotatedX = (x * Math.Cos(radians)) - (y * Math.Sin(radians));
        double rotatedY = (y * Math.Cos(radians)) + (x * Math.Sin(radians));
        return new Point(viewportCenter.X + (rotatedX * scale), viewportCenter.Y - (rotatedY * scale));
    }

    private void DrawSiteGrid(
        DrawingContext context,
        Rect bounds,
        Point center,
        HumanSiteMapPoint commander,
        double scale,
        IBrush grid
    )
    {
        var axis = new Pen(grid, 1, dashStyle: DashStyle.Dash);
        Point west = Transform(new HumanSiteMapPoint(-1_500, 0), center, commander, scale);
        Point east = Transform(new HumanSiteMapPoint(1_500, 0), center, commander, scale);
        Point south = Transform(new HumanSiteMapPoint(0, -1_500), center, commander, scale);
        Point north = Transform(new HumanSiteMapPoint(0, 1_500), center, commander, scale);
        context.DrawLine(axis, Clamp(west, bounds), Clamp(east, bounds));
        context.DrawLine(axis, Clamp(south, bounds), Clamp(north, bounds));
        context.DrawLine(
            new Pen(DangerBrush ?? Brushes.OrangeRed, 1.5),
            Transform(new HumanSiteMapPoint(0, 0), center, commander, scale),
            Clamp(north, bounds)
        );
    }

    private void DrawBuildings(
        DrawingContext context,
        HumanSiteMapProjection projection,
        Point center,
        HumanSiteMapPoint commander,
        double scale
    )
    {
        foreach (HumanSiteProjectedBuilding building in projection.Buildings)
        {
            IBrush brush = GetBuildingBrush(building.Name);
            foreach (HumanSiteProjectedPath path in building.Paths)
            {
                StreamGeometry geometry = CreatePath(path, center, commander, scale);
                context.DrawGeometry(brush, new Pen(brush, 0.75), geometry);
            }
        }
    }

    private StreamGeometry CreatePath(
        HumanSiteProjectedPath path,
        Point center,
        HumanSiteMapPoint commander,
        double scale
    )
    {
        var geometry = new StreamGeometry();
        using StreamGeometryContext target = geometry.Open();
        bool figureOpen = false;
        foreach (HumanSitePathSegment segment in path.Segments)
        {
            switch (segment.Kind)
            {
                case HumanSitePathSegmentKind.Move:
                    if (figureOpen)
                    {
                        target.EndFigure(isClosed: false);
                    }

                    target.BeginFigure(Transform(segment.First, center, commander, scale), isFilled: true);
                    figureOpen = true;
                    break;

                case HumanSitePathSegmentKind.Line when figureOpen:
                    target.LineTo(Transform(segment.First, center, commander, scale));
                    break;

                case HumanSitePathSegmentKind.CubicBezier when figureOpen:
                    target.CubicBezierTo(
                        Transform(segment.First, center, commander, scale),
                        Transform(segment.Second, center, commander, scale),
                        Transform(segment.Third, center, commander, scale)
                    );
                    break;

                case HumanSitePathSegmentKind.Close when figureOpen:
                    target.EndFigure(isClosed: true);
                    figureOpen = false;
                    break;
            }
        }

        if (figureOpen)
        {
            target.EndFigure(isClosed: false);
        }

        return geometry;
    }

    private void DrawOuterLimit(DrawingContext context, Point center, HumanSiteMapPoint commander, double scale)
    {
        Point origin = Transform(default, center, commander, scale);
        double radius = HumanSiteViewModel.ShipCallLimitMeters * scale;
        context.DrawEllipse(null, new Pen(DangerBrush ?? Brushes.OrangeRed, 1, DashStyle.Dash), origin, radius, radius);
    }

    private void DrawVehicleMarkers(DrawingContext context, Point center, HumanSiteMapPoint commander, double scale)
    {
        if (ShipOffset is { } ship)
        {
            Point location = Transform(ship, center, commander, scale);
            if (ShowShipDismissalBoundary)
            {
                double radius = HumanSiteViewModel.ShipDismissalLimitMeters * scale;
                context.DrawEllipse(
                    null,
                    new Pen(WarningBrush ?? Brushes.Gold, 1, DashStyle.Dash),
                    location,
                    radius,
                    radius
                );
            }

            IBrush brush = HasShipDeparted ? MutedBrush ?? Brushes.DimGray : AccentBrush ?? Brushes.Cyan;
            context.DrawEllipse(MapBackground, new Pen(brush, 2), location, 24, 24);
            DrawLabel(context, LocalizationCatalog.Translate("SHIP"), location, brush, 8);
        }

        if (SrvOffset is { } srv)
        {
            Point location = Transform(srv, center, commander, scale);
            IBrush brush = WarningBrush ?? Brushes.Gold;
            context.DrawRectangle(
                MapBackground,
                new Pen(brush, 2),
                new Rect(location.X - 10, location.Y - 10, 20, 20),
                3,
                3
            );
            DrawLabel(context, LocalizationCatalog.Translate("SRV"), location, brush, 7);
        }
    }

    private void DrawLandingPad(
        DrawingContext context,
        HumanSiteProjectedPoint pad,
        Point center,
        HumanSiteMapPoint commander,
        double scale
    )
    {
        (double Width, double Height) size = pad.LandingPadSize switch
        {
            HumanSiteLandingPadSize.Small => (Width: 50d, Height: 70d),
            HumanSiteLandingPadSize.Medium => (Width: 70d, Height: 135d),
            _ => (Width: 90d, Height: 170d),
        };
        Point[] points = new[]
        {
            OrientedPoint(pad.Offset, -size.Width / 2, -size.Height / 2, pad.Rotation),
            OrientedPoint(pad.Offset, size.Width / 2, -size.Height / 2, pad.Rotation),
            OrientedPoint(pad.Offset, size.Width / 2, size.Height / 2, pad.Rotation),
            OrientedPoint(pad.Offset, -size.Width / 2, size.Height / 2, pad.Rotation),
        }
            .Select(point => Transform(point, center, commander, scale))
            .ToArray();
        StreamGeometry geometry = CreatePolygon(points);
        IBrush brush = AccentBrush ?? Brushes.Cyan;
        context.DrawGeometry(null, new Pen(brush, 1.5), geometry);
        Point location = Transform(pad.Offset, center, commander, scale);
        DrawLabel(context, pad.Name, location, brush, 10);
    }

    private void DrawDoor(
        DrawingContext context,
        HumanSiteProjectedPoint door,
        Point center,
        HumanSiteMapPoint commander,
        double scale
    )
    {
        Point location = Transform(door.Offset, center, commander, scale);
        IBrush brush = GetSecurityBrush(door.SecurityLevel);
        double radius = Math.Clamp(2 * scale, 2, 7);
        context.DrawRectangle(
            brush,
            new Pen(TextBrush ?? Brushes.White, 0.75),
            new Rect(location.X - radius, location.Y - radius / 2, radius * 2, radius)
        );
    }

    private void DrawNamedPoint(
        DrawingContext context,
        HumanSiteProjectedPoint point,
        Point center,
        HumanSiteMapPoint commander,
        double scale
    )
    {
        Point location = Transform(point.Offset, center, commander, scale);
        string symbol = point.Name switch
        {
            "Atmos" => "A",
            "Alarm" => "!",
            "Auth" => "K",
            "Medkit" => "+",
            "Battery" => "B",
            "Power" => "P",
            _ => "·",
        };
        DrawLabel(
            context,
            symbol,
            location,
            point.Name == "Power" ? AccentBrush ?? Brushes.Cyan : GetSecurityBrush(point.SecurityLevel),
            Math.Clamp(7 + scale, 8, 14)
        );
        DrawFloorChevron(context, location, point.Floor);
    }

    private void DrawTerminal(
        DrawingContext context,
        HumanSiteProjectedPoint terminal,
        bool processed,
        Point center,
        HumanSiteMapPoint commander,
        double scale
    )
    {
        Point location = Transform(terminal.Offset, center, commander, scale);
        IBrush brush = processed
            ? ProcessedBrush ?? MutedBrush ?? Brushes.DarkGreen
            : GetSecurityBrush(terminal.SecurityLevel);
        double radius = Math.Clamp(3 * scale, 3, 9);
        context.DrawRectangle(
            null,
            new Pen(brush, 1.5),
            new Rect(location.X - radius, location.Y - radius, radius * 2, radius * 2),
            2,
            2
        );
        context.DrawLine(
            new Pen(brush, 1),
            new Point(location.X - radius / 2, location.Y),
            new Point(location.X + radius / 2, location.Y)
        );
        DrawFloorChevron(context, location, terminal.Floor);
    }

    private void DrawCollectedMaterials(DrawingContext context, Point center, HumanSiteMapPoint commander, double scale)
    {
        if (CollectedMaterials is null)
        {
            return;
        }

        IBrush brush = TextBrush ?? Brushes.White;
        foreach (
            HumanSiteMapPoint offset in CollectedMaterials
                .Where(material => material.Offset.IsFinite)
                .Select(material => material.Offset)
        )
        {
            Point location = Transform(offset, center, commander, scale);
            context.DrawEllipse(MapBackground, new Pen(brush, 1.25), location, 2.5, 2.5);
        }
    }

    private void DrawQuestRoutes(DrawingContext context, Point center, HumanSiteMapPoint commander, double scale)
    {
        if (QuestRoutes is null)
        {
            return;
        }

        IBrush brush = QuestBrush ?? WarningBrush ?? Brushes.Gold;
        foreach (HumanSiteQuestRoute route in QuestRoutes)
        {
            if (route.Waypoints.Count < 2 || !double.IsFinite(route.Width) || route.Width < 0)
            {
                continue;
            }

            var pen = new Pen(
                brush,
                Math.Clamp(route.Width * scale, 1, 80),
                lineCap: PenLineCap.Round,
                lineJoin: PenLineJoin.Round
            );
            Point prior = Transform(route.Waypoints[0], center, commander, scale);
            for (int index = 1; index < route.Waypoints.Count; index++)
            {
                Point next = Transform(route.Waypoints[index], center, commander, scale);
                context.DrawLine(pen, prior, next);
                prior = next;
            }
        }
    }

    private void DrawQuestMarkers(
        DrawingContext context,
        Rect bounds,
        Point center,
        HumanSiteMapPoint commander,
        double scale
    )
    {
        if (QuestMarkers is null)
        {
            return;
        }

        foreach (HumanSiteQuestMarker marker in QuestMarkers)
        {
            if (!marker.Offset.IsFinite || !double.IsFinite(marker.Radius) || marker.Radius < 0)
            {
                continue;
            }

            Point location = Transform(marker.Offset, center, commander, scale);
            double radius = Math.Clamp(marker.Radius * scale, 1, Math.Max(bounds.Width, bounds.Height) * 4);
            IBrush brush = marker.IsWithinTarget
                ? AccentBrush ?? Brushes.Cyan
                : QuestBrush ?? WarningBrush ?? Brushes.Gold;
            context.DrawEllipse(null, new Pen(brush, 2), location, radius, radius);
        }
    }

    private void DrawConflictZonePoint(
        DrawingContext context,
        HumanSiteProjectedPoint point,
        Point center,
        HumanSiteMapPoint commander,
        double scale
    )
    {
        Point location = Transform(point.Offset, center, commander, scale);
        if (string.Equals(point.Name, "P", StringComparison.OrdinalIgnoreCase))
        {
            IBrush power = PowerPostBrush ?? WarningBrush ?? Brushes.Gold;
            var pen = new Pen(power, 1.5);
            context.DrawEllipse(null, pen, location, 6, 6);
            context.DrawLine(pen, new Point(location.X + 2, location.Y - 5), new Point(location.X - 2, location.Y));
            context.DrawLine(pen, new Point(location.X - 2, location.Y), new Point(location.X + 2, location.Y));
            context.DrawLine(pen, new Point(location.X + 2, location.Y), new Point(location.X - 2, location.Y + 5));
            return;
        }

        double dx = point.Offset.X - commander.X;
        double dy = point.Offset.Y - commander.Y;
        bool isLocal = Math.Sqrt(dx * dx + dy * dy) < 5;
        IBrush brush = isLocal
            ? LocalCheckpointBrush ?? SuccessBrush ?? Brushes.LimeGreen
            : CheckpointBrush ?? WarningBrush ?? Brushes.Gold;
        context.DrawEllipse(null, new Pen(brush, 1.5), location, 6, 6);
        if (!string.IsNullOrWhiteSpace(point.Name))
        {
            DrawLabel(context, point.Name, new Point(location.X, location.Y - 13), brush, 9);
        }
    }

    private void DrawCommander(DrawingContext context, Point center)
    {
        IBrush brush = SuccessBrush ?? Brushes.LimeGreen;
        context.DrawEllipse(MapBackground, new Pen(brush, 2), center, 6, 6);
        context.DrawLine(new Pen(brush, 2), center, new Point(center.X, center.Y - 14));
    }

    private void DrawOriginWarning(
        DrawingContext context,
        Rect bounds,
        Point center,
        HumanSiteMapPoint commander,
        double scale
    )
    {
        Point origin = Transform(default, center, commander, scale);
        double dx = origin.X - center.X;
        double dy = origin.Y - center.Y;
        double distance = Math.Sqrt((dx * dx) + (dy * dy));
        if (distance <= 0)
        {
            return;
        }

        double length = Math.Min(90, Math.Min(bounds.Width, bounds.Height) / 4);
        double x = dx / distance;
        double y = dy / distance;
        var end = new Point(center.X + (x * length), center.Y + (y * length));
        IBrush brush = DangerBrush ?? Brushes.OrangeRed;
        var pen = new Pen(brush, 3);
        context.DrawLine(pen, center, end);
        context.DrawLine(pen, end, new Point(end.X - (x * 14) - (y * 8), end.Y - (y * 14) + (x * 8)));
        context.DrawLine(pen, end, new Point(end.X - (x * 14) + (y * 8), end.Y - (y * 14) - (x * 8)));
    }

    private Point Transform(HumanSiteMapPoint point, Point center, HumanSiteMapPoint commander, double scale)
    {
        return TransformMapPoint(point, commander, CommanderHeading, center, scale);
    }

    private IBrush GetBuildingBrush(string name)
    {
        if (
            name.StartsWith("HAB", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("LAB", StringComparison.OrdinalIgnoreCase)
        )
        {
            return SuccessBrush ?? Brushes.SeaGreen;
        }

        if (name.StartsWith("CMD", StringComparison.OrdinalIgnoreCase))
        {
            return DangerBrush ?? Brushes.Firebrick;
        }

        if (name.StartsWith("POW", StringComparison.OrdinalIgnoreCase))
        {
            return WarningBrush ?? Brushes.Goldenrod;
        }

        if (name.StartsWith("EXT", StringComparison.OrdinalIgnoreCase))
        {
            return AccentBrush ?? Brushes.SteelBlue;
        }

        return SurfaceBrush ?? MutedBrush ?? Brushes.SaddleBrown;
    }

    private IBrush GetSecurityBrush(int securityLevel)
    {
        return securityLevel switch
        {
            <= 0 => SuccessBrush ?? Brushes.LimeGreen,
            1 => AccentBrush ?? Brushes.Cyan,
            2 => WarningBrush ?? Brushes.Gold,
            _ => DangerBrush ?? Brushes.OrangeRed,
        };
    }

    private void DrawFloorChevron(DrawingContext context, Point location, int floor)
    {
        if (floor < 2)
        {
            return;
        }

        IBrush brush = TextBrush ?? Brushes.White;
        DrawLabel(context, floor >= 3 ? "⌃⌃" : "⌃", new Point(location.X, location.Y + 8), brush, 7);
    }

    private static HumanSiteMapPoint OrientedPoint(HumanSiteMapPoint origin, double x, double y, double rotation)
    {
        double radians = rotation * Math.PI / 180;
        return new HumanSiteMapPoint(
            origin.X + (x * Math.Cos(radians)) + (y * Math.Sin(radians)),
            origin.Y + (y * Math.Cos(radians)) - (x * Math.Sin(radians))
        );
    }

    private static StreamGeometry CreatePolygon(Point[] points)
    {
        var geometry = new StreamGeometry();
        using StreamGeometryContext target = geometry.Open();
        target.BeginFigure(points[0], isFilled: true);
        for (int index = 1; index < points.Length; index++)
        {
            target.LineTo(points[index]);
        }

        target.EndFigure(isClosed: true);
        return geometry;
    }

    private static Point Clamp(Point point, Rect bounds)
    {
        return new Point(
            Math.Clamp(point.X, bounds.Left, bounds.Right),
            Math.Clamp(point.Y, bounds.Top, bounds.Bottom)
        );
    }

    private static void DrawLabel(DrawingContext context, string text, Point location, IBrush brush, double size)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter", FontStyle.Normal, FontWeight.SemiBold),
            size,
            brush
        );
        context.DrawText(formatted, new Point(location.X - (formatted.Width / 2), location.Y - (formatted.Height / 2)));
    }
}
