using System.ComponentModel;
using System.Runtime.CompilerServices;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class OverlayBehaviorViewModel : INotifyPropertyChanged
{
    private readonly OverlayBehaviorSettingsStore settingsStore;
    private OverlayBehaviorPreferences preferences;
    private static readonly ApplicationMonitorOption AutomaticMonitor = new(null, "Automatic (primary monitor)");
    private IReadOnlyList<ApplicationMonitorOption> monitorOptions = [AutomaticMonitor];
    private OdysseySuitType currentSuit;
    private bool isOnFoot;
    private bool hasStatus;
    private bool hasCommander;
    private bool isShutdown;
    private bool isAtMainMenu;
    private bool isAtCarrierManagement;
    private string settingsStatus = string.Empty;

    public OverlayBehaviorViewModel(OverlayBehaviorSettingsStore settingsStore)
    {
        this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        preferences = settingsStore.Load();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<ApplicationMonitorOption> MonitorOptions => monitorOptions;

    public string? PreferredMonitorId => preferences.PreferredMonitorId;

    public bool LockToMonitor
    {
        get => preferences.LockToMonitor;
        set => Update(preferences with { LockToMonitor = value });
    }

    public ApplicationMonitorOption SelectedMonitor
    {
        get =>
            monitorOptions.FirstOrDefault(option => MonitorIdsEqual(option.Id, PreferredMonitorId)) ?? AutomaticMonitor;
        set
        {
            if (value is not null)
            {
                Update(preferences with { PreferredMonitorId = value.Id });
            }
        }
    }

    public void SetAvailableMonitors(IEnumerable<ApplicationMonitorOption> availableMonitors)
    {
        ArgumentNullException.ThrowIfNull(availableMonitors);
        var options = new List<ApplicationMonitorOption> { AutomaticMonitor };
        options.AddRange(
            availableMonitors
                .Where(option => !string.IsNullOrWhiteSpace(option.Id))
                .DistinctBy(
                    option => option.Id,
                    OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal
                )
        );
        if (PreferredMonitorId is { } id && !options.Any(option => MonitorIdsEqual(option.Id, id)))
        {
            options.Add(new ApplicationMonitorOption(id, $"{id} (not connected; using primary monitor)"));
        }

        monitorOptions = options;
        OnPropertyChanged(nameof(MonitorOptions));
        OnPropertyChanged(nameof(SelectedMonitor));
    }

    private static bool MonitorIdsEqual(string? left, string? right) =>
        string.Equals(
            left,
            right,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal
        );

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
        set => Update(preferences with { HideMultiGameCommanderOverlay = value });
    }

    public bool ShouldSuppressForSuit =>
        isOnFoot
        && (
            currentSuit == OdysseySuitType.Dominator && HideInDominatorSuit
            || currentSuit == OdysseySuitType.Maverick && HideInMaverickSuit
        );

    public bool ShouldSuppressForSession =>
        !hasStatus || !hasCommander || isShutdown || isAtMainMenu || isAtCarrierManagement;

    public string CurrentSuitText =>
        currentSuit switch
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

    public void UpdateSessionContext(
        bool hasCurrentStatus,
        bool hasCurrentCommander,
        bool shutdown,
        bool atMainMenu,
        bool atCarrierManagement = false
    )
    {
        if (
            hasStatus == hasCurrentStatus
            && hasCommander == hasCurrentCommander
            && isShutdown == shutdown
            && isAtMainMenu == atMainMenu
            && isAtCarrierManagement == atCarrierManagement
        )
        {
            return;
        }

        hasStatus = hasCurrentStatus;
        hasCommander = hasCurrentCommander;
        isShutdown = shutdown;
        isAtMainMenu = atMainMenu;
        isAtCarrierManagement = atCarrierManagement;
        OnPropertyChanged(nameof(ShouldSuppressForSession));
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
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SettingsStatus =
                "Overlay behavior changed for this session but " + "could not be saved: " + exception.Message;
        }

        OnPropertyChanged(nameof(KeepWhenGameLosesFocus));
        OnPropertyChanged(nameof(SelectedMonitor));
        OnPropertyChanged(nameof(PreferredMonitorId));
        OnPropertyChanged(nameof(LockToMonitor));
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
