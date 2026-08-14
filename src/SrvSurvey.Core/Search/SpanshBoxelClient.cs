using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using SrvSurvey.Core.Network;

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
    private const int MaximumAttemptsPerPage = 3;
    private const int MaximumPages = 1_000;
    private const int MaximumResponseBytes = 8 * 1024 * 1024;
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromSeconds(30);
    private static readonly Uri DefaultApiBaseUri = new("https://spansh.co.uk/api/");
    private static readonly HttpClient SharedClient = CreateSharedClient();

    private readonly HttpClient client;
    private readonly Uri apiBaseUri;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;

    public SpanshBoxelClient(
        HttpClient? client = null,
        Uri? apiBaseUri = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        this.client = client ?? SharedClient;
        this.apiBaseUri = apiBaseUri ?? DefaultApiBaseUri;
        this.delay = delay ?? Task.Delay;
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
            var payload = await SearchPageAsync(
                    requestUri,
                    boxel,
                    page,
                    cancellationToken)
                .ConfigureAwait(false);
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
                    ParseUpdatedAt(result.UpdatedAt),
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

    private async Task<SpanshSearchResponse> SearchPageAsync(
        Uri requestUri,
        BoxelAddress boxel,
        int page,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaximumAttemptsPerPage; attempt++)
        {
            try
            {
                var request = new SpanshSearchRequest(
                    page,
                    PageSize,
                    [new SpanshSort(new SpanshSortDirection("asc"))],
                    new SpanshFilters(new SpanshNameFilter(boxel.Prefix + "*")));
                using var requestMessage = new HttpRequestMessage(
                    HttpMethod.Post,
                    requestUri)
                {
                    Content = JsonContent.Create(request),
                };
                using var response = await client.SendAsync(
                        requestMessage,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!response.IsSuccessStatusCode
                    && attempt < MaximumAttemptsPerPage
                    && IsTransient(response.StatusCode))
                {
                    await delay(
                            GetRetryDelay(response, attempt),
                            cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                return await BoundedHttpContent.ReadFromJsonAsync<SpanshSearchResponse>(
                        response.Content,
                        MaximumResponseBytes,
                        "The Spansh boxel-search response",
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false)
                    ?? throw new HttpRequestException(
                        "Spansh returned an empty systems search response.");
            }
            catch (Exception exception) when (
                attempt < MaximumAttemptsPerPage
                && !cancellationToken.IsCancellationRequested
                && exception is HttpRequestException or TaskCanceledException)
            {
                await delay(GetRetryDelay(attempt), cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        throw new HttpRequestException(
            $"Spansh failed after {MaximumAttemptsPerPage} attempts for {boxel.Prefix}, page {page + 1}.");
    }

    private static bool IsTransient(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;
    }

    private static TimeSpan GetRetryDelay(
        HttpResponseMessage response,
        int attempt)
    {
        var retryAfter = response.Headers.RetryAfter;
        var requested = retryAfter?.Delta
            ?? (retryAfter?.Date - DateTimeOffset.UtcNow)
            ?? GetRetryDelay(attempt);
        return requested <= TimeSpan.Zero
            ? TimeSpan.Zero
            : TimeSpan.FromTicks(Math.Min(requested.Ticks, MaximumRetryDelay.Ticks));
    }

    private static TimeSpan GetRetryDelay(int attempt)
    {
        return attempt == 1
            ? TimeSpan.FromMilliseconds(250)
            : TimeSpan.FromSeconds(1);
    }

    private static DateTimeOffset? ParseUpdatedAt(string? value)
    {
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces
                | DateTimeStyles.AssumeUniversal,
            out var timestamp)
                ? timestamp
                : null;
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
        string? UpdatedAt,
        IReadOnlyList<object>? Bodies);
}
