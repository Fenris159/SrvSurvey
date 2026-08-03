using System.ComponentModel;
using System.Runtime.CompilerServices;
using SrvSurvey.Core.Combat;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class CombatViewModel : INotifyPropertyChanged
{
    private readonly CombatSettingsStore settingsStore;
    private readonly CommanderProfileStore profileStore;
    private readonly CombatState state;
    private EliteStatus? status;
    private string? musicTrack;
    private string? frontierId;
    private string? commanderName;
    private bool isOdyssey = true;
    private bool autoShowFootCombat;
    private bool autoShowMassacreMissions;
    private bool suppressForActiveBuildProjects;
    private bool hasActiveBuildProjects;
    private bool footSessionActive;
    private string statusMessage = string.Empty;
    private IReadOnlyList<MassacreMissionViewModel> massacreMissions = [];

    public CombatViewModel(
        CombatSettingsStore settingsStore,
        CommanderProfileStore profileStore,
        CombatState? state = null)
    {
        this.settingsStore = settingsStore
            ?? throw new ArgumentNullException(nameof(settingsStore));
        this.profileStore = profileStore
            ?? throw new ArgumentNullException(nameof(profileStore));
        this.state = state ?? new CombatState();
        var preferences = settingsStore.Load();
        autoShowFootCombat = preferences.AutoShowFootCombat;
        autoShowMassacreMissions = preferences.AutoShowMassacreMissions;
        suppressForActiveBuildProjects =
            preferences.SuppressForActiveBuildProjects;
        RebuildMassacreMissions();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool AutoShowFootCombat
    {
        get => autoShowFootCombat;
        set
        {
            if (!SetField(ref autoShowFootCombat, value))
            {
                return;
            }

            SavePreferences();
            UpdateFootSession();
            NotifyOverlayState();
        }
    }

    public bool AutoShowMassacreMissions
    {
        get => autoShowMassacreMissions;
        set
        {
            if (!SetField(ref autoShowMassacreMissions, value))
            {
                return;
            }

            SavePreferences();
            NotifyOverlayState();
        }
    }

    public bool SuppressForActiveBuildProjects
    {
        get => suppressForActiveBuildProjects;
        set
        {
            if (!SetField(ref suppressForActiveBuildProjects, value))
            {
                return;
            }

            SavePreferences();
            UpdateFootSession();
            NotifyOverlayState();
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set
        {
            if (SetField(ref statusMessage, value))
            {
                OnPropertyChanged(nameof(HasStatusMessage));
            }
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public string SettlementName => state.SettlementName ?? string.Empty;

    public int FootCombatKills => state.FootCombatKills;

    public string FootCombatBonds => $"{state.FootCombatBonds:N0} CR";

    public IReadOnlyList<MassacreMissionViewModel> MassacreMissions =>
        massacreMissions;

    public bool HasMassacreMissions => state.MassacreMissions.Count > 0;

    public bool ShouldShowFootCombat => AutoShowFootCombat
        && !ShouldSuppressOverlays
        && state.IsAtWarSettlement
        && IsFootCombatStatusEligible(status);

    public bool ShouldShowMassacreMissions => AutoShowMassacreMissions
        && !ShouldSuppressOverlays
        && HasMassacreMissions
        && IsMassacreStatusEligible(status);

    private bool ShouldSuppressOverlays =>
        SuppressForActiveBuildProjects && hasActiveBuildProjects;

    public void LoadProfile(
        string? profileFrontierId,
        string? profileCommanderName,
        bool profileIsOdyssey,
        CombatSnapshot snapshot)
    {
        frontierId = profileFrontierId;
        commanderName = profileCommanderName;
        isOdyssey = profileIsOdyssey;
        state.Reset(snapshot);
        RebuildMassacreMissions();
        footSessionActive = false;
        StatusMessage = string.Empty;
        NotifyAllState();
    }

    public void SetActiveBuildProjects(bool value)
    {
        if (value == hasActiveBuildProjects)
        {
            return;
        }

        hasActiveBuildProjects = value;
        UpdateFootSession();
        NotifyOverlayState();
    }

    public async Task ApplyUpdateAsync(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        EliteStatus? currentStatus,
        bool processHistoricalProgress)
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        if (currentStatus is not null)
        {
            status = currentStatus;
            UpdateFootSession();
        }

        var persistenceChanged = false;
        var stateChanged = false;
        var modeChanged = false;
        foreach (var journalEvent in journalEvents)
        {
            if (journalEvent.EventName is "Fileheader" or "LoadGame")
            {
                modeChanged |= musicTrack is not null;
                musicTrack = null;
            }
            else if (journalEvent.EventName == "Music"
                && journalEvent.Payload.TryGetProperty(
                    "MusicTrack",
                    out var track))
            {
                var nextMusicTrack = track.GetString();
                modeChanged |= !string.Equals(
                    musicTrack,
                    nextMusicTrack,
                    StringComparison.Ordinal);
                musicTrack = nextMusicTrack;
            }

            if (!ShouldApplyMissionEvent(journalEvent.EventName))
            {
                continue;
            }

            var result = state.Apply(
                journalEvent,
                countProgress: processHistoricalProgress,
                countFootCombat: ShouldShowFootCombat);
            stateChanged |= result.StateChanged;
            persistenceChanged |= result.PersistenceChanged;
            UpdateFootSession();
        }

        if (persistenceChanged)
        {
            await SaveCombatAsync();
        }

        if (stateChanged || currentStatus is not null || modeChanged)
        {
            if (stateChanged)
            {
                RebuildMassacreMissions();
            }

            NotifyAllState();
        }
    }

    private bool ShouldApplyMissionEvent(string eventName)
    {
        return AutoShowMassacreMissions
            || eventName is not (
                "MissionAccepted"
                or "MissionCompleted"
                or "MissionFailed"
                or "MissionAbandoned"
                or "Bounty");
    }

    private void UpdateFootSession()
    {
        var shouldBeActive = ShouldShowFootCombat;
        if (shouldBeActive && !footSessionActive)
        {
            state.ResetFootCombatSession();
        }

        footSessionActive = shouldBeActive;
    }

    private async Task SaveCombatAsync()
    {
        if (string.IsNullOrWhiteSpace(frontierId))
        {
            return;
        }

        try
        {
            await profileStore.SaveCombatAsync(
                frontierId,
                commanderName,
                isOdyssey,
                state.CreateSnapshot());
            StatusMessage = "Combat mission progress saved.";
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            StatusMessage = "Combat mission progress changed for this session "
                + "but could not be saved: "
                + exception.Message;
        }
    }

    private void SavePreferences()
    {
        try
        {
            settingsStore.Save(new CombatPreferences(
                AutoShowFootCombat,
                AutoShowMassacreMissions,
                SuppressForActiveBuildProjects));
            StatusMessage = string.Empty;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            StatusMessage = "Combat overlay settings changed for this session "
                + "but could not be saved: "
                + exception.Message;
        }
    }

    private void NotifyAllState()
    {
        OnPropertyChanged(nameof(SettlementName));
        OnPropertyChanged(nameof(FootCombatKills));
        OnPropertyChanged(nameof(FootCombatBonds));
        OnPropertyChanged(nameof(MassacreMissions));
        OnPropertyChanged(nameof(HasMassacreMissions));
        NotifyOverlayState();
    }

    private void RebuildMassacreMissions()
    {
        massacreMissions = state.MassacreMissions
            .OrderBy(mission => mission.TargetFaction, StringComparer.Ordinal)
            .ThenBy(mission => mission.MissionGiver, StringComparer.Ordinal)
            .Select(mission => new MassacreMissionViewModel(mission))
            .ToArray();
    }

    private void NotifyOverlayState()
    {
        OnPropertyChanged(nameof(ShouldShowFootCombat));
        OnPropertyChanged(nameof(ShouldShowMassacreMissions));
    }

    private bool IsFootCombatStatusEligible(EliteStatus? status)
    {
        var mode = OverlayGameModeResolver.Resolve(
            status,
            musicTrack: musicTrack);
        return status is not null
            && status.Altitude < 100
            && mode is OverlayGameMode.OnFoot or OverlayGameMode.InSrv;
    }

    private bool IsMassacreStatusEligible(EliteStatus? status)
    {
        if (status is null)
        {
            return false;
        }

        var mode = OverlayGameModeResolver.Resolve(
            status,
            musicTrack: musicTrack);
        return mode is OverlayGameMode.ExternalPanel
            or OverlayGameMode.StationServices
            or OverlayGameMode.SuperCruising
            or OverlayGameMode.Flying;
    }

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class MassacreMissionViewModel(
    MassacreMissionSnapshot mission)
{
    public long MissionId => mission.MissionId;

    public string TargetFaction => mission.TargetFaction;

    public string MissionGiver => mission.MissionGiver;

    public int Remaining => mission.Remaining;

    public int KillCount => mission.KillCount;

    public bool IsComplete => Remaining == 0;

    public string RemainingText => IsComplete ? "COMPLETE" : $"{Remaining:N0}";

    public double RowOpacity => IsComplete ? 0.58 : 1;
}
