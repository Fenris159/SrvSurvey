using System.ComponentModel;
using System.Runtime.CompilerServices;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class BiologyRewardSettingsViewModel : INotifyPropertyChanged
{
    private readonly BiologyRewardSettingsStore settingsStore;
    private BiologyRewardThresholds thresholds;
    private string statusMessage = string.Empty;

    public BiologyRewardSettingsViewModel(BiologyRewardSettingsStore settingsStore)
    {
        this.settingsStore = settingsStore
            ?? throw new ArgumentNullException(nameof(settingsStore));
        thresholds = settingsStore.Load();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public double BucketOneMillions
    {
        get => thresholds.BucketOneMillions;
        set => Update(BiologyRewardThresholds.Normalize(
            value,
            thresholds.BucketTwoMillions,
            thresholds.BucketThreeMillions));
    }

    public double BucketTwoMillions
    {
        get => thresholds.BucketTwoMillions;
        set => Update(BiologyRewardThresholds.Normalize(
            thresholds.BucketOneMillions,
            value,
            thresholds.BucketThreeMillions));
    }

    public double BucketThreeMillions
    {
        get => thresholds.BucketThreeMillions;
        set => Update(BiologyRewardThresholds.Normalize(
            thresholds.BucketOneMillions,
            thresholds.BucketTwoMillions,
            value));
    }

    public BiologyRewardThresholds Thresholds => thresholds;

    /// <summary>
    /// Species-group illustration rewards matching upstream FormSettings picBucket paint:
    /// 1 bar, 2 bars, 3 bars, then all 4 bars.
    /// </summary>
    public long PreviewOneBarReward => 1;

    /// <summary>Just above bucket one so only the bottom two segments fill.</summary>
    public long PreviewTwoBarReward =>
        ToCredits(thresholds.BucketOneMillions) + 1;

    /// <summary>Just above bucket two so three segments fill.</summary>
    public long PreviewThreeBarReward =>
        ToCredits(thresholds.BucketTwoMillions) + 1;

    /// <summary>Just above bucket three so all four segments fill.</summary>
    public long PreviewFourBarReward =>
        ToCredits(thresholds.BucketThreeMillions) + 1;

    /// <summary>Legacy alias used by earlier preview bindings.</summary>
    public long BucketOneSampleReward => PreviewTwoBarReward;

    /// <summary>Legacy alias used by earlier preview bindings.</summary>
    public long BucketTwoSampleReward => PreviewThreeBarReward;

    /// <summary>Legacy alias used by earlier preview bindings.</summary>
    public long BucketThreeSampleReward => PreviewFourBarReward;

    public string StatusMessage
    {
        get => statusMessage;
        private set
        {
            if (statusMessage == value)
            {
                return;
            }

            statusMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasStatusMessage));
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    private void Update(BiologyRewardThresholds next)
    {
        if (thresholds == next)
        {
            return;
        }

        thresholds = next;
        try
        {
            settingsStore.Save(thresholds);
            StatusMessage = string.Empty;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            StatusMessage =
                "Reward bands changed for this session but could not be saved: "
                + exception.Message;
        }

        OnPropertyChanged(nameof(BucketOneMillions));
        OnPropertyChanged(nameof(BucketTwoMillions));
        OnPropertyChanged(nameof(BucketThreeMillions));
        OnPropertyChanged(nameof(PreviewTwoBarReward));
        OnPropertyChanged(nameof(PreviewThreeBarReward));
        OnPropertyChanged(nameof(PreviewFourBarReward));
        OnPropertyChanged(nameof(BucketOneSampleReward));
        OnPropertyChanged(nameof(BucketTwoSampleReward));
        OnPropertyChanged(nameof(BucketThreeSampleReward));
        OnPropertyChanged(nameof(Thresholds));
    }

    private static long ToCredits(double millions) =>
        Math.Max(0, (long)(millions * 1_000_000d));

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
