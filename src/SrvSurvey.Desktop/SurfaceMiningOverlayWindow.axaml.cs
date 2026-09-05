using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class SurfaceMiningOverlayWindow : Window
{
    public SurfaceMiningOverlayWindow()
    {
        InitializeComponent();
    }

    public SurfaceMiningOverlayWindow(
        SurfaceMiningOverlayViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }
}
