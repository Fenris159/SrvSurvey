using Avalonia.Controls;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class MultiGameCommanderOverlayWindow : Window
{
    public MultiGameCommanderOverlayWindow()
    {
        InitializeComponent();
        OverlayThemeResources.Apply(this);
    }

    public MultiGameCommanderOverlayWindow(
        CommanderInstancesViewModel viewModel)
        : this()
    {
        DataContext = viewModel
            ?? throw new ArgumentNullException(nameof(viewModel));
    }
}
