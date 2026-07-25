using Avalonia.Controls;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class StationInfoOverlayWindow : Window
{
    public StationInfoOverlayWindow()
        : this(new StationInfoOverlayViewModel(
            new StationInfoViewModel(new SystemSummaryClient()),
            Platform.Overlay.OverlayPlatformCapabilities.DetectCurrent()))
    {
    }

    public StationInfoOverlayWindow(StationInfoOverlayViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }
}
