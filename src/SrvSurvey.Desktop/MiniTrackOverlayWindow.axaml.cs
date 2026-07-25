using Avalonia.Controls;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class MiniTrackOverlayWindow : Window
{
    public MiniTrackOverlayWindow()
        : this(CreateDesignViewModel())
    {
    }

    public MiniTrackOverlayWindow(SurfaceSurveyOverlayViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }

    private static SurfaceSurveyOverlayViewModel CreateDesignViewModel()
    {
        var root = Path.GetTempPath();
        var survey = new SystemSurveyViewModel(
            new SystemSurveySettingsStore(Path.Combine(root, "ui-settings.json")));
        var store = new SystemSurfaceStore(root);
        return new SurfaceSurveyOverlayViewModel(
            new SurfaceSurveyViewModel(
                survey,
                store,
                new SurfaceSurveyJournalTracker(
                    store,
                    ExobiologyReferenceCatalog.LoadEmbedded())),
            Platform.Overlay.OverlayPlatformCapabilities.DetectCurrent());
    }
}
