using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class GuardianStatusOverlayWindow : Window
{
    public GuardianStatusOverlayWindow()
        : this(new GuardianOverlayViewModel(
            new GuardianViewModel(Path.GetTempPath()),
            Platform.Overlay.OverlayPlatformCapabilities.DetectCurrent()))
    {
    }

    public GuardianStatusOverlayWindow(GuardianOverlayViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }
}
