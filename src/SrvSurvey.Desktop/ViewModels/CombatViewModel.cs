using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
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
    private bool footSessionActive;
    private string statusMessage = string.Empty;
    private IReadOnlyList<MassacreMissionViewModel> massacreMissions = [];
    private bool? journalShipContext;
    private bool? fighterParentShipContext;
    private bool replayUntrackedMissionHistory;

    public CombatViewModel(
        CombatSettingsStore settingsStore,
        CommanderProfileStore profileStore,
        CombatState? state = null
    )
    {
        this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        this.profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        this.state = state ?? new CombatState();
        CombatPreferences preferences = settingsStore.Load();
        autoShowFootCombat = preferences.AutoShowFootCombat;
        autoShowMassacreMissions = preferences.AutoShowMassacreMissions;
        suppressForActiveBuildProjects = preferences.SuppressForActiveBuildProjects;
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

    /// <summary>Keeps the legacy saved preference; shared priority applies it only to a visible colony panel.</summary>
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

    public string SettlementHeader => SettlementName.ToUpperInvariant();

    public int FootCombatKills => state.FootCombatKills;

    public string FootCombatBonds => $"{state.FootCombatBonds:N0} CR";

    public IReadOnlyList<MassacreMissionViewModel> MassacreMissions => massacreMissions;

    public bool HasMassacreMissions => state.MassacreMissions.Count > 0;

    public bool ShouldShowFootCombat =>
        AutoShowFootCombat && state.IsAtWarSettlement && IsFootCombatStatusEligible(status);

    public bool ShouldShowMassacreMissions =>
        AutoShowMassacreMissions && HasMassacreMissions && IsMassacreStatusEligible(status);

    public void LoadProfile(
        string? profileFrontierId,
        string? profileCommanderName,
        bool profileIsOdyssey,
        CombatSnapshot snapshot
    )
    {
        frontierId = profileFrontierId;
        commanderName = profileCommanderName;
        isOdyssey = profileIsOdyssey;
        state.Reset(snapshot);
        journalShipContext = null;
        fighterParentShipContext = null;
        // Other commander totals can have a checkpoint even when the old tracker ignored every mission.
        replayUntrackedMissionHistory = state.MassacreMissions.Count == 0;
        RebuildMassacreMissions();
        footSessionActive = false;
        StatusMessage = string.Empty;
        NotifyAllState();
    }

    /// <summary>
    /// Installs representative combat overlay content for the position editor.
    /// </summary>
    internal void InstallEditorPreview()
    {
        LoadProfile(
            "FDEV-RAVEN",
            "Raven",
            profileIsOdyssey: true,
            new CombatSnapshot([
                new MassacreMissionSnapshot(
                    MissionId: 1001,
                    MissionGiver: "Raven Colonial",
                    TargetFaction: "Blue Fortune Corp",
                    Expires: null,
                    KillCount: 20,
                    Remaining: 8
                ),
                new MassacreMissionSnapshot(
                    MissionId: 1002,
                    MissionGiver: "Allied Co-op",
                    TargetFaction: "Silver Legal Group",
                    Expires: null,
                    KillCount: 15,
                    Remaining: 7
                ),
                new MassacreMissionSnapshot(
                    MissionId: 1003,
                    MissionGiver: "System Authority",
                    TargetFaction: "Crimson Raiders",
                    Expires: null,
                    KillCount: 10,
                    Remaining: 0
                ),
            ])
        );
        state.InstallEditorSession(
            settlementName: "Mitchell's Claim",
            factionState: "War",
            kills: 22,
            bonds: 6_420_000
        );
        NotifyAllState();
    }

    public async Task ApplyUpdateAsync(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        EliteStatus? currentStatus,
        bool processHistoricalProgress
    )
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        bool? shipContext = InitialShipContext(journalEvents, currentStatus);
        if (currentStatus is not null)
        {
            status = currentStatus;
            UpdateFootSession();
        }

        bool persistenceChanged = false;
        bool stateChanged = false;
        bool modeChanged = false;
        foreach (JournalEventEnvelope journalEvent in journalEvents)
        {
            shipContext = ApplyVehicleContext(journalEvent, shipContext);
            if (journalEvent.EventName is "Fileheader" or "LoadGame")
            {
                modeChanged |= musicTrack is not null;
                musicTrack = null;
            }
            else if (
                journalEvent.EventName == "Music"
                && journalEvent.Payload.TryGetProperty("MusicTrack", out JsonElement track)
            )
            {
                string? nextMusicTrack = track.GetString();
                modeChanged |= !string.Equals(musicTrack, nextMusicTrack, StringComparison.Ordinal);
                musicTrack = nextMusicTrack;
            }

            if (!ShouldApplyMissionEvent(journalEvent.EventName))
            {
                continue;
            }

            CombatApplyResult result = state.Apply(
                journalEvent,
                countProgress: processHistoricalProgress || replayUntrackedMissionHistory,
                countFootCombat: ShouldCountFootCombat(shipContext, processHistoricalProgress),
                countShipCombat: ShouldCountShipCombat(shipContext)
            );
            stateChanged |= result.StateChanged;
            persistenceChanged |= result.PersistenceChanged;
            UpdateFootSession();
        }

        journalShipContext = shipContext;
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
            || eventName
                is not ("MissionAccepted" or "MissionCompleted" or "MissionFailed" or "MissionAbandoned" or "Bounty");
    }

    private bool ShouldCountShipCombat(bool? shipContext) => AutoShowMassacreMissions && shipContext == true;

    private bool ShouldCountFootCombat(bool? shipContext, bool processHistoricalProgress) =>
        processHistoricalProgress && ShouldShowFootCombat && shipContext != true;

    private bool? InitialShipContext(IReadOnlyList<JournalEventEnvelope> entries, EliteStatus? latestStatus)
    {
        if (entries.Any(entry => IsVehicleEvent(entry.EventName)))
        {
            return journalShipContext ?? ShipContext(status);
        }
        return latestStatus is not null ? ShipContext(latestStatus) : journalShipContext ?? ShipContext(status);
    }

    private static bool? ShipContext(EliteStatus? value) =>
        value is null ? null : !value.OnFoot && !value.InSrv && !value.InTaxi && (value.InMainShip || value.InFighter);

    private static bool IsVehicleEvent(string eventName) =>
        eventName
            is "Fileheader"
                or "LoadGame"
                or "Disembark"
                or "Embark"
                or "LaunchSRV"
                or "DockSRV"
                or "SRVDestroyed"
                or "LaunchFighter"
                or "DockFighter";

    private bool? ApplyVehicleContext(JournalEventEnvelope entry, bool? previous)
    {
        switch (entry.EventName)
        {
            case "Fileheader":
                fighterParentShipContext = null;
                return null;
            case "LoadGame":
                fighterParentShipContext = null;
                return
                    entry.Payload.TryGetProperty("Ship", out JsonElement ship) && ship.ValueKind == JsonValueKind.String
                    ? IsShipSymbol(ship.GetString()!)
                    : previous;
            case "Disembark":
            case "LaunchSRV":
                return false;
            case "Embark":
                return !IsTrue(entry.Payload, "SRV") && !IsTrue(entry.Payload, "Taxi");
            case "DockSRV":
                return true;
            case "SRVDestroyed":
                return null;
            case "LaunchFighter" when IsTrue(entry.Payload, "PlayerControlled"):
                fighterParentShipContext = previous;
                return true;
            case "DockFighter":
                bool? parent = fighterParentShipContext;
                fighterParentShipContext = null;
                return parent ?? true;
            default:
                return previous;
        }
    }

    private static bool IsTrue(JsonElement root, string key) =>
        root.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.True;

    private static bool IsShipSymbol(string symbol) =>
        !string.IsNullOrWhiteSpace(symbol)
        && !symbol.Equals("OnFoot", StringComparison.OrdinalIgnoreCase)
        && !symbol.Equals("testbuggy", StringComparison.OrdinalIgnoreCase)
        && !symbol.Contains("_srv_", StringComparison.OrdinalIgnoreCase)
        && !EliteSrvTypes.IsNomad(symbol)
        && !EliteSrvTypes.IsRhino(symbol);

    private void UpdateFootSession()
    {
        bool shouldBeActive = ShouldShowFootCombat;
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
            await profileStore.SaveCombatAsync(frontierId, commanderName, isOdyssey, state.CreateSnapshot());
            StatusMessage = "Combat mission progress saved.";
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            StatusMessage =
                "Combat mission progress changed for this session " + "but could not be saved: " + exception.Message;
        }
    }

    private void SavePreferences()
    {
        try
        {
            settingsStore.Save(
                new CombatPreferences(AutoShowFootCombat, AutoShowMassacreMissions, SuppressForActiveBuildProjects)
            );
            StatusMessage = string.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StatusMessage =
                "Combat overlay settings changed for this session " + "but could not be saved: " + exception.Message;
        }
    }

    private void NotifyAllState()
    {
        OnPropertyChanged(nameof(SettlementName));
        OnPropertyChanged(nameof(SettlementHeader));
        OnPropertyChanged(nameof(FootCombatKills));
        OnPropertyChanged(nameof(FootCombatBonds));
        OnPropertyChanged(nameof(MassacreMissions));
        OnPropertyChanged(nameof(HasMassacreMissions));
        NotifyOverlayState();
    }

    private void RebuildMassacreMissions()
    {
        IReadOnlyList<MassacreMissionViewModel> updated = state
            .MassacreMissions.OrderBy(mission => mission.TargetFaction, StringComparer.Ordinal)
            .ThenBy(mission => mission.MissionGiver, StringComparer.Ordinal)
            .Select(mission => new MassacreMissionViewModel(mission))
            .ToArray();
        massacreMissions = OverlayListRetention.Keep(
            massacreMissions,
            updated,
            (left, right) =>
                (left.MissionId, left.TargetFaction, left.MissionGiver, left.Remaining, left.KillCount)
                == (right.MissionId, right.TargetFaction, right.MissionGiver, right.Remaining, right.KillCount)
        );
    }

    private void NotifyOverlayState()
    {
        OnPropertyChanged(nameof(ShouldShowFootCombat));
        OnPropertyChanged(nameof(ShouldShowMassacreMissions));
    }

    private bool IsFootCombatStatusEligible(EliteStatus? status)
    {
        OverlayGameMode mode = OverlayGameModeResolver.Resolve(status, musicTrack: musicTrack);
        return status is not null && status.Altitude < 100 && mode is OverlayGameMode.OnFoot or OverlayGameMode.InSrv;
    }

    private bool IsMassacreStatusEligible(EliteStatus? status)
    {
        if (status is null)
        {
            return false;
        }

        OverlayGameMode mode = OverlayGameModeResolver.Resolve(status, musicTrack: musicTrack);
        return mode
            is OverlayGameMode.ExternalPanel
                or OverlayGameMode.StationServices
                or OverlayGameMode.SuperCruising
                or OverlayGameMode.Flying;
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class MassacreMissionViewModel(MassacreMissionSnapshot mission)
{
    public long MissionId => mission.MissionId;

    public string TargetFaction => mission.TargetFaction;

    public string MissionGiver => mission.MissionGiver;

    public int Remaining => mission.Remaining;

    public int KillCount => mission.KillCount;

    public bool IsComplete => Remaining == 0;

    public int Completed => Math.Clamp(KillCount - Remaining, 0, KillCount);

    public string ProgressText => $"{Completed:N0}/{KillCount:N0}";

    public double RowOpacity => IsComplete ? 0.58 : 1;
}
