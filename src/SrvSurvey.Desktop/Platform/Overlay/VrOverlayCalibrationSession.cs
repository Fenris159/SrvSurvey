using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Platform.Overlay;

/// <summary>Owns VR-only sample and control surfaces independently of live panel visibility.</summary>
internal sealed class VrOverlayCalibrationSession : IDisposable
{
    public const string ControlsPlotterName = "PlotAdjustVR";
    private RegisteredOverlayWindow? controls;
    private RegisteredOverlayWindow? preview;
    private bool awaitingHeadPose;

    public VrOverlayCalibration ControlsCalibration { get; private set; } =
        new(8, new Vector3(0, -5, 11), new Vector3(15, 0, 0));

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
                Padding = new Thickness(20),
                Background = new SolidColorBrush(Color.Parse("#171A21")),
                Child = new VrCalibrationView { DataContext = viewModel, IsInHeadset = true },
            };
            controls = CreateSurface(ControlsPlotterName, content, 760);
            ControlsCalibration = new(8, new Vector3(0, -5, 11), new Vector3(15, 0, 0));
            awaitingHeadPose = true;
        }
        if (awaitingHeadPose && pose is not null)
        {
            ControlsCalibration = VrOverlayTransform.PlaceInFront(ControlsCalibration, pose);
            ControlsCalibration = ControlsCalibration with
            {
                Position = ControlsCalibration.Position - new Vector3(0, 5, 0),
                Rotation = ControlsCalibration.Rotation with { X = 15 },
            };
            awaitingHeadPose = false;
        }

        if (preview?.PlotterName != viewModel.SelectedOverlayName)
        {
            preview?.Window.Close();
            preview = null;
            if (
                viewModel.SelectedOverlayName is { } name
                && OverlayRuntimePresentationFactory.TryCreate(name, out Control? presentation, out _)
            )
            {
                preview = CreateSurface(name, presentation!, OverlayLayoutCatalog.GetRequired(name).PreviewSize.Width);
            }
        }
        return preview is null ? [controls] : [controls, preview];
    }

    public bool Owns(RegisteredOverlayWindow registration) => registration == controls || registration == preview;

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
        preview?.Window.Close();
        preview = null;
        controls?.Window.Close();
        controls = null;
    }
}
