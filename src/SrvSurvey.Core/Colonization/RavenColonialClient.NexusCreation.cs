using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace SrvSurvey.Core.Colonization;

public sealed record ColonizationNexusDraft(
    string Name,
    string Notes,
    bool IsPrivate,
    IReadOnlyList<string> Systems,
    IReadOnlyList<string> Commanders,
    IReadOnlyList<long> CarrierIds
)
{
    public string? Owner { get; init; }
}

public sealed record ColonizationNexusCarrierMatch
{
    [JsonPropertyName("market_id")]
    public long MarketId { get; init; }
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("carrier_name")]
    public string? CarrierName { get; init; }

    [JsonIgnore]
    public string Label => string.IsNullOrWhiteSpace(CarrierName) ? Name : $"{CarrierName} ({Name})";
}

/// <summary>Creation is acknowledged separately so configuration retries can reuse the returned Nexus ID.</summary>
public interface IRavenColonialNexusWriter
{
    Task<ColonizationNexus> SetNexusSystemsAsync(
        string id,
        IReadOnlyList<string> systems,
        string commander,
        string apiKey,
        CancellationToken cancellationToken
    );
    Task<ColonizationNexus> SetNexusPrivacyAsync(
        string id,
        bool isPrivate,
        string commander,
        string apiKey,
        CancellationToken cancellationToken
    );
    Task<ColonizationNexus> RenameNexusAsync(
        string id,
        string name,
        string commander,
        string apiKey,
        CancellationToken cancellationToken
    );
    Task DeleteNexusAsync(string id, string commander, string apiKey, CancellationToken cancellationToken);
    Task<ColonizationNexus> CreateNexusAsync(
        string name,
        string commander,
        string apiKey,
        CancellationToken cancellationToken
    );
    Task<ColonizationNexus> ConfigureNexusAsync(
        string id,
        ColonizationNexusDraft draft,
        string commander,
        string apiKey,
        CancellationToken cancellationToken
    );
    Task<IReadOnlyList<ColonizationNexusCarrierMatch>> FindNexusCarriersAsync(
        string name,
        string commander,
        string apiKey,
        CancellationToken cancellationToken
    );
}

public sealed partial class RavenColonialClient : IRavenColonialNexusWriter
{
    public Task<ColonizationNexus> SetNexusSystemsAsync(
        string id,
        IReadOnlyList<string> systems,
        string commander,
        string apiKey,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return SendNexusChangeAsync(
            HttpMethod.Post,
            $"{Uri.EscapeDataString(id.Trim())}/setSystems",
            RequiredNexusSystems(systems),
            commander,
            apiKey,
            id.Trim(),
            cancellationToken
        );
    }

    public Task<ColonizationNexus> SetNexusPrivacyAsync(
        string id,
        bool isPrivate,
        string commander,
        string apiKey,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return SendNexusChangeAsync(
            HttpMethod.Post,
            $"{Uri.EscapeDataString(id.Trim())}/setPrivate",
            isPrivate,
            commander,
            apiKey,
            id.Trim(),
            cancellationToken
        );
    }

    public Task<ColonizationNexus> RenameNexusAsync(
        string id,
        string name,
        string commander,
        string apiKey,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return SendNexusChangeAsync(
            HttpMethod.Post,
            $"{Uri.EscapeDataString(id.Trim())}/setName",
            name.Trim(),
            commander,
            apiKey,
            id.Trim(),
            cancellationToken
        );
    }

    public async Task DeleteNexusAsync(string id, string commander, string apiKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        using HttpRequestMessage request = CreateNexusRequest(
            $"api/Chain/delete/{Uri.EscapeDataString(id.Trim())}",
            commander,
            apiKey
        );
        request.Method = HttpMethod.Delete;
        using HttpResponseMessage response = await SendAsync(request, cancellationToken, readSuccessBody: false)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            await ReadRequiredAsync<ColonizationNexus>(response, "delete Nexus", cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public Task<ColonizationNexus> CreateNexusAsync(
        string name,
        string commander,
        string apiKey,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return SendNexusChangeAsync(
            HttpMethod.Put,
            "create",
            new { name = name.Trim() },
            commander,
            apiKey,
            null,
            cancellationToken
        );
    }

    public async Task<ColonizationNexus> ConfigureNexusAsync(
        string id,
        ColonizationNexusDraft draft,
        string commander,
        string apiKey,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentException.ThrowIfNullOrWhiteSpace(commander);
        string[] systems = RequiredNexusSystems(draft.Systems);
        string path = Uri.EscapeDataString(id.Trim());
        Task<ColonizationNexus> UpdateAsync<T>(string operation, T value) =>
            SendNexusChangeAsync(
                HttpMethod.Post,
                $"{path}/{operation}",
                value,
                commander,
                apiKey,
                id.Trim(),
                cancellationToken
            );
        // These setters replace values. Repeating configuration after a partial failure does not create another Nexus.
        await UpdateAsync("setPrivate", draft.IsPrivate).ConfigureAwait(false);
        await UpdateAsync("setNotes", draft.Notes).ConfigureAwait(false);
        await UpdateAsync("setSystems", systems).ConfigureAwait(false);
        long[] carriers = draft.CarrierIds.Where(id => id > 0).Distinct().ToArray();
        foreach (long marketId in carriers)
        {
            await EnsureNexusCarrierAsync(marketId, commander, apiKey, cancellationToken).ConfigureAwait(false);
        }
        await UpdateAsync("setFCs", carriers).ConfigureAwait(false);
        // Membership is last: removing the caller must not revoke access before the other updates finish.
        return await UpdateAsync("setCmdrs", Names(new[] { draft.Owner ?? commander }.Concat(draft.Commanders)))
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ColonizationNexusCarrierMatch>> FindNexusCarriersAsync(
        string name,
        string commander,
        string apiKey,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        using HttpRequestMessage request = CreateNexusRequest(
            $"api/FC/query/{Uri.EscapeDataString(name.Trim())}",
            commander,
            apiKey
        );
        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        ColonizationNexusCarrierMatch[] matches = await ReadRequiredAsync<ColonizationNexusCarrierMatch[]>(
                response,
                "find Fleet Carriers",
                cancellationToken
            )
            .ConfigureAwait(false);
        return matches
            .OfType<ColonizationNexusCarrierMatch>()
            .Where(match => match.MarketId > 0 && !string.IsNullOrWhiteSpace(match.Name))
            .DistinctBy(match => match.MarketId)
            .OrderBy(match => match.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private async Task<ColonizationNexus> SendNexusChangeAsync<T>(
        HttpMethod method,
        string path,
        T content,
        string commander,
        string apiKey,
        string? expectedId,
        CancellationToken cancellationToken
    )
    {
        using HttpRequestMessage request = CreateNexusRequest($"api/Chain/{path}", commander, apiKey);
        request.Method = method;
        request.Content = JsonContent.Create(content, options: JsonOptions);
        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        ColonizationNexus nexus = await ReadRequiredAsync<ColonizationNexus>(
                response,
                "save Nexus details",
                cancellationToken
            )
            .ConfigureAwait(false);
        if (
            string.IsNullOrWhiteSpace(nexus.Id)
            || string.IsNullOrWhiteSpace(nexus.Name)
            || expectedId is not null && nexus.Id != expectedId
        )
        {
            throw new InvalidDataException("Raven Colonial returned an invalid Nexus acknowledgement.");
        }
        return NormalizeNexus(nexus);
    }

    private async Task EnsureNexusCarrierAsync(long marketId, string commander, string apiKey, CancellationToken token)
    {
        using HttpRequestMessage request = CreateNexusRequest(
            $"api/FC/{marketId.ToString(CultureInfo.InvariantCulture)}/spansh",
            commander,
            apiKey
        );
        request.Method = HttpMethod.Post;
        using HttpResponseMessage response = await SendAsync(request, token, readSuccessBody: false)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.Conflict)
        {
            await ReadRequiredAsync<ColonizationFleetCarrier>(response, "register a Nexus Fleet Carrier", token)
                .ConfigureAwait(false);
        }
    }

    private static string[] Names(IEnumerable<string> names) =>
        names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string[] RequiredNexusSystems(IReadOnlyList<string> systems)
    {
        ArgumentNullException.ThrowIfNull(systems);
        string[] names = Names(systems);
        if (names.Length == 0)
        {
            throw new ArgumentException("A Nexus must retain at least one system.", nameof(systems));
        }
        return names;
    }
}
