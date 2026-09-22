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
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(origin, route))
        {
            Content = JsonContent.Create(
                new
                {
                    filters,
                    reference_system = reference.Trim(),
                    size = SpanshRoutes.PageSize(route),
                    page,
                    sort = new[]
                    {
                        new Dictionary<string, object>
                        {
                            [sort] = new { direction = sort == "distance" ? "asc" : "desc" },
                        },
                    },
                }
            ),
        };
        try
        {
            using HttpResponseMessage response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
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
}
