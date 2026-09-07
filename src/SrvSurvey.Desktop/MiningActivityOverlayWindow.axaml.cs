using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class MiningActivityOverlayWindow : Window
{
    public MiningActivityOverlayWindow() : this(new MiningActivityOverlayViewModel(null, false)) { }
    public MiningActivityOverlayWindow(MiningActivityOverlayViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Title = viewModel.IsFiregroups ? "SrvSurvey Firegroups overlay" : "SrvSurvey mining notifications overlay";
    }
}
