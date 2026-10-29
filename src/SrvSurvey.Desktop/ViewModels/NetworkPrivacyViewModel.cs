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
        this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        preferences = settingsStore.Load();
        UploadPrivacy = new JournalUploadPrivacy(settingsStore.UploadPrivacyPath, preferences.StealthModeEnabled);
    }

    public JournalUploadPrivacy UploadPrivacy { get; }

    public bool StealthModeEnabled
    {
        get => preferences.StealthModeEnabled;
        set
        {
            if (Update(preferences with { StealthModeEnabled = value }))
            {
                UploadPrivacy.SetEnabled(value);
            }
        }
    }

    public string StealthStatus =>
        UploadPrivacy.LastError
        ?? (
            StealthModeEnabled
                ? "Uploads are held locally until a live docking. Restarting the application does not release them."
                : "Stealth Mode is off. Enabled sharing services upload normally."
        );

    public void RefreshStealthStatus() => OnPropertyChanged(nameof(StealthStatus));

    public event PropertyChangedEventHandler? PropertyChanged;

    public event Action<bool>? EddnUploadEnabledChanged;

    /// <summary>Notifies the delivery module after a saved EDAstro consent transition.</summary>
    public event Action<bool>? EdAstroUploadEnabledChanged;

    /// <summary>Whether this installation contributes new live journal data to EDAstro.</summary>
    public bool EdAstroUploadEnabled
    {
        get => preferences.EdAstroUploadEnabled;
        set => TrySetEdAstroUploadEnabled(value);
    }

    /// <summary>Privacy card status for the persisted EDAstro choice.</summary>
    public string EdAstroConsentSummary =>
        EdAstroUploadEnabled
            ? "EDAstro sharing is enabled for live Commander sessions."
            : "EDAstro sharing is disabled.";

    /// <summary>Saves EDAstro consent and rolls back the choice if delivery cannot transition.</summary>
    public bool TrySetEdAstroUploadEnabled(bool value) => TrySetSharingEnabled(value, edAstro: true);

    public bool EddnUploadEnabled
    {
        get => preferences.EddnUploadEnabled;
        set => TrySetEddnUploadEnabled(value);
    }

    public string EddnConsentSummary =>
        EddnUploadEnabled ? "EDDN sharing is enabled for live Commander sessions." : "EDDN sharing is disabled.";

    /// <summary>Saves EDDN consent and restores the previous choice if its runtime transition fails.</summary>
    public bool TrySetEddnUploadEnabled(bool value) => TrySetSharingEnabled(value, edAstro: false);

    /// <summary>Commits one provider's sharing choice independently with persistence-first runtime notification.</summary>
    private bool TrySetSharingEnabled(bool value, bool edAstro)
    {
        bool current = edAstro ? preferences.EdAstroUploadEnabled : preferences.EddnUploadEnabled;
        if (current == value)
        {
            return true;
        }
        string provider = edAstro ? "EDAstro" : "EDDN";
        Action<bool> notify = enabled => NotifySharingEnabled(enabled, edAstro);
        NetworkPrivacyPreferences previous = preferences;
        NetworkPrivacyPreferences updated = edAstro
            ? previous with
            {
                EdAstroUploadEnabled = value,
            }
            : previous with
            {
                EddnUploadEnabled = value,
            };
        if (!Update(updated))
        {
            return false;
        }
        try
        {
            notify.Invoke(value);
            return true;
        }
        catch (Exception exception)
        {
            RollBackSharingConsent(previous, current, notify, provider, exception);
            return false;
        }
    }

    /// <summary>Notifies the selected provider after a consent change or rollback.</summary>
    private void NotifySharingEnabled(bool enabled, bool edAstro)
    {
        if (edAstro)
        {
            EdAstroUploadEnabledChanged?.Invoke(enabled);
        }
        else
        {
            EddnUploadEnabledChanged?.Invoke(enabled);
        }
    }

    /// <summary>Restores saved consent and runtime delivery state after a failed transition, reporting rollback errors.</summary>
    private void RollBackSharingConsent(
        NetworkPrivacyPreferences previous,
        bool enabled,
        Action<bool> notify,
        string provider,
        Exception originalException
    )
    {
        preferences = previous;
        List<string> rollbackErrors = [];
        try
        {
            settingsStore.Save(previous);
        }
        catch (Exception exception)
        {
            rollbackErrors.Add("The previous consent could not be saved: " + exception.Message);
        }
        try
        {
            notify.Invoke(enabled);
        }
        catch (Exception exception)
        {
            rollbackErrors.Add($"The {provider} runtime rollback failed: " + exception.Message);
        }
        OnPropertyChanged(string.Empty);
        StatusMessage =
            rollbackErrors.Count == 0
                ? $"The {provider} sharing choice was not changed because the runtime transition failed; the previous choice was restored: "
                    + originalException.Message
                : $"The {provider} sharing choice could not be fully restored after the runtime transition failed: "
                    + originalException.Message
                    + Environment.NewLine
                    + string.Join(Environment.NewLine, rollbackErrors);
    }

    private static bool IsExpectedSettingsException(Exception exception)
    {
        return exception
            is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or InvalidOperationException;
    }

    public bool UploadGreenGasGiantCandidates
    {
        get => preferences.UploadGreenGasGiantCandidates;
        set => Update(preferences with { UploadGreenGasGiantCandidates = value });
    }

    public bool UploadHumanSettlementGeometry
    {
        get => preferences.UploadHumanSettlementGeometry;
        set => Update(preferences with { UploadHumanSettlementGeometry = value });
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

    public void ReportPublicationResult(GreenGasGiantPublicationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Warnings.Count > 0)
        {
            StatusMessage = string.Join(Environment.NewLine, result.Warnings);
        }
        else if (result.Published.Count == 1)
        {
            StatusMessage = $"Uploaded a {result.Published[0].Tag} Green Gas Giant candidate.";
        }
        else if (result.Published.Count > 1)
        {
            StatusMessage = $"Uploaded {result.Published.Count:N0} Green Gas Giant candidates.";
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
            StatusMessage = $"Queued {result.Published[0].EventName} for EDDN.";
        }
        else if (result.Published.Count > 1)
        {
            StatusMessage = $"Queued {result.Published.Count:N0} journal events for EDDN.";
        }
    }

    public void ReportPublicationResult(CanonnHumanSitePublicationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!string.IsNullOrWhiteSpace(result.Warning))
        {
            StatusMessage = result.Warning;
        }
        else if (result.Published is { } published)
        {
            StatusMessage = $"Uploaded settlement geometry for {published.Name} to Canonn.";
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
        catch (Exception exception) when (IsExpectedSettingsException(exception))
        {
            StatusMessage =
                "The privacy preference was not changed because it could not be saved: " + exception.Message;
            return false;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
