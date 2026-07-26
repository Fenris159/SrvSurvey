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
    private GuiFocus guiFocus;
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
            OnPropertyChanged(nameof(ShouldShow));
        }
    }

    public bool ShouldShow => Enabled
        && guiFocus is not GuiFocus.GalaxyMap
        && guiFocus is not GuiFocus.SystemMap;

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
            guiFocus = status.GuiFocus;
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
