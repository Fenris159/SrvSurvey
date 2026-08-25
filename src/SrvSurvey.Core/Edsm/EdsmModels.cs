using Newtonsoft.Json.Linq;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Edsm;

public sealed record EdsmPublicationOptions(
    string? ApiKey,
    string? EdsmCommanderName,
    string? ActiveCommanderName,
    string? FrontierId,
    string? GameVersion,
    string? GameBuild,
    bool IsOdyssey)
{
    public override string ToString() =>
        $"EdsmPublicationOptions {{ HasApiKey = {!string.IsNullOrWhiteSpace(ApiKey)}, EdsmCommanderName = {EdsmCommanderName}, ActiveCommanderName = {ActiveCommanderName}, FrontierId = {FrontierId}, GameVersion = {GameVersion}, GameBuild = {GameBuild}, IsOdyssey = {IsOdyssey} }}";
}

public sealed record EdsmPublicationUpdate(
    IReadOnlyList<JournalEventEnvelope> JournalEvents,
    string? JournalPath,
    bool AllowPublishing,
    EdsmPublicationOptions Options);

public sealed record EdsmPublicationResult(
    int QueuedEventCount,
    int AcceptedEventCount,
    int PendingEventCount,
    IReadOnlyList<string> QueuedEventNames,
    IReadOnlyList<string> Warnings)
{
    public static EdsmPublicationResult Empty { get; } = new(
        0,
        0,
        0,
        [],
        []);
}

internal sealed record EdsmCredentials(
    string CommanderName,
    string ApiKey);

internal sealed record EdsmSession(
    string ActiveCommanderName,
    string FrontierId,
    string? JournalPath,
    string GameVersion,
    string GameBuild,
    bool IsLive,
    bool IsBeta)
{
    internal static EdsmSession? Create(
        EdsmPublicationOptions options,
        string? journalPath)
    {
        ArgumentNullException.ThrowIfNull(options);
        var activeCommanderName = options.ActiveCommanderName?.Trim();
        var frontierId = options.FrontierId?.Trim();
        var gameVersion = options.GameVersion?.Trim();
        var gameBuild = options.GameBuild?.Trim();
        if (string.IsNullOrWhiteSpace(activeCommanderName)
            || string.IsNullOrWhiteSpace(frontierId)
            || string.IsNullOrWhiteSpace(gameVersion)
            || string.IsNullOrWhiteSpace(gameBuild))
        {
            return null;
        }

        return new EdsmSession(
            activeCommanderName,
            frontierId,
            string.IsNullOrWhiteSpace(journalPath) ? null : journalPath,
            gameVersion,
            gameBuild,
            EdsmPublisher.IsLiveVersion(gameVersion, options.IsOdyssey),
            EdsmPublisher.IsBetaVersion(gameVersion));
    }

    internal bool Matches(EdsmSession other)
    {
        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(
                ActiveCommanderName,
                other.ActiveCommanderName,
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                FrontierId,
                other.FrontierId,
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(JournalPath, other.JournalPath, pathComparison)
            && string.Equals(GameVersion, other.GameVersion, StringComparison.Ordinal)
            && string.Equals(GameBuild, other.GameBuild, StringComparison.Ordinal)
            && IsLive == other.IsLive
            && IsBeta == other.IsBeta;
    }

    internal EdsmCredentials? GetCredentials(
        string? edsmCommanderName,
        string? apiKey)
    {
        var normalizedCommander = edsmCommanderName?.Trim();
        var normalizedKey = apiKey?.Trim();
        return string.IsNullOrWhiteSpace(normalizedCommander)
            || string.IsNullOrWhiteSpace(normalizedKey)
                ? null
                : new EdsmCredentials(normalizedCommander, normalizedKey);
    }
}

internal sealed record EdsmQueuedEvent(
    long AuthorizationGeneration,
    string EventName,
    string RawJson);

internal sealed class EdsmJournalContext
{
    internal string? SystemName { get; private set; }

    internal long? SystemAddress { get; private set; }

    internal JArray? SystemCoordinates { get; private set; }

    internal long? MarketId { get; private set; }

    internal string? StationName { get; private set; }

    internal long? ShipId { get; private set; }

    internal bool InMulticrew { get; private set; }

    internal void Reset()
    {
        SystemName = null;
        SystemAddress = null;
        SystemCoordinates = null;
        MarketId = null;
        StationName = null;
        ShipId = null;
        InMulticrew = false;
    }

    internal void Apply(JObject entry)
    {
        var eventName = entry.Value<string>("event");
        UpdateMulticrew(eventName, entry);
        switch (eventName)
        {
            case "LoadGame":
                SystemName = null;
                SystemAddress = null;
                SystemCoordinates = null;
                MarketId = null;
                StationName = null;
                ShipId = ReadInt64(entry, "ShipID") ?? ShipId;
                break;

            case "Location":
                ApplySystem(entry);
                if (entry.Value<bool?>("Docked") == true)
                {
                    ApplyStation(entry);
                }
                else
                {
                    MarketId = null;
                    StationName = null;
                }

                break;

            case "FSDJump":
            case "CarrierJump":
                ApplySystem(entry);
                MarketId = null;
                StationName = null;
                break;

            case "Docked":
                ApplySystem(entry);
                ApplyStation(entry);
                break;

            case "Undocked":
                MarketId = null;
                StationName = null;
                break;

            case "Loadout":
            case "SetUserShipName":
            case "ShipyardSwap":
                ShipId = ReadInt64(entry, "ShipID") ?? ShipId;
                break;

            case "ShipyardBuy":
            case "ShipyardNew":
                ShipId = ReadInt64(entry, "NewShipID")
                    ?? ReadInt64(entry, "ShipID")
                    ?? ShipId;
                break;
        }
    }

    internal JObject AddTransientFields(JObject entry)
    {
        var augmented = (JObject)entry.DeepClone();
        SetIfKnown(augmented, "_systemAddress", SystemAddress);
        SetIfKnown(augmented, "_systemName", SystemName);
        if (SystemCoordinates is not null)
        {
            augmented["_systemCoordinates"] = SystemCoordinates.DeepClone();
        }

        SetIfKnown(augmented, "_marketId", MarketId);
        SetIfKnown(augmented, "_stationName", StationName);
        SetIfKnown(augmented, "_shipId", ShipId);
        return augmented;
    }

    private void ApplySystem(JObject entry)
    {
        SystemName = entry.Value<string>("StarSystem") ?? SystemName;
        SystemAddress = ReadInt64(entry, "SystemAddress") ?? SystemAddress;
        if (entry["StarPos"] is JArray { Count: >= 3 } starPosition
            && starPosition.Take(3).All(item => item.Type is
                JTokenType.Integer or JTokenType.Float))
        {
            SystemCoordinates = new JArray(
                starPosition.Take(3).Select(item => item.DeepClone()));
        }
    }

    private void ApplyStation(JObject entry)
    {
        MarketId = ReadInt64(entry, "MarketID") ?? MarketId;
        StationName = entry.Value<string>("StationName") ?? StationName;
    }

    private void UpdateMulticrew(string? eventName, JObject entry)
    {
        if (eventName is "LoadGame"
            or "QuitACrew"
            or "EndCrewSession"
            or "CrewMemberQuits")
        {
            InMulticrew = false;
        }
        else if (eventName is "JoinACrew" or "ChangeCrewRole"
            || entry.Value<bool?>("Multicrew") == true)
        {
            InMulticrew = true;
        }
    }

    private static long? ReadInt64(JObject entry, string propertyName)
    {
        var token = entry[propertyName];
        return token?.Type == JTokenType.Integer
            ? token.Value<long>()
            : long.TryParse(
                token?.Value<string>(),
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var value)
                ? value
                : null;
    }

    private static void SetIfKnown(
        JObject target,
        string propertyName,
        object? value)
    {
        if (value is not null)
        {
            target[propertyName] = JToken.FromObject(value);
        }
    }
}
