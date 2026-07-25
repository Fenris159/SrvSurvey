using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class MassacreMissionsOverlayWindow : Window
{
    public MassacreMissionsOverlayWindow()
    {
        InitializeComponent();
    }

    public MassacreMissionsOverlayWindow(CombatOverlayViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }
}
