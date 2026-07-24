using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace SrvSurvey.Core.Search;

public interface IBoxelSystemResolver
{
    Task<IReadOnlyList<BoxelSystemObservation>> SearchAsync(
        BoxelAddress boxel,
        CancellationToken cancellationToken = default);
}

public sealed class SpanshBoxelClient : IBoxelSystemResolver
{
    private const int PageSize = 50;
    private const int MaximumPages = 1_000;
    private static readonly Uri DefaultApiBaseUri = new("https://spansh.co.uk/api/");
    private static readonly HttpClient SharedClient = CreateSharedClient();

    private readonly HttpClient client;
    private readonly Uri apiBaseUri;

    public SpanshBoxelClient(
        HttpClient? client = null,
        Uri? apiBaseUri = null)
    {
        this.client = client ?? SharedClient;
        this.apiBaseUri = apiBaseUri ?? DefaultApiBaseUri;
    }

    public async Task<IReadOnlyList<BoxelSystemObservation>> SearchAsync(
        BoxelAddress boxel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(boxel);
        var requestUri = new Uri(apiBaseUri, "systems/search");
        var observations = new Dictionary<string, BoxelSystemObservation>(
            StringComparer.Ordinal);
        var received = 0;

        for (var page = 0; page < MaximumPages; page++)
        {
            var request = new SpanshSearchRequest(
                page,
                PageSize,
                [new SpanshSort(new SpanshSortDirection("asc"))],
                new SpanshFilters(new SpanshNameFilter(boxel.Prefix + "*")));
            using var response = await client.PostAsJsonAsync(
                    requestUri,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadFromJsonAsync<SpanshSearchResponse>(
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false)
                ?? throw new HttpRequestException(
                    "Spansh returned an empty systems search response.");
            var results = payload.Results ?? [];
            received += results.Count;

            foreach (var result in results)
            {
                var resolved = result.Id64 > 0
                    ? BoxelAddress.TryFromSystemAddress(
                        result.Id64,
                        result.Name,
                        out var resultBoxel)
                    : BoxelAddress.TryParse(result.Name, out resultBoxel);
                if (!resolved
                    || resultBoxel is null
                    || !string.Equals(
                        resultBoxel.Prefix,
                        boxel.Prefix,
                        StringComparison.Ordinal)
                    || !double.IsFinite(result.X)
                    || !double.IsFinite(result.Y)
                    || !double.IsFinite(result.Z))
                {
                    continue;
                }

                var observation = new BoxelSystemObservation(
                    resultBoxel,
                    new GalacticCoordinate(result.X, result.Y, result.Z),
                    null,
                    result.UpdatedAt,
                    result.Bodies is { Count: > 0 });
                observations[resultBoxel.GeneratedName] = observation;
            }

            if (results.Count == 0
                || received >= payload.Count
                || results.Count < PageSize)
            {
                return observations.Values
                    .OrderBy(observation => observation.Boxel.N2)
                    .ToArray();
            }
        }

        throw new HttpRequestException(
            $"Spansh returned more than {MaximumPages * PageSize:N0} systems for {boxel.Prefix}.");
    }

    private static HttpClient CreateSharedClient()
    {
        var sharedClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20),
        };
        sharedClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "SrvSurvey-Avalonia/1.0");
        return sharedClient;
    }

    private sealed record SpanshSearchRequest(
        int Page,
        int Size,
        IReadOnlyList<SpanshSort> Sort,
        SpanshFilters Filters);

    private sealed record SpanshSort(
        [property: JsonPropertyName("name")]
        SpanshSortDirection Direction);

    private sealed record SpanshSortDirection(string Direction);

    private sealed record SpanshFilters(SpanshNameFilter Name);

    private sealed record SpanshNameFilter(string Value);

    private sealed record SpanshSearchResponse(
        int Count,
        IReadOnlyList<SpanshSystem>? Results);

    private sealed record SpanshSystem(
        [property: JsonPropertyName("id64")]
        long Id64,
        string? Name,
        double X,
        double Y,
        double Z,
        [property: JsonPropertyName("updated_at")]
        DateTimeOffset? UpdatedAt,
        IReadOnlyList<object>? Bodies);
}
