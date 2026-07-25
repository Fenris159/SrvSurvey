using Avalonia.Controls;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class BiologyStatusOverlayWindow : Window
{
    public BiologyStatusOverlayWindow()
        : this(CreateDesignViewModel())
    {
    }

    public BiologyStatusOverlayWindow(SystemSurveyOverlayViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }

    private static SystemSurveyOverlayViewModel CreateDesignViewModel()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            "SrvSurvey-BiologyStatus-Overlay-Design",
            "ui-settings.json");
        return new SystemSurveyOverlayViewModel(
            new SystemSurveyViewModel(
                new SystemSurveySettingsStore(settingsPath)),
            OverlayPlatformCapabilities.DetectCurrent());
    }
}
