using System.ComponentModel;
using System.Runtime.CompilerServices;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class PulseOverlayViewModel : INotifyPropertyChanged
{
    private static readonly TimeSpan PulseDuration = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ScoCooldownDuration = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ScoReadyThreshold = TimeSpan.FromSeconds(9);
    private readonly PulseOverlaySettingsStore settingsStore;
    private readonly TimeProvider timeProvider;
    private PulseOverlayPreferences preferences;
    private DateTimeOffset? pulseExpiresAtUtc;
    private DateTimeOffset? scoStoppedAtUtc;
    private bool supercruiseOverdrive;
    private EliteStatus? status;
    private string? musicTrack;
    private string settingsStatus = string.Empty;

    public PulseOverlayViewModel(
        PulseOverlaySettingsStore settingsStore,
        TimeProvider? timeProvider = null)
    {
        this.settingsStore = settingsStore
            ?? throw new ArgumentNullException(nameof(settingsStore));
        this.timeProvider = timeProvider ?? TimeProvider.System;
        preferences = settingsStore.Load();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool Enabled
    {
        get => preferences.Enabled;
        set
        {
            if (value == preferences.Enabled)
            {
                return;
            }

            preferences = preferences with { Enabled = value };
            SavePreferences();
            OnPropertyChanged();
            OnPropertyChanged(nameof(HideJournalWriteTimer));
            OnPropertyChanged(nameof(ShouldShow));
        }
    }

    /// <summary>
    /// Upstream FormSettings polarity for hideJournalWriteTimer: checked hides the entire
    /// PlotPulse overlay (journal pulse and SCO indicator). There is no separate timer-only flag.
    /// </summary>
    public bool HideJournalWriteTimer
    {
        get => !Enabled;
        set => Enabled = !value;
    }

    public bool ShouldShow
    {
        get
        {
            var mode = OverlayGameModeResolver.Resolve(
                status,
                musicTrack: musicTrack);
            return Enabled
                && mode is not OverlayGameMode.GalaxyMap
                    and not OverlayGameMode.SystemMap;
        }
    }

    public double PulseHeight
    {
        get
        {
            if (pulseExpiresAtUtc is not { } expires)
            {
                return 0;
            }

            var remaining = (expires - timeProvider.GetUtcNow()).TotalSeconds;
            return Math.Clamp(
                remaining / PulseDuration.TotalSeconds * 20,
                0,
                20);
        }
    }

    public bool IsScoActive => supercruiseOverdrive;

    public bool IsScoCoolingDown => !supercruiseOverdrive
        && GetScoElapsed() is { } elapsed
        && elapsed < ScoReadyThreshold;

    public bool IsScoReady => !supercruiseOverdrive
        && GetScoElapsed() is { } elapsed
        && elapsed >= ScoReadyThreshold
        && elapsed < ScoCooldownDuration;

    public double ScoIndicatorTop
    {
        get
        {
            var elapsed = GetScoElapsed()?.TotalSeconds ?? 0;
            return Math.Clamp(elapsed * 2, 0, 20);
        }
    }

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

    /// <summary>
    /// Installs a representative journal/SCO state for the position editor.
    /// </summary>
    internal void InstallEditorPreview(
        PulseEditorPreviewState state = PulseEditorPreviewState.ScoCooling)
    {
        var now = timeProvider.GetUtcNow();
        pulseExpiresAtUtc = now + TimeSpan.FromSeconds(6);
        (bool isActive, DateTimeOffset? stoppedAtUtc) = state switch
        {
            PulseEditorPreviewState.ScoActive => (true, (DateTimeOffset?)null),
            PulseEditorPreviewState.ScoReady =>
                (false, now - ScoReadyThreshold),
            PulseEditorPreviewState.JournalPulse => (false, (DateTimeOffset?)null),
            _ => (false, now - TimeSpan.FromSeconds(4)),
        };
        supercruiseOverdrive = isActive;
        scoStoppedAtUtc = stoppedAtUtc;
        OnPropertyChanged(nameof(PulseHeight));
        OnPropertyChanged(nameof(IsScoActive));
        OnPropertyChanged(nameof(IsScoCoolingDown));
        OnPropertyChanged(nameof(IsScoReady));
        OnPropertyChanged(nameof(ScoIndicatorTop));
        OnPropertyChanged(nameof(ShouldShow));
    }

    public void ApplyUpdate(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        EliteStatus? status,
        bool isBootstrapRead)
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        var now = timeProvider.GetUtcNow();
        if (!isBootstrapRead && (journalEvents.Count > 0 || status is not null))
        {
            pulseExpiresAtUtc = now + PulseDuration;
        }

        if (status is not null)
        {
            var nextOverdrive = status.SupercruiseOverdrive;
            if (!isBootstrapRead && supercruiseOverdrive && !nextOverdrive)
            {
                scoStoppedAtUtc = now;
            }
            else if (nextOverdrive)
            {
                scoStoppedAtUtc = null;
            }

            supercruiseOverdrive = nextOverdrive;
            this.status = status;
        }

        foreach (var journalEvent in journalEvents)
        {
            if (journalEvent.EventName is "Fileheader" or "LoadGame")
            {
                musicTrack = null;
            }
            else if (journalEvent.EventName == "Music"
                && journalEvent.Payload.TryGetProperty(
                    "MusicTrack",
                    out var track))
            {
                musicTrack = track.GetString();
            }
        }

        RaiseRuntimeProperties();
    }

    public void Refresh()
    {
        var now = timeProvider.GetUtcNow();
        if (pulseExpiresAtUtc <= now)
        {
            pulseExpiresAtUtc = null;
        }

        if (scoStoppedAtUtc is { } stopped
            && now - stopped >= ScoCooldownDuration)
        {
            scoStoppedAtUtc = null;
        }

        RaiseRuntimeProperties();
    }

    private TimeSpan? GetScoElapsed()
    {
        if (scoStoppedAtUtc is not { } stopped)
        {
            return null;
        }

        return timeProvider.GetUtcNow() - stopped;
    }

    private void SavePreferences()
    {
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
            SettingsStatus = "Pulse overlay preference changed for this session "
                + "but could not be saved: " + exception.Message;
        }
    }

    private void RaiseRuntimeProperties()
    {
        OnPropertyChanged(nameof(ShouldShow));
        OnPropertyChanged(nameof(PulseHeight));
        OnPropertyChanged(nameof(IsScoActive));
        OnPropertyChanged(nameof(IsScoCoolingDown));
        OnPropertyChanged(nameof(IsScoReady));
        OnPropertyChanged(nameof(ScoIndicatorTop));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

internal enum PulseEditorPreviewState
{
    ScoCooling,
    ScoActive,
    ScoReady,
    JournalPulse,
}
