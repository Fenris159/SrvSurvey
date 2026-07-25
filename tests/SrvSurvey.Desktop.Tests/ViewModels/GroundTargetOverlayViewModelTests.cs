using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class GroundTargetOverlayViewModelTests
{
    [Fact]
    public void WrapsGuidanceAndReportsPassivePreparation()
    {
        var groundTarget = new GroundTargetViewModel(
            new GroundTargetSettingsStore(Path.Combine(
                Path.GetTempPath(),
                $"SrvSurvey-ground-target-overlay-tests-{Guid.NewGuid():N}")));
        var viewModel = new GroundTargetOverlayViewModel(
            groundTarget,
            OverlayPlatformCapabilities.ForHost(OverlayHostKind.Windows));

        Assert.Same(groundTarget, viewModel.GroundTarget);
        Assert.Equal("PASSIVE", viewModel.InputMode);

        viewModel.ApplyPreparation(new OverlayPreparationResult(
            IsPrepared: true,
            IsClickThrough: false,
            "Click-through was rejected."));

        Assert.Equal("BLOCKED", viewModel.InputMode);
        Assert.Equal("Click-through was rejected.", viewModel.PlatformStatus);
    }
}
