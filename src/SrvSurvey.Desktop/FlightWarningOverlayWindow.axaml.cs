using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class FlightWarningOverlayWindow : Window
{
    public FlightWarningOverlayWindow()
    {
        InitializeComponent();
    }

    public FlightWarningOverlayWindow(SystemSurveyOverlayViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }
}
