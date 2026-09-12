using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Navigation;

namespace SrvSurvey.Desktop.Controls;

public sealed class MineMapControl : Control
{
    internal const double PlanningCircleRadiusMeters = 4_500;

    public static readonly StyledProperty<MineMapSurvey?> SurveyProperty = AvaloniaProperty.Register<
        MineMapControl,
        MineMapSurvey?
    >(nameof(Survey));
    public static readonly StyledProperty<SurfaceCoordinate?> PlayerLocationProperty = AvaloniaProperty.Register<
        MineMapControl,
        SurfaceCoordinate?
    >(nameof(PlayerLocation));
    public static readonly StyledProperty<double> PlayerHeadingProperty = AvaloniaProperty.Register<
        MineMapControl,
        double
    >(nameof(PlayerHeading));
    public static readonly StyledProperty<double> ViewportZoomProperty = AvaloniaProperty.Register<
        MineMapControl,
        double
    >(nameof(ViewportZoom), 1);
    public static readonly StyledProperty<bool> AllowViewportInteractionProperty = AvaloniaProperty.Register<
        MineMapControl,
        bool
    >(nameof(AllowViewportInteraction));
    public static readonly StyledProperty<bool> ShowMarkerLabelsProperty = AvaloniaProperty.Register<
        MineMapControl,
        bool
    >(nameof(ShowMarkerLabels), true);
    public static readonly StyledProperty<IReadOnlySet<string>?> VisibleMaterialsProperty = AvaloniaProperty.Register<
        MineMapControl,
        IReadOnlySet<string>?
    >(nameof(VisibleMaterials));
    public static readonly StyledProperty<IReadOnlySet<Guid>?> VisibleMarkerIdsProperty = AvaloniaProperty.Register<
        MineMapControl,
        IReadOnlySet<Guid>?
    >(nameof(VisibleMarkerIds));
    public static readonly StyledProperty<SurfaceCoordinate?> PlanningCircleCenterProperty = AvaloniaProperty.Register<
        MineMapControl,
        SurfaceCoordinate?
    >(nameof(PlanningCircleCenter));
    public static readonly StyledProperty<IBrush?> MapBackgroundProperty = AvaloniaProperty.Register<
        MineMapControl,
        IBrush?
    >(nameof(MapBackground));
    public static readonly StyledProperty<IBrush?> GridBrushProperty = AvaloniaProperty.Register<
        MineMapControl,
        IBrush?
    >(nameof(GridBrush));
    public static readonly StyledProperty<IBrush?> AccentBrushProperty = AvaloniaProperty.Register<
        MineMapControl,
        IBrush?
    >(nameof(AccentBrush));
    public static readonly StyledProperty<IBrush?> PlayerBrushProperty = AvaloniaProperty.Register<
        MineMapControl,
        IBrush?
    >(nameof(PlayerBrush));
    public static readonly StyledProperty<IBrush?> TextBrushProperty = AvaloniaProperty.Register<
        MineMapControl,
        IBrush?
    >(nameof(TextBrush));
    public static readonly StyledProperty<IBrush?> ZoneBrushProperty = AvaloniaProperty.Register<
        MineMapControl,
        IBrush?
    >(nameof(ZoneBrush));

    private static readonly Color[] FallbackMarkerColors =
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
            ViewportZoomProperty,
            AllowViewportInteractionProperty,
            ShowMarkerLabelsProperty,
            VisibleMaterialsProperty,
            VisibleMarkerIdsProperty,
            PlanningCircleCenterProperty,
            MapBackgroundProperty,
            GridBrushProperty,
            AccentBrushProperty,
            PlayerBrushProperty,
            TextBrushProperty,
            ZoneBrushProperty
        );
        ViewportZoomProperty.Changed.AddClassHandler<MineMapControl>((control, _) => control.OnViewportZoomChanged());
    }

    private Point? dragOrigin;
    private Vector dragStartOffset;
    private Vector viewportOffset;
    private IPointer? capturedPointer;
    private Point? planningDragOrigin;
    private Vector planningDragCenterOffset;
    private bool planningCircleExistedAtPress;
    private bool planningCircleMoved;
    private IPointer? capturedPlanningPointer;

    public MineMapSurvey? Survey
    {
        get => GetValue(SurveyProperty);
        set => SetValue(SurveyProperty, value);
    }
    public SurfaceCoordinate? PlayerLocation
    {
        get => GetValue(PlayerLocationProperty);
        set => SetValue(PlayerLocationProperty, value);
    }
    public double PlayerHeading
    {
        get => GetValue(PlayerHeadingProperty);
        set => SetValue(PlayerHeadingProperty, value);
    }
    public double ViewportZoom
    {
        get => GetValue(ViewportZoomProperty);
        set => SetValue(ViewportZoomProperty, value);
    }
    public bool AllowViewportInteraction
    {
        get => GetValue(AllowViewportInteractionProperty);
        set => SetValue(AllowViewportInteractionProperty, value);
    }
    public bool ShowMarkerLabels
    {
        get => GetValue(ShowMarkerLabelsProperty);
        set => SetValue(ShowMarkerLabelsProperty, value);
    }
    public IReadOnlySet<string>? VisibleMaterials
    {
        get => GetValue(VisibleMaterialsProperty);
        set => SetValue(VisibleMaterialsProperty, value);
    }
    public IReadOnlySet<Guid>? VisibleMarkerIds
    {
        get => GetValue(VisibleMarkerIdsProperty);
        set => SetValue(VisibleMarkerIdsProperty, value);
    }
    public SurfaceCoordinate? PlanningCircleCenter
    {
        get => GetValue(PlanningCircleCenterProperty);
        set => SetValue(PlanningCircleCenterProperty, value);
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
    public IBrush? PlayerBrush
    {
        get => GetValue(PlayerBrushProperty);
        set => SetValue(PlayerBrushProperty, value);
    }
    public IBrush? TextBrush
    {
        get => GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
    }
    public IBrush? ZoneBrush
    {
        get => GetValue(ZoneBrushProperty);
        set => SetValue(ZoneBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(MapBackground ?? Brushes.Transparent, null, new Rect(Bounds.Size));
        var survey = Survey;
        var text = TextBrush ?? Brushes.White;
        if (survey is null)
        {
            DrawText(context, "No live mining map", new Point(8, Bounds.Height / 2), text, 14);
            return;
        }

        var radiusPixels = Math.Max(1, Math.Min(Bounds.Width, Bounds.Height) / 2 - 28);
        var viewportCenter = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var center = viewportCenter + viewportOffset;
        var zoom = NormalizeViewportZoom(ViewportZoom);
        int mapRadiusKilometers = GetMapRadiusKilometers(survey.LocationRadiusMeters);
        double scale = radiusPixels / (mapRadiusKilometers * 1000) * zoom;
        IBrush grid = GridBrush ?? Brushes.Gray;
        IBrush accent = AccentBrush ?? Brushes.Cyan;
        IBrush zone = ZoneBrush ?? Brushes.Gold;
        var gridPen = new Pen(grid, 1.15);

        var localBounds = new Rect(Bounds.Size);
        using (context.PushClip(localBounds))
        {
            DrawDistanceGrid(context, center, radiusPixels, zoom, mapRadiusKilometers, gridPen, text);

            double locationRadius = survey.LocationRadiusMeters * scale;
            context.DrawEllipse(null, new Pen(zone, 2), center, locationRadius, locationRadius);
            context.DrawEllipse(null, new Pen(accent, 2), center, 7, 7);
            context.DrawEllipse(accent, null, center, 2.5, 2.5);

            DrawPlanningCircle(context, survey, center, scale, accent);

            var markerScale = GetMarkerScale(zoom);
            DrawMarkers(context, survey, center, scale, markerScale, localBounds, text);
            DrawPlayer(context, survey, center, scale, markerScale, localBounds);
        }
    }

    private static void DrawDistanceGrid(
        DrawingContext context,
        Point center,
        double radiusPixels,
        double zoom,
        int mapRadiusKilometers,
        Pen gridPen,
        IBrush text
    )
    {
        foreach (DistanceRing ring in CreateDistanceRings(radiusPixels, zoom, mapRadiusKilometers))
        {
            context.DrawEllipse(null, gridPen, center, ring.RadiusPixels, ring.RadiusPixels);
            DrawText(
                context,
                ring.Kilometers.ToString("0.##", CultureInfo.InvariantCulture),
                new Point(center.X + 5, center.Y - ring.RadiusPixels + 3),
                text,
                10
            );
        }

        foreach (var heading in Enumerable.Range(0, 8).Select(index => index * 45))
        {
            var radians = heading * Math.PI / 180d;
            var edge = new Point(
                center.X + Math.Sin(radians) * radiusPixels,
                center.Y - Math.Cos(radians) * radiusPixels
            );
            context.DrawLine(gridPen, center, edge);
            var label = heading.ToString(CultureInfo.InvariantCulture) + "°";
            var horizontalOffset = heading switch
            {
                0 or 180 => 8,
                > 180 => 22,
                _ => 0,
            };
            var labelPoint = new Point(
                center.X + Math.Sin(radians) * (radiusPixels + 14) - horizontalOffset,
                center.Y - Math.Cos(radians) * (radiusPixels + 14) - 6
            );
            DrawText(context, label, labelPoint, text, 10);
        }
    }

    private void DrawMarkers(
        DrawingContext context,
        MineMapSurvey survey,
        Point center,
        double scale,
        double markerScale,
        Rect localBounds,
        IBrush text
    )
    {
        var markerLabelScale = GetMarkerLabelScale(ViewportZoom);
        foreach (var marker in survey.Markers)
        {
            if (VisibleMaterials is { } visible && !visible.Contains(marker.Material))
            {
                continue;
            }

            if (VisibleMarkerIds is { } visibleMarkerIds && !visibleMarkerIds.Contains(marker.Id))
            {
                continue;
            }

            var point = ToPoint(survey, marker.Location, center, scale);
            if (!localBounds.Inflate(12 * markerScale).Contains(point))
            {
                continue;
            }

            var brush = new SolidColorBrush(ColorFor(marker.Material));
            context.DrawEllipse(brush, new Pen(text, 0.75 * markerScale), point, 5 * markerScale, 5 * markerScale);
            if (ShowMarkerLabels)
            {
                DrawText(
                    context,
                    marker.Material,
                    new Point(point.X + 8 * markerScale, point.Y - 8 * markerScale),
                    text,
                    10 * markerLabelScale
                );
            }
        }
    }

    private void DrawPlanningCircle(
        DrawingContext context,
        MineMapSurvey survey,
        Point mapCenter,
        double scale,
        IBrush brush
    )
    {
        if (PlanningCircleCenter is not { } planningCenter)
        {
            return;
        }

        Point center = ToPoint(survey, planningCenter, mapCenter, scale);
        double radius = PlanningCircleRadiusMeters * scale;
        context.DrawEllipse(null, new Pen(brush, 2, DashStyle.Dash), center, radius, radius);
    }

    private void DrawPlayer(
        DrawingContext context,
        MineMapSurvey survey,
        Point center,
        double scale,
        double markerScale,
        Rect localBounds
    )
    {
        if (PlayerLocation is not { } player)
        {
            return;
        }

        var point = ToPoint(survey, player, center, scale);
        if (!localBounds.Inflate(12).Contains(point))
        {
            return;
        }

        DrawCommander(context, point, PlayerHeading, PlayerBrush ?? Brushes.LimeGreen, markerScale);
    }

    public static Color ColorFor(string material)
    {
        if (SurfaceMiningCommodityCatalog.TryResolve(material, out var commodity))
        {
            return Color.Parse(commodity.ColorHex);
        }

        var hash = 17;
        foreach (var character in material.ToUpperInvariant())
        {
            hash = unchecked(hash * 31 + character);
        }

        return FallbackMarkerColors[(hash & int.MaxValue) % FallbackMarkerColors.Length];
    }

    public void ResetViewport()
    {
        viewportOffset = default;
        SetCurrentValue(ViewportZoomProperty, 1);
        StopDragging(null);
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (!CanInteractWithViewport || e.Delta.Y == 0)
        {
            return;
        }

        var currentZoom = NormalizeViewportZoom(ViewportZoom);
        var nextZoom = NormalizeViewportZoom(currentZoom * (e.Delta.Y > 0 ? 1.1 : 0.9));
        var pointer = e.GetPosition(this);
        var center = new Rect(Bounds.Size).Center;
        var ratio = nextZoom / currentZoom;
        var relative = pointer - center - viewportOffset;
        viewportOffset = new Vector(
            pointer.X - center.X - relative.X * ratio,
            pointer.Y - center.Y - relative.Y * ratio
        );
        viewportOffset = ClampViewportOffset(viewportOffset, Bounds.Size, nextZoom);
        SetCurrentValue(ViewportZoomProperty, nextZoom);
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        PointerPoint point = e.GetCurrentPoint(this);
        if (CanInteractWithViewport && point.Properties.IsRightButtonPressed)
        {
            BeginPlanningCircleInteraction(e);
            return;
        }

        if (
            !CanInteractWithViewport
            || NormalizeViewportZoom(ViewportZoom) <= 1
            || !point.Properties.IsLeftButtonPressed
        )
        {
            return;
        }

        dragOrigin = e.GetPosition(this);
        dragStartOffset = viewportOffset;
        capturedPointer = e.Pointer;
        e.Pointer.Capture(this);
        Cursor = new Cursor(StandardCursorType.SizeAll);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (planningDragOrigin is { } planningOrigin)
        {
            Point point = e.GetPosition(this);
            Point delta = point - planningOrigin;
            if (!planningCircleMoved && delta.X * delta.X + delta.Y * delta.Y < 9)
            {
                return;
            }

            planningCircleMoved = true;
            SetPlanningCircleFromPoint(point + planningDragCenterOffset);
            e.Handled = true;
            return;
        }

        if (dragOrigin is not { } origin)
        {
            return;
        }

        viewportOffset = ClampViewportOffset(
            dragStartOffset + (e.GetPosition(this) - origin),
            Bounds.Size,
            ViewportZoom
        );
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (capturedPlanningPointer is { } planningPointer && ReferenceEquals(e.Pointer, planningPointer))
        {
            if (planningCircleExistedAtPress && !planningCircleMoved)
            {
                SetCurrentValue(PlanningCircleCenterProperty, null);
            }

            StopPlanningCircleInteraction(e.Pointer);
            e.Handled = true;
            return;
        }

        StopDragging(e.Pointer);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        StopPlanningCircleInteraction(null);
        StopDragging(null);
    }

    internal static double NormalizeViewportZoom(double zoom) => double.IsFinite(zoom) ? Math.Clamp(zoom, 1, 15) : 1;

    internal static int GetMapRadiusKilometers(double locationRadiusMeters) =>
        Math.Max(4, (int)Math.Ceiling(locationRadiusMeters / 1000));

    internal static IReadOnlyList<DistanceRing> CreateDistanceRings(
        double radiusPixels,
        double zoom,
        int mapRadiusKilometers
    ) =>
        Enumerable
            .Range(1, Math.Max(1, mapRadiusKilometers))
            .Select(kilometers => new DistanceRing(
                kilometers,
                kilometers * radiusPixels / Math.Max(1, mapRadiusKilometers) * NormalizeViewportZoom(zoom)
            ))
            .ToArray();

    internal static double GetMarkerScale(double zoom) => Math.Clamp(Math.Sqrt(NormalizeViewportZoom(zoom)), 1, 3);

    internal static double GetMarkerLabelScale(double zoom) =>
        Math.Clamp(Math.Pow(NormalizeViewportZoom(zoom), 0.25), 1, 1.8);

    internal static Vector ClampViewportOffset(Vector requested, Size viewportSize, double zoom)
    {
        var normalizedZoom = NormalizeViewportZoom(zoom);
        if (normalizedZoom <= 1 || viewportSize.Width <= 0 || viewportSize.Height <= 0)
        {
            return default;
        }

        return new Vector(
            Math.Clamp(
                requested.X,
                -viewportSize.Width * (normalizedZoom - 1) / 2,
                viewportSize.Width * (normalizedZoom - 1) / 2
            ),
            Math.Clamp(
                requested.Y,
                -viewportSize.Height * (normalizedZoom - 1) / 2,
                viewportSize.Height * (normalizedZoom - 1) / 2
            )
        );
    }

    private bool CanInteractWithViewport => AllowViewportInteraction && Survey is not null;

    private void OnViewportZoomChanged()
    {
        viewportOffset = ClampViewportOffset(viewportOffset, Bounds.Size, ViewportZoom);
        if (NormalizeViewportZoom(ViewportZoom) <= 1)
        {
            StopDragging(null);
        }

        InvalidateVisual();
    }

    private void StopDragging(IPointer? pointer)
    {
        if (dragOrigin is null)
        {
            return;
        }

        dragOrigin = null;
        if (capturedPointer is { } captured && (pointer is null || ReferenceEquals(pointer, captured)))
        {
            captured.Capture(null);
        }

        capturedPointer = null;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    private void BeginPlanningCircleInteraction(PointerPressedEventArgs e)
    {
        if (Survey is not { } survey)
        {
            return;
        }

        StopDragging(null);
        Point pointerPosition = e.GetPosition(this);
        planningDragOrigin = pointerPosition;
        planningCircleExistedAtPress = PlanningCircleCenter is not null;
        planningCircleMoved = false;
        planningDragCenterOffset = PlanningCircleCenter is { } planningCenter
            ? ToPoint(survey, planningCenter, GetViewportCenter(), GetMapScale(survey)) - pointerPosition
            : default;
        if (!planningCircleExistedAtPress)
        {
            SetPlanningCircleFromPoint(pointerPosition);
        }

        capturedPlanningPointer = e.Pointer;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void SetPlanningCircleFromPoint(Point point)
    {
        if (Survey is not { } survey)
        {
            return;
        }

        SetCurrentValue(
            PlanningCircleCenterProperty,
            ToSurfaceCoordinate(survey, point, GetViewportCenter(), GetMapScale(survey))
        );
        InvalidateVisual();
    }

    private void StopPlanningCircleInteraction(IPointer? pointer)
    {
        planningDragOrigin = null;
        planningDragCenterOffset = default;
        planningCircleExistedAtPress = false;
        planningCircleMoved = false;
        if (capturedPlanningPointer is { } captured && (pointer is null || ReferenceEquals(pointer, captured)))
        {
            captured.Capture(null);
        }

        capturedPlanningPointer = null;
    }

    private Point GetViewportCenter() => new Rect(Bounds.Size).Center + viewportOffset;

    private double GetMapScale(MineMapSurvey survey)
    {
        double radiusPixels = Math.Max(1, Math.Min(Bounds.Width, Bounds.Height) / 2 - 28);
        return radiusPixels
            / (GetMapRadiusKilometers(survey.LocationRadiusMeters) * 1000)
            * NormalizeViewportZoom(ViewportZoom);
    }

    private static Point ToPoint(MineMapSurvey survey, SurfaceCoordinate location, Point center, double scale)
    {
        var distance = SurfaceNavigation.GetDistance(survey.Center, location, survey.PlanetRadiusMeters);
        var bearing = SurfaceNavigation.GetBearing(survey.Center, location) * Math.PI / 180d;
        return new Point(
            center.X + Math.Sin(bearing) * distance * scale,
            center.Y - Math.Cos(bearing) * distance * scale
        );
    }

    internal static SurfaceCoordinate ToSurfaceCoordinate(MineMapSurvey survey, Point point, Point center, double scale)
    {
        Point offset = point - center;
        double distance = Math.Sqrt(offset.X * offset.X + offset.Y * offset.Y) / scale;
        double bearing = SurfaceNavigation.NormalizeDegrees(Math.Atan2(offset.X, -offset.Y) * 180 / Math.PI);
        return MineMapService.GetDestination(survey.Center, bearing, distance, survey.PlanetRadiusMeters);
    }

    private void DrawCommander(DrawingContext context, Point position, double heading, IBrush brush, double markerScale)
    {
        var radius = 6 * markerScale;
        var pen = new Pen(brush, 2 * markerScale);
        context.DrawEllipse(MapBackground ?? Brushes.Transparent, pen, position, radius, radius);
        context.DrawLine(pen, position, GetCommanderHeadingEnd(position, radius, heading));
    }

    internal static Point GetCommanderHeadingEnd(Point location, double radius, double heading = 0) =>
        GuardianSiteMapControl.GetCommanderHeadingEnd(location, radius, heading);

    internal readonly record struct DistanceRing(double Kilometers, double RadiusPixels);

    private static void DrawText(DrawingContext context, string value, Point origin, IBrush brush, double size)
    {
        var formatted = new FormattedText(
            value,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Century Gothic, Segoe UI, sans-serif"),
            size,
            brush
        );
        context.DrawText(formatted, origin);
    }
}
