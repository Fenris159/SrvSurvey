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

    public IReadOnlyList<string> EddnEnvironments { get; } =
        ["live", "beta", "dev"];

    public bool EddnUploadEnabled
    {
        get => preferences.EddnUploadEnabled;
        set
        {
            if (preferences.EddnUploadEnabled == value)
            {
                return;
            }

            Update(preferences with { EddnUploadEnabled = value });
            EddnUploadEnabledChanged?.Invoke(value);
        }
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
                $"Queued {result.Published[0].EventName} for EDDN ({result.Published[0].Environment}).";
        }
        else if (result.Published.Count > 1)
        {
            StatusMessage =
                $"Queued {result.Published.Count:N0} journal events for EDDN "
                + $"({result.Published[0].Environment}).";
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
