using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class FootCombatOverlayWindow : Window
{
    public FootCombatOverlayWindow()
    {
        InitializeComponent();
    }

    public FootCombatOverlayWindow(CombatOverlayViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }
}
