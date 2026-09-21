using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class MiningCargoOverlayWindow : Window
{
    public MiningCargoOverlayWindow()
        : this(new MiningCargoOverlayViewModel(null)) { }

    public MiningCargoOverlayWindow(MiningCargoOverlayViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }
}
