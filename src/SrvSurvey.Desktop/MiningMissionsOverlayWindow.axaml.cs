using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class MiningMissionsOverlayWindow : Window
{
    public MiningMissionsOverlayWindow()
        : this(new MiningMissionsOverlayViewModel(null)) { }

    public MiningMissionsOverlayWindow(MiningMissionsOverlayViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
