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

    /// <summary>Sample reward just above bucket one for the species-group preview bar.</summary>
    public long BucketOneSampleReward =>
        ToSampleReward(thresholds.BucketOneMillions);

    /// <summary>Sample reward just above bucket two for the species-group preview bar.</summary>
    public long BucketTwoSampleReward =>
        ToSampleReward(thresholds.BucketTwoMillions);

    /// <summary>Sample reward just above bucket three for the species-group preview bar.</summary>
    public long BucketThreeSampleReward =>
        ToSampleReward(thresholds.BucketThreeMillions);

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
        OnPropertyChanged(nameof(BucketOneSampleReward));
        OnPropertyChanged(nameof(BucketTwoSampleReward));
        OnPropertyChanged(nameof(BucketThreeSampleReward));
        OnPropertyChanged(nameof(Thresholds));
    }

    private static long ToSampleReward(double millions) =>
        Math.Max(1, (long)(millions * 1_000_000d) + 1);

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
