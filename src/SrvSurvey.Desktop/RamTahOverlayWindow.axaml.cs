using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class RamTahOverlayWindow : Window
{
    public RamTahOverlayWindow()
        : this(new GuardianOverlayViewModel(
            new GuardianViewModel(Path.GetTempPath()),
            Platform.Overlay.OverlayPlatformCapabilities.DetectCurrent()))
    {
    }

    public RamTahOverlayWindow(GuardianOverlayViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }
}
