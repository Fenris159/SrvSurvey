using System.ComponentModel;
using System.Runtime.CompilerServices;
using SrvSurvey.Core.Network;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class VoxStellarSharingViewModel : INotifyPropertyChanged
{
    private readonly VoxStellarSettingsStore settingsStore;
    private VoxStellarPreferences preferences;
    private string statusMessage = string.Empty;

    public VoxStellarSharingViewModel(
        VoxStellarSettingsStore settingsStore,
        bool isUploadAvailable)
    {
        this.settingsStore = settingsStore
            ?? throw new ArgumentNullException(nameof(settingsStore));
        IsUploadAvailable = isUploadAvailable;
        preferences = settingsStore.Load();
        if (!IsUploadAvailable)
        {
            statusMessage =
                "VoxStellar upload is unavailable in this build because its integration signing key is not configured.";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event Action<bool>? UploadEnabledChanged;

    public bool IsUploadAvailable { get; }

    public bool CanChangeUploadPreference =>
        IsUploadAvailable || JournalUploadEnabled;

    public bool JournalUploadEnabled
    {
        get => preferences.JournalUploadEnabled;
        set
        {
            if (value && !IsUploadAvailable)
            {
                StatusMessage =
                    "VoxStellar upload cannot be enabled because this build does not include its integration signing key.";
                OnPropertyChanged();
                return;
            }

            if (preferences.JournalUploadEnabled == value)
            {
                return;
            }

            Update(preferences with { JournalUploadEnabled = value });
            OnPropertyChanged(nameof(CanChangeUploadPreference));
            UploadEnabledChanged?.Invoke(value);
        }
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

    public void ReportPublicationResult(VoxStellarPublicationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Warnings.Count > 0)
        {
            StatusMessage = string.Join(Environment.NewLine, result.Warnings);
        }
        else if (result.QueuedEventNames.Count == 1)
        {
            StatusMessage =
                $"Queued {result.QueuedEventNames[0]} for VoxStellar.";
        }
        else if (result.QueuedEventNames.Count > 1)
        {
            StatusMessage =
                $"Queued {result.QueuedEventNames.Count:N0} exploration events for VoxStellar.";
        }
    }

    public void ReportLinkFailure(string message)
    {
        StatusMessage = "Could not open VoxStellar: " + message;
    }

    private void Update(VoxStellarPreferences updated)
    {
        preferences = updated;
        OnPropertyChanged(string.Empty);
        try
        {
            settingsStore.Save(preferences);
            StatusMessage = IsUploadAvailable
                ? string.Empty
                : "VoxStellar upload is unavailable in this build because its integration signing key is not configured.";
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or InvalidOperationException)
        {
            StatusMessage =
                "The VoxStellar preference changed for this session but could not be saved: "
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
