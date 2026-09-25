using System.Text.Json;
using System.Text.Json.Serialization;

namespace SrvSurvey.Core.Search;

public interface ISystemNameSuggestionClient
{
    Task<IReadOnlyList<SystemNameSuggestion>> SearchAsync(string query, CancellationToken cancellationToken = default);
}

public sealed record SystemNameSuggestion(string Name, long SystemAddress, string Source);

public sealed class ArdentSystemNameSuggestionClient : ISystemNameSuggestionClient, IDisposable
{
    private const int MaximumResponseBytes = 2 * 1024 * 1024;
    private const int MaximumSuggestions = 15;
    private static readonly HttpClient SharedClient = CreateSharedClient();

    private readonly ArdentApi api;

    public ArdentSystemNameSuggestionClient(HttpClient? client = null, Uri? apiBaseUri = null)
    {
        api = new ArdentApi(client ?? SharedClient, apiBaseUri);
    }

    public void Dispose() => api.Dispose();

    public async Task<IReadOnlyList<SystemNameSuggestion>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        string normalized = query.Trim();
        if (normalized.Length < 3)
        {
            return [];
        }

        using JsonDocument document = await api.GetAsync(
                ArdentRoutes.SystemName(normalized),
                MaximumResponseBytes,
                "The Ardent system-name response",
                cancellationToken
            )
            .ConfigureAwait(false);
        IReadOnlyList<ArdentSystemSuggestion>? payload = JsonSerializer.Deserialize<
            IReadOnlyList<ArdentSystemSuggestion>
        >(document.RootElement);

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return (payload ?? [])
            .Where(entry =>
                entry.SystemAddress > 0 && !string.IsNullOrWhiteSpace(entry.SystemName) && names.Add(entry.SystemName)
            )
            .Take(MaximumSuggestions)
            .Select(entry => new SystemNameSuggestion(entry.SystemName.Trim(), entry.SystemAddress, "Ardent"))
            .ToArray();
    }

    private static HttpClient CreateSharedClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SrvSurvey-Avalonia/1.0");
        return client;
    }

    private sealed record ArdentSystemSuggestion(
        [property: JsonPropertyName("systemAddress")] long SystemAddress,
        [property: JsonPropertyName("systemName")] string SystemName
    );
}
