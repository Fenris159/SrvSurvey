using Avalonia.Controls;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class SystemStatusOverlayWindow : Window
{
    public SystemStatusOverlayWindow()
        : this(CreateDesignViewModel()) { }

    public SystemStatusOverlayWindow(SystemSurveyOverlayViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }

    private static SystemSurveyOverlayViewModel CreateDesignViewModel()
    {
        string settingsPath = Path.Combine(
            Path.GetTempPath(),
            "SrvSurvey-SystemStatus-Overlay-Design",
            "ui-settings.json"
        );
        return new SystemSurveyOverlayViewModel(
            new SystemSurveyViewModel(new SystemSurveySettingsStore(settingsPath)),
            OverlayPlatformCapabilities.DetectCurrent()
        );
    }
}
