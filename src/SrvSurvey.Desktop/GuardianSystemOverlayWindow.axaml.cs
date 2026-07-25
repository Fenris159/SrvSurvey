using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class GuardianSystemOverlayWindow : Window
{
    public GuardianSystemOverlayWindow()
        : this(new GuardianOverlayViewModel(
            new GuardianViewModel(Path.GetTempPath()),
            Platform.Overlay.OverlayPlatformCapabilities.DetectCurrent()))
    {
    }

    public GuardianSystemOverlayWindow(GuardianOverlayViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }
}
