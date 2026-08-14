using System.Text.Json.Serialization;
using SrvSurvey.Core.Network;

namespace SrvSurvey.Core.Search;

public sealed class EdsmSystemNameSuggestionClient : ISystemNameSuggestionClient
{
    private const int MaximumResponseBytes = 4 * 1024 * 1024;
    private const int MaximumSuggestions = 15;
    private static readonly Uri DefaultApiUri = new(
        "https://www.edsm.net/api-v1/systems");
    private static readonly HttpClient SharedClient = CreateSharedClient();

    private readonly HttpClient client;
    private readonly Uri apiUri;

    public EdsmSystemNameSuggestionClient(
        HttpClient? client = null,
        Uri? apiUri = null)
    {
        this.client = client ?? SharedClient;
        this.apiUri = apiUri ?? DefaultApiUri;
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

        var queryParameter = long.TryParse(
            normalized,
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out var systemAddress)
            && systemAddress > 0
                ? $"systemId64={systemAddress}"
                : $"systemName={Uri.EscapeDataString(normalized)}";
        var uriBuilder = new UriBuilder(apiUri)
        {
            Query = queryParameter + "&showId=1",
        };
        using var response = await client.GetAsync(
                uriBuilder.Uri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var payload = await BoundedHttpContent.ReadFromJsonAsync<
                IReadOnlyList<EdsmSystemSuggestion>>(
                response.Content,
                MaximumResponseBytes,
                "The EDSM system-name response",
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return (payload ?? [])
            .Where(entry => entry.SystemAddress > 0
                && !string.IsNullOrWhiteSpace(entry.Name)
                && names.Add(entry.Name))
            .Take(MaximumSuggestions)
            .Select(entry => new SystemNameSuggestion(
                entry.Name.Trim(),
                entry.SystemAddress,
                "EDSM"))
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

    private sealed record EdsmSystemSuggestion(
        string Name,
        [property: JsonPropertyName("id64")]
        long SystemAddress);
}
