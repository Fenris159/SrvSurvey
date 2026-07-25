using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class PriorScansOverlayWindow : Window
{
    public PriorScansOverlayWindow()
    {
        InitializeComponent();
    }

    public PriorScansOverlayWindow(PriorScansOverlayViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }
}
