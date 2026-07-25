using Avalonia.Controls;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class GroundTargetOverlayWindow : Window
{
    public GroundTargetOverlayWindow()
        : this(CreateDesignViewModel())
    {
    }

    public GroundTargetOverlayWindow(GroundTargetOverlayViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }

    private static GroundTargetOverlayViewModel CreateDesignViewModel()
    {
        return new GroundTargetOverlayViewModel(
            new GroundTargetViewModel(
                new GroundTargetSettingsStore(Path.Combine(
                    Path.GetTempPath(),
                    "SrvSurvey-GroundTarget-Overlay-Design"))),
            OverlayPlatformCapabilities.DetectCurrent());
    }
}
