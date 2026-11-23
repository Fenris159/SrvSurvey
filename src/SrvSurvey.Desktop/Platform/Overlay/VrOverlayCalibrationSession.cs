using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Platform.Overlay;

/// <summary>Owns VR-only sample and control surfaces independently of live panel visibility.</summary>
internal sealed class VrOverlayCalibrationSession : IDisposable
{
    public const string ControlsPlotterName = "PlotAdjustVR";
    private const float PositionRecenterThresholdMetersSquared = 0.04f; // 20 cm
    private static readonly VrOverlayCalibration DefaultControlsCalibration = new(
        8,
        new Vector3(0, -5, 10),
        Vector3.Zero
    );
    private RegisteredOverlayWindow? controls;
    private readonly Dictionary<string, RegisteredOverlayWindow> previews = new(StringComparer.Ordinal);
    private float? snappedYaw;
    private Vector3? anchoredHeadPosition;

    public VrOverlayCalibration ControlsCalibration { get; private set; } = DefaultControlsCalibration;

    public IReadOnlyList<RegisteredOverlayWindow> Refresh(
        VrOverlayViewModel viewModel,
        VrOverlayHeadPose? pose,
        int maxRegistrations = int.MaxValue
    )
    {
        if (!viewModel.IsAdjusting)
        {
            Dispose();
            return [];
        }

        EnsureControlsSurface(viewModel);
        UpdateControlsPlacement(pose);
        IReadOnlyList<string> desiredNames = viewModel.VisibleAdjustmentOverlays;
        SyncPreviewSurfaces(desiredNames);
        return BuildOrderedRegistrations(desiredNames, viewModel.SelectedOverlayName, maxRegistrations);
    }

    private void EnsureControlsSurface(VrOverlayViewModel viewModel)
    {
        if (controls is not null)
        {
            return;
        }

        var content = new Border
        {
            Padding = new Thickness(16),
            Child = new VrCalibrationView { DataContext = viewModel, IsInHeadset = true },
        };
        content[!Border.BackgroundProperty] = new DynamicResourceExtension("RavenWindowBrush");
        controls = CreateSurface(ControlsPlotterName, content, 712);
        ControlsCalibration = DefaultControlsCalibration;
        snappedYaw = null;
        anchoredHeadPosition = null;
    }

    private void UpdateControlsPlacement(VrOverlayHeadPose? pose)
    {
        if (pose is null)
        {
            return;
        }

        float nextYaw = VrOverlayTransform.SnapCalibrationYaw(pose.Yaw, snappedYaw);
        bool shifted =
            anchoredHeadPosition is null
            || Vector3.DistanceSquared(pose.Position, anchoredHeadPosition.Value)
                > PositionRecenterThresholdMetersSquared;
        bool yawChanged = snappedYaw is null || MathF.Abs(snappedYaw.Value - nextYaw) > 0.001f;
        if (yawChanged || shifted)
        {
            snappedYaw = nextYaw;
            anchoredHeadPosition = pose.Position;
            ControlsCalibration = VrOverlayTransform.PlaceCalibrationControls(pose, nextYaw);
        }
    }

    private void SyncPreviewSurfaces(IReadOnlyList<string> desiredNames)
    {
        var desiredSet = new HashSet<string>(desiredNames, StringComparer.Ordinal);
        foreach (string stale in previews.Keys.Where(name => !desiredSet.Contains(name)).ToArray())
        {
            previews[stale].Window.Close();
            previews.Remove(stale);
        }

        foreach (string name in desiredNames)
        {
            if (
                !previews.ContainsKey(name)
                && OverlayRuntimePresentationFactory.TryCreate(name, out Control? presentation, out _)
            )
            {
                previews[name] = CreateSurface(
                    name,
                    presentation!,
                    OverlayLayoutCatalog.GetRequired(name).PreviewSize.Width
                );
            }
        }
    }

    private List<RegisteredOverlayWindow> BuildOrderedRegistrations(
        IReadOnlyList<string> desiredNames,
        string? selectedOverlayName,
        int maxRegistrations
    )
    {
        if (controls is null || maxRegistrations <= 0)
        {
            return [];
        }

        var result = new List<RegisteredOverlayWindow>(Math.Min(maxRegistrations, 1 + previews.Count)) { controls };
        if (maxRegistrations == 1)
        {
            return result;
        }

        RegisteredOverlayWindow? selectedWindow =
            selectedOverlayName is not null && previews.TryGetValue(selectedOverlayName, out RegisteredOverlayWindow? w)
                ? w
                : null;
        int siblingLimit = maxRegistrations - 1 - (selectedWindow is null ? 0 : 1);
        int addedSiblings = 0;
        foreach (string name in desiredNames)
        {
            if (addedSiblings >= siblingLimit)
            {
                break;
            }

            if (
                !string.Equals(name, selectedOverlayName, StringComparison.Ordinal)
                && previews.TryGetValue(name, out RegisteredOverlayWindow? sibling)
            )
            {
                result.Add(sibling);
                addedSiblings++;
            }
        }

        if (selectedWindow is not null)
        {
            result.Add(selectedWindow);
        }

        return result;
    }

    public bool Owns(RegisteredOverlayWindow registration) =>
        registration == controls || previews.ContainsValue(registration);

    private static RegisteredOverlayWindow CreateSurface(string name, Control content, double width)
    {
        var window = new Window
        {
            Content = content,
            Width = width,
            SizeToContent = SizeToContent.Height,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowDecorations = WindowDecorations.None,
            Opacity = 0,
            Position = new PixelPoint(-32000, -32000),
        };
        window.Show();
        window.UpdateLayout();
        return new RegisteredOverlayWindow(window, name, content, IsVisible: true, ParticipatesInPlacement: false);
    }

    public void Dispose()
    {
        foreach (RegisteredOverlayWindow preview in previews.Values)
        {
            preview.Window.Close();
        }
        previews.Clear();
        controls?.Window.Close();
        controls = null;
        snappedYaw = null;
        anchoredHeadPosition = null;
    }
}
