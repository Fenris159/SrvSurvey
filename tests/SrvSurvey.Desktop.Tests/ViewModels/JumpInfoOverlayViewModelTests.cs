using SrvSurvey.Core.Navigation;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class JumpInfoOverlayViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-JumpInfoOverlayViewModel-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ReportsPlatformPreparationAndInputMode()
    {
        using var jumpInfo = new JumpInfoViewModel(
            new EmptySystemSummaryClient(),
            new JumpInfoSettingsStore(
                Path.Combine(temporaryDirectory, "ui-settings.json")));
        var viewModel = new JumpInfoOverlayViewModel(
            jumpInfo,
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

    private sealed class EmptySystemSummaryClient : ISystemSummaryClient
    {
        public Task<SystemSummaryLoadResult> GetAsync(
            string systemName,
            long systemAddress,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("No target is configured.");
        }
    }
}
