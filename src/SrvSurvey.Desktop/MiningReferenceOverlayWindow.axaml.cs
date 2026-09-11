using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class MiningReferenceOverlayWindow : Window
{
    public MiningReferenceOverlayWindow() => InitializeComponent();

    public MiningReferenceOverlayWindow(MineMapViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }
}
