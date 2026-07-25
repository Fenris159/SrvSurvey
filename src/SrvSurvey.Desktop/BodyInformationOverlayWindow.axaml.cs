using Avalonia.Controls;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class BodyInformationOverlayWindow : Window
{
    public BodyInformationOverlayWindow()
        : this(CreateDesignViewModel())
    {
    }

    public BodyInformationOverlayWindow(SystemSurveyOverlayViewModel viewModel)
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
