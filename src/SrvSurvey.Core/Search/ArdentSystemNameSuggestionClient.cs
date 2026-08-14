using System.Text.Json.Serialization;
using SrvSurvey.Core.Network;

namespace SrvSurvey.Core.Search;

public interface ISystemNameSuggestionClient
{
    Task<IReadOnlyList<SystemNameSuggestion>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default);
}

public sealed record SystemNameSuggestion(
    string Name,
    long SystemAddress,
    string Source);

public sealed class ArdentSystemNameSuggestionClient : ISystemNameSuggestionClient
{
    private const int MaximumResponseBytes = 2 * 1024 * 1024;
    private const int MaximumSuggestions = 15;
    private static readonly Uri DefaultApiBaseUri = new(
        "https://api.ardent-insight.com/v2/search/system/name/");
    private static readonly HttpClient SharedClient = CreateSharedClient();

    private readonly HttpClient client;
    private readonly Uri apiBaseUri;

    public ArdentSystemNameSuggestionClient(
        HttpClient? client = null,
        Uri? apiBaseUri = null)
    {
        this.client = client ?? SharedClient;
        this.apiBaseUri = apiBaseUri ?? DefaultApiBaseUri;
    }

    public async Task<IReadOnlyList<SystemNameSuggestion>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var normalized = query.Trim();
        if (normalized.Length < 3)
        {
            return [];
        }

        var requestUri = new Uri(
            apiBaseUri,
            Uri.EscapeDataString(normalized));
        using var response = await client.GetAsync(
                requestUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var payload = await BoundedHttpContent.ReadFromJsonAsync<
                IReadOnlyList<ArdentSystemSuggestion>>(
                response.Content,
                MaximumResponseBytes,
                "The Ardent system-name response",
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return (payload ?? [])
            .Where(entry => entry.SystemAddress > 0
                && !string.IsNullOrWhiteSpace(entry.SystemName)
                && names.Add(entry.SystemName))
            .Take(MaximumSuggestions)
            .Select(entry => new SystemNameSuggestion(
                entry.SystemName.Trim(),
                entry.SystemAddress,
                "Ardent"))
            .ToArray();
    }

    private static HttpClient CreateSharedClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "SrvSurvey-Avalonia/1.0");
        return client;
    }

    private sealed record ArdentSystemSuggestion(
        [property: JsonPropertyName("systemAddress")]
        long SystemAddress,
        [property: JsonPropertyName("systemName")]
        string SystemName);
}
