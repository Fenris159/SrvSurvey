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
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
