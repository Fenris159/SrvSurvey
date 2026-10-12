using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class ExplorationPoiOverlayWindow : Window
{
    /// <summary>Creates the passive overlay window with the supplied POI presentation model.</summary>
    public ExplorationPoiOverlayWindow()
        : this(null) { }

    /// <summary>Creates the passive overlay window with the supplied POI presentation model.</summary>
    public ExplorationPoiOverlayWindow(ExplorationPoiViewModel? viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
