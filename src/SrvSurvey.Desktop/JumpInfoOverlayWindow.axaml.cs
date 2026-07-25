using Avalonia.Controls;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class JumpInfoOverlayWindow : Window
{
    public JumpInfoOverlayWindow()
        : this(CreateDesignViewModel())
    {
    }

    public JumpInfoOverlayWindow(JumpInfoOverlayViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }

    private static JumpInfoOverlayViewModel CreateDesignViewModel()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            "SrvSurvey-JumpInfo-Overlay-Design",
            "ui-settings.json");
        return new JumpInfoOverlayViewModel(
            new JumpInfoViewModel(
                new EmptySystemSummaryClient(),
                new JumpInfoSettingsStore(settingsPath)),
            OverlayPlatformCapabilities.DetectCurrent());
    }

    private sealed class EmptySystemSummaryClient : ISystemSummaryClient
    {
        public Task<SystemSummaryLoadResult> GetAsync(
            string systemName,
            long systemAddress,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new SystemSummaryLoadResult(
                new SystemSummary(
                    systemName,
                    systemAddress,
                    null,
                    null,
                    null,
                    0,
                    0,
                    null,
                    null,
                    null,
                    null,
                    new SystemPoiSummary(0, 0, 0, 0, 0, 0, 0),
                    []),
                []));
        }
    }
}
