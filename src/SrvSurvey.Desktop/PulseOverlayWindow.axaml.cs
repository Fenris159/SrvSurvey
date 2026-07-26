using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class PulseOverlayWindow : Window
{
    public PulseOverlayWindow()
        : this(null)
    {
    }

    public PulseOverlayWindow(PulseOverlayViewModel? viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
