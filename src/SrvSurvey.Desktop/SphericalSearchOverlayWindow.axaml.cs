using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class SphericalSearchOverlayWindow : Window
{
    public SphericalSearchOverlayWindow()
    {
        InitializeComponent();
    }

    public SphericalSearchOverlayWindow(
        SphericalSearchOverlayViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }
}
