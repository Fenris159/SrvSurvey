using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class MineMapZoomOverlayWindow : Window
{
    public MineMapZoomOverlayWindow() => InitializeComponent();

    public MineMapZoomOverlayWindow(MineMapViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }
}