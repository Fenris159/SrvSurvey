using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SrvSurvey.Core.Search;

public interface INearestSystemsClient
{
    Task<NearestSystemsSearchResult> SearchCanonnAsync(
        GalacticCoordinate reference,
        string biologicalSignal,
        string commanderName,
        int limit = 5,
        CancellationToken cancellationToken = default);

    Task<NearestSystemsSearchResult> SearchMissingVariantsAsync(
        GalacticCoordinate reference,
        string genus,
        string species,
        IReadOnlyList<string> variantColors,
        CancellationToken cancellationToken = default);
}

public sealed class NearestSystemsClient : INearestSystemsClient
{
    private static readonly Uri DefaultCanonnBaseUri = new(
        "https://us-central1-canonn-api-236217.cloudfunctions.net/query/");
    private static readonly Uri DefaultSpanshBaseUri = new(
        "https://spansh.co.uk/api/");
    private static readonly HttpClient SharedClient = CreateSharedClient();

    private readonly HttpClient client;
    private readonly Uri canonnBaseUri;
    private readonly Uri spanshBaseUri;

    public NearestSystemsClient(
        HttpClient? client = null,
        Uri? canonnBaseUri = null,
        Uri? spanshBaseUri = null)
    {
        this.client = client ?? SharedClient;
        this.canonnBaseUri = EnsureTrailingSlash(
            canonnBaseUri ?? DefaultCanonnBaseUri);
        this.spanshBaseUri = EnsureTrailingSlash(
            spanshBaseUri ?? DefaultSpanshBaseUri);
    }

    public async Task<NearestSystemsSearchResult> SearchCanonnAsync(
        GalacticCoordinate reference,
        string biologicalSignal,
        string commanderName,
        int limit = 5,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(biologicalSignal);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 25);
        var uri = CreateCanonnUri(
            "nearest/codex",
            new Dictionary<string, string>
            {
                ["x"] = reference.X.ToString(
                    "R",
                    CultureInfo.InvariantCulture),
                ["y"] = reference.Y.ToString(
                    "R",
                    CultureInfo.InvariantCulture),
                ["z"] = reference.Z.ToString(
                    "R",
                    CultureInfo.InvariantCulture),
                ["name"] = biologicalSignal.Trim(),
                ["limit"] = limit.ToString(CultureInfo.InvariantCulture),
            });
        using var response = await client.GetAsync(
            uri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<CanonnNearest>(
                cancellationToken: cancellationToken)
            .ConfigureAwait(false)
            ?? throw new HttpRequestException(
                "Canonn returned an empty nearest-system response.");
        var nearest = (payload.Nearest ?? [])
            .Where(entry => !string.IsNullOrWhiteSpace(entry.System)
                && double.IsFinite(entry.Distance)
                && double.IsFinite(entry.X)
                && double.IsFinite(entry.Y)
                && double.IsFinite(entry.Z))
            .Take(limit)
            .ToArray();
        var rows = await Task.WhenAll(nearest.Select(entry =>
            CreateCanonnRowAsync(entry, commanderName, cancellationToken)))
            .ConfigureAwait(false);
        return new NearestSystemsSearchResult(rows, null);
    }

    public async Task<NearestSystemsSearchResult> SearchMissingVariantsAsync(
        GalacticCoordinate reference,
        string genus,
        string species,
        IReadOnlyList<string> variantColors,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(genus);
        ArgumentException.ThrowIfNullOrWhiteSpace(species);
        ArgumentNullException.ThrowIfNull(variantColors);
        var variants = variantColors
            .Where(color => !string.IsNullOrWhiteSpace(color))
            .Select(color => PascalFirst(color.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (variants.Length == 0)
        {
            throw new ArgumentException(
                "At least one biological variant color is required.",
                nameof(variantColors));
        }

        var request = new SpanshBodiesRequest(
            new SpanshFilters(
            [
                new SpanshLandmarkFilter(
                    PascalFirst(genus.Trim()),
                    [PascalWords(species.Trim())],
                    variants),
            ]),
            [new SpanshSort(new SpanshSortDirection("asc"))],
            10,
            0,
            new SpanshReference(reference.X, reference.Y, reference.Z));
        using var response = await client.PostAsJsonAsync(
                new Uri(spanshBaseUri, "bodies/search"),
                request,
                cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<SpanshBodies>(
                cancellationToken: cancellationToken)
            .ConfigureAwait(false)
            ?? throw new HttpRequestException(
                "Spansh returned an empty bodies-search response.");
        var rows = (payload.Results ?? [])
            .Where(body => !string.IsNullOrWhiteSpace(body.SystemName)
                && double.IsFinite(body.Distance)
                && double.IsFinite(body.SystemX)
                && double.IsFinite(body.SystemY)
                && double.IsFinite(body.SystemZ))
            .GroupBy(body => body.SystemName!, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(5)
            .Select(body => new NearestSystemSearchRow(
                body.SystemName!,
                body.Distance,
                CreateSpanshNotes(body, species.Trim()),
                new GalacticCoordinate(
                    body.SystemX,
                    body.SystemY,
                    body.SystemZ),
                body.SystemId64 > 0 ? body.SystemId64 : null,
                NearestSystemSource.Spansh))
            .ToArray();
        return new NearestSystemsSearchResult(
            rows,
            string.IsNullOrWhiteSpace(payload.SearchReference)
                ? null
                : payload.SearchReference);
    }

    public static string SummarizeCanonnSystemPoi(
        IReadOnlyList<CanonnCodexEntry> codexEntries)
    {
        ArgumentNullException.ThrowIfNull(codexEntries);
        var distinctSignals = codexEntries
            .Select(entry => entry.EntryId)
            .ToHashSet();
        var backup = $"System bio signals: {distinctSignals.Count:N0}";
        var summary = new Dictionary<string, HashSet<string>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var entry in codexEntries)
        {
            if (!string.Equals(
                    entry.HudCategory,
                    "Biology",
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(entry.Body))
            {
                return backup;
            }

            var body = entry.Body.Replace(" ", string.Empty);
            if (!summary.TryGetValue(body, out var signals))
            {
                signals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                summary[body] = signals;
            }

            if (!string.IsNullOrWhiteSpace(entry.EnglishName))
            {
                signals.Add(entry.EnglishName);
            }
        }

        if (summary.Count == 0)
        {
            return "No bio signals in system";
        }

        return "Body " + string.Join(
            ", ",
            summary.Select(pair =>
                $"{pair.Key}: {pair.Value.Count:N0} signals"));
    }

    private async Task<NearestSystemSearchRow> CreateCanonnRowAsync(
        CanonnNearestEntry entry,
        string commanderName,
        CancellationToken cancellationToken)
    {
        string notes;
        try
        {
            var uri = CreateCanonnUri(
                "getSystemPoi",
                new Dictionary<string, string>
                {
                    ["system"] = entry.System!,
                    ["odyssey"] = "Y",
                    ["cmdr"] = commanderName?.Trim() ?? string.Empty,
                });
            using var response = await client.GetAsync(
                uri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var poi = await response.Content.ReadFromJsonAsync<CanonnSystemPoi>(
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            notes = poi is null
                ? "System details unavailable"
                : SummarizeCanonnSystemPoi(poi.Codex ?? []);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or JsonException)
        {
            notes = "System details unavailable";
        }

        return new NearestSystemSearchRow(
            entry.System!,
            entry.Distance,
            notes,
            new GalacticCoordinate(entry.X, entry.Y, entry.Z),
            null,
            NearestSystemSource.Canonn);
    }

    private Uri CreateCanonnUri(
        string relativePath,
        IReadOnlyDictionary<string, string> query)
    {
        var builder = new UriBuilder(new Uri(canonnBaseUri, relativePath))
        {
            Query = string.Join(
                "&",
                query.Select(pair =>
                    $"{Uri.EscapeDataString(pair.Key)}="
                    + Uri.EscapeDataString(pair.Value))),
        };
        return builder.Uri;
    }

    private static string CreateSpanshNotes(
        SpanshBody body,
        string species)
    {
        var colors = (body.Landmarks ?? [])
            .Where(landmark => string.Equals(
                landmark.Subtype,
                species,
                StringComparison.Ordinal))
            .Select(landmark => landmark.Variant)
            .Where(variant => !string.IsNullOrWhiteSpace(variant))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var prefix = string.Join(", ", colors);
        var bodyName = body.Name?.Replace(
            body.SystemName + " ",
            string.Empty,
            StringComparison.Ordinal) ?? "Unknown";
        var notes = $"{prefix} - body: {bodyName}, dist to arrival: "
            + FormatLightSeconds(body.DistanceToArrival);
        var signalCount = (body.Signals ?? [])
            .FirstOrDefault(signal => string.Equals(
                signal.Name,
                "Biological",
                StringComparison.Ordinal))?.Count;
        return signalCount > 0
            ? notes + $", {signalCount:N0} bio signals"
            : notes;
    }

    private static string FormatLightSeconds(double distance)
    {
        return distance > 1_000
            ? $"{distance / 1_000:N1}k LS"
            : $"{distance:N0} LS";
    }

    private static string PascalFirst(string value)
    {
        return value.Length == 0
            ? string.Empty
            : char.ToUpperInvariant(value[0]) + value[1..];
    }

    private static string PascalWords(string value)
    {
        return string.Join(
            ' ',
            value.Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(word => char.ToUpperInvariant(word[0])
                    + word[1..].ToLowerInvariant()));
    }

    private static Uri EnsureTrailingSlash(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri)
        {
            throw new ArgumentException("The service URI must be absolute.");
        }

        return uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? uri
            : new Uri(uri.AbsoluteUri + "/");
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

    private sealed record CanonnNearest(
        IReadOnlyList<CanonnNearestEntry>? Nearest);

    private sealed record CanonnNearestEntry(
        double Distance,
        string? System,
        double X,
        double Y,
        double Z);

    private sealed record CanonnSystemPoi(
        IReadOnlyList<CanonnCodexEntry>? Codex);

    private sealed record SpanshBodiesRequest(
        SpanshFilters Filters,
        IReadOnlyList<SpanshSort> Sort,
        int Size,
        int Page,
        [property: JsonPropertyName("reference_coords")]
        SpanshReference ReferenceCoordinates);

    private sealed record SpanshFilters(
        IReadOnlyList<SpanshLandmarkFilter> Landmarks);

    private sealed record SpanshLandmarkFilter(
        string Type,
        IReadOnlyList<string> Subtype,
        IReadOnlyList<string> Variant);

    private sealed record SpanshSort(
        [property: JsonPropertyName("distance")]
        SpanshSortDirection Distance);

    private sealed record SpanshSortDirection(string Direction);

    private sealed record SpanshReference(double X, double Y, double Z);

    private sealed record SpanshBodies(
        [property: JsonPropertyName("search_reference")]
        string? SearchReference,
        IReadOnlyList<SpanshBody>? Results);

    private sealed record SpanshBody(
        double Distance,
        [property: JsonPropertyName("distance_to_arrival")]
        double DistanceToArrival,
        string? Name,
        IReadOnlyList<SpanshSignal>? Signals,
        IReadOnlyList<SpanshLandmark>? Landmarks,
        [property: JsonPropertyName("system_id64")]
        long SystemId64,
        [property: JsonPropertyName("system_name")]
        string? SystemName,
        [property: JsonPropertyName("system_x")]
        double SystemX,
        [property: JsonPropertyName("system_y")]
        double SystemY,
        [property: JsonPropertyName("system_z")]
        double SystemZ);

    private sealed record SpanshSignal(string? Name, int Count);

    private sealed record SpanshLandmark(string? Subtype, string? Variant);
}

public sealed record NearestSystemsSearchResult(
    IReadOnlyList<NearestSystemSearchRow> Rows,
    string? SpanshSearchReference);

public sealed record NearestSystemSearchRow(
    string SystemName,
    double Distance,
    string Notes,
    GalacticCoordinate Coordinate,
    long? SystemAddress,
    NearestSystemSource Source);

public enum NearestSystemSource
{
    Canonn,
    Spansh,
}

public sealed record CanonnCodexEntry(
    string? Body,
    [property: JsonPropertyName("english_name")]
    string? EnglishName,
    [property: JsonPropertyName("entryid")]
    long? EntryId,
    [property: JsonPropertyName("hud_category")]
    string? HudCategory);
