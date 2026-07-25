using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class SystemSurveyOverlayViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-SystemSurveyOverlayViewModel-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ReportsPlatformPreparationAndInputMode()
    {
        var survey = new SystemSurveyViewModel(
            new SystemSurveySettingsStore(
                Path.Combine(temporaryDirectory, "ui-settings.json")));
        var viewModel = new SystemSurveyOverlayViewModel(
            survey,
            OverlayPlatformCapabilities.ForHost(OverlayHostKind.Windows));

        Assert.Equal("PASSIVE", viewModel.InputMode);

        viewModel.ApplyPreparation(new OverlayPreparationResult(
            IsPrepared: true,
            IsClickThrough: false,
            "Click-through failed."));

        Assert.Equal("BLOCKED", viewModel.InputMode);
        Assert.Equal("Click-through failed.", viewModel.PlatformStatus);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
