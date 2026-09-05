using System.Text.Json;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Journal;

public sealed class JournalSessionState
{
    private const string ShipIdProperty = "ShipID";
    private readonly Dictionary<long, string> srvTypesById = [];
    private long? pendingPlayerControlledFighterId;
    private bool isNomadStatusConfirmationPending;

    public string? GameVersion { get; private set; }

    public string? GameBuild { get; private set; }

    /// <summary>Journal galaxy from Fileheader; independent of expansion ownership.</summary>
    public bool? IsLegacy { get; private set; }

    /// <summary>Odyssey expansion flag from the latest LoadGame.</summary>
    public bool? IsOdyssey { get; private set; }

    public bool? IsHorizons { get; private set; }

    public string? CommanderName { get; private set; }

    public string? FrontierId { get; private set; }

    public string? GameMode { get; private set; }

    public string? ShipType { get; private set; }

    public long? ShipId { get; private set; }

    public string? ShipName { get; private set; }

    public string? ShipIdent { get; private set; }

    public string? ActiveSrvType { get; private set; }

    public bool IsNomadActive => EliteSrvTypes.IsNomad(ActiveSrvType);

    public long? KnownNomadVehicleId { get; private set; }

    public OdysseySuitType CurrentSuit { get; private set; }

    public bool IsFighterLaunched { get; private set; }

    public string? SystemName { get; private set; }

    public string? StationName { get; private set; }

    public long? SystemAddress { get; private set; }

    public GalacticCoordinate? StarPosition { get; private set; }

    public string? BodyName { get; private set; }

    public bool IsShutdown { get; private set; }

    public bool IsAtMainMenu { get; private set; }

    public bool IsAtCarrierManagement { get; private set; }

    public string? MusicTrack { get; private set; }

    public DateTimeOffset? LastEventTimestamp { get; private set; }

    public int ValidEventCount { get; private set; }

    public int RecognizedEventCount { get; private set; }

    public int UnhandledEventCount => ValidEventCount - RecognizedEventCount;

    public bool Apply(JournalEventEnvelope journalEvent)
    {
        ArgumentNullException.ThrowIfNull(journalEvent);
        ValidEventCount++;
        LastEventTimestamp = journalEvent.Timestamp ?? LastEventTimestamp;
        var root = journalEvent.Payload;

        switch (journalEvent.EventName)
        {
            case "Fileheader":
                ResetVehicleSessionState();
                GameVersion = GetString(root, "gameversion") ?? GameVersion;
                GameBuild = GetString(root, "build") ?? GameBuild;
                IsLegacy = GetBoolean(root, "Odyssey") is { } isLive
                    ? !isLive
                    : null;
                IsOdyssey = null;
                IsHorizons = null;
                IsShutdown = false;
                // Elite creates the journal before LoadGame while it is still
                // in the initial front end, but does not consistently emit a
                // MainMenu music event there. Treat the new pre-session journal
                // as the main menu until LoadGame confirms gameplay has begun.
                IsAtMainMenu = true;
                IsAtCarrierManagement = false;
                MusicTrack = null;
                break;

            case "Commander":
                var nextCommanderName = GetString(root, "Name");
                var nextFrontierId = GetString(root, "FID");
                if (HasCommanderChanged(nextCommanderName, nextFrontierId))
                {
                    ResetVehicleSessionState();
                }

                CommanderName = nextCommanderName ?? CommanderName;
                FrontierId = nextFrontierId ?? FrontierId;
                break;

            case "LoadGame":
                ApplyLoadGame(root);
                break;

            case "Loadout":
                ShipType = GetString(root, "Ship") ?? ShipType;
                ShipId = GetInt64(root, ShipIdProperty) ?? ShipId;
                ShipName = GetString(root, nameof(ShipName)) ?? ShipName;
                ShipIdent = GetString(root, nameof(ShipIdent))
                    ?? GetString(root, "ShipIDent")
                    ?? ShipIdent;
                break;

            case "ShipyardSwap":
                ShipType = GetString(root, nameof(ShipType)) ?? ShipType;
                ShipId = GetInt64(root, ShipIdProperty) ?? ShipId;
                break;

            case "ShipyardBuy":
            case "ShipyardNew":
                ShipType = GetString(root, nameof(ShipType)) ?? ShipType;
                ShipId = GetInt64(root, "NewShipID")
                    ?? GetInt64(root, ShipIdProperty)
                    ?? ShipId;
                break;

            case "SetUserShipName":
                ShipType = GetString(root, "Ship") ?? ShipType;
                ShipId = GetInt64(root, ShipIdProperty) ?? ShipId;
                ShipName = GetString(root, "UserShipName") ?? ShipName;
                ShipIdent = GetString(root, "UserShipId") ?? ShipIdent;
                break;

            case "LaunchSRV":
                var launchedSrvType = GetString(root, "SRVType");
                ActiveSrvType = launchedSrvType ?? ActiveSrvType;
                RememberSrvType(root, launchedSrvType);
                pendingPlayerControlledFighterId = null;
                isNomadStatusConfirmationPending =
                    EliteSrvTypes.IsNomad(launchedSrvType);
                break;

            case "DockSRV":
                RememberSrvType(root, GetString(root, "SRVType"));
                ResetActiveVehicleState();
                break;

            case "SRVDestroyed":
                ForgetSrvType(root);
                ResetActiveVehicleState();
                break;

            case "LaunchFighter":
                ApplyLaunchFighter(root);
                break;

            case "DockFighter":
                IsFighterLaunched = false;
                pendingPlayerControlledFighterId = null;
                isNomadStatusConfirmationPending = false;
                if (IsNomadActive)
                {
                    ActiveSrvType = null;
                }

                break;

            case "Embark" when GetBoolean(root, "SRV") == true:
                var embarkedVehicleId = GetInt64(root, "ID");
                if (embarkedVehicleId is { } embarkedId
                    && srvTypesById.TryGetValue(embarkedId, out var embarkedSrvType))
                {
                    ActiveSrvType = embarkedSrvType;
                    isNomadStatusConfirmationPending = EliteSrvTypes.IsNomad(embarkedSrvType);
                }

                break;

            case "Disembark" when GetBoolean(root, "SRV") == true:
                ActiveSrvType = null;
                pendingPlayerControlledFighterId = null;
                isNomadStatusConfirmationPending = false;
                break;

            case "SuitLoadout":
            case "SwitchSuitLoadout":
                CurrentSuit = ParseSuitType(GetString(root, "SuitName"));
                break;

            case "Location":
                SystemName = GetString(root, "StarSystem") ?? SystemName;
                SystemAddress = GetInt64(root, nameof(SystemAddress)) ?? SystemAddress;
                StarPosition = GetGalacticCoordinate(root, "StarPos")
                    ?? StarPosition;
                BodyName = GetCurrentPlanetName(root);
                StationName = GetBoolean(root, "Docked") == true
                    ? GetString(root, nameof(StationName)) ?? StationName
                    : null;
                IsShutdown = false;
                IsAtMainMenu = false;
                break;

            case "Docked":
                SystemName = GetString(root, "StarSystem") ?? SystemName;
                SystemAddress = GetInt64(root, nameof(SystemAddress)) ?? SystemAddress;
                StationName = GetString(root, nameof(StationName)) ?? StationName;
                IsShutdown = false;
                IsAtMainMenu = false;
                break;

            case "Undocked":
                StationName = null;
                break;

            case "SupercruiseExit":
            case "FSDJump":
            case "CarrierJump":
                SystemName = GetString(root, "StarSystem") ?? SystemName;
                SystemAddress = GetInt64(root, nameof(SystemAddress)) ?? SystemAddress;
                StarPosition = GetGalacticCoordinate(root, "StarPos")
                    ?? StarPosition;
                BodyName = GetCurrentPlanetName(root);
                StationName = null;
                IsShutdown = false;
                IsAtMainMenu = false;
                break;

            case "ApproachBody":
                BodyName = GetString(root, "Body") ?? BodyName;
                break;

            case "LeaveBody":
                // The legacy application clears touchdown/SRV coordinates but
                // retains the current planet until another location event.
                break;

            case "StartJump" when string.Equals(
                GetString(root, "JumpType"),
                "Hyperspace",
                StringComparison.Ordinal):
                // The departure event arrives before FSDJump. Drop only the
                // live body/vehicle context while retaining the durable
                // commander, ship, and origin-system identity.
                ClearLiveLocationContext();
                break;

            case "Died":
            case "Resurrect":
                ClearLiveLocationContext();
                IsShutdown = false;
                break;

            case "Music":
                var musicTrack = GetString(root, nameof(MusicTrack));
                MusicTrack = musicTrack;
                IsAtMainMenu = string.Equals(
                    musicTrack,
                    "MainMenu",
                    StringComparison.Ordinal);
                IsAtCarrierManagement = string.Equals(
                    musicTrack,
                    "FleetCarrier_Managment",
                    StringComparison.Ordinal);
                if (IsAtMainMenu)
                {
                    ClearLiveLocationContext();
                }

                break;

            case "Shutdown":
                IsShutdown = true;
                IsAtMainMenu = false;
                IsAtCarrierManagement = false;
                MusicTrack = null;
                break;

            default:
                return false;
        }

        RecognizedEventCount++;
        return true;
    }

    private void ApplyLoadGame(JsonElement root)
    {
        var loadedCommanderName = GetString(root, "Commander");
        var loadedFrontierId = GetString(root, "FID");
        if (HasCommanderChanged(loadedCommanderName, loadedFrontierId))
        {
            ResetVehicleSessionState();
        }
        else
        {
            // Elite can emit another LoadGame while the commander is on foot,
            // reporting the suit as Ship. Keep vehicle IDs learned earlier in
            // this journal so a later Embark can restore the Nomad identity.
            ResetActiveVehicleState();
        }

        var loadedShipType = GetString(root, "Ship");
        CommanderName = loadedCommanderName ?? CommanderName;
        FrontierId = loadedFrontierId ?? FrontierId;
        GameMode = GetString(root, nameof(GameMode)) ?? GameMode;
        GameVersion = GetString(root, "gameversion") ?? GameVersion;
        GameBuild = GetString(root, "build") ?? GameBuild;
        IsOdyssey = GetBoolean(root, "Odyssey");
        IsHorizons = GetBoolean(root, "Horizons");
        ShipType = loadedShipType ?? ShipType;
        var loadedShipId = GetInt64(root, ShipIdProperty);
        ShipId = loadedShipId ?? ShipId;
        ShipName = GetString(root, nameof(ShipName)) ?? ShipName;
        ShipIdent = GetString(root, nameof(ShipIdent)) ?? ShipIdent;
        if (string.Equals(loadedShipType, "mev_rhino", StringComparison.OrdinalIgnoreCase))
        {
            ActiveSrvType = loadedShipType;
            if (loadedShipId is { } rhinoId)
            {
                srvTypesById[rhinoId] = "mev_rhino";
            }
        }

        if (EliteSrvTypes.IsNomad(loadedShipType))
        {
            ActiveSrvType = EliteSrvTypes.Nomad;
            if (loadedShipId is { } loadedVehicleId)
            {
                srvTypesById[loadedVehicleId] = EliteSrvTypes.Nomad;
                KnownNomadVehicleId = loadedVehicleId;
            }
        }

        IsShutdown = false;
        IsAtMainMenu = false;
        IsAtCarrierManagement = false;
        MusicTrack = null;
    }

    private void ApplyLaunchFighter(JsonElement root)
    {
        IsFighterLaunched = true;
        if (GetBoolean(root, "PlayerControlled") != true)
        {
            return;
        }

        pendingPlayerControlledFighterId = GetInt64(root, "ID");
        if (pendingPlayerControlledFighterId is { } vehicleId
            && srvTypesById.TryGetValue(vehicleId, out var srvType)
            && EliteSrvTypes.IsNomad(srvType))
        {
            ActiveSrvType = srvType;
            IsFighterLaunched = false;
            pendingPlayerControlledFighterId = null;
            isNomadStatusConfirmationPending = true;
        }
    }

    public bool ReconcileVehicleStatus(EliteStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        var previousSrvType = ActiveSrvType;
        var previousFighterState = IsFighterLaunched;

        if (status.InSrv && pendingPlayerControlledFighterId is { } vehicleId)
        {
            ActiveSrvType = EliteSrvTypes.Nomad;
            srvTypesById[vehicleId] = EliteSrvTypes.Nomad;
            KnownNomadVehicleId = vehicleId;
            IsFighterLaunched = false;
            pendingPlayerControlledFighterId = null;
            isNomadStatusConfirmationPending = false;
        }
        else if (status.InSrv)
        {
            isNomadStatusConfirmationPending = false;
        }
        else if (status.InFighter)
        {
            if (IsNomadActive)
            {
                ActiveSrvType = null;
            }

            pendingPlayerControlledFighterId = null;
            isNomadStatusConfirmationPending = false;
        }
        else if (IsNomadActive && !isNomadStatusConfirmationPending)
        {
            ActiveSrvType = null;
        }

        return !string.Equals(
                previousSrvType,
                ActiveSrvType,
                StringComparison.OrdinalIgnoreCase)
            || previousFighterState != IsFighterLaunched;
    }

    private void ClearLiveLocationContext()
    {
        ActiveSrvType = null;
        IsFighterLaunched = false;
        pendingPlayerControlledFighterId = null;
        isNomadStatusConfirmationPending = false;
        BodyName = null;
    }

    private void RememberSrvType(JsonElement root, string? srvType)
    {
        var vehicleId = GetInt64(root, "ID");
        if (vehicleId is not null && !string.IsNullOrWhiteSpace(srvType))
        {
            srvTypesById[vehicleId.Value] = srvType;
            if (EliteSrvTypes.IsNomad(srvType))
            {
                KnownNomadVehicleId = vehicleId.Value;
            }
            else if (KnownNomadVehicleId == vehicleId.Value)
            {
                KnownNomadVehicleId = null;
            }
        }
    }

    private void ForgetSrvType(JsonElement root)
    {
        if (GetInt64(root, "ID") is { } vehicleId)
        {
            srvTypesById.Remove(vehicleId);
            if (KnownNomadVehicleId == vehicleId)
            {
                KnownNomadVehicleId = null;
            }
        }
    }

    private bool HasCommanderChanged(
        string? nextCommanderName,
        string? nextFrontierId)
    {
        var frontierIdChanged = !string.IsNullOrWhiteSpace(nextFrontierId)
            && !string.IsNullOrWhiteSpace(FrontierId)
            && !string.Equals(
                nextFrontierId,
                FrontierId,
                StringComparison.OrdinalIgnoreCase);
        var commanderNameChanged = !string.IsNullOrWhiteSpace(nextCommanderName)
            && !string.IsNullOrWhiteSpace(CommanderName)
            && !string.Equals(
                nextCommanderName,
                CommanderName,
                StringComparison.OrdinalIgnoreCase);
        return frontierIdChanged || commanderNameChanged;
    }

    private void ResetActiveVehicleState()
    {
        ActiveSrvType = null;
        IsFighterLaunched = false;
        pendingPlayerControlledFighterId = null;
        isNomadStatusConfirmationPending = false;
    }

    private void ResetVehicleSessionState()
    {
        ResetActiveVehicleState();
        srvTypesById.Clear();
        KnownNomadVehicleId = null;
    }

    public JournalSnapshot CreateSnapshot(
        string? sourcePath,
        int malformedLineCount = 0)
    {
        return new JournalSnapshot(
            sourcePath,
            GameVersion,
            GameBuild,
            IsOdyssey,
            CommanderName,
            FrontierId,
            GameMode,
            SystemName,
            SystemAddress,
            StarPosition,
            BodyName,
            IsShutdown,
            LastEventTimestamp,
            ValidEventCount,
            RecognizedEventCount,
            malformedLineCount)
        {
            IsLegacy = IsLegacy,
            IsHorizons = IsHorizons,
        };
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static bool? GetBoolean(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && (value.ValueKind == JsonValueKind.True
                || value.ValueKind == JsonValueKind.False)
                ? value.GetBoolean()
                : null;
    }

    private static string? GetCurrentPlanetName(JsonElement root)
    {
        return GetString(root, "BodyType") == "Planet"
            ? GetString(root, "Body")
            : null;
    }

    private static long? GetInt64(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String
            && long.TryParse(value.GetString(), out number)
                ? number
                : null;
    }

    private static GalacticCoordinate? GetGalacticCoordinate(
        JsonElement root,
        string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Array
            || value.GetArrayLength() < 3)
        {
            return null;
        }

        var coordinates = value.EnumerateArray().Take(3).ToArray();
        if (coordinates.Any(coordinate =>
                coordinate.ValueKind != JsonValueKind.Number
                || !coordinate.TryGetDouble(out var number)
                || !double.IsFinite(number)))
        {
            return null;
        }

        return new GalacticCoordinate(
            coordinates[0].GetDouble(),
            coordinates[1].GetDouble(),
            coordinates[2].GetDouble());
    }

    private static OdysseySuitType ParseSuitType(string? suitName)
    {
        if (string.IsNullOrWhiteSpace(suitName))
        {
            return OdysseySuitType.Unknown;
        }

        return suitName switch
        {
            _ when suitName.StartsWith(
                "flightsuit",
                StringComparison.OrdinalIgnoreCase) => OdysseySuitType.Flight,
            _ when suitName.StartsWith(
                "explorationsuit",
                StringComparison.OrdinalIgnoreCase) => OdysseySuitType.Artemis,
            _ when suitName.StartsWith(
                "utilitysuit",
                StringComparison.OrdinalIgnoreCase) => OdysseySuitType.Maverick,
            _ when suitName.StartsWith(
                "tacticalsuit",
                StringComparison.OrdinalIgnoreCase) => OdysseySuitType.Dominator,
            _ => OdysseySuitType.Unknown,
        };
    }
}

public enum OdysseySuitType
{
    Unknown,
    Flight,
    Artemis,
    Maverick,
    Dominator,
}
