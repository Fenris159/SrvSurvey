using System.Text.Json;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Journal;

public sealed class JournalSessionState
{
    public string? GameVersion { get; private set; }

    public string? GameBuild { get; private set; }

    public bool? IsOdyssey { get; private set; }

    public string? CommanderName { get; private set; }

    public string? FrontierId { get; private set; }

    public string? GameMode { get; private set; }

    public string? ShipType { get; private set; }

    public string? ActiveSrvType { get; private set; }

    public OdysseySuitType CurrentSuit { get; private set; }

    public bool IsFighterLaunched { get; private set; }

    public string? SystemName { get; private set; }

    public long? SystemAddress { get; private set; }

    public GalacticCoordinate? StarPosition { get; private set; }

    public string? BodyName { get; private set; }

    public bool IsShutdown { get; private set; }

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
                GameVersion = GetString(root, "gameversion") ?? GameVersion;
                GameBuild = GetString(root, "build") ?? GameBuild;
                IsOdyssey = GetBoolean(root, "Odyssey") ?? IsOdyssey;
                IsShutdown = false;
                break;

            case "Commander":
                CommanderName = GetString(root, "Name") ?? CommanderName;
                FrontierId = GetString(root, "FID") ?? FrontierId;
                break;

            case "LoadGame":
                CommanderName = GetString(root, "Commander") ?? CommanderName;
                FrontierId = GetString(root, "FID") ?? FrontierId;
                GameMode = GetString(root, "GameMode") ?? GameMode;
                GameVersion = GetString(root, "gameversion") ?? GameVersion;
                GameBuild = GetString(root, "build") ?? GameBuild;
                IsOdyssey = GetBoolean(root, "Odyssey") ?? IsOdyssey;
                ShipType = GetString(root, "Ship") ?? ShipType;
                IsShutdown = false;
                break;

            case "Loadout":
                ShipType = GetString(root, "Ship") ?? ShipType;
                break;

            case "ShipyardSwap":
                ShipType = GetString(root, "ShipType") ?? ShipType;
                break;

            case "LaunchSRV":
                ActiveSrvType = GetString(root, "SRVType") ?? ActiveSrvType;
                break;

            case "DockSRV":
                ActiveSrvType = null;
                break;

            case "LaunchFighter":
                IsFighterLaunched = true;
                break;

            case "DockFighter":
                IsFighterLaunched = false;
                break;

            case "SuitLoadout":
            case "SwitchSuitLoadout":
                CurrentSuit = ParseSuitType(GetString(root, "SuitName"));
                break;

            case "Location":
            case "SupercruiseExit":
            case "FSDJump":
            case "CarrierJump":
                SystemName = GetString(root, "StarSystem") ?? SystemName;
                SystemAddress = GetInt64(root, "SystemAddress") ?? SystemAddress;
                StarPosition = GetGalacticCoordinate(root, "StarPos")
                    ?? StarPosition;
                BodyName = GetCurrentPlanetName(root);
                IsShutdown = false;
                break;

            case "ApproachBody":
                BodyName = GetString(root, "Body") ?? BodyName;
                break;

            case "LeaveBody":
                // The legacy application clears touchdown/SRV coordinates but
                // retains the current planet until another location event.
                break;

            case "Died":
            case "Resurrect":
                ClearLiveLocationContext();
                IsShutdown = false;
                break;

            case "Music" when string.Equals(
                GetString(root, "MusicTrack"),
                "MainMenu",
                StringComparison.Ordinal):
                ClearLiveLocationContext();
                break;

            case "Shutdown":
                IsShutdown = true;
                break;

            default:
                return false;
        }

        RecognizedEventCount++;
        return true;
    }

    private void ClearLiveLocationContext()
    {
        ActiveSrvType = null;
        IsFighterLaunched = false;
        BodyName = null;
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
            malformedLineCount);
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
