using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class BiologyRewardSettingsViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-biology-reward-vm-tests-{Guid.NewGuid():N}");

    [Fact]
    public void EditingThresholdsKeepsBandsOrderedAndPersistsThem()
    {
        var path = Path.Combine(temporaryDirectory, "ui-settings.json");
        var store = new BiologyRewardSettingsStore(path);
        var viewModel = new BiologyRewardSettingsViewModel(store);

        viewModel.BucketOneMillions = 8;
        viewModel.BucketThreeMillions = 6;

        Assert.Equal(new BiologyRewardThresholds(8, 8, 8), viewModel.Thresholds);
        Assert.Equal(viewModel.Thresholds, store.Load());
        Assert.False(viewModel.HasStatusMessage);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
