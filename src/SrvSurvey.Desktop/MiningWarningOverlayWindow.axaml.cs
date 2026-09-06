using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class MiningWarningOverlayWindow : Window
{
    public MiningWarningOverlayWindow()
        : this(OverlayEditorPreviewFactories.CreateSurfaceMining())
    {
    }

    public MiningWarningOverlayWindow(SurfaceMiningOverlayViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }
}
