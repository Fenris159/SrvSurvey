using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class MiningSessionOverlayWindow : Window
{
    public MiningSessionOverlayWindow()
        : this(new MiningSessionOverlayViewModel(null)) { }

    public MiningSessionOverlayWindow(MiningSessionOverlayViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
