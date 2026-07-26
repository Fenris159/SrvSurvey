using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class OverlayBehaviorViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-overlay-behavior-vm-tests-{Guid.NewGuid():N}");

    [Fact]
    public void SuitSuppressionRequiresOnFootAndTheMatchingPreference()
    {
        var viewModel = CreateViewModel();
        viewModel.HideInDominatorSuit = true;

        viewModel.UpdateContext(OdysseySuitType.Dominator, onFoot: false);
        Assert.False(viewModel.ShouldSuppressForSuit);

        viewModel.UpdateContext(OdysseySuitType.Dominator, onFoot: true);
        Assert.True(viewModel.ShouldSuppressForSuit);
        Assert.Equal("Dominator suit", viewModel.CurrentSuitText);

        viewModel.UpdateContext(OdysseySuitType.Maverick, onFoot: true);
        Assert.False(viewModel.ShouldSuppressForSuit);
        viewModel.HideInMaverickSuit = true;
        Assert.True(viewModel.ShouldSuppressForSuit);
    }

    [Fact]
    public void FocusPreferencePersists()
    {
        var viewModel = CreateViewModel();

        viewModel.KeepWhenGameLosesFocus = true;

        Assert.True(new OverlayBehaviorSettingsStore(Path.Combine(
            temporaryDirectory,
            "ui-settings.json")).Load().KeepWhenGameLosesFocus);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private OverlayBehaviorViewModel CreateViewModel()
    {
        Directory.CreateDirectory(temporaryDirectory);
        return new OverlayBehaviorViewModel(
            new OverlayBehaviorSettingsStore(Path.Combine(
                temporaryDirectory,
                "ui-settings.json")));
    }
}
