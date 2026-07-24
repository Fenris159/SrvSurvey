using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    private static readonly Uri DefaultApiBaseUri = new("https://spansh.co.uk/api/");
    private static readonly HttpClient SharedClient = CreateSharedClient();

    private readonly HttpClient client;
    private readonly Uri apiBaseUri;
    private readonly TimeSpan pollInterval;
    private readonly TimeSpan maximumWait;

    public SpanshRouteClient(
        HttpClient? client = null,
        Uri? apiBaseUri = null,
        TimeSpan? pollInterval = null,
        TimeSpan? maximumWait = null)
    {
        this.client = client ?? SharedClient;
        this.apiBaseUri = apiBaseUri ?? DefaultApiBaseUri;
        this.pollInterval = pollInterval ?? TimeSpan.FromSeconds(5);
        this.maximumWait = maximumWait ?? TimeSpan.FromSeconds(60);
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
        string? lastState = null;
        string? lastStatus = null;
        while (true)
        {
            using var response = await client.GetAsync(requestUri, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(
                    cancellationToken)
                .ConfigureAwait(false);
            JsonObject root;
            try
            {
                root = (await JsonNode.ParseAsync(
                        stream,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false)) as JsonObject
                    ?? throw InvalidResponse("the root value is not an object");
            }
            catch (JsonException exception)
            {
                throw InvalidResponse("the response is not valid JSON", exception);
            }

            lastState = GetString(root, "state");
            lastStatus = GetString(root, "status");
            if (string.Equals(
                lastState,
                "completed",
                StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(
                    lastStatus,
                    "ok",
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw InvalidResponse(
                        $"job {route.JobId:D} completed with status "
                            + $"'{lastStatus ?? "unknown"}'");
                }

                return ParseRoute(root, route.Kind);
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
            var delay = pollInterval <= remaining ? pollInterval : remaining;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static IReadOnlyList<FollowRouteHop> ParseRoute(
        JsonObject root,
        SpanshRouteKind kind)
    {
        JsonArray? rows = kind switch
        {
            SpanshRouteKind.Generic => root["result"] as JsonArray,
            SpanshRouteKind.Tourist or SpanshRouteKind.Neutron =>
                root["result"]?["system_jumps"] as JsonArray,
            SpanshRouteKind.Galaxy => root["result"]?["jumps"] as JsonArray,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
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

        return hops;
    }

    private static FollowRouteHop ParseHop(
        JsonObject root,
        int index,
        SpanshRouteKind kind)
    {
        var nameProperty = kind is SpanshRouteKind.Tourist
            or SpanshRouteKind.Neutron
                ? "system"
                : "name";
        var name = GetString(root, nameProperty);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw InvalidResponse(
                $"route hop {index + 1} has no valid {nameProperty}");
        }

        GalacticCoordinate? position = null;
        var x = GetDouble(root, "x");
        var y = GetDouble(root, "y");
        var z = GetDouble(root, "z");
        if (x is not null && y is not null && z is not null)
        {
            try
            {
                position = new GalacticCoordinate(x.Value, y.Value, z.Value);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                throw InvalidResponse(
                    $"route hop {index + 1} has invalid coordinates",
                    exception);
            }
        }

        return new FollowRouteHop(
            name,
            GetInt64(root, "id64"),
            position,
            kind == SpanshRouteKind.Generic
                ? SummarizeGenericRouteBodies(name, root["bodies"])
                : null,
            kind == SpanshRouteKind.Galaxy
                && GetBoolean(root, "must_refuel") == true,
            kind == SpanshRouteKind.Galaxy
                && GetBoolean(root, "has_neutron") == true);
    }

    private static string? SummarizeGenericRouteBodies(
        string systemName,
        JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is not JsonArray bodies)
        {
            throw InvalidResponse("a generic route hop has invalid bodies");
        }

        var summaries = new Dictionary<string, List<string>>(
            StringComparer.Ordinal);
        var seen = new Dictionary<string, HashSet<string>>(
            StringComparer.Ordinal);
        var orderedBodies = bodies
            .Select((body, index) => new
            {
                Index = index,
                Body = body as JsonObject,
            })
            .OrderBy(item => item.Body is null
                ? long.MaxValue
                : GetInt64(item.Body, "id") ?? long.MaxValue)
            .ThenBy(item => item.Index);
        foreach (var item in orderedBodies)
        {
            var body = item.Body
                ?? throw InvalidResponse("a generic route body is not an object");
            var bodyName = GetString(body, "name")
                ?? throw InvalidResponse("a generic route body has no name");
            var shortName = bodyName
                .Replace(systemName + " ", string.Empty, StringComparison.Ordinal)
                .Replace(" ", string.Empty, StringComparison.Ordinal);
            if (body["landmarks"] is null)
            {
                AddSummary(summaries, seen, "Scan", shortName);
                continue;
            }

            if (body["landmarks"] is not JsonArray landmarks)
            {
                throw InvalidResponse("a generic route body has invalid landmarks");
            }

            foreach (var landmarkNode in landmarks)
            {
                if (landmarkNode is not JsonObject landmark
                    || string.IsNullOrWhiteSpace(GetString(landmark, "subtype")))
                {
                    throw InvalidResponse("a generic route landmark has no subtype");
                }

                AddSummary(
                    summaries,
                    seen,
                    GetString(landmark, "subtype")!,
                    shortName);
            }
        }

        return summaries.Count == 0
            ? null
            : string.Join(
                "\r\n",
                summaries.Select(summary =>
                    $"{summary.Key}: [{string.Join(", ", summary.Value)}]"));
    }

    private static void AddSummary(
        Dictionary<string, List<string>> summaries,
        Dictionary<string, HashSet<string>> seen,
        string label,
        string bodyName)
    {
        if (!summaries.TryGetValue(label, out var names))
        {
            names = [];
            summaries[label] = names;
            seen[label] = new HashSet<string>(StringComparer.Ordinal);
        }

        if (seen[label].Add(bodyName))
        {
            names.Add(bodyName);
        }
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

    private static string? GetString(JsonObject root, string propertyName)
    {
        return root[propertyName] is JsonValue value
            && value.TryGetValue<string>(out var result)
                ? result
                : null;
    }

    private static long? GetInt64(JsonObject root, string propertyName)
    {
        return root[propertyName] is JsonValue value
            && value.TryGetValue<long>(out var result)
                ? result
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
