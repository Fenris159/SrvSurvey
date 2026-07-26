using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class GalaxyMapOverlayWindow : Window
{
    public GalaxyMapOverlayWindow()
        : this(null)
    {
    }

    public GalaxyMapOverlayWindow(GalaxyMapOverlayViewModel? viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
