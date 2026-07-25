using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class HumanSiteOverlayWindow : Window
{
    public HumanSiteOverlayWindow()
        : this(new HumanSiteOverlayViewModel(
            new HumanSiteViewModel(),
            Platform.Overlay.OverlayPlatformCapabilities.DetectCurrent()))
    {
    }

    public HumanSiteOverlayWindow(HumanSiteOverlayViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }
}
