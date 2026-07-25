using System.ComponentModel;
using System.Runtime.CompilerServices;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class NetworkPrivacyViewModel : INotifyPropertyChanged
{
    private readonly NetworkPrivacySettingsStore settingsStore;
    private NetworkPrivacyPreferences preferences;
    private string statusMessage = string.Empty;

    public NetworkPrivacyViewModel(NetworkPrivacySettingsStore settingsStore)
    {
        this.settingsStore = settingsStore
            ?? throw new ArgumentNullException(nameof(settingsStore));
        preferences = settingsStore.Load();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<string> EddnEnvironments { get; } =
        ["dev", "beta", "live"];

    public bool EddnUploadEnabled
    {
        get => preferences.EddnUploadEnabled;
        set => Update(preferences with { EddnUploadEnabled = value });
    }

    public string EddnEnvironment
    {
        get => preferences.EddnEnvironment;
        set => Update(preferences with
        {
            EddnEnvironment =
                NetworkPrivacySettingsStore.NormalizeEnvironment(value),
        });
    }

    public bool UploadGreenGasGiantCandidates
    {
        get => preferences.UploadGreenGasGiantCandidates;
        set => Update(preferences with
        {
            UploadGreenGasGiantCandidates = value,
        });
    }

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

    private void Update(NetworkPrivacyPreferences updated)
    {
        if (preferences == updated)
        {
            return;
        }

        preferences = updated;
        OnPropertyChanged(string.Empty);
        try
        {
            settingsStore.Save(preferences);
            StatusMessage = string.Empty;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or InvalidOperationException)
        {
            StatusMessage =
                "The privacy preference changed for this session but could not be saved: "
                + exception.Message;
        }
    }

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}
