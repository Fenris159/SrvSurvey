using Avalonia.Controls;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class LastFssBodyOverlayWindow : Window
{
    public LastFssBodyOverlayWindow()
        : this(CreateDesignViewModel())
    {
    }

    public LastFssBodyOverlayWindow(SystemSurveyOverlayViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }

    private static SystemSurveyOverlayViewModel CreateDesignViewModel()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            "SrvSurvey-SystemSurvey-Overlay-Design",
            "ui-settings.json");
        return new SystemSurveyOverlayViewModel(
            new SystemSurveyViewModel(
                new SystemSurveySettingsStore(settingsPath)),
            OverlayPlatformCapabilities.DetectCurrent());
    }
}
