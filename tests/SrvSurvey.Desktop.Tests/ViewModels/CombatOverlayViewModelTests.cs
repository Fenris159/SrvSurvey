using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class CombatOverlayViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-combat-overlay-tests-{Guid.NewGuid():N}");

    [Fact]
    public void WrapsCombatStateAndReportsPassivePreparation()
    {
        var combat = new CombatViewModel(
            new CombatSettingsStore(Path.Combine(
                temporaryDirectory,
                "ui-settings.json")),
            new CommanderProfileStore(temporaryDirectory));
        var viewModel = new CombatOverlayViewModel(
            combat,
            OverlayPlatformCapabilities.ForHost(OverlayHostKind.Windows));

        Assert.Same(combat, viewModel.Combat);
        Assert.Equal("PASSIVE", viewModel.InputMode);

        viewModel.ApplyPreparation(new OverlayPreparationResult(
            IsPrepared: true,
            IsClickThrough: false,
            "Click-through was rejected."));

        Assert.Equal("BLOCKED", viewModel.InputMode);
        Assert.Equal("Click-through was rejected.", viewModel.PlatformStatus);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
