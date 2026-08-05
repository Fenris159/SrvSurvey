using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Controls;
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

    [Fact]
    public void SpeciesGroupPreviewRewardsFillOneThroughFourBars()
    {
        var path = Path.Combine(temporaryDirectory, "ui-settings.json");
        var viewModel = new BiologyRewardSettingsViewModel(
            new BiologyRewardSettingsStore(path));
        var thresholds = BiologyRewardThresholds.Default;

        Assert.Equal(
            [
                BiologyRewardBandSegment.Filled,
                BiologyRewardBandSegment.Empty,
                BiologyRewardBandSegment.Empty,
                BiologyRewardBandSegment.Empty,
            ],
            BiologyRewardBandScale.Calculate(
                viewModel.PreviewOneBarReward,
                viewModel.PreviewOneBarReward,
                thresholds).Segments);
        Assert.Equal(
            [
                BiologyRewardBandSegment.Filled,
                BiologyRewardBandSegment.Filled,
                BiologyRewardBandSegment.Empty,
                BiologyRewardBandSegment.Empty,
            ],
            BiologyRewardBandScale.Calculate(
                viewModel.PreviewTwoBarReward,
                viewModel.PreviewTwoBarReward,
                thresholds).Segments);
        Assert.Equal(
            [
                BiologyRewardBandSegment.Filled,
                BiologyRewardBandSegment.Filled,
                BiologyRewardBandSegment.Filled,
                BiologyRewardBandSegment.Empty,
            ],
            BiologyRewardBandScale.Calculate(
                viewModel.PreviewThreeBarReward,
                viewModel.PreviewThreeBarReward,
                thresholds).Segments);
        Assert.Equal(
            [
                BiologyRewardBandSegment.Filled,
                BiologyRewardBandSegment.Filled,
                BiologyRewardBandSegment.Filled,
                BiologyRewardBandSegment.Filled,
            ],
            BiologyRewardBandScale.Calculate(
                viewModel.PreviewFourBarReward,
                viewModel.PreviewFourBarReward,
                thresholds).Segments);
    }

    [Fact]
    public void PreviewRewardsStayStrictlyAboveNearIntegerMillionThresholds()
    {
        var path = Path.Combine(temporaryDirectory, "ui-settings.json");
        var viewModel = new BiologyRewardSettingsViewModel(
            new BiologyRewardSettingsStore(path));

        // Just below the displayed 3 M boundary; truncation alone would under-fill.
        viewModel.BucketOneMillions = 2.999999999d;
        viewModel.BucketTwoMillions = 6.999999999d;
        viewModel.BucketThreeMillions = 11.999999999d;

        var state = BiologyRewardBandScale.Calculate(
            viewModel.PreviewTwoBarReward,
            viewModel.PreviewTwoBarReward,
            viewModel.Thresholds);

        Assert.Equal(BiologyRewardBandSegment.Filled, state.Segments[0]);
        Assert.Equal(BiologyRewardBandSegment.Filled, state.Segments[1]);
        Assert.True(viewModel.PreviewTwoBarReward > 3_000_000);
        Assert.True(viewModel.PreviewThreeBarReward > 7_000_000);
        Assert.True(viewModel.PreviewFourBarReward > 12_000_000);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
