using System.Text.Json.Serialization;
using SrvSurvey.Core.Network;

namespace SrvSurvey.Core.Search;

public sealed class SpanshStarSystemResolver : IStarSystemResolver
{
    private const int MaximumResponseBytes = 8 * 1024 * 1024;

    private static readonly Uri DefaultApiBaseUri = new("https://spansh.co.uk/api/");
    private static readonly HttpClient SharedClient = CreateSharedClient();

    private readonly HttpClient client;
    private readonly Uri apiBaseUri;

    public SpanshStarSystemResolver(
        HttpClient? client = null,
        Uri? apiBaseUri = null)
    {
        this.client = client ?? SharedClient;
        this.apiBaseUri = apiBaseUri ?? DefaultApiBaseUri;
    }

    public async Task<IReadOnlyList<StarSystemReference>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var requestUri = new Uri(
            apiBaseUri,
            "systems/field_values/system_names?q="
                + Uri.EscapeDataString(query.Trim()));
        using var response = await client.GetAsync(
                requestUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var payload = await BoundedHttpContent.ReadFromJsonAsync<SpanshSystemResponse>(
                response.Content,
                MaximumResponseBytes,
                "The Spansh system-name response",
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (payload?.Systems is null)
        {
            return [];
        }

        var results = new List<StarSystemReference>(payload.Systems.Count);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var system in payload.Systems)
        {
            if (string.IsNullOrWhiteSpace(system.Name)
                || !double.IsFinite(system.X)
                || !double.IsFinite(system.Y)
                || !double.IsFinite(system.Z)
                || !names.Add(system.Name))
            {
                continue;
            }

            results.Add(new StarSystemReference(
                system.Name,
                system.SystemAddress,
                new GalacticCoordinate(system.X, system.Y, system.Z)));
        }

        return results;
    }

    private static HttpClient CreateSharedClient()
    {
        var sharedClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
        sharedClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "SrvSurvey-Avalonia/1.0");
        return sharedClient;
    }

    private sealed record SpanshSystemResponse(
        [property: JsonPropertyName("min_max")]
        IReadOnlyList<SpanshSystem>? Systems);

    private sealed record SpanshSystem(
        [property: JsonPropertyName("id64")]
        long SystemAddress,
        string Name,
        double X,
        double Y,
        double Z);
}
