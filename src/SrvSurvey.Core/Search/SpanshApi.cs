using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SrvSurvey.Core.Network;

namespace SrvSurvey.Core.Search;

public static class SpanshRoutes
{
    public const string Bodies = "bodies/search";
    public const string Systems = "systems/search";
    public const string Stations = "stations/search";

    public static int PageSize(string route) => route == Stations ? 20 : 100;
}

public sealed class SpanshApi
{
    public const int MaximumResponseBytes = 8 * 1024 * 1024;
    private const int MaximumAttempts = 3;
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromSeconds(30);
    public static readonly Uri Origin = new("https://spansh.co.uk/api/");

    private readonly HttpClient client;
    private readonly Uri origin;
    private readonly Action<string, Exception>? onFailure;

    public SpanshApi(HttpClient client, Uri? origin = null, Action<string, Exception>? onFailure = null)
    {
        this.client = client;
        this.origin = origin ?? Origin;
        this.onFailure = onFailure;
    }

    public async Task<JsonDocument> SearchAsync(
        string route,
        string reference,
        Dictionary<string, object> filters,
        int page,
        string sort = "distance",
        int maximumBytes = MaximumResponseBytes,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        var payload = new
        {
            filters,
            reference_system = reference.Trim(),
            size = SpanshRoutes.PageSize(route),
            page,
            sort = new[]
            {
                new Dictionary<string, object> { [sort] = new { direction = sort == "distance" ? "asc" : "desc" } },
            },
        };
        try
        {
            for (int attempt = 1; attempt <= MaximumAttempts; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(origin, route))
                {
                    Content = JsonContent.Create(payload),
                };
                using HttpResponseMessage response = await client
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
                if (attempt < MaximumAttempts && IsTransient(response.StatusCode))
                {
                    await Task.Delay(RetryDelay(response, attempt), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                return await BoundedHttpContent
                    .ReadJsonDocumentAsync(
                        response.Content,
                        Math.Min(maximumBytes, MaximumResponseBytes),
                        "Mining search response",
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }

            throw new InvalidOperationException("Spansh search exhausted its attempts without a response.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or InvalidDataException)
        {
            try
            {
                onFailure?.Invoke(route, ex);
            }
            catch (Exception)
            {
                // Diagnostics must never replace the provider failure.
            }

            throw;
        }
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status
            is HttpStatusCode.RequestTimeout
                or HttpStatusCode.TooManyRequests
                or HttpStatusCode.InternalServerError
                or HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout;

    internal static TimeSpan RetryDelay(HttpResponseMessage response, int attempt)
    {
        TimeSpan? requested =
            response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow);
        if (requested is { } serverDelay && serverDelay > TimeSpan.Zero)
        {
            return serverDelay;
        }

        double seconds = Math.Min(2 * Math.Pow(2, attempt - 1), MaximumRetryDelay.TotalSeconds);
        return TimeSpan.FromSeconds(seconds * (0.8 + Random.Shared.NextDouble() * 0.4));
    }
}
