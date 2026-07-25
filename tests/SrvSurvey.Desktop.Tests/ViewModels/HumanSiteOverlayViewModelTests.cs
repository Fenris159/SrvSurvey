using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class HumanSiteOverlayViewModelTests
{
    [Fact]
    public void PreparationReportsPassiveOrBlockedInputMode()
    {
        var viewModel = new HumanSiteOverlayViewModel(
            new HumanSiteViewModel(),
            OverlayPlatformCapabilities.ForHost(OverlayHostKind.Windows));

        Assert.Equal("PASSIVE", viewModel.InputMode);
        viewModel.ApplyPreparation(new OverlayPreparationResult(
            IsPrepared: false,
            IsClickThrough: false,
            "Blocked"));

        Assert.Equal("BLOCKED", viewModel.InputMode);
        Assert.Equal("Blocked", viewModel.PlatformStatus);
    }
}
