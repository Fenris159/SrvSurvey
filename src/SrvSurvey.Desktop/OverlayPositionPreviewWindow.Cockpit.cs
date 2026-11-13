using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using SrvSurvey.Desktop.Controls;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class OverlayPositionPreviewWindow
{
    private CockpitOverlayViewModel? cockpitPanels;
    private CockpitPanelPose? displayedCockpitPose;
    private bool applyingCockpitPose;
    private PixelRect? cockpitViewport;
    private double cockpitViewportScaling = 1;
    private double? cockpitBaseMaxHeight;
    private bool cockpitPreviewQueued;
    private bool adjustingCockpitProjection;
    private PixelPoint? pendingCockpitOrigin;
    internal event EventHandler? ProjectionLayoutSettled;
    internal bool IsAdjustingCockpitProjection => adjustingCockpitProjection || pendingCockpitOrigin is not null;
    private Control CockpitPaddingHost =>
        PreviewSurface.Parent is LayoutTransformControl container ? container : PreviewSurface;

    /// <summary>Settles visual padding before the editor snapshots its untransformed placement.</summary>
    internal void FlushCockpitProjection()
    {
        UpdateLayout();
        CompleteCockpitLayout();
        ApplyCockpitPreview();
        CompleteCockpitLayout();
    }

    /// <summary>Keeps a deferred bounds fit anchored to the latest accepted drag position.</summary>
    internal void AcceptCockpitDragOrigin(PixelPoint origin)
    {
        if (pendingCockpitOrigin is not null)
        {
            pendingCockpitOrigin = origin;
        }
    }

    private void InitializeCockpitEditor()
    {
        PanelRotationHandle.PropertyChanged += OnRotationHandleChanged;
        PanelRotationHandle.ResetRequested += OnRotationResetRequested;
        PanelRotationHandleBorder.AddHandler(PointerPressedEvent, OnRotationHandlePressed, RoutingStrategies.Tunnel);
        PanelProjectionHost.SizeChanged += (_, _) => ScheduleCockpitPreview();
        PositionChanged += (_, _) => ScheduleCockpitPreview();
        RefreshCockpitHandle();
    }

    internal void ConfigureCockpitViewport(PixelRect bounds, double scaling)
    {
        cockpitViewport = bounds;
        cockpitViewportScaling = scaling;
        ScheduleCockpitPreview();
    }

    private void ScheduleCockpitPreview()
    {
        if (cockpitPreviewQueued || adjustingCockpitProjection)
        {
            return;
        }
        cockpitPreviewQueued = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                cockpitPreviewQueued = false;
                CompleteCockpitLayout();
                ApplyCockpitPreview();
            },
            // Native configure/input events must settle before another projection fit.
            // Loaded-priority refits can starve X11 events and oscillate by one pixel.
            DispatcherPriority.Background
        );
    }

    private void CompleteCockpitLayout()
    {
        if (!IsVisible || pendingCockpitOrigin is not { } origin)
        {
            return;
        }
        adjustingCockpitProjection = true;
        try
        {
            UpdateLayout();
            PixelPoint offset = GetPanelMetrics(RenderScaling).OriginOffset;
            Position = new PixelPoint(origin.X - offset.X, origin.Y - offset.Y);
            pendingCockpitOrigin = null;
        }
        finally
        {
            adjustingCockpitProjection = false;
        }
        ProjectionLayoutSettled?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Shares the editor transaction while keeping each corner handle bound to its own panel.</summary>
    internal void ConfigureCockpitPanels(CockpitOverlayViewModel? model)
    {
        cockpitPanels?.PropertyChanged -= OnCockpitPoseChanged;
        cockpitPanels = model;
        cockpitPanels?.PropertyChanged += OnCockpitPoseChanged;
        RefreshCockpitHandle();
    }

    private void OnCockpitPoseChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (
            e.PropertyName
            is nameof(CockpitOverlayViewModel.Yaw)
                or nameof(CockpitOverlayViewModel.Pitch)
                or nameof(CockpitOverlayViewModel.Roll)
                or nameof(CockpitOverlayViewModel.Depth)
                or nameof(CockpitOverlayViewModel.CalibratedLens)
                or nameof(CockpitOverlayViewModel.Enabled)
        )
        {
            // Selected-pose notifications are shared across all previews. Only the edited panel
            // needs repainting; lens and mode changes still refresh every panel.
            if (
                e.PropertyName
                    is not nameof(CockpitOverlayViewModel.CalibratedLens)
                        and not nameof(CockpitOverlayViewModel.Enabled)
                && cockpitPanels?.Resolve(Definition.Name) == displayedCockpitPose
            )
            {
                return;
            }
            RefreshCockpitHandle();
        }
    }

    private void RefreshCockpitHandle()
    {
        applyingCockpitPose = true;
        CockpitPanelPose pose = cockpitPanels?.Resolve(Definition.Name) ?? new CockpitPanelPose();
        displayedCockpitPose = pose;
        PanelRotationHandle.Yaw = pose.Yaw;
        PanelRotationHandle.Pitch = pose.Pitch;
        PanelRotationHandle.Roll = pose.Roll;
        PanelRotationHandle.Depth = pose.Depth;
        PanelRotationHandle.IsEnabled = cockpitPanels is not null;
        ToolTip.SetTip(
            PanelRotationHandleBorder,
            PanelRotationHandle.IsEnabled
                ? "Drag the orb center to tilt; drag its rim to roll. Right-click the orb to reset rotation, or press Home. Save with the editor toolbar."
                : "Open the overlay position editor to adjust this panel's angle."
        );
        applyingCockpitPose = false;
        ApplyCockpitPreview();
    }

    private void OnRotationHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(PanelRotationHandleBorder).Properties.IsRightButtonPressed)
        {
            OnRotationResetRequested(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }
        if (e.GetCurrentPoint(PanelRotationHandleBorder).Properties.IsLeftButtonPressed)
        {
            if (PanelRotationHandle.IsEnabled)
            {
                PanelRotationHandle.BeginRotation(e);
            }
            e.Handled = true;
        }
    }

    private void OnRotationResetRequested(object? sender, EventArgs e)
    {
        if (cockpitPanels is { } model)
        {
            model.SetPanelPose(Definition.Name, model.Resolve(Definition.Name) with { Yaw = 0, Pitch = 0, Roll = 0 });
        }
    }

    private void OnRotationHandleChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (
            !applyingCockpitPose
            && cockpitPanels is not null
            && (
                e.Property == PanelRotationOrb.YawProperty
                || e.Property == PanelRotationOrb.PitchProperty
                || e.Property == PanelRotationOrb.RollProperty
                || e.Property == PanelRotationOrb.DepthProperty
            )
        )
        {
            cockpitPanels.SetPanelPose(
                Definition.Name,
                new CockpitPanelPose(
                    PanelRotationHandle.Yaw,
                    PanelRotationHandle.Pitch,
                    PanelRotationHandle.Roll,
                    PanelRotationHandle.Depth
                )
            );
        }
    }

    private void ApplyCockpitPreview()
    {
        if (
            adjustingCockpitProjection
            || !IsVisible
            || PanelProjectionHost.Child is not { } panel
            || panel.Bounds.Width <= 0
            || panel.Bounds.Height <= 0
        )
        {
            return;
        }
        Size size = panel.Bounds.Size;
        CockpitPaddingHost.ClipToBounds = false;
        PixelPoint origin = pendingCockpitOrigin ?? GetPanelScreenOrigin(RenderScaling);
        double physicalScale = RenderScaling * scaleFactor;
        if (physicalScale <= 0)
        {
            panel.RenderTransform = null;
            panel.Opacity = 1;
            PanelProjectionHost.Clip = null;
            FitCockpitProjection(
                Matrix.Identity,
                size,
                origin,
                null,
                ProjectEditorChrome(Matrix.Identity, size, out _)
            );
            return;
        }
        PixelRect viewport = cockpitViewport ?? new PixelRect(0, 0, viewportWidth ?? 1920, viewportHeight ?? 1080);
        Rect? visibleViewport =
            cockpitPanels?.Enabled != true
                ? null
                : new Rect(
                    (viewport.X - origin.X) / physicalScale,
                    (viewport.Y - origin.Y) / physicalScale,
                    viewport.Width / physicalScale,
                    viewport.Height / physicalScale
                );
        PanelProjectionHost.Clip = visibleViewport is { } visible ? new RectangleGeometry(visible) : null;
        Matrix? projection = cockpitPanels is null
            ? Matrix.Identity
            : CockpitPanelProjection.CreateEditorPreview(
                new Rect(origin.X, origin.Y, size.Width * physicalScale, size.Height * physicalScale),
                size,
                viewport,
                cockpitViewportScaling,
                cockpitPanels.Resolve(Definition.Name),
                cockpitPanels.CalibratedLens
            );
        panel.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute);
        panel.RenderTransform =
            cockpitPanels is not null && projection is { } matrix ? new MatrixTransform(matrix) : null;
        panel.Opacity = projection is null ? 0 : 1;
        Matrix chromeProjection = projection ?? Matrix.Identity;
        Rect chromeBounds = ProjectEditorChrome(chromeProjection, size, out bool chromeVisible);
        if (!chromeVisible)
        {
            // The title and frame extend beyond the content. Keep a readable reset handle
            // rather than fitting a chrome rectangle that crosses the camera plane.
            chromeProjection = Matrix.Identity;
            panel.Opacity = 0;
        }
        FitCockpitProjection(chromeProjection, size, origin, visibleViewport, chromeBounds);
    }

    /// <summary>Projects all editor chrome through the content's plane, retaining flat coordinates for placement.</summary>
    private Rect ProjectEditorChrome(Matrix projection, Size panelSize, out bool visible)
    {
        Point panelOrigin = GetEditorLayoutOrigin(PanelProjectionHost);
        ProjectionReferenceOutline.Width = Math.Max(
            panelSize.Width
                + PreviewBody.BorderThickness.Left
                + PreviewBody.BorderThickness.Right
                + PreviewBody.Padding.Left
                + PreviewBody.Padding.Right,
            EditorFolderTab.DesiredSize.Width
        );
        ProjectionReferenceOutline.Height =
            panelSize.Height
            + PreviewBody.BorderThickness.Top
            + PreviewBody.BorderThickness.Bottom
            + PreviewBody.Padding.Top
            + PreviewBody.Padding.Bottom;
        Control[] elements = [ProjectionReferenceOutline, EditorFolderTab];
        visible = elements.All(chrome => IsInFrontOfCamera(projection, GetChromeRectangle(chrome, panelOrigin)));
        if (!visible)
        {
            projection = Matrix.Identity;
        }
        Rect? chromeBounds = null;
        foreach (Control chrome in elements)
        {
            Vector offset = GetEditorLayoutOrigin(chrome) - panelOrigin;
            chrome.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute);
            chrome.RenderTransform = new MatrixTransform(
                Matrix.CreateTranslation(offset.X, offset.Y)
                    * projection
                    * Matrix.CreateTranslation(-offset.X, -offset.Y)
            );
            Rect bounds = GetProjectedBounds(projection, GetChromeRectangle(chrome, panelOrigin));
            chromeBounds = chromeBounds?.Union(bounds) ?? bounds;
        }

        Vector outlineOffset = GetEditorLayoutOrigin(ProjectionReferenceOutline) - panelOrigin;
        Point corner = projection.Transform(
            new Point(
                outlineOffset.X + ProjectionReferenceOutline.Width,
                outlineOffset.Y + ProjectionReferenceOutline.Height
            )
        );
        Point top = projection.Transform(
            new Point(outlineOffset.X + ProjectionReferenceOutline.Width, outlineOffset.Y)
        );
        Vector edge = corner - top;
        Vector outward = edge.Length > .001 ? new Vector(edge.Y, -edge.X) / edge.Length : new Vector(1, 0);
        Point interior = projection.Transform(
            new Point(
                outlineOffset.X + ProjectionReferenceOutline.Width / 2,
                outlineOffset.Y + ProjectionReferenceOutline.Height / 2
            )
        );
        Vector towardInterior = interior - corner;
        if (outward.X * towardInterior.X + outward.Y * towardInterior.Y > 0)
        {
            outward = -outward;
        }
        var knobSize = new Size(PanelRotationHandleBorder.Width, PanelRotationHandleBorder.Height);
        double clearance = (Math.Abs(outward.X) * knobSize.Width + Math.Abs(outward.Y) * knobSize.Height) / 2 + 8;
        Vector along = edge.Length > .001 ? edge / edge.Length : new Vector(0, 1);
        double inset = (Math.Abs(along.X) * knobSize.Width + Math.Abs(along.Y) * knobSize.Height) / 2;
        Point knobOrigin =
            corner + outward * clearance - along * inset - new Vector(knobSize.Width / 2, knobSize.Height / 2);
        Vector knobOffset = GetEditorLayoutOrigin(PanelRotationHandleBorder) - panelOrigin;
        PanelRotationHandleBorder.RenderTransform = new TranslateTransform(
            knobOrigin.X - knobOffset.X,
            knobOrigin.Y - knobOffset.Y
        );
        return chromeBounds!.Value.Union(new Rect(knobOrigin, knobSize));
    }

    private Rect GetChromeRectangle(Control chrome, Point panelOrigin)
    {
        Vector offset = GetEditorLayoutOrigin(chrome) - panelOrigin;
        Size size =
            chrome == ProjectionReferenceOutline
                ? new Size(ProjectionReferenceOutline.Width, ProjectionReferenceOutline.Height)
                : chrome.Bounds.Size;
        return new Rect(new Point(offset.X, offset.Y), size);
    }

    private static bool IsInFrontOfCamera(Matrix projection, Rect rectangle)
    {
        bool InFront(Point point)
        {
            double depth = projection.M13 * point.X + projection.M23 * point.Y + projection.M33;
            return double.IsFinite(depth) && depth > .001;
        }
        return InFront(rectangle.TopLeft)
            && InFront(rectangle.TopRight)
            && InFront(rectangle.BottomRight)
            && InFront(rectangle.BottomLeft);
    }

    private Point GetEditorLayoutOrigin(Control control)
    {
        Vector offset = default;
        for (
            Control? current = control;
            current is not null && current != PreviewSurface;
            current = current.Parent as Control
        )
        {
            offset += current.Bounds.Position;
        }
        return new Point(offset.X, offset.Y);
    }

    private static Rect GetProjectedBounds(Matrix projection, Rect rectangle)
    {
        Point[] corners =
        [
            projection.Transform(rectangle.TopLeft),
            projection.Transform(rectangle.TopRight),
            projection.Transform(rectangle.BottomRight),
            projection.Transform(rectangle.BottomLeft),
        ];
        return new Rect(
            new Point(corners.Min(p => p.X), corners.Min(p => p.Y)),
            new Point(corners.Max(p => p.X), corners.Max(p => p.Y))
        );
    }

    private void FitCockpitProjection(
        Matrix projection,
        Size size,
        PixelPoint origin,
        Rect? visibleViewport,
        Rect chromeBounds
    )
    {
        Point offset = PanelProjectionHost.TranslatePoint(default, PreviewSurface) ?? default;
        Rect extent = GetProjectedBounds(projection, new Rect(size));
        if (visibleViewport is { } viewport)
        {
            extent = extent.Intersect(viewport);
        }
        extent = extent.Union(chromeBounds);
        var padding = new Thickness(
            ProjectionOverflow((-extent.Left - offset.X) * scaleFactor),
            ProjectionOverflow((-extent.Top - offset.Y) * scaleFactor),
            ProjectionOverflow((extent.Right + offset.X - PreviewSurface.Bounds.Width) * scaleFactor),
            ProjectionOverflow((extent.Bottom + offset.Y - PreviewSurface.Bounds.Height) * scaleFactor)
        );
        double maximumHeight = (cockpitBaseMaxHeight ?? MaxHeight) + padding.Top + padding.Bottom;
        bool unchangedHeight =
            (double.IsPositiveInfinity(maximumHeight) && double.IsPositiveInfinity(MaxHeight))
            || Math.Abs(maximumHeight - MaxHeight) < .01;
        if (padding == CockpitPaddingHost.Margin && unchangedHeight)
        {
            return;
        }
        adjustingCockpitProjection = true;
        try
        {
            cockpitBaseMaxHeight ??= MaxHeight;
            pendingCockpitOrigin ??= origin;
            CockpitPaddingHost.Margin = padding;
            MaxHeight = maximumHeight;
            UpdateLayout();
            PixelPoint newOffset = GetPanelMetrics(RenderScaling).OriginOffset;
            Position = new PixelPoint(origin.X - newOffset.X, origin.Y - newOffset.Y);
        }
        finally
        {
            adjustingCockpitProjection = false;
        }
        ScheduleCockpitPreview();
    }

    private static double ProjectionOverflow(double amount) => Math.Max(0, Math.Ceiling(amount - 0.01));
}
