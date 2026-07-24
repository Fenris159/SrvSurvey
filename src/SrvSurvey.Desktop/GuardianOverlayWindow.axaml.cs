using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class GuardianOverlayWindow : Window
{
    public GuardianOverlayWindow()
        : this(new GuardianOverlayViewModel(
            new GuardianViewModel(Path.GetTempPath()),
            Platform.Overlay.OverlayPlatformCapabilities.DetectCurrent()))
    {
    }

    public GuardianOverlayWindow(GuardianOverlayViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }
}
