using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class MineMapOverlayWindow : Window
{
    public MineMapOverlayWindow() => InitializeComponent();

    public MineMapOverlayWindow(MineMapViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }
}
