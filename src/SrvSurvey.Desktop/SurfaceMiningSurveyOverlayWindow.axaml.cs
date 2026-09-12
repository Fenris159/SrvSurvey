using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class SurfaceMiningSurveyOverlayWindow : Window
{
    public SurfaceMiningSurveyOverlayWindow()
        : this(null) { }

    public SurfaceMiningSurveyOverlayWindow(MineMapViewModel? viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
