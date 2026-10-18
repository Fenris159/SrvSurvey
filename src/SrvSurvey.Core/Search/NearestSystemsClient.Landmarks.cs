using System.Net.Http.Json;
using System.Text.Json.Serialization;
using SrvSurvey.Core.Network;

namespace SrvSurvey.Core.Search;

public sealed partial class NearestSystemsClient
{
    /// <summary>Searches one landmark predicate and recalls pages until twenty unique systems or the end.</summary>
    public async Task<NearestSystemsSearchResult> SearchLandmarksAsync(
        GalacticCoordinate reference,
        CodexLandmarkFilter filter,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(filter);
        var predicate = new Dictionary<string, object>();
        AddField(predicate, "type", string.IsNullOrWhiteSpace(filter.Type) ? [] : [filter.Type.Trim()]);
        AddField(predicate, "subtype", filter.Subtypes);
        AddField(predicate, "variant", filter.Variants);
        if (!predicate.ContainsKey("type") && !predicate.ContainsKey("subtype"))
        {
            throw new ArgumentException("Choose a landmark type or subtype before searching.", nameof(filter));
        }
        var payload = new
        {
            reference_coords = new
            {
                x = reference.X,
                y = reference.Y,
                z = reference.Z,
            },
            size = ResultLimit,
            page = 0,
            sort = new[] { new { distance = new { direction = "asc" } } },
            filters = new { landmarks = new[] { predicate } },
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(spanshBaseUri, "bodies/search"))
        {
            Content = JsonContent.Create(payload),
        };
        using HttpResponseMessage response = await client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        SpanshBodies first =
            await BoundedHttpContent
                .ReadFromJsonAsync<SpanshBodies>(
                    response.Content,
                    MaximumResponseBytes,
                    "Spansh Codex search",
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false)
            ?? throw new InvalidDataException("Spansh returned an empty search response.");
        var bodies = new List<SpanshBody>();
        var seenBodies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        SpanshBodies page = first;
        for (int index = 0; index < SpanshRoutes.SearchResultLimit / ResultLimit; index++)
        {
            int previousCount = bodies.Count;
            bodies.AddRange(
                (page.Results ?? []).Where(body =>
                    !string.IsNullOrWhiteSpace(body.SystemName)
                    && ValidLocation(body.Distance, body.SystemX, body.SystemY, body.SystemZ)
                    && seenBodies.Add(body.SystemName + "\n" + body.Name)
                )
            );
            if (
                bodies.Count == previousCount
                || bodies.DistinctBy(body => body.SystemName, StringComparer.OrdinalIgnoreCase).Count() >= ResultLimit
                || (page.Results?.Count ?? 0) < ResultLimit
                || (first.Count.HasValue && (index + 1) * ResultLimit >= first.Count.Value)
                || string.IsNullOrWhiteSpace(first.SearchReference)
            )
            {
                break;
            }

            page = await ReadAsync<SpanshBodies>(
                    new Uri(
                        spanshBaseUri,
                        $"bodies/search/recall/{Uri.EscapeDataString(first.SearchReference)}/{index + 1}"
                    ),
                    "Spansh Codex search page",
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        NearestSystemSearchRow[] rows = bodies
            .OrderBy(body => body.Distance)
            .GroupBy(body => body.SystemName!, StringComparer.OrdinalIgnoreCase)
            .Take(ResultLimit)
            .Select(group => CreateSpanshRow(group, filter, first.SearchReference))
            .ToArray();
        return new(rows, first.SearchReference);
    }

    private static void AddField(Dictionary<string, object> predicate, string name, IReadOnlyList<string> values)
    {
        string[] selected = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        IReadOnlyList<string> known = name switch
        {
            "type" => CodexCatalog.Types,
            "subtype" => CodexCatalog.Subtypes,
            _ => CodexCatalog.Variants,
        };
        selected = selected
            .Select(value =>
                known.FirstOrDefault(item => item.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? value
            )
            .ToArray();
        if (selected.Length > 0)
        {
            predicate[name] = new { value = selected };
        }
    }

    private static NearestSystemSearchRow CreateSpanshRow(
        IEnumerable<SpanshBody> bodies,
        CodexLandmarkFilter filter,
        string? searchReference
    )
    {
        SpanshBody first = bodies.First();
        return new(
            first.SystemName!,
            first.Distance,
            string.Join(
                "; ",
                bodies.Select(body => SpanshNotes(body, filter)).Distinct(StringComparer.OrdinalIgnoreCase)
            ),
            new(first.SystemX, first.SystemY, first.SystemZ),
            first.SystemId64 > 0 ? first.SystemId64 : null,
            NearestSystemSource.Spansh
        )
        {
            SpanshSearchReference = searchReference,
        };
    }

    private static string SpanshNotes(SpanshBody body, CodexLandmarkFilter filter)
    {
        string[] entries = (body.Landmarks ?? [])
            .Where(landmark =>
                (
                    string.IsNullOrWhiteSpace(filter.Type)
                    || filter.Type.Equals(landmark.Type, StringComparison.OrdinalIgnoreCase)
                )
                && (
                    filter.Subtypes.Count == 0
                    || filter.Subtypes.Contains(landmark.Subtype, StringComparer.OrdinalIgnoreCase)
                )
                && (
                    filter.Variants.Count == 0
                    || filter.Variants.Contains(landmark.Variant, StringComparer.OrdinalIgnoreCase)
                )
            )
            .Select(landmark =>
                string.IsNullOrWhiteSpace(landmark.Variant)
                    ? landmark.Subtype ?? landmark.Type ?? "Codex feature"
                    : $"{landmark.Subtype} - {landmark.Variant}"
            )
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string name =
            body.Name == body.SystemName
                ? "main star"
                : body.Name?.Replace(body.SystemName + " ", string.Empty, StringComparison.Ordinal) ?? "unknown body";
        string distance =
            body.DistanceToArrival > 1_000
                ? $"{body.DistanceToArrival / 1_000:N1}k LS"
                : $"{body.DistanceToArrival:N0} LS";
        string features = entries.Length > 0 ? string.Join(", ", entries) : "Matching Codex feature";
        return $"{features} · body: {name}, arrival: {distance}";
    }

    private sealed record SpanshBodies(
        [property: JsonPropertyName("search_reference")] string? SearchReference,
        IReadOnlyList<SpanshBody>? Results,
        int? Count
    );

    private sealed record SpanshBody(
        double Distance,
        [property: JsonPropertyName("distance_to_arrival")] double DistanceToArrival,
        string? Name,
        IReadOnlyList<SpanshLandmark>? Landmarks,
        [property: JsonPropertyName("system_id64")] long SystemId64,
        [property: JsonPropertyName("system_name")] string? SystemName,
        [property: JsonPropertyName("system_x")] double SystemX,
        [property: JsonPropertyName("system_y")] double SystemY,
        [property: JsonPropertyName("system_z")] double SystemZ
    );

    private sealed record SpanshLandmark(string? Type, string? Subtype, string? Variant);
}
