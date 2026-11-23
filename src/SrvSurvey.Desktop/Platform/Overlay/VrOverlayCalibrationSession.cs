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

    public IReadOnlyList<RegisteredOverlayWindow> Refresh(VrOverlayViewModel viewModel, VrOverlayHeadPose? pose)
    {
        if (!viewModel.IsAdjusting)
        {
            Dispose();
            return [];
        }

        if (controls is null)
        {
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
        if (pose is not null)
        {
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

        IReadOnlyList<string> desiredNames = viewModel.VisibleAdjustmentOverlays;
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

        var result = new List<RegisteredOverlayWindow>(1 + previews.Count) { controls };
        foreach (string name in desiredNames)
        {
            if (
                !string.Equals(name, viewModel.SelectedOverlayName, StringComparison.Ordinal)
                && previews.TryGetValue(name, out RegisteredOverlayWindow? sibling)
            )
            {
                result.Add(sibling);
            }
        }

        if (
            viewModel.SelectedOverlayName is { } selected
            && previews.TryGetValue(selected, out RegisteredOverlayWindow? selectedWindow)
        )
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
