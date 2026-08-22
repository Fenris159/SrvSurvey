using System.ComponentModel;
using System.Runtime.CompilerServices;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Network;
using SrvSurvey.Core.Settlements;
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

    public event Action<bool>? EddnUploadEnabledChanged;

    public bool EddnUploadEnabled
    {
        get => preferences.EddnUploadEnabled;
        set => TrySetEddnUploadEnabled(value);
    }

    public string EddnConsentSummary => EddnUploadEnabled
        ? "EDDN sharing is enabled for live Commander sessions."
        : "EDDN sharing is disabled.";

    public bool TrySetEddnUploadEnabled(bool value)
    {
        if (preferences.EddnUploadEnabled == value)
        {
            return true;
        }

        if (Update(preferences with { EddnUploadEnabled = value }))
        {
            EddnUploadEnabledChanged?.Invoke(value);
            return true;
        }

        return false;
    }

    public bool UploadGreenGasGiantCandidates
    {
        get => preferences.UploadGreenGasGiantCandidates;
        set => Update(preferences with
        {
            UploadGreenGasGiantCandidates = value,
        });
    }

    public bool UploadHumanSettlementGeometry
    {
        get => preferences.UploadHumanSettlementGeometry;
        set => Update(preferences with
        {
            UploadHumanSettlementGeometry = value,
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

    public void ReportPublicationResult(
        GreenGasGiantPublicationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Warnings.Count > 0)
        {
            StatusMessage = string.Join(Environment.NewLine, result.Warnings);
        }
        else if (result.Published.Count == 1)
        {
            StatusMessage =
                $"Uploaded a {result.Published[0].Tag} Green Gas Giant candidate.";
        }
        else if (result.Published.Count > 1)
        {
            StatusMessage =
                $"Uploaded {result.Published.Count:N0} Green Gas Giant candidates.";
        }
    }

    public void ReportPublicationResult(EddnPublicationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Warnings.Count > 0)
        {
            StatusMessage = string.Join(Environment.NewLine, result.Warnings);
        }
        else if (result.Published.Count == 1)
        {
            StatusMessage =
                $"Queued {result.Published[0].EventName} for EDDN (test schemas).";
        }
        else if (result.Published.Count > 1)
        {
            StatusMessage =
                $"Queued {result.Published.Count:N0} journal events for EDDN "
                + "(test schemas).";
        }
    }

    public void ReportPublicationResult(
        CanonnHumanSitePublicationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!string.IsNullOrWhiteSpace(result.Warning))
        {
            StatusMessage = result.Warning;
        }
        else if (result.Published is { } published)
        {
            StatusMessage =
                $"Uploaded settlement geometry for {published.Name} to Canonn.";
        }
    }

    private bool Update(NetworkPrivacyPreferences updated)
    {
        if (preferences == updated)
        {
            return true;
        }

        try
        {
            settingsStore.Save(updated);
            preferences = updated;
            OnPropertyChanged(string.Empty);
            StatusMessage = string.Empty;
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or InvalidOperationException)
        {
            StatusMessage =
                "The privacy preference was not changed because it could not be saved: "
                + exception.Message;
            return false;
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
