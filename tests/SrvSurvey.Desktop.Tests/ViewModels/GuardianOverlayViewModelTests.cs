using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class GuardianOverlayViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-overlay-vm-tests-{Guid.NewGuid():N}");

    [Fact]
    public void PreparationMakesActualInputModeVisible()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var viewModel = new GuardianOverlayViewModel(
            new GuardianViewModel(temporaryDirectory),
            OverlayPlatformCapabilities.ForHost(OverlayHostKind.Windows));

        Assert.Equal("PASS-THROUGH UNAVAILABLE", viewModel.InputMode);

        viewModel.ApplyPreparation(new OverlayPreparationResult(
            IsPrepared: true,
            IsClickThrough: true,
            "Click-through enabled."));

        Assert.True(viewModel.IsClickThrough);
        Assert.Equal("CLICK-THROUGH", viewModel.InputMode);
        Assert.Equal("Click-through enabled.", viewModel.PlatformStatus);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
