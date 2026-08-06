using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Network;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Routes;

public interface ISpanshRouteClient
{
    Task<IReadOnlyList<FollowRouteHop>> GetRouteAsync(
        SpanshRouteReference route,
        CancellationToken cancellationToken = default);
}

public sealed class SpanshRouteClient : ISpanshRouteClient
{
    private const int MaximumResponseBytes = 32 * 1024 * 1024;

    private static readonly Uri DefaultApiBaseUri = new("https://spansh.co.uk/api/");
    private static readonly HttpClient SharedClient = CreateSharedClient();

    private readonly HttpClient client;
    private readonly Uri apiBaseUri;
    private readonly TimeSpan pollInterval;
    private readonly TimeSpan maximumPollInterval;
    private readonly TimeSpan maximumWait;
    private readonly bool useExponentialPolling;

    public SpanshRouteClient(
        HttpClient? client = null,
        Uri? apiBaseUri = null,
        TimeSpan? pollInterval = null,
        TimeSpan? maximumWait = null,
        TimeSpan? maximumPollInterval = null)
    {
        this.client = client ?? SharedClient;
        this.apiBaseUri = apiBaseUri ?? DefaultApiBaseUri;
        this.pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(500);
        this.maximumWait = maximumWait ?? TimeSpan.FromMinutes(10);
        this.maximumPollInterval = maximumPollInterval
            ?? (pollInterval is null ? TimeSpan.FromSeconds(16) : this.pollInterval);
        useExponentialPolling = pollInterval is null;
        if (this.pollInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pollInterval),
                "The polling interval cannot be negative.");
        }

        if (this.maximumWait < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumWait),
                "The maximum wait cannot be negative.");
        }

        if (this.maximumPollInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumPollInterval),
                "The maximum polling interval cannot be negative.");
        }
    }

    public async Task<IReadOnlyList<FollowRouteHop>> GetRouteAsync(
        SpanshRouteReference route,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(route);
        var requestUri = new Uri(
            apiBaseUri,
            "results/" + route.JobId.ToString("D").ToUpperInvariant());
        var timer = Stopwatch.StartNew();
        var nextPollInterval = pollInterval;
        string? lastState = null;
        string? lastStatus = null;
        while (true)
        {
            using var response = await client.GetAsync(
                    requestUri,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            JsonObject root;
            try
            {
                root = (await BoundedHttpContent.ReadJsonNodeAsync(
                        response.Content,
                        MaximumResponseBytes,
                        "The Spansh route response",
                        cancellationToken)
                    .ConfigureAwait(false)) as JsonObject
                    ?? throw InvalidResponse("the root value is not an object");
            }
            catch (JsonException exception)
            {
                throw InvalidResponse("the response is not valid JSON", exception);
            }

            lastState = GetString(root, "state");
            lastStatus = GetString(root, "status");
            if (string.Equals(lastStatus, "error", StringComparison.OrdinalIgnoreCase)
                || string.Equals(lastStatus, "failed", StringComparison.OrdinalIgnoreCase))
            {
                throw InvalidResponse(
                    $"job {route.JobId:D} returned status '{lastStatus}'");
            }

            if (string.Equals(lastStatus, "ok", StringComparison.OrdinalIgnoreCase)
                && root["result"] is not null)
            {
                return ParseRoute(root, route.Kind);
            }

            if (string.Equals(lastState, "completed", StringComparison.OrdinalIgnoreCase))
            {
                throw InvalidResponse(
                    $"job {route.JobId:D} completed with status "
                        + $"'{lastStatus ?? "unknown"}'");
            }

            if (timer.Elapsed >= maximumWait)
            {
                throw new TimeoutException(
                    $"Spansh route {route.JobId:D} did not complete within "
                        + $"{maximumWait.TotalSeconds:N0} seconds "
                        + $"(state: {lastState ?? "unknown"}, "
                        + $"status: {lastStatus ?? "unknown"}).");
            }

            var remaining = maximumWait - timer.Elapsed;
            var delay = nextPollInterval <= remaining
                ? nextPollInterval
                : remaining;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            if (useExponentialPolling && nextPollInterval < maximumPollInterval)
            {
                nextPollInterval = TimeSpan.FromTicks(Math.Min(
                    nextPollInterval.Ticks * 2,
                    maximumPollInterval.Ticks));
            }
        }
    }

    private static IReadOnlyList<FollowRouteHop> ParseRoute(
        JsonObject root,
        SpanshRouteKind kind)
    {
        var result = root["result"];
        return kind switch
        {
            SpanshRouteKind.Generic => ParseDetectedRoute(result),
            SpanshRouteKind.Riches or SpanshRouteKind.Exobiology =>
                ParseRows(result as JsonArray, kind),
            SpanshRouteKind.Tourist or SpanshRouteKind.Neutron =>
                ParseRows(result?["system_jumps"] as JsonArray, kind),
            SpanshRouteKind.Galaxy
                or SpanshRouteKind.FleetCarrier
                or SpanshRouteKind.Colonisation =>
                ParseRows(result?["jumps"] as JsonArray, kind),
            SpanshRouteKind.Trade => ParseTradeRoute(result as JsonArray),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static IReadOnlyList<FollowRouteHop> ParseDetectedRoute(
        JsonNode? result)
    {
        if (result is JsonArray rows)
        {
            var isTradeRoute = rows
                .OfType<JsonObject>()
                .Any(row => row["source"] is JsonObject
                    || row["destination"] is JsonObject);
            if (isTradeRoute)
            {
                return ParseTradeRoute(rows);
            }

            return ParseRows(
                rows,
                LooksLikeExobiology(rows)
                    ? SpanshRouteKind.Exobiology
                    : SpanshRouteKind.Generic);
        }

        if (result is JsonObject route)
        {
            if (route["system_jumps"] is JsonArray systemJumps)
            {
                return ParseRows(systemJumps, SpanshRouteKind.Neutron);
            }

            if (route["jumps"] is JsonArray jumps)
            {
                return ParseRows(jumps, SpanshRouteKind.Generic);
            }
        }

        throw InvalidResponse("the result has no recognized route hops");
    }

    private static bool LooksLikeExobiology(JsonArray rows)
    {
        return rows
            .OfType<JsonObject>()
            .Select(row => row["bodies"])
            .OfType<JsonArray>()
            .SelectMany(bodies => bodies.OfType<JsonObject>())
            .Any(body => body.ContainsKey("landmarks"));
    }

    private static List<FollowRouteHop> ParseRows(
        JsonArray? rows,
        SpanshRouteKind kind)
    {
        if (rows is null)
        {
            throw InvalidResponse(
                $"the {kind.ToString().ToLowerInvariant()} result has no route hops");
        }

        var hops = new List<FollowRouteHop>(rows.Count);
        for (var index = 0; index < rows.Count; index++)
        {
            if (rows[index] is not JsonObject row)
            {
                throw InvalidResponse($"route hop {index + 1} is not an object");
            }

            hops.Add(ParseHop(row, index, kind));
        }

        if (!ShouldAggregateBodyHops(kind, hops))
        {
            return hops;
        }

        return AggregateBodyRouteHops(hops);
    }

    private static bool ShouldAggregateBodyHops(
        SpanshRouteKind kind,
        IReadOnlyList<FollowRouteHop> hops)
    {
        if (kind is SpanshRouteKind.Riches or SpanshRouteKind.Exobiology)
        {
            return true;
        }

        return kind == SpanshRouteKind.Generic
            && hops.Any(hop => hop.BioTargets.Count > 0);
    }

    private static List<FollowRouteHop> ParseTradeRoute(
        JsonArray? legs)
    {
        if (legs is null)
        {
            throw InvalidResponse("the trade result has no route legs");
        }

        if (legs.Count == 0)
        {
            return [];
        }

        var hops = new List<FollowRouteHop>(legs.Count + 1);
        for (var index = 0; index < legs.Count; index++)
        {
            if (legs[index] is not JsonObject leg)
            {
                throw InvalidResponse($"trade route leg {index + 1} is not an object");
            }

            var source = ParseTradeStop(
                leg["source"] as JsonObject,
                index,
                "source");
            if (hops.Count == 0 || !IsSameSystem(hops[^1], source))
            {
                hops.Add(source);
            }

            hops.Add(ParseTradeStop(
                leg["destination"] as JsonObject,
                index,
                "destination"));
        }

        return hops;
    }

    private static bool IsSameSystem(
        FollowRouteHop left,
        FollowRouteHop right)
    {
        if (left.SystemAddress is not null && right.SystemAddress is not null)
        {
            return left.SystemAddress == right.SystemAddress;
        }

        return string.Equals(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static FollowRouteHop ParseTradeStop(
        JsonObject? stop,
        int legIndex,
        string stopKind)
    {
        if (stop is null)
        {
            throw InvalidResponse(
                $"trade route leg {legIndex + 1} has no {stopKind}");
        }

        var systemName = GetString(stop, "system");
        if (string.IsNullOrWhiteSpace(systemName))
        {
            throw InvalidResponse(
                $"trade route leg {legIndex + 1} has no {stopKind} system");
        }

        var station = GetString(stop, "station");
        return new FollowRouteHop(
            systemName,
            GetInt64(stop, "system_id64"),
            ParsePosition(stop, $"trade route leg {legIndex + 1} {stopKind}"),
            string.IsNullOrWhiteSpace(station) ? null : $"Station: {station}",
            false,
            false);
    }

    private static FollowRouteHop ParseHop(
        JsonObject root,
        int index,
        SpanshRouteKind kind)
    {
        var name = GetString(root, "system") ?? GetString(root, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            throw InvalidResponse(
                $"route hop {index + 1} has no valid system name");
        }

        var bodyTargets = SupportsBodyTargets(kind)
            ? ParseBodyTargets(
                name,
                root["bodies"],
                kind == SpanshRouteKind.Exobiology)
            : null;
        var notes = GetString(root, "notes") ?? GetString(root, "note");
        if (GetBoolean(root, "must_restock") == true)
        {
            notes = string.IsNullOrWhiteSpace(notes)
                ? "Carrier tritium restock required"
                : notes + "\r\nCarrier tritium restock required";
        }

        return new FollowRouteHop(
            name,
            GetInt64(root, "id64"),
            ParsePosition(root, $"route hop {index + 1}"),
            notes,
            GetBoolean(root, "must_refuel") == true,
            GetBoolean(root, "has_neutron") == true
                || GetBoolean(root, "neutron_star") == true,
            bodyTargets,
            kind == SpanshRouteKind.FleetCarrier
                ? ParseCarrierHop(root)
                : null);
    }

    private static FollowRouteCarrierHop ParseCarrierHop(JsonObject root)
    {
        return new FollowRouteCarrierHop(
            GetDouble(root, "distance"),
            GetDouble(root, "distance_to_destination"),
            GetDouble(root, "fuel_remaining"),
            GetDouble(root, "tritium_in_market"),
            GetDouble(root, "fuel_used"),
            GetBoolean(root, "has_icy_ring") == true,
            GetBoolean(root, "is_system_pristine") == true,
            GetBoolean(root, "must_restock") == true,
            GetDouble(root, "restock_amount"));
    }

    private static bool SupportsBodyTargets(SpanshRouteKind kind)
    {
        return kind is not SpanshRouteKind.FleetCarrier
            and not SpanshRouteKind.Colonisation
            and not SpanshRouteKind.Trade;
    }

    private static List<FollowRouteHop> AggregateBodyRouteHops(
        List<FollowRouteHop> hops)
    {
        if (hops.Count < 2)
        {
            return hops;
        }

        var result = new List<FollowRouteHop>(hops.Count);
        foreach (var hop in hops)
        {
            var existingIndex = result.FindIndex(existing =>
                IsSameSystem(existing, hop));
            if (existingIndex < 0)
            {
                result.Add(hop);
                continue;
            }

            var existing = result[existingIndex];
            result[existingIndex] = existing with
            {
                Position = existing.Position ?? hop.Position,
                Notes = MergeNotes(existing.Notes, hop.Notes),
                Refuel = existing.Refuel || hop.Refuel,
                Neutron = existing.Neutron || hop.Neutron,
                Bio = MergeBioTargets(existing.BioTargets, hop.BioTargets),
            };
        }

        return result;
    }

    private static List<FollowRouteBioTarget> MergeBioTargets(
        IReadOnlyList<FollowRouteBioTarget> existing,
        IReadOnlyList<FollowRouteBioTarget> incoming)
    {
        var result = existing.ToList();
        foreach (var target in incoming)
        {
            var index = result.FindIndex(candidate =>
                (target.BodyId is not null && candidate.BodyId == target.BodyId)
                || string.Equals(
                    candidate.BodyName,
                    target.BodyName,
                    StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                result.Add(target);
                continue;
            }

            var current = result[index];
            result[index] = current with
            {
                Species = current.Species
                    .Concat(target.Species)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                IsCompleted = current.IsCompleted || target.IsCompleted,
                Subtype = current.Subtype ?? target.Subtype,
                DistanceToArrivalLs = current.DistanceToArrivalLs
                    ?? target.DistanceToArrivalLs,
                EstimatedScanValue = MaxNullable(
                    current.EstimatedScanValue,
                    target.EstimatedScanValue),
                EstimatedMappingValue = MaxNullable(
                    current.EstimatedMappingValue,
                    target.EstimatedMappingValue),
                EstimatedBiologyValue = MergeBiologyValues(current, target),
                IsTerraformable = current.IsTerraformable
                    || target.IsTerraformable,
                IsBiological = current.IsBiological || target.IsBiological,
            };
        }

        return result;
    }

    private static long? MaxNullable(long? first, long? second)
    {
        if (first is null)
        {
            return second;
        }

        if (second is null)
        {
            return first;
        }

        return Math.Max(first.Value, second.Value);
    }

    private static long? MergeBiologyValues(
        FollowRouteBioTarget first,
        FollowRouteBioTarget second)
    {
        if (first.EstimatedBiologyValue is null)
        {
            return second.EstimatedBiologyValue;
        }

        if (second.EstimatedBiologyValue is null)
        {
            return first.EstimatedBiologyValue;
        }

        var sharesSpecies = first.Species.Intersect(
            second.Species,
            StringComparer.OrdinalIgnoreCase).Any();
        return sharesSpecies
            ? Math.Max(
                first.EstimatedBiologyValue.Value,
                second.EstimatedBiologyValue.Value)
            : checked(
                first.EstimatedBiologyValue.Value
                + second.EstimatedBiologyValue.Value);
    }

    private static string? MergeNotes(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first))
        {
            return string.IsNullOrWhiteSpace(second) ? null : second;
        }

        if (string.IsNullOrWhiteSpace(second)
            || string.Equals(first, second, StringComparison.Ordinal))
        {
            return first;
        }

        return first + "\r\n" + second;
    }

    private static GalacticCoordinate? ParsePosition(
        JsonObject root,
        string context)
    {
        var x = GetDouble(root, "x");
        var y = GetDouble(root, "y");
        var z = GetDouble(root, "z");
        if (x is not null && y is not null && z is not null)
        {
            try
            {
                return new GalacticCoordinate(x.Value, y.Value, z.Value);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                throw InvalidResponse(
                    $"{context} has invalid coordinates",
                    exception);
            }
        }

        return null;
    }

    private static List<FollowRouteBioTarget>? ParseBodyTargets(
        string systemName,
        JsonNode? node,
        bool isBiologicalRoute)
    {
        if (node is null)
        {
            return null;
        }

        if (node is not JsonArray bodies)
        {
            throw InvalidResponse("a body route hop has invalid bodies");
        }

        var result = new List<FollowRouteBioTarget>(bodies.Count);
        foreach (var bodyNode in bodies
            .Select((body, index) => new
            {
                Index = index,
                Body = body as JsonObject,
            })
            .Where(item => item.Body is not null)
            .OrderBy(item => GetInt64(item.Body!, "id") ?? long.MaxValue)
            .ThenBy(item => item.Index))
        {
            result.Add(ParseBodyTarget(
                systemName,
                bodyNode.Index,
                bodyNode.Body!,
                isBiologicalRoute));
        }

        return result.Count == 0 ? null : result;
    }

    private static FollowRouteBioTarget ParseBodyTarget(
        string systemName,
        int bodyIndex,
        JsonObject body,
        bool isBiologicalRoute)
    {
        var bodyName = GetString(body, "name")
            ?? GetString(body, "body_name")
            ?? throw InvalidResponse(
                $"a route body {bodyIndex + 1} has no name");

        var species = ReadSpecies(body, out var speciesValues);
        var biologyValue = ReadBiologyValue(body, speciesValues);

        return new FollowRouteBioTarget(
            NormalizeBodyName(systemName, bodyName),
            GetInt64Any(body, "id", "body_id", "bodyId"),
            species,
            Subtype: GetStringAny(body, "subtype", "body_subtype", "type"),
            DistanceToArrivalLs: GetDoubleAny(
                body,
                "distance_to_arrival",
                "distance_to_arrival_ls",
                "distanceToArrival"),
            EstimatedScanValue: GetInt64Any(
                body,
                "estimated_scan_value",
                "scan_value",
                "estimatedScanValue"),
            EstimatedMappingValue: GetInt64Any(
                body,
                "estimated_mapping_value",
                "mapping_value",
                "estimatedMappingValue"),
            EstimatedBiologyValue: biologyValue,
            IsTerraformable: IsTerraformable(body),
            IsBiological: isBiologicalRoute
                || species.Count > 0
                || biologyValue is not null);
    }

    private static List<string> ReadSpecies(
        JsonObject body,
        out Dictionary<string, long> speciesValues)
    {
        speciesValues = [];
        if (body["landmarks"] is not JsonArray landmarks)
        {
            if (body["landmarks"] is not null)
            {
                throw InvalidResponse("a route body has invalid landmarks");
            }

            return [];
        }

        return ReadSpeciesFromLandmarks(landmarks, speciesValues);
    }

    private static List<string> ReadSpeciesFromLandmarks(
        JsonArray landmarks,
        Dictionary<string, long> speciesValues)
    {
        var species = new List<string>();
        foreach (var landmarkNode in landmarks)
        {
            if (landmarkNode is not JsonObject landmark)
            {
                throw InvalidResponse(
                    "a route body landmark is not an object");
            }

            var name = GetString(landmark, "subtype")
                ?? GetString(landmark, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                throw InvalidResponse(
                    "a route body landmark has no subtype");
            }

            var trimmed = name.Trim();
            if (!species.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                species.Add(trimmed);
            }

            var value = GetInt64Any(
                landmark,
                "value",
                "estimated_value",
                "landmark_value",
                "scan_value");
            if (value is not null
                && (!speciesValues.TryGetValue(trimmed, out var existing)
                    || value > existing))
            {
                speciesValues[trimmed] = value.Value;
            }
        }

        return species;
    }

    private static long? ReadBiologyValue(
        JsonObject body,
        Dictionary<string, long> speciesValues)
    {
        return GetInt64Any(
                body,
                "landmark_value",
                "estimated_biology_value",
                "estimated_bio_value",
                "biology_value")
            ?? (speciesValues.Count == 0
                ? null
                : speciesValues.Values.Sum());
    }

    private static string NormalizeBodyName(string systemName, string bodyName)
    {
        var prefix = systemName.TrimEnd() + " ";
        return bodyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? bodyName[prefix.Length..].Trim()
            : bodyName.Trim();
    }

    private static bool IsTerraformable(JsonObject body)
    {
        if (GetBooleanAny(body, "is_terraformable", "terraformable") == true)
        {
            return true;
        }

        var state = GetStringAny(
            body,
            "terraforming_state",
            "terraformingState");
        return !string.IsNullOrWhiteSpace(state)
            && !string.Equals(state, "Not terraformable", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(state, "None", StringComparison.OrdinalIgnoreCase);
    }

    private static InvalidDataException InvalidResponse(
        string detail,
        Exception? innerException = null)
    {
        return new InvalidDataException(
            $"The Spansh route response is invalid: {detail}.",
            innerException);
    }

    private static bool? GetBoolean(JsonObject root, string propertyName)
    {
        return root[propertyName] is JsonValue value
            && value.TryGetValue<bool>(out var result)
                ? result
                : null;
    }

    private static bool? GetBooleanAny(
        JsonObject root,
        params string[] propertyNames)
    {
        var node = FindValue(root, propertyNames);
        if (node is null)
        {
            return null;
        }

        if (node.TryGetValue<bool>(out var boolean))
        {
            return boolean;
        }

        return node.TryGetValue<string>(out var text)
            && bool.TryParse(text, out boolean)
                ? boolean
                : null;
    }

    private static string? GetString(JsonObject root, string propertyName)
    {
        return root[propertyName] is JsonValue value
            && value.TryGetValue<string>(out var result)
                ? result
                : null;
    }

    private static string? GetStringAny(
        JsonObject root,
        params string[] propertyNames)
    {
        var node = FindValue(root, propertyNames);
        return node is not null && node.TryGetValue<string>(out var result)
            ? (string.IsNullOrWhiteSpace(result)) switch
            {
                true => null,
                false => result.Trim()
            }
            : null;
    }

    private static long? GetInt64(JsonObject root, string propertyName)
    {
        return root[propertyName] is JsonValue value
            && value.TryGetValue<long>(out var result)
                ? result
                : null;
    }

    private static long? GetInt64Any(
        JsonObject root,
        params string[] propertyNames)
    {
        var node = FindValue(root, propertyNames);
        if (node is null)
        {
            return null;
        }

        if (node.TryGetValue<long>(out var integer))
        {
            return integer;
        }

        if (node.TryGetValue<double>(out var number)
            && double.IsFinite(number)
            && number is >= long.MinValue and <= long.MaxValue)
        {
            return (long)Math.Round(number, MidpointRounding.AwayFromZero);
        }

        return node.TryGetValue<string>(out var text)
            && double.TryParse(
                text,
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out number)
            && double.IsFinite(number)
            && number is >= long.MinValue and <= long.MaxValue
                ? (long)Math.Round(number, MidpointRounding.AwayFromZero)
                : null;
    }

    private static double? GetDouble(JsonObject root, string propertyName)
    {
        if (root[propertyName] is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<double>(out var result))
        {
            return result;
        }

        return value.TryGetValue<long>(out var integer) ? integer : null;
    }

    private static double? GetDoubleAny(
        JsonObject root,
        params string[] propertyNames)
    {
        var node = FindValue(root, propertyNames);
        if (node is null)
        {
            return null;
        }

        if (node.TryGetValue<double>(out var number)
            && double.IsFinite(number))
        {
            return number;
        }

        if (node.TryGetValue<long>(out var integer))
        {
            return integer;
        }

        return node.TryGetValue<string>(out var text)
            && double.TryParse(
                text,
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out number)
            && double.IsFinite(number)
                ? number
                : null;
    }

    private static JsonValue? FindValue(
        JsonObject root,
        IReadOnlyList<string> propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (root[propertyName] is JsonValue exact)
            {
                return exact;
            }

            var match = root.FirstOrDefault(candidate => string.Equals(
                candidate.Key,
                propertyName,
                StringComparison.OrdinalIgnoreCase));
            if (match.Value is JsonValue value)
            {
                return value;
            }
        }

        return null;
    }

    private static HttpClient CreateSharedClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "SrvSurvey-Avalonia/1.0");
        return client;
    }
}
