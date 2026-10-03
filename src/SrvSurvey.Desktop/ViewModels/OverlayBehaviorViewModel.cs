using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class OverlayBehaviorViewModel : INotifyPropertyChanged
{
    private readonly OverlayBehaviorSettingsStore settingsStore;
    private OverlayBehaviorPreferences preferences;
    private readonly bool startupBypassWindowManagement;
    private static readonly ApplicationMonitorOption AutomaticMonitor = new(null, "Automatic (primary monitor)");
    private readonly ObservableCollection<ApplicationMonitorOption> monitorOptions = [AutomaticMonitor];
    private bool isRefreshingMonitors;
    private OdysseySuitType currentSuit;
    private bool isOnFoot;
    private bool hasStatus;
    private bool hasCommander;
    private bool isShutdown;
    private bool isAtMainMenu;
    private bool isAtCarrierManagement;
    private string settingsStatus = string.Empty;

    /// <summary>Loads saved preferences and remembers the native-management choice used for this session.</summary>
    public OverlayBehaviorViewModel(OverlayBehaviorSettingsStore settingsStore)
    {
        this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        preferences = settingsStore.Load();
        startupBypassWindowManagement = preferences.BypassWindowManagement;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<ApplicationMonitorOption> MonitorOptions => monitorOptions;

    public string? PreferredMonitorId => preferences.PreferredMonitorId;

    /// <summary>Opts live X11 panels and their position editor into unmanaged placement on the next start.</summary>
    public bool BypassWindowManagement
    {
        get => preferences.BypassWindowManagement;
        set => Update(preferences with { BypassWindowManagement = value });
    }

    /// <summary>Warns only when the saved bypass choice differs from this session's startup choice.</summary>
    public bool WindowManagementRestartRequired => BypassWindowManagement != startupBypassWindowManagement;

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
            if (value is not null && !isRefreshingMonitors)
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

        isRefreshingMonitors = true;
        try
        {
            ReconcileMonitorOptions(options);
        }
        finally
        {
            isRefreshingMonitors = false;
        }

        OnPropertyChanged(nameof(SelectedMonitor));
    }

    private void ReconcileMonitorOptions(List<ApplicationMonitorOption> options)
    {
        foreach (ApplicationMonitorOption option in options.Skip(1))
        {
            AddOrReplaceMonitorOption(option);
        }

        OnPropertyChanged(nameof(SelectedMonitor));

        for (int index = monitorOptions.Count - 1; index > 0; index--)
        {
            if (!options.Contains(monitorOptions[index]))
            {
                monitorOptions.RemoveAt(index);
            }
        }

        for (int index = 1; index < options.Count; index++)
        {
            int currentIndex = monitorOptions.IndexOf(options[index]);
            if (currentIndex != index)
            {
                monitorOptions.Move(currentIndex, index);
            }
        }
    }

    private void AddOrReplaceMonitorOption(ApplicationMonitorOption option)
    {
        if (monitorOptions.Contains(option))
        {
            return;
        }

        int previousIndex = -1;
        for (int index = 1; index < monitorOptions.Count; index++)
        {
            if (MonitorIdsEqual(monitorOptions[index].Id, option.Id))
            {
                previousIndex = index;
                break;
            }
        }

        if (previousIndex >= 0)
        {
            monitorOptions.Insert(previousIndex, option);
        }
        else
        {
            monitorOptions.Add(option);
        }
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

    /// <summary>Saves a preference change and refreshes dependent controls, reporting persistence failures.</summary>
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
        OnPropertyChanged(nameof(BypassWindowManagement));
        OnPropertyChanged(nameof(WindowManagementRestartRequired));
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
