using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class GuardianZoomOverlayWindow : Window
{
    public GuardianZoomOverlayWindow()
        : this(new GuardianZoomOverlayViewModel(_ => { }))
    {
    }

    public GuardianZoomOverlayWindow(GuardianZoomOverlayViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }
}
