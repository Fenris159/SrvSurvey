using System.ComponentModel;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using SrvSurvey.Desktop.Controls;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class OverlayPositionPreviewWindow
{
    private CockpitOverlayViewModel? cockpitPanels;
    private bool applyingCockpitPose;

    private void InitializeCockpitEditor()
    {
        PanelRotationHandle.PropertyChanged += OnRotationHandleChanged;
        PanelRotationHandleBorder.AddHandler(PointerPressedEvent, OnRotationHandlePressed, RoutingStrategies.Tunnel);
        PanelProjectionHost.SizeChanged += (_, _) => ApplyCockpitPreview();
        RefreshCockpitHandle();
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
                ? "Drag the orb center to tilt; drag its rim to roll. Right-click the orb to reset rotation. Save with the editor toolbar."
                : "Open the overlay position editor to adjust this panel's angle."
        );
        applyingCockpitPose = false;
        ApplyCockpitPreview();
    }

    private void OnRotationHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(PanelRotationHandleBorder).Properties.IsRightButtonPressed)
        {
            if (cockpitPanels is { } model)
            {
                model.SetPanelPose(
                    Definition.Name,
                    model.Resolve(Definition.Name) with
                    {
                        Yaw = 0,
                        Pitch = 0,
                        Roll = 0,
                    }
                );
            }
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
        if (PanelProjectionHost.Child is not { } panel)
        {
            return;
        }
        if (cockpitPanels is null)
        {
            panel.RenderTransform = null;
            panel.Opacity = 1;
            return;
        }
        Size size = panel.Bounds.Size;
        var viewport = new Size(
            Math.Max(size.Width, viewportWidth ?? 1920),
            Math.Max(size.Height, viewportHeight ?? 1080)
        );
        var rectangle = new Rect(
            (viewport.Width - size.Width) / 2,
            (viewport.Height - size.Height) / 2,
            size.Width,
            size.Height
        );
        Matrix? projection = CockpitPanelProjection.Create(
            rectangle,
            viewport,
            cockpitPanels.Resolve(Definition.Name),
            Matrix4x4.Identity
        );
        panel.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute);
        panel.RenderTransform = projection is { } matrix
            ? new MatrixTransform(matrix * Matrix.CreateTranslation(-rectangle.X, -rectangle.Y))
            : null;
        panel.Opacity = projection is null ? 0 : 1;
    }
}
