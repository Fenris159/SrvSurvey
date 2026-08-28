using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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
    public static readonly StyledProperty<GuardianSiteProximitySnapshot?>
        CommanderMapPositionProperty = AvaloniaProperty.Register<
            GuardianSiteMapControl,
            GuardianSiteProximitySnapshot?>(nameof(CommanderMapPosition));
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
    public static readonly StyledProperty<string?> HighlightedPointNameProperty =
        AvaloniaProperty.Register<GuardianSiteMapControl, string?>(
            nameof(HighlightedPointName));
    public static readonly StyledProperty<string?> SelectedPointNameProperty =
        AvaloniaProperty.Register<GuardianSiteMapControl, string?>(
            nameof(SelectedPointName));
    public static readonly DirectProperty<GuardianSiteMapControl, string?>
        HoveredPointNameProperty = AvaloniaProperty.RegisterDirect<
            GuardianSiteMapControl,
            string?>(
                nameof(HoveredPointName),
                control => control.HoveredPointName);
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
    public static readonly StyledProperty<bool> IsLegendOnlyProperty =
        AvaloniaProperty.Register<GuardianSiteMapControl, bool>(
            nameof(IsLegendOnly));
    public static readonly StyledProperty<bool> AllowViewportInteractionProperty =
        AvaloniaProperty.Register<GuardianSiteMapControl, bool>(
            nameof(AllowViewportInteraction));
    public static readonly StyledProperty<double> ViewportZoomProperty =
        AvaloniaProperty.Register<GuardianSiteMapControl, double>(
            nameof(ViewportZoom),
            1);

    internal const double MinimumViewportZoom = 1;
    internal const double MaximumViewportZoom = 15;

    private IPointer? capturedPointer;
    private Point? dragOrigin;
    private Vector dragStartOffset;
    private Vector viewportOffset;
    private string? hoveredPointName;

    static GuardianSiteMapControl()
    {
        AffectsRender<GuardianSiteMapControl>(
            ProjectionProperty,
            ProximityProperty,
            CommanderMapPositionProperty,
            MapScaleProperty,
            CommanderHeadingProperty,
            TargetPointNameProperty,
            HighlightedPointNameProperty,
            SelectedPointNameProperty,
            HoveredPointNameProperty,
            MapBackgroundProperty,
            GridBrushProperty,
            AccentBrushProperty,
            MutedBrushProperty,
            PresentBrushProperty,
            AbsentBrushProperty,
            EmptyBrushProperty,
            ShowLegendProperty,
            IsLegendOnlyProperty,
            ViewportZoomProperty);
        ViewportZoomProperty.Changed.AddClassHandler<GuardianSiteMapControl>(
            (control, _) => control.OnViewportZoomChanged());
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

    public GuardianSiteProximitySnapshot? CommanderMapPosition
    {
        get => GetValue(CommanderMapPositionProperty);
        set => SetValue(CommanderMapPositionProperty, value);
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

    public string? HighlightedPointName
    {
        get => GetValue(HighlightedPointNameProperty);
        set => SetValue(HighlightedPointNameProperty, value);
    }

    public string? SelectedPointName
    {
        get => GetValue(SelectedPointNameProperty);
        set => SetValue(SelectedPointNameProperty, value);
    }

    public string? HoveredPointName
    {
        get => hoveredPointName;
        private set => SetAndRaise(
            HoveredPointNameProperty,
            ref hoveredPointName,
            value);
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

    public bool IsLegendOnly
    {
        get => GetValue(IsLegendOnlyProperty);
        set => SetValue(IsLegendOnlyProperty, value);
    }

    public bool AllowViewportInteraction
    {
        get => GetValue(AllowViewportInteractionProperty);
        set => SetValue(AllowViewportInteractionProperty, value);
    }

    public double ViewportZoom
    {
        get => GetValue(ViewportZoomProperty);
        set => SetValue(ViewportZoomProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = new Rect(Bounds.Size);
        if (!IsLegendOnly)
        {
            context.DrawRectangle(
                MapBackground ?? Brushes.Transparent,
                null,
                bounds);
        }

        if (Projection is not { } projection
            || bounds.Width <= 0
            || bounds.Height <= 0)
        {
            return;
        }

        if (IsLegendOnly)
        {
            DrawLegendRows(
                context,
                projection,
                left: 2,
                top: 4,
                rowHeight: 26,
                availableWidth: Math.Max(1, bounds.Width - 4),
                fontSize: 13);
            return;
        }

        var (viewportCenter, scale, mapImage) = CalculateViewport(
            bounds,
            projection);
        if (mapImage is not null)
        {
            DrawMapImage(
                context,
                projection,
                mapImage,
                viewportCenter,
                scale);
        }

        var mapOrigin = TransformMapPoint(
            0,
            0,
            Proximity,
            CommanderHeading,
            viewportCenter,
            scale);
        var gridExtent = Math.Max(bounds.Width, bounds.Height) / scale * 2;
        DrawReferenceGrid(
            context,
            projection,
            mapImage,
            viewportCenter,
            mapOrigin,
            gridExtent,
            scale);

        DrawHeadingLines(
            context,
            projection,
            mapOrigin,
            gridExtent * scale,
            CommanderHeading,
            scale);
        if (Proximity is null && CommanderMapPosition is { } commander)
        {
            DrawCommander(
                context,
                TransformMapPoint(
                    commander.MapX,
                    commander.MapY,
                    proximity: null,
                    commanderHeading: 0,
                    viewportCenter,
                    scale),
                projection.IsRuins,
                scale);
        }

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
                Math.Max(bounds.Width, bounds.Height),
                scale);
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
                    scale),
                scale);
        }

        if (Proximity is not null)
        {
            DrawCommander(
                context,
                viewportCenter,
                projection.IsRuins,
                scale);
        }

        if (ShowLegend)
        {
            DrawLegend(context, projection);
        }

        if (mapImage is null)
        {
            DrawMissingMapNotice(context, bounds, projection.SiteType);
        }
    }

    private void DrawReferenceGrid(
        DrawingContext context,
        GuardianSiteMapProjection projection,
        IImage? mapImage,
        Point viewportCenter,
        Point mapOrigin,
        double gridExtent,
        double scale)
    {
        if (mapImage is not null)
        {
            return;
        }

        var grid = GridBrush ?? Brushes.Gray;
        var accent = AccentBrush ?? Brushes.Cyan;
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
    }

    public static double CalculateFittedScale(
        Rect bounds,
        GuardianSiteMapProjection projection,
        IImage? mapImage)
    {
        ArgumentNullException.ThrowIfNull(projection);
        var horizontalRoom = Math.Max(1, bounds.Width / 2 - 30);
        var verticalRoom = Math.Max(1, bounds.Height / 2 - 30);
        var maximumX = projection.MaximumDistance;
        var maximumY = projection.MaximumDistance;
        if (mapImage is not null
            && double.IsFinite(projection.ImageScaleFactor)
            && projection.ImageScaleFactor > 0)
        {
            var imageLeft = -projection.ImageOffset.X
                * projection.ImageScaleFactor;
            var imageTop = -projection.ImageOffset.Y
                * projection.ImageScaleFactor;
            var imageRight = imageLeft
                + mapImage.Size.Width * projection.ImageScaleFactor;
            var imageBottom = imageTop
                + mapImage.Size.Height * projection.ImageScaleFactor;
            maximumX = Math.Max(
                maximumX,
                Math.Max(Math.Abs(imageLeft), Math.Abs(imageRight)));
            maximumY = Math.Max(
                maximumY,
                Math.Max(Math.Abs(imageTop), Math.Abs(imageBottom)));
        }

        return Math.Min(
            horizontalRoom / Math.Max(1, maximumX),
            verticalRoom / Math.Max(1, maximumY));
    }

    public static Matrix CreateMapTransform(
        GuardianSiteProximitySnapshot? proximity,
        double commanderHeading,
        Point viewportCenter,
        double scale)
    {
        if (!double.IsFinite(scale) || scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scale));
        }

        var heading = double.IsFinite(commanderHeading)
            ? commanderHeading
            : 0;
        var radians = heading * Math.PI / 180;
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        var mapX = proximity?.MapX ?? 0;
        var mapY = proximity?.MapY ?? 0;
        var scaleX = scale * cosine;
        var skewY = -scale * sine;
        var skewX = scale * sine;
        var scaleY = scale * cosine;
        return new Matrix(
            scaleX,
            skewY,
            skewX,
            scaleY,
            viewportCenter.X - (mapX * scaleX) - (mapY * skewX),
            viewportCenter.Y - (mapX * skewY) - (mapY * scaleY));
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

    internal static double NormalizeViewportZoom(double zoom)
    {
        return double.IsFinite(zoom)
            ? Math.Clamp(zoom, MinimumViewportZoom, MaximumViewportZoom)
            : MinimumViewportZoom;
    }

    internal static Vector ClampViewportOffset(
        Vector requested,
        Size viewportSize,
        double zoom)
    {
        var normalizedZoom = NormalizeViewportZoom(zoom);
        if (normalizedZoom <= MinimumViewportZoom
            || viewportSize.Width <= 0
            || viewportSize.Height <= 0)
        {
            return default;
        }

        var maximumX = viewportSize.Width * (normalizedZoom - 1) / 2;
        var maximumY = viewportSize.Height * (normalizedZoom - 1) / 2;
        return new Vector(
            Math.Clamp(requested.X, -maximumX, maximumX),
            Math.Clamp(requested.Y, -maximumY, maximumY));
    }

    public void ResetViewport()
    {
        viewportOffset = default;
        SetCurrentValue(ViewportZoomProperty, MinimumViewportZoom);
        StopDragging(null);
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(
        PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (!CanInteractWithViewport || e.Delta.Y == 0)
        {
            return;
        }

        var currentZoom = NormalizeViewportZoom(ViewportZoom);
        var nextZoom = NormalizeViewportZoom(
            currentZoom * (e.Delta.Y > 0 ? 1.1 : 0.9));
        var pointer = e.GetPosition(this);
        var center = new Rect(Bounds.Size).Center;
        var ratio = nextZoom / currentZoom;
        var relative = pointer - center - viewportOffset;
        viewportOffset = new Vector(
            pointer.X - center.X - relative.X * ratio,
            pointer.Y - center.Y - relative.Y * ratio);
        viewportOffset = ClampViewportOffset(
            viewportOffset,
            Bounds.Size,
            nextZoom);
        SetCurrentValue(ViewportZoomProperty, nextZoom);
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!CanInteractWithViewport
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var pointerPosition = e.GetPosition(this);
        if (HitTestPoint(pointerPosition) is { } point)
        {
            SetCurrentValue(SelectedPointNameProperty, point.Name);
            e.Handled = true;
            return;
        }

        SetCurrentValue(SelectedPointNameProperty, null);
        if (NormalizeViewportZoom(ViewportZoom) <= MinimumViewportZoom)
        {
            e.Handled = true;
            return;
        }

        dragOrigin = pointerPosition;
        dragStartOffset = viewportOffset;
        capturedPointer = e.Pointer;
        e.Pointer.Capture(this);
        Cursor = new Cursor(StandardCursorType.SizeAll);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (dragOrigin is not { } origin)
        {
            UpdateHoveredPoint(e.GetPosition(this));
            return;
        }

        viewportOffset = ClampViewportOffset(
            dragStartOffset + (e.GetPosition(this) - origin),
            Bounds.Size,
            ViewportZoom);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        HoveredPointName = null;
    }

    protected override void OnPointerReleased(
        PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        StopDragging(e.Pointer);
    }

    protected override void OnPointerCaptureLost(
        PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        StopDragging(null);
    }

    private bool CanInteractWithViewport => AllowViewportInteraction
        && !IsLegendOnly
        && Projection is not null;

    private void OnViewportZoomChanged()
    {
        viewportOffset = ClampViewportOffset(
            viewportOffset,
            Bounds.Size,
            ViewportZoom);
        if (NormalizeViewportZoom(ViewportZoom) <= MinimumViewportZoom)
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
        var pointerToRelease = pointer ?? capturedPointer;
        capturedPointer = null;
        pointerToRelease?.Capture(null);
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    private void UpdateHoveredPoint(Point pointerPosition)
    {
        HoveredPointName = HitTestPoint(pointerPosition)?.Name;
    }

    private GuardianProjectedPoint? HitTestPoint(Point pointerPosition)
    {
        if (Projection is not { } projection
            || Bounds.Width <= 0
            || Bounds.Height <= 0)
        {
            return null;
        }

        var bounds = new Rect(Bounds.Size);
        var (viewportCenter, scale, _) = CalculateViewport(bounds, projection);
        return projection.Points
            .Select(point => new
            {
                Point = point,
                Screen = TransformMapPoint(
                    point.X,
                    point.Y,
                    Proximity,
                    CommanderHeading,
                    viewportCenter,
                    scale),
            })
            .Select(candidate => new
            {
                candidate.Point,
                Distance = Math.Sqrt(
                    Math.Pow(candidate.Screen.X - pointerPosition.X, 2)
                    + Math.Pow(candidate.Screen.Y - pointerPosition.Y, 2)),
            })
            .Where(candidate => candidate.Distance <= GetHitRadius(
                candidate.Point,
                projection,
                scale))
            .OrderBy(candidate => candidate.Distance)
            .Select(candidate => candidate.Point)
            .FirstOrDefault();
    }

    private (Point Center, double Scale, IImage? MapImage) CalculateViewport(
        Rect bounds,
        GuardianSiteMapProjection projection)
    {
        var viewportZoom = NormalizeViewportZoom(ViewportZoom);
        viewportOffset = ClampViewportOffset(
            viewportOffset,
            bounds.Size,
            viewportZoom);
        var mapImage = GuardianMapImageCatalog.Find(projection);
        var fittedScale = CalculateFittedScale(bounds, projection, mapImage);
        var baseScale = double.IsFinite(MapScale) && MapScale > 0
            ? Math.Clamp(MapScale, 0.1, 20)
            : fittedScale;
        return (
            bounds.Center + viewportOffset,
            baseScale * viewportZoom,
            mapImage);
    }

    private static double GetHitRadius(
        GuardianProjectedPoint point,
        GuardianSiteMapProjection projection,
        double markerScale)
    {
        var (_, ringRadius) = GetSurveyMarkerRadii(
            point.Type,
            projection.IsRuins);
        return Math.Max(12, (ringRadius + 4) * markerScale);
    }

    private void DrawCommander(
        DrawingContext context,
        Point location,
        bool isRuins,
        double markerScale)
    {
        var brush = PresentBrush ?? Brushes.LimeGreen;
        var radius = (isRuins ? 10 : 4) * markerScale;
        var pen = new Pen(brush, (isRuins ? 4 : 2) * markerScale);
        context.DrawEllipse(MapBackground, pen, location, radius, radius);
        context.DrawLine(
            pen,
            location,
            GetCommanderHeadingEnd(location, radius));
    }

    internal static Point GetCommanderHeadingEnd(Point location, double radius)
    {
        return location - new Vector(0, radius * 2);
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
        DrawLegendRows(
            context,
            projection,
            16,
            38,
            rowHeight,
            width - 8,
            fontSize: 10);
    }

    private void DrawLegendRows(
        DrawingContext context,
        GuardianSiteMapProjection projection,
        double left,
        double top,
        double rowHeight,
        double availableWidth,
        double fontSize)
    {
        var symbolScale = IsLegendOnly ? 1.25 : 1;
        var entries = CreateLegendEntries(projection);
        var useTwoColumns = IsLegendOnly && availableWidth >= 220;
        var compactEntries = useTwoColumns
            ? entries.Where(entry => !IsFullWidthLegendEntry(entry)).ToArray()
            : entries.ToArray();
        var fullWidthEntries = useTwoColumns
            ? entries.Where(IsFullWidthLegendEntry).ToArray()
            : [];
        var columnCount = useTwoColumns ? 2 : 1;
        const double columnGap = 8;
        var columnWidth = (availableWidth
            - columnGap * (columnCount - 1)) / columnCount;
        for (var index = 0; index < compactEntries.Length; index++)
        {
            var entry = compactEntries[index];
            var row = index / columnCount;
            var column = index % columnCount;
            var entryLeft = left + column * (columnWidth + columnGap);
            var center = new Point(
                entryLeft + 16,
                top + (rowHeight / 2) + row * rowHeight);
            DrawLegendSymbol(context, center, entry, symbolScale);
            var text = CreateLegendText(
                entry.Label,
                FontWeight.Normal,
                fontSize);
            text.MaxTextWidth = Math.Max(1, columnWidth - 40);
            text.MaxTextHeight = rowHeight;
            context.DrawText(
                text,
                new Point(entryLeft + 36, center.Y - text.Height / 2));
        }

        var fullWidthTop = top
            + Math.Ceiling(compactEntries.Length / (double)columnCount)
            * rowHeight;
        var entryTop = fullWidthTop;
        for (var index = 0; index < fullWidthEntries.Length; index++)
        {
            var entry = fullWidthEntries[index];
            var text = CreateLegendText(
                entry.Label,
                FontWeight.Normal,
                fontSize);
            text.MaxTextWidth = Math.Max(1, availableWidth - 40);
            var entryHeight = Math.Max(rowHeight, text.Height + 2);
            var center = new Point(
                left + 16,
                entryTop + entryHeight / 2);
            DrawLegendSymbol(context, center, entry, symbolScale);
            context.DrawText(
                text,
                new Point(left + 36, center.Y - text.Height / 2));
            entryTop += entryHeight;
        }
    }

    private static bool IsFullWidthLegendEntry(GuardianMapLegendEntry entry)
    {
        return entry.IsActiveObelisk
            || entry.Type is GuardianPoiType.Pylon
                or GuardianPoiType.Component
            || entry.Kind != GuardianMapLegendKind.Point;
    }

    private void DrawMapImage(
        DrawingContext context,
        GuardianSiteMapProjection projection,
        IImage mapImage,
        Point viewportCenter,
        double scale)
    {
        if (!double.IsFinite(projection.ImageScaleFactor)
            || projection.ImageScaleFactor <= 0)
        {
            return;
        }

        var destination = new Rect(
            -projection.ImageOffset.X * projection.ImageScaleFactor,
            -projection.ImageOffset.Y * projection.ImageScaleFactor,
            mapImage.Size.Width * projection.ImageScaleFactor,
            mapImage.Size.Height * projection.ImageScaleFactor);
        using (context.PushTransform(CreateMapTransform(
            Proximity,
            CommanderHeading,
            viewportCenter,
            scale)))
        {
            context.DrawImage(
                mapImage,
                new Rect(mapImage.Size),
                destination);
        }
    }

    private void DrawMissingMapNotice(
        DrawingContext context,
        Rect bounds,
        string siteType)
    {
        var message = LocalizationCatalog.Translate(
            $"Map artwork is not available for {siteType}.");
        var text = CreateLegendText(message, FontWeight.SemiBold);
        var padding = 8d;
        var surface = new Rect(
            Math.Max(8, bounds.Center.X - (text.Width / 2) - padding),
            Math.Max(8, bounds.Bottom - text.Height - (padding * 3)),
            Math.Min(bounds.Width - 16, text.Width + (padding * 2)),
            text.Height + (padding * 2));
        context.DrawRectangle(
            MapBackground ?? Brushes.Black,
            new Pen(AbsentBrush ?? Brushes.Red, 1),
            surface,
            4,
            4);
        context.DrawText(
            text,
            new Point(surface.X + padding, surface.Y + padding));
    }

    private void DrawLegendSymbol(
        DrawingContext context,
        Point center,
        GuardianMapLegendEntry entry,
        double symbolScale = 1)
    {
        var accent = AccentBrush ?? Brushes.Cyan;
        if (entry.Kind == GuardianMapLegendKind.SiteHeading)
        {
            context.DrawLine(
                new Pen(accent, 2 * symbolScale),
                new Point(
                    center.X - 6 * symbolScale,
                    center.Y + 5 * symbolScale),
                new Point(
                    center.X + 5 * symbolScale,
                    center.Y - 6 * symbolScale));
            return;
        }

        if (entry.Kind == GuardianMapLegendKind.TowerHeading)
        {
            context.DrawLine(
                new Pen(
                    EmptyBrush ?? Brushes.Goldenrod,
                    2 * symbolScale),
                new Point(
                    center.X - 6 * symbolScale,
                    center.Y + 5 * symbolScale),
                new Point(
                    center.X + 5 * symbolScale,
                    center.Y - 6 * symbolScale));
            return;
        }

        if (entry.Kind == GuardianMapLegendKind.SurveyNeeded)
        {
            GuardianSurveyMarkerDrawing.Draw(
                context,
                center,
                haloRadius: 8 * symbolScale,
                ringRadius: 7 * symbolScale,
                dotRadius: 0.6 * symbolScale);
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
                entry.IsActiveObelisk,
                entry.IsScannedObelisk,
                string.Empty,
                [],
                IsRamTahNeededObelisk: entry.IsRamTahNeededObelisk),
            center,
            new GuardianSiteMapProjection(
                "Alpha",
                [],
                [],
                1,
                IsRuins: true,
                SiteHeading: 0,
                RelicTowerHeading: 45),
            headingLength: 0,
            markerScale: (entry.IsActiveObelisk ? 0.55 : 1) * symbolScale);
    }

    private FormattedText CreateLegendText(
        string text,
        FontWeight weight,
        double fontSize = 10)
    {
        return new FormattedText(
            LocalizationCatalog.Translate(text),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(
                "Century Gothic, Segoe UI, sans-serif",
                FontStyle.Normal,
                weight),
            fontSize,
            MutedBrush ?? Brushes.Wheat);
    }

    private static List<GuardianMapLegendEntry> CreateLegendEntries(
        GuardianSiteMapProjection projection)
    {
        var entries = new List<GuardianMapLegendEntry>
        {
            new("Relic Tower", GuardianPoiType.Relic),
            new("Orb", GuardianPoiType.Orb),
            new("Casket", GuardianPoiType.Casket),
            new("Tablet", GuardianPoiType.Tablet),
            new("Totem", GuardianPoiType.Totem),
            new("Urn", GuardianPoiType.Urn),
            new("Empty puddle", GuardianPoiType.EmptyPuddle, GuardianPoiStatus.Empty),
            new("Obelisk", GuardianPoiType.Obelisk),
            new(
                "Active obelisk · unscanned",
                GuardianPoiType.Obelisk,
                IsActiveObelisk: true),
            new(
                "Active obelisk · scanned",
                GuardianPoiType.Obelisk,
                IsActiveObelisk: true,
                IsScannedObelisk: true),
            new(
                "Active obelisk · Ram Tah needed",
                GuardianPoiType.Obelisk,
                IsActiveObelisk: true,
                IsRamTahNeededObelisk: true),
        };
        if (!projection.IsRuins)
        {
            entries.Add(new GuardianMapLegendEntry(
                "Energy pylon",
                GuardianPoiType.Pylon));
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
        double headingLength,
        double markerScale = 1)
    {
        var style = GuardianLegacyMapDrawing.GetPointStyle(
            point.Type,
            point.Status,
            point.IsActiveObelisk);
        var pen = CreatePen(style, markerScale);
        var fill = style.HasFill ? new SolidColorBrush(style.Fill) : null;
        var rotation = GuardianLegacyMapDrawing.GetGlyphRotation(
            point,
            projection,
            CommanderHeading);
        DrawSurveyMarkerIfNeeded(context, point, location, projection, markerScale);
        DrawRelicHeadingIfNeeded(
            context,
            point,
            location,
            headingLength,
            rotation,
            markerScale);
        DrawTargetOrNearestHighlight(context, point, location, markerScale);
        DrawPointerSelectionHighlight(context, point, location, markerScale);
        if (point.Type == GuardianPoiType.Obelisk && point.IsActiveObelisk)
        {
            DrawActiveObeliskEffect(
                context,
                point,
                location,
                rotation,
                markerScale);
        }

        DrawPointGlyph(new PointGlyphDraw
        {
            Context = context,
            Point = point,
            Location = location,
            Projection = projection,
            Pen = pen,
            Fill = fill,
            Rotation = rotation,
            MarkerScale = markerScale,
        });
    }

    private static void DrawSurveyMarkerIfNeeded(
        DrawingContext context,
        GuardianProjectedPoint point,
        Point location,
        GuardianSiteMapProjection projection,
        double markerScale)
    {
        if (!RequiresSurveyMarker(point))
        {
            return;
        }

        var (haloRadius, ringRadius) = GetSurveyMarkerRadii(
            point.Type,
            projection.IsRuins);
        GuardianSurveyMarkerDrawing.Draw(
            context,
            location,
            haloRadius * markerScale,
            ringRadius * markerScale,
            dotRadius: 0.75 * markerScale);
    }

    private static void DrawRelicHeadingIfNeeded(
        DrawingContext context,
        GuardianProjectedPoint point,
        Point location,
        double headingLength,
        double rotation,
        double markerScale)
    {
        if (point.Type != GuardianPoiType.Relic
            || !point.HasIndividualRelicHeading
            || headingLength <= 0)
        {
            return;
        }

        var (start, end) = GuardianLegacyMapDrawing.CreateHeadingLine(
            location,
            headingLength,
            rotation);
        context.DrawLine(
            new Pen(
                new SolidColorBrush(
                    GuardianLegacyMapDrawing.IndividualTowerHeading),
                10 * markerScale),
            start,
            end);
    }

    private void DrawTargetOrNearestHighlight(
        DrawingContext context,
        GuardianProjectedPoint point,
        Point location,
        double markerScale)
    {
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
        if (!isTarget && !isNearest)
        {
            return;
        }

        var highlightRadius = 14 * markerScale;
        context.DrawEllipse(
            null,
            new Pen(
                new SolidColorBrush(GuardianLegacyMapDrawing.Target),
                4 * markerScale,
                dashStyle: DashStyle.Dot),
            location,
            highlightRadius,
            highlightRadius);
    }

    private void DrawPointerSelectionHighlight(
        DrawingContext context,
        GuardianProjectedPoint point,
        Point location,
        double markerScale)
    {
        var isHovered = string.Equals(
            point.Name,
            HoveredPointName,
            StringComparison.OrdinalIgnoreCase);
        var isSelected = string.Equals(
            point.Name,
            HighlightedPointName ?? SelectedPointName,
            StringComparison.OrdinalIgnoreCase);
        if (!isHovered && !isSelected)
        {
            return;
        }

        var radius = 14 * markerScale;
        var brush = PresentBrush ?? Brushes.LimeGreen;
        var thickness = (isSelected ? 4d : 3d) * markerScale;
        context.DrawEllipse(
            null,
            new Pen(
                brush,
                thickness,
                dashStyle: new DashStyle([3.5, 2], 0.5)),
            location,
            radius,
            radius);
    }

    private sealed class PointGlyphDraw
    {
        public required DrawingContext Context { get; init; }
        public required GuardianProjectedPoint Point { get; init; }
        public required Point Location { get; init; }
        public required GuardianSiteMapProjection Projection { get; init; }
        public required Pen Pen { get; init; }
        public IBrush? Fill { get; init; }
        public double Rotation { get; init; }
        public double MarkerScale { get; init; }
    }

    private static void DrawPointGlyph(PointGlyphDraw draw)
    {
        switch (draw.Point.Type)
        {
            case GuardianPoiType.Obelisk:
            case GuardianPoiType.BrokenObelisk:
                DrawObeliskGlyph(
                    draw.Context,
                    draw.Point,
                    draw.Location,
                    draw.Pen,
                    draw.Rotation,
                    draw.MarkerScale);
                break;

            case GuardianPoiType.Pylon:
                DrawPylonGlyph(
                    draw.Context,
                    draw.Point,
                    draw.Location,
                    draw.Pen,
                    draw.Rotation,
                    draw.MarkerScale);
                break;

            case GuardianPoiType.Component:
                DrawPolyline(
                    draw.Context,
                    GuardianLegacyMapDrawing.CreateGlyphPoints(
                        draw.Point.Type,
                        draw.Location,
                        draw.Rotation,
                        draw.MarkerScale),
                    draw.Pen);
                DrawComponentMaterials(
                    draw.Context,
                    draw.Location,
                    draw.Point.ComponentMaterials,
                    draw.MarkerScale);
                break;

            case GuardianPoiType.DestructiblePanel:
                DrawDestructiblePanel(
                    draw.Context,
                    draw.Point,
                    draw.Location,
                    draw.Pen,
                    draw.MarkerScale);
                break;

            case GuardianPoiType.Relic:
                draw.Context.DrawGeometry(
                    draw.Fill,
                    draw.Pen,
                    CreatePolygon(
                        GuardianLegacyMapDrawing.CreateGlyphPoints(
                            draw.Point.Type,
                            draw.Location,
                            draw.Rotation,
                            draw.MarkerScale)));
                break;

            default:
                var radius = GuardianLegacyMapDrawing.GetPuddleRadius(
                    draw.Projection,
                    draw.Point) * draw.MarkerScale;
                draw.Context.DrawEllipse(
                    draw.Fill,
                    draw.Pen,
                    draw.Location,
                    radius,
                    radius);
                break;
        }
    }

    private static void DrawObeliskGlyph(
        DrawingContext context,
        GuardianProjectedPoint point,
        Point location,
        Pen pen,
        double rotation,
        double markerScale)
    {
        DrawPolyline(
            context,
            GuardianLegacyMapDrawing.CreateGlyphPoints(
                point.Type,
                location,
                rotation,
                markerScale),
            pen);
        if (point.Type == GuardianPoiType.BrokenObelisk)
        {
            return;
        }

        context.DrawLine(
            pen,
            location + GuardianLegacyMapDrawing.RotateClockwise(
                new Point(0.2 * markerScale, 0),
                rotation),
            location + GuardianLegacyMapDrawing.RotateClockwise(
                new Point(-0.5 * markerScale, -1.2 * markerScale),
                rotation));
        context.DrawLine(
            pen,
            location + GuardianLegacyMapDrawing.RotateClockwise(
                new Point(0.2 * markerScale, 0),
                rotation),
            location + GuardianLegacyMapDrawing.RotateClockwise(
                new Point(1.5 * markerScale, -0.8 * markerScale),
                rotation));
    }

    private static void DrawPylonGlyph(
        DrawingContext context,
        GuardianProjectedPoint point,
        Point location,
        Pen pen,
        double rotation,
        double markerScale)
    {
        DrawPolyline(
            context,
            GuardianLegacyMapDrawing.CreateGlyphPoints(
                point.Type,
                location,
                rotation,
                markerScale),
            pen);
        context.DrawLine(
            pen,
            location,
            location + GuardianLegacyMapDrawing.RotateClockwise(
                new Point(0, 3 * markerScale),
                rotation));
    }

    private static void DrawDestructiblePanel(
        DrawingContext context,
        GuardianProjectedPoint point,
        Point location,
        Pen pen,
        double markerScale)
    {
        var materialColor = GuardianLegacyMapDrawing
            .GetComponentMaterialColor(
            point.ComponentMaterials.Count > 0
                ? point.ComponentMaterials[0]
                : default);
        context.DrawRectangle(
            materialColor is { } known
                ? new SolidColorBrush(known)
                : null,
            materialColor is not null
                ? new Pen(Brushes.Black, markerScale)
                : pen,
            new Rect(
                location.X - 2 * markerScale,
                location.Y - 2 * markerScale,
                4 * markerScale,
                4 * markerScale));
    }

    private static void DrawHeadingLines(
        DrawingContext context,
        GuardianSiteMapProjection projection,
        Point mapOrigin,
        double length,
        double commanderHeading,
        double markerScale)
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
                4 * markerScale,
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
                4 * markerScale),
            towerStart,
            towerEnd);
    }

    private static void DrawActiveObeliskEffect(
        DrawingContext context,
        GuardianProjectedPoint point,
        Point location,
        double rotation,
        double markerScale)
    {
        var color = GuardianLegacyMapDrawing.GetActiveObeliskEffectColor(point);
        for (var step = 0; step < 6; step++)
        {
            var radius = (15 - step * 2.2) * markerScale;
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

    private static Pen CreatePen(
        GuardianLegacyPointStyle style,
        double markerScale)
    {
        var dash = style.Pattern switch
        {
            GuardianLegacyStrokePattern.Dash => DashStyle.Dash,
            GuardianLegacyStrokePattern.Dot => DashStyle.Dot,
            _ => null,
        };
        return new Pen(
            new SolidColorBrush(style.Stroke),
            style.StrokeWidth * markerScale,
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
        GetSurveyMarkerRadii(
            GuardianPoiType type,
            bool isRuins)
    {
        var diameter = type == GuardianPoiType.Relic ? 16d : 10d;
        if (isRuins)
        {
            diameter *= 1.6;
        }

        return (diameter / 2 + 5, diameter / 2 + 4);
    }

    private static void DrawComponentMaterials(
        DrawingContext context,
        Point location,
        IReadOnlyList<GuardianComponentMaterial> materials,
        double markerScale)
    {
        var centers = GuardianLegacyMapDrawing.CreateComponentMaterialCenters(
            location,
            markerScale);
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
                new Pen(Brushes.Black, markerScale),
                centers[index],
                2 * markerScale,
                2 * markerScale);
        }
    }

    private void DrawGroup(
        DrawingContext context,
        GuardianProjectedGroup group,
        Point location,
        double markerScale)
    {
        var brush = AccentBrush ?? Brushes.Cyan;
        var text = new FormattedText(
            group.Name,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter", FontStyle.Normal, FontWeight.Normal),
            22 * markerScale,
            brush);
        context.DrawText(
            text,
            new Point(
                location.X - text.Width / 2 + 2 * markerScale,
                location.Y - text.Height / 2 + 2 * markerScale));
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
        GuardianMapLegendKind Kind = GuardianMapLegendKind.Point,
        bool IsActiveObelisk = false,
        bool IsScannedObelisk = false,
        bool IsRamTahNeededObelisk = false);

    private enum GuardianMapLegendKind
    {
        Point,
        SiteHeading,
        TowerHeading,
        SurveyNeeded,
    }
}
