using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class SurfaceSurveyOverlayWindow : Window
{
    public SurfaceSurveyOverlayWindow()
    {
        InitializeComponent();
    }

    public SurfaceSurveyOverlayWindow(
        SurfaceSurveyOverlayViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }
}
