using System.ComponentModel;
using System.Runtime.CompilerServices;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class DesktopBehaviorViewModel : INotifyPropertyChanged
{
    private readonly DesktopBehaviorSettingsStore settingsStore;
    private readonly IGameWindowSwitcher gameWindowSwitcher;
    private DesktopBehaviorPreferences preferences;
    private IReadOnlyList<ApplicationMonitorOption> monitorOptions =
        [ApplicationMonitorOption.Automatic];
    private string statusMessage = string.Empty;

    public DesktopBehaviorViewModel(
        DesktopBehaviorSettingsStore settingsStore,
        IGameWindowSwitcher gameWindowSwitcher)
    {
        this.settingsStore = settingsStore
            ?? throw new ArgumentNullException(nameof(settingsStore));
        this.gameWindowSwitcher = gameWindowSwitcher
            ?? throw new ArgumentNullException(nameof(gameWindowSwitcher));
        preferences = settingsStore.Load();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? ApplicationWindowPreferencesChanged;

    public bool FocusGameOnStart
    {
        get => preferences.FocusGameOnStart;
        set => Update(preferences with { FocusGameOnStart = value });
    }

    public bool FocusGameOnMinimize
    {
        get => preferences.FocusGameOnMinimize;
        set => Update(preferences with { FocusGameOnMinimize = value });
    }

    public bool FocusGameAfterFsdJump
    {
        get => preferences.FocusGameAfterFsdJump;
        set => Update(preferences with { FocusGameAfterFsdJump = value });
    }

    public bool MinimizeToTray
    {
        get => preferences.MinimizeToTray;
        set => Update(preferences with { MinimizeToTray = value });
    }

    public bool ReduceMotion
    {
        get => preferences.ReduceMotion;
        set => Update(preferences with { ReduceMotion = value });
    }

    public IReadOnlyList<ApplicationMonitorOption> MonitorOptions =>
        monitorOptions;

    public ApplicationMonitorOption SelectedMonitor
    {
        get => monitorOptions.FirstOrDefault(option => string.Equals(
            option.Id,
            preferences.PreferredMonitorId,
            MonitorIdComparison))
            ?? ApplicationMonitorOption.Automatic;
        set
        {
            if (value is not null)
            {
                Update(preferences with { PreferredMonitorId = value.Id });
            }
        }
    }

    public IReadOnlyList<ApplicationWindowScaleOption>
        ApplicationWindowScaleOptions => ApplicationWindowScaleCatalog.All;

    public ApplicationWindowScaleOption SelectedApplicationWindowScale
    {
        get => ApplicationWindowScaleCatalog.All.First(option =>
            option.Percent == preferences.ApplicationWindowScalePercent);
        set
        {
            if (value is not null)
            {
                Update(preferences with
                {
                    ApplicationWindowScalePercent = value.Percent,
                });
            }
        }
    }

    public string? PreferredMonitorId => preferences.PreferredMonitorId;

    public int ApplicationWindowScalePercent =>
        preferences.ApplicationWindowScalePercent;

    public ApplicationWindowPosition? LastApplicationWindowPosition =>
        preferences.LastApplicationWindowPosition;

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

    public void SetAvailableMonitors(
        IEnumerable<ApplicationMonitorOption> availableMonitors)
    {
        ArgumentNullException.ThrowIfNull(availableMonitors);
        var options = new List<ApplicationMonitorOption>
        {
            ApplicationMonitorOption.Automatic,
        };
        options.AddRange(availableMonitors
            .Where(option => !string.IsNullOrWhiteSpace(option.Id))
            .DistinctBy(option => option.Id, MonitorIdComparer));

        var preferredMonitorId = preferences.PreferredMonitorId;
        if (preferredMonitorId is not null
            && !options.Any(option => string.Equals(
                option.Id,
                preferredMonitorId,
                MonitorIdComparison)))
        {
            options.Add(new ApplicationMonitorOption(
                preferredMonitorId,
                $"{preferredMonitorId} (not connected; using primary monitor)"));
        }

        monitorOptions = options;
        OnPropertyChanged(nameof(MonitorOptions));
        OnPropertyChanged(nameof(SelectedMonitor));
    }

    public void RememberApplicationWindowPosition(
        ApplicationWindowPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);
        Update(preferences with { LastApplicationWindowPosition = position });
    }

    public void ReportTrayUnavailable(string reason)
    {
        StatusMessage = "The system tray is unavailable; minimize-to-tray will "
            + "leave SrvSurvey in the taskbar. " + reason;
    }

    public bool RequestStartupFocus()
    {
        return !FocusGameOnStart || TryFocusGame("application startup");
    }

    public bool RequestMinimizeFocus()
    {
        return !FocusGameOnMinimize || TryFocusGame("application minimize");
    }

    public void ApplyJournalEvents(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        bool isBootstrapRead)
    {
        if (isBootstrapRead
            || !FocusGameAfterFsdJump
            || !journalEvents.Any(journalEvent =>
                journalEvent.EventName == "FSDJump"))
        {
            return;
        }

        _ = TryFocusGame("FSD jump completion");
    }

    private bool TryFocusGame(string reason)
    {
        var focused = gameWindowSwitcher.TryActivateCurrent();
        StatusMessage = focused
            ? string.Empty
            : "Elite Dangerous could not be focused after " + reason
                + "; no matching game window was available.";
        return focused;
    }

    private void Update(DesktopBehaviorPreferences next)
    {
        if (preferences == next)
        {
            return;
        }

        var applicationWindowPreferencesChanged =
            !string.Equals(
                preferences.PreferredMonitorId,
                next.PreferredMonitorId,
                MonitorIdComparison)
            || preferences.ApplicationWindowScalePercent
                != next.ApplicationWindowScalePercent;
        preferences = next;
        try
        {
            settingsStore.Save(preferences);
            StatusMessage = string.Empty;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            StatusMessage =
                "Desktop behavior changed for this session but could not be saved: "
                + exception.Message;
        }

        OnPropertyChanged(nameof(FocusGameOnStart));
        OnPropertyChanged(nameof(FocusGameOnMinimize));
        OnPropertyChanged(nameof(FocusGameAfterFsdJump));
        OnPropertyChanged(nameof(MinimizeToTray));
        OnPropertyChanged(nameof(ReduceMotion));
        OnPropertyChanged(nameof(SelectedMonitor));
        OnPropertyChanged(nameof(PreferredMonitorId));
        OnPropertyChanged(nameof(SelectedApplicationWindowScale));
        OnPropertyChanged(nameof(ApplicationWindowScalePercent));
        OnPropertyChanged(nameof(LastApplicationWindowPosition));
        if (applicationWindowPreferencesChanged)
        {
            ApplicationWindowPreferencesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static StringComparison MonitorIdComparison =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static StringComparer MonitorIdComparer =>
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record ApplicationMonitorOption(string? Id, string DisplayName)
{
    public static ApplicationMonitorOption Automatic { get; } = new(
        null,
        "Automatic (operating system default)");

    public override string ToString() => DisplayName;
}
