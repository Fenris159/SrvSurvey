using System.ComponentModel;
using System.Runtime.CompilerServices;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class OverlayBehaviorViewModel : INotifyPropertyChanged
{
    private readonly OverlayBehaviorSettingsStore settingsStore;
    private OverlayBehaviorPreferences preferences;
    private OdysseySuitType currentSuit;
    private bool isOnFoot;
    private string settingsStatus = string.Empty;

    public OverlayBehaviorViewModel(OverlayBehaviorSettingsStore settingsStore)
    {
        this.settingsStore = settingsStore
            ?? throw new ArgumentNullException(nameof(settingsStore));
        preferences = settingsStore.Load();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool KeepWhenGameLosesFocus
    {
        get => preferences.KeepWhenGameLosesFocus;
        set => Update(preferences with { KeepWhenGameLosesFocus = value });
    }

    public bool HideInDominatorSuit
    {
        get => preferences.HideInDominatorSuit;
        set => Update(preferences with { HideInDominatorSuit = value });
    }

    public bool HideInMaverickSuit
    {
        get => preferences.HideInMaverickSuit;
        set => Update(preferences with { HideInMaverickSuit = value });
    }

    public bool HideMultiGameCommanderOverlay
    {
        get => preferences.HideMultiGameCommanderOverlay;
        set => Update(preferences with
        {
            HideMultiGameCommanderOverlay = value,
        });
    }

    public bool ShouldSuppressForSuit => isOnFoot
        && (currentSuit == OdysseySuitType.Dominator && HideInDominatorSuit
            || currentSuit == OdysseySuitType.Maverick && HideInMaverickSuit);

    public string CurrentSuitText => currentSuit switch
    {
        OdysseySuitType.Flight => "Flight suit",
        OdysseySuitType.Artemis => "Artemis suit",
        OdysseySuitType.Maverick => "Maverick suit",
        OdysseySuitType.Dominator => "Dominator suit",
        _ => "Suit not reported",
    };

    public string SettingsStatus
    {
        get => settingsStatus;
        private set
        {
            if (settingsStatus == value)
            {
                return;
            }

            settingsStatus = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSettingsStatus));
        }
    }

    public bool HasSettingsStatus => !string.IsNullOrWhiteSpace(SettingsStatus);

    public void UpdateContext(OdysseySuitType suit, bool onFoot)
    {
        if (currentSuit == suit && isOnFoot == onFoot)
        {
            return;
        }

        currentSuit = suit;
        isOnFoot = onFoot;
        OnPropertyChanged(nameof(CurrentSuitText));
        OnPropertyChanged(nameof(ShouldSuppressForSuit));
    }

    private void Update(OverlayBehaviorPreferences next)
    {
        if (preferences == next)
        {
            return;
        }

        preferences = next;
        try
        {
            settingsStore.Save(preferences);
            SettingsStatus = string.Empty;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            SettingsStatus = "Overlay behavior changed for this session but "
                + "could not be saved: " + exception.Message;
        }

        OnPropertyChanged(nameof(KeepWhenGameLosesFocus));
        OnPropertyChanged(nameof(HideInDominatorSuit));
        OnPropertyChanged(nameof(HideInMaverickSuit));
        OnPropertyChanged(nameof(HideMultiGameCommanderOverlay));
        OnPropertyChanged(nameof(ShouldSuppressForSuit));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
