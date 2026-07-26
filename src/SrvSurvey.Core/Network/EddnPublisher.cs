using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Network;

public interface IEddnPublisher
{
    Task<EddnPublicationResult> ApplyAsync(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        EliteStatus? status,
        bool enabled,
        string environment,
        bool allowPublishing,
        CancellationToken cancellationToken = default);
}

public sealed class EddnPublisher : IEddnPublisher
{
    private const int MaximumPayloadBytes = 1024 * 1024;
    private const int MaximumResponseDetailBytes = 2048;
    private static readonly Uri LiveEndpoint =
        new("https://eddn.edcd.io:4430/upload/");
    private static readonly Uri BetaEndpoint =
        new("https://beta.eddn.edcd.io:4431/upload/");
    private static readonly Uri DevEndpoint =
        new("https://dev.eddn.edcd.io:4432/upload/");
    private static readonly HttpClient SharedClient = CreateSharedClient();
    private static readonly IReadOnlyDictionary<string, Uri> DefaultEndpoints =
        new Dictionary<string, Uri>(StringComparer.Ordinal)
        {
            ["live"] = LiveEndpoint,
            ["beta"] = BetaEndpoint,
            ["dev"] = DevEndpoint,
        };
    private static readonly IReadOnlyDictionary<string, string> SchemaByEvent =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CodexEntry"] = "codexentry/1",
            ["ApproachSettlement"] = "approachsettlement/1",
            ["DockingGranted"] = "dockinggranted/1",
            ["DockingDenied"] = "dockingdenied/1",
            ["FSSAllBodiesFound"] = "fssallbodiesfound/1",
            ["FSSBodySignals"] = "fssbodysignals/1",
            ["FSSDiscoveryScan"] = "fssdiscoveryscan/1",
            ["NavBeaconScan"] = "navbeaconscan/1",
            ["NavRoute"] = "navroute/1",
            ["ScanBaryCentre"] = "scanbarycentre/1",
            ["Docked"] = "journal/1",
            ["FSDJump"] = "journal/1",
            ["CarrierJump"] = "journal/1",
            ["Scan"] = "journal/1",
            ["Location"] = "journal/1",
            ["SAASignalsFound"] = "journal/1",
        };
    private static readonly IReadOnlyDictionary<string, string[]>
        RequiredMessageFieldsByEvent =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["CodexEntry"] =
                    ["timestamp", "event", "System", "StarPos", "SystemAddress", "EntryID"],
                ["ApproachSettlement"] =
                    ["timestamp", "event", "StarSystem", "StarPos", "SystemAddress", "Name", "BodyID", "BodyName", "Latitude", "Longitude"],
                ["DockingGranted"] =
                    ["timestamp", "event", "MarketID", "StationName"],
                ["DockingDenied"] =
                    ["timestamp", "event", "MarketID", "StationName", "Reason"],
                ["FSSAllBodiesFound"] =
                    ["timestamp", "event", "SystemName", "StarPos", "SystemAddress", "Count"],
                ["FSSBodySignals"] =
                    ["timestamp", "event", "StarSystem", "StarPos", "SystemAddress", "BodyID", "Signals"],
                ["FSSDiscoveryScan"] =
                    ["timestamp", "event", "SystemName", "StarPos", "SystemAddress", "BodyCount", "NonBodyCount"],
                ["NavBeaconScan"] =
                    ["timestamp", "event", "StarSystem", "StarPos", "SystemAddress", "NumBodies"],
                ["NavRoute"] = ["timestamp", "event", "Route"],
                ["ScanBaryCentre"] =
                    ["timestamp", "event", "StarSystem", "StarPos", "SystemAddress", "BodyID"],
                ["Docked"] =
                    ["timestamp", "event", "StarSystem", "StarPos", "SystemAddress"],
                ["FSDJump"] =
                    ["timestamp", "event", "StarSystem", "StarPos", "SystemAddress"],
                ["CarrierJump"] =
                    ["timestamp", "event", "StarSystem", "StarPos", "SystemAddress"],
                ["Scan"] =
                    ["timestamp", "event", "StarSystem", "StarPos", "SystemAddress"],
                ["Location"] =
                    ["timestamp", "event", "StarSystem", "StarPos", "SystemAddress"],
                ["SAASignalsFound"] =
                    ["timestamp", "event", "StarSystem", "StarPos", "SystemAddress"],
            };

    private readonly HttpClient client;
    private readonly IReadOnlyDictionary<string, Uri> endpoints;
    private readonly string softwareVersion;
    private string? commanderName;
    private string? gameVersion;
    private string? gameBuild;
    private bool? horizons;
    private bool? odyssey;
    private string? systemName;
    private long? systemAddress;
    private double[]? starPosition;
    private string? journalBodyName;
    private int? journalBodyId;
    private string? statusBodyName;

    public EddnPublisher(
        string softwareVersion,
        HttpClient? client = null,
        IReadOnlyDictionary<string, Uri>? endpoints = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(softwareVersion);
        this.softwareVersion = softwareVersion.Trim();
        this.client = client ?? SharedClient;
        this.endpoints = endpoints ?? DefaultEndpoints;
        ValidateEndpoints(this.endpoints);
    }

    public async Task<EddnPublicationResult> ApplyAsync(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        EliteStatus? status,
        bool enabled,
        string environment,
        bool allowPublishing,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        statusBodyName = string.IsNullOrWhiteSpace(status?.BodyName)
            ? null
            : status.BodyName;
        var published = new List<EddnPublishedEvent>();
        var warnings = new List<string>();
        var normalizedEnvironment = NormalizeEnvironment(environment);

        foreach (var journalEvent in journalEvents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UpdateContext(journalEvent);
            if (!enabled
                || !allowPublishing
                || !SchemaByEvent.TryGetValue(
                    journalEvent.EventName,
                    out var schema))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(commanderName))
            {
                warnings.Add(
                    $"EDDN skipped {journalEvent.EventName}: the current Commander is unknown.");
                continue;
            }

            if (!TryCreateMessage(journalEvent, out var message, out var reason))
            {
                warnings.Add(
                    $"EDDN skipped {journalEvent.EventName}: {reason}");
                continue;
            }

            var schemaRef = "https://eddn.edcd.io/schemas/" + schema;
            if (normalizedEnvironment != "live")
            {
                schemaRef += "/test";
            }

            var payload = new JsonObject
            {
                ["$schemaRef"] = schemaRef,
                ["header"] = new JsonObject
                {
                    ["uploaderID"] = commanderName,
                    ["gameversion"] = gameVersion ?? string.Empty,
                    ["gamebuild"] = gameBuild ?? string.Empty,
                    ["softwareName"] = "SrvSurvey",
                    ["softwareVersion"] = softwareVersion,
                },
                ["message"] = message,
            };
            var payloadBytes = Encoding.UTF8.GetBytes(payload.ToJsonString());
            if (payloadBytes.Length > MaximumPayloadBytes)
            {
                warnings.Add(
                    $"EDDN skipped {journalEvent.EventName}: the encoded message exceeded 1 MiB.");
                continue;
            }

            try
            {
                await SendAsync(
                        endpoints[normalizedEnvironment],
                        payloadBytes,
                        cancellationToken)
                    .ConfigureAwait(false);
                published.Add(new EddnPublishedEvent(
                    journalEvent.EventName,
                    schemaRef,
                    normalizedEnvironment));
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                warnings.Add(
                    $"EDDN could not publish {journalEvent.EventName}: the request timed out.");
            }
            catch (HttpRequestException exception)
            {
                warnings.Add(
                    $"EDDN could not publish {journalEvent.EventName}: {exception.Message}");
            }
            catch (IOException exception)
            {
                warnings.Add(
                    $"EDDN could not publish {journalEvent.EventName}: {exception.Message}");
            }
        }

        return new EddnPublicationResult(published, warnings);
    }

    public static string NormalizeEnvironment(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "live" => "live",
            "beta" => "beta",
            _ => "dev",
        };
    }

    private async Task SendAsync(
        Uri endpoint,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = new ByteArrayContent(payload),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(
            "application/json")
        {
            CharSet = "utf-8",
        };
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue(
            "SrvSurvey",
            softwareVersion));
        using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = await ReadBoundedResponseAsync(
                response.Content,
                cancellationToken)
            .ConfigureAwait(false);
        var summary = string.IsNullOrWhiteSpace(detail)
            ? response.ReasonPhrase ?? "request failed"
            : detail.Replace('\r', ' ').Replace('\n', ' ').Trim();
        throw new HttpRequestException(
            $"HTTP {(int)response.StatusCode} ({summary})",
            inner: null,
            response.StatusCode);
    }

    private bool TryCreateMessage(
        JournalEventEnvelope journalEvent,
        out JsonObject message,
        out string reason)
    {
        message = JsonNode.Parse(journalEvent.RawJson) as JsonObject
            ?? throw new InvalidDataException(
                "A parsed journal event was not a JSON object.");
        RemoveProperties(message, static name =>
            name.EndsWith("_Localised", StringComparison.Ordinal));
        AddGameFlags(message);

        switch (journalEvent.EventName)
        {
            case "CodexEntry":
                RemoveProperties(message, static name =>
                    name is "BodyID" or "BodyName" or "IsNewEntry"
                        or "NewTraitsDiscovered");
                if (!HasMatchingSystem(
                    journalEvent.Payload,
                    "System",
                    requireSystemName: true))
                {
                    reason = "the event did not match the current system context.";
                    return false;
                }

                message["StarPos"] = CreateStarPositionNode();
                if (statusBodyName is not null)
                {
                    message["BodyName"] = statusBodyName;
                    if (journalBodyId is not null
                        && string.Equals(
                            statusBodyName,
                            journalBodyName,
                            StringComparison.Ordinal))
                    {
                        message["BodyID"] = journalBodyId.Value;
                    }
                }

                break;

            case "ApproachSettlement":
            case "FSSBodySignals":
            case "NavBeaconScan":
            case "SAASignalsFound":
                if (!HasMatchingSystem(journalEvent.Payload))
                {
                    reason = "the event did not match the current system context.";
                    return false;
                }

                message["StarSystem"] = systemName;
                message["StarPos"] = CreateStarPositionNode();
                break;

            case "FSSAllBodiesFound":
                if (!HasMatchingSystem(
                    journalEvent.Payload,
                    "SystemName",
                    requireSystemName: true))
                {
                    reason = "the event did not match the current system context.";
                    return false;
                }

                message["StarPos"] = CreateStarPositionNode();
                break;

            case "ScanBaryCentre":
                if (!HasMatchingSystem(
                    journalEvent.Payload,
                    "StarSystem",
                    requireSystemName: true))
                {
                    reason = "the event did not match the current system context.";
                    return false;
                }

                message["StarPos"] = CreateStarPositionNode();
                break;

            case "FSSDiscoveryScan":
                RemoveProperties(message, static name => name == "Progress");
                if (!HasMatchingSystem(
                    journalEvent.Payload,
                    "SystemName",
                    requireSystemName: true))
                {
                    reason = "the event did not match the current system context.";
                    return false;
                }

                message["StarPos"] = CreateStarPositionNode();
                break;

            case "Docked":
                RemoveProperties(message, static name =>
                    name is "Wanted" or "ActiveFine" or "CockpitBreach");
                if (!HasMatchingSystem(
                    journalEvent.Payload,
                    "StarSystem",
                    requireSystemName: true))
                {
                    reason = "the event did not match the current system context.";
                    return false;
                }

                message["StarPos"] = CreateStarPositionNode();
                break;

            case "Scan":
                if (!HasMatchingSystem(
                    journalEvent.Payload,
                    "StarSystem",
                    requireSystemName: true))
                {
                    reason = "the event did not match the current system context.";
                    return false;
                }

                message["StarPos"] = CreateStarPositionNode();
                break;

            case "FSDJump":
            case "CarrierJump":
                RemoveProperties(message, static name =>
                    name is "Wanted" or "BoostUsed" or "FuelLevel"
                        or "FuelUsed" or "JumpDist" or "HappiestSystem"
                        or "HomeSystem" or "MyReputation"
                        or "SquadronFaction");
                if (!HasCompleteEventSystem(journalEvent.Payload))
                {
                    reason = "the destination system context was incomplete.";
                    return false;
                }

                break;

            case "Location":
                RemoveProperties(message, static name =>
                    name is "Wanted" or "Latitude" or "Longitude"
                        or "HappiestSystem" or "HomeSystem"
                        or "MyReputation" or "SquadronFaction");
                if (!HasCompleteEventSystem(journalEvent.Payload))
                {
                    reason = "the location system context was incomplete.";
                    return false;
                }

                break;

            case "DockingGranted":
            case "DockingDenied":
            case "NavRoute":
                break;
        }

        var missingFields = new List<string>();
        foreach (var field in RequiredMessageFieldsByEvent[
            journalEvent.EventName])
        {
            if (!HasRequiredValue(message, field))
            {
                missingFields.Add(field);
            }
        }

        if (missingFields.Count > 0)
        {
            reason = "required field(s) were missing: "
                + string.Join(", ", missingFields)
                + ".";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private void UpdateContext(JournalEventEnvelope journalEvent)
    {
        var payload = journalEvent.Payload;
        switch (journalEvent.EventName)
        {
            case "Fileheader":
                commanderName = null;
                horizons = null;
                odyssey = null;
                systemName = null;
                systemAddress = null;
                starPosition = null;
                ClearJournalBody();
                gameVersion = GetString(payload, "gameversion");
                gameBuild = GetString(payload, "build");
                break;

            case "Commander":
                commanderName = GetString(payload, "Name") ?? commanderName;
                break;

            case "LoadGame":
                commanderName = GetString(payload, "Commander")
                    ?? commanderName;
                gameVersion ??= GetString(payload, "gameversion");
                gameBuild ??= GetString(payload, "build");
                horizons = GetBoolean(payload, "Horizons") ?? horizons;
                odyssey = GetBoolean(payload, "Odyssey") ?? odyssey;
                break;

            case "Location":
                UpdateSystemContext(payload);
                UpdateJournalBody(payload);
                break;

            case "FSDJump":
            case "CarrierJump":
                ClearJournalBody();
                UpdateSystemContext(payload);
                break;

            case "ApproachBody":
                UpdateJournalBody(payload);
                break;

            case "LeaveBody":
                ClearJournalBody();
                break;
        }
    }

    private void UpdateSystemContext(JsonElement payload)
    {
        var nextAddress = GetInt64(payload, "SystemAddress");
        if (nextAddress is not > 0)
        {
            return;
        }

        if (systemAddress != nextAddress)
        {
            systemName = null;
            starPosition = null;
            ClearJournalBody();
        }

        systemAddress = nextAddress;
        systemName = GetString(payload, "StarSystem") ?? systemName;
        starPosition = GetCoordinate(payload, "StarPos") ?? starPosition;
    }

    private void UpdateJournalBody(JsonElement payload)
    {
        var name = GetString(payload, "BodyName")
            ?? GetString(payload, "Body");
        var id = GetInt32(payload, "BodyID");
        if (!string.IsNullOrWhiteSpace(name) && id is >= 0)
        {
            journalBodyName = name;
            journalBodyId = id;
        }
    }

    private void ClearJournalBody()
    {
        journalBodyName = null;
        journalBodyId = null;
    }

    private bool HasMatchingSystem(
        JsonElement payload,
        string? systemNameProperty = null,
        bool requireSystemName = false)
    {
        if (!HasCompleteSystemContext())
        {
            return false;
        }

        var eventAddress = GetInt64(payload, "SystemAddress");
        if (eventAddress is not > 0 || eventAddress != systemAddress)
        {
            return false;
        }

        if (systemNameProperty is null)
        {
            return true;
        }

        var eventSystemName = GetString(payload, systemNameProperty);
        return (!requireSystemName && eventSystemName is null)
            || (!string.IsNullOrWhiteSpace(eventSystemName)
                && string.Equals(
                eventSystemName,
                systemName,
                StringComparison.OrdinalIgnoreCase));
    }

    private bool HasCompleteEventSystem(JsonElement payload)
    {
        return HasMatchingSystem(
                payload,
                "StarSystem",
                requireSystemName: true)
            && GetCoordinate(payload, "StarPos") is not null;
    }

    private bool HasCompleteSystemContext()
    {
        return !string.IsNullOrWhiteSpace(systemName)
            && systemAddress is > 0
            && starPosition is { Length: 3 };
    }

    private JsonArray CreateStarPositionNode()
    {
        return new JsonArray(
            starPosition![0],
            starPosition[1],
            starPosition[2]);
    }

    private void AddGameFlags(JsonObject message)
    {
        if (horizons is not null)
        {
            message["horizons"] = horizons.Value;
        }

        if (odyssey is not null)
        {
            message["odyssey"] = odyssey.Value;
        }
    }

    private static void RemoveProperties(
        JsonNode? node,
        Func<string, bool> shouldRemove)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject.ToArray())
            {
                if (shouldRemove(property.Key))
                {
                    jsonObject.Remove(property.Key);
                }
                else
                {
                    RemoveProperties(property.Value, shouldRemove);
                }
            }
        }
        else if (node is JsonArray jsonArray)
        {
            foreach (var item in jsonArray)
            {
                RemoveProperties(item, shouldRemove);
            }
        }
    }

    private static bool HasRequiredValue(JsonObject message, string name)
    {
        if (!message.TryGetPropertyValue(name, out var value)
            || value is null)
        {
            return false;
        }

        return value is not JsonValue jsonValue
            || !jsonValue.TryGetValue<string>(out var text)
            || !string.IsNullOrWhiteSpace(text);
    }

    private static string? GetString(JsonElement payload, string name)
    {
        return payload.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static long? GetInt64(JsonElement payload, string name)
    {
        return payload.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var result)
                ? result
                : null;
    }

    private static int? GetInt32(JsonElement payload, string name)
    {
        return payload.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var result)
                ? result
                : null;
    }

    private static bool? GetBoolean(JsonElement payload, string name)
    {
        return payload.TryGetProperty(name, out var value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? value.GetBoolean()
                : null;
    }

    private static double[]? GetCoordinate(JsonElement payload, string name)
    {
        if (!payload.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.Array
            || value.GetArrayLength() != 3)
        {
            return null;
        }

        var coordinate = new double[3];
        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number
                || !item.TryGetDouble(out var number)
                || !double.IsFinite(number))
            {
                return null;
            }

            coordinate[index++] = number;
        }

        return coordinate;
    }

    private static async Task<string> ReadBoundedResponseAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(
                cancellationToken)
            .ConfigureAwait(false);
        var buffer = new byte[MaximumResponseDetailBytes];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(
                    buffer.AsMemory(total, buffer.Length - total),
                    cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return Encoding.UTF8.GetString(buffer, 0, total);
    }

    private static void ValidateEndpoints(
        IReadOnlyDictionary<string, Uri> endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        foreach (var environment in new[] { "dev", "beta", "live" })
        {
            if (!endpoints.TryGetValue(environment, out var endpoint)
                || !endpoint.IsAbsoluteUri
                || endpoint.Scheme != Uri.UriSchemeHttps)
            {
                throw new ArgumentException(
                    $"The EDDN {environment} endpoint must be an absolute HTTPS URI.",
                    nameof(endpoints));
            }
        }
    }

    private static HttpClient CreateSharedClient()
    {
        return new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20),
        };
    }
}

public sealed record EddnPublishedEvent(
    string EventName,
    string SchemaReference,
    string Environment);

public sealed record EddnPublicationResult(
    IReadOnlyList<EddnPublishedEvent> Published,
    IReadOnlyList<string> Warnings);
