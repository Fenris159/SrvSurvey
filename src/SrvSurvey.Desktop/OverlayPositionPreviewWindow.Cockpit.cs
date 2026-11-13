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
    private bool applyingCockpitPose;
    private PixelRect? cockpitViewport;
    private double cockpitViewportScaling = 1;
    private double? cockpitBaseMaxHeight;
    private bool cockpitPreviewQueued;
    private bool adjustingCockpitProjection;
    private PixelPoint? pendingCockpitOrigin;
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
            DispatcherPriority.Loaded
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
        )
        {
            RefreshCockpitHandle();
        }
    }

    private void RefreshCockpitHandle()
    {
        applyingCockpitPose = true;
        CockpitPanelPose pose = cockpitPanels?.Resolve(Definition.Name) ?? new CockpitPanelPose();
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
        if (
            e.GetCurrentPoint(PanelRotationHandleBorder).Properties.IsLeftButtonPressed
            && !PanelRotationHandle.IsEnabled
        )
        {
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
            FitCockpitProjection(Matrix.Identity, size, origin, null);
            return;
        }
        PixelRect viewport = cockpitViewport ?? new PixelRect(0, 0, viewportWidth ?? 1920, viewportHeight ?? 1080);
        Rect? visibleViewport = cockpitPanels is null
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
        FitCockpitProjection(projection ?? Matrix.Identity, size, origin, visibleViewport);
    }

    private void FitCockpitProjection(Matrix projection, Size size, PixelPoint origin, Rect? visibleViewport)
    {
        Point[] corners =
        [
            projection.Transform(default),
            projection.Transform(new Point(size.Width, 0)),
            projection.Transform(new Point(size.Width, size.Height)),
            projection.Transform(new Point(0, size.Height)),
        ];
        Point offset = PanelProjectionHost.TranslatePoint(default, PreviewSurface) ?? default;
        var extent = new Rect(
            new Point(corners.Min(p => p.X), corners.Min(p => p.Y)),
            new Point(corners.Max(p => p.X), corners.Max(p => p.Y))
        );
        if (visibleViewport is { } viewport)
        {
            extent = extent.Intersect(viewport);
        }
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
