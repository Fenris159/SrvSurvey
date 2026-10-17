using System.Globalization;

namespace SrvSurvey.Core.Colonization;

/// <summary>The Raven operations used by the linked-carrier manifest editor.</summary>
public interface IRavenColonialCarrierManifestClient
{
    Task<IReadOnlyList<ColonizationFleetCarrier>> GetLinkedFleetCarriersAsync(
        string commander,
        CancellationToken cancellationToken
    );
    Task<ColonizationFleetCarrier?> GetFleetCarrierAsync(long marketId, CancellationToken cancellationToken = default);
    Task<ColonizationFleetCarrier> RefreshFleetCarrierAsync(
        long marketId,
        string apiKey,
        CancellationToken cancellationToken
    );
    Task<ColonizationFleetCarrier> SetFleetCarrierLocationAsync(
        long marketId,
        string systemName,
        string apiKey,
        CancellationToken cancellationToken
    );
    Task<IReadOnlyDictionary<string, int>> ReplaceFleetCarrierCargoAsync(
        long marketId,
        IReadOnlyDictionary<string, int> cargo,
        string apiKey,
        CancellationToken cancellationToken = default
    );
}

public sealed partial class RavenColonialClient : IRavenColonialCarrierManifestClient
{
    public async Task<IReadOnlyList<ColonizationFleetCarrier>> GetLinkedFleetCarriersAsync(
        string commander,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commander);
        return await GetAsync<ColonizationFleetCarrier[]>(
                    $"api/cmdr/{Uri.EscapeDataString(commander.Trim())}/fc/all",
                    "load linked Fleet Carriers",
                    cancellationToken
                )
                .ConfigureAwait(false)
            ?? [];
    }

    /// <summary>As in Raven's editor, asks the service to refresh the carrier from Frontier.</summary>
    public Task<ColonizationFleetCarrier> RefreshFleetCarrierAsync(
        long marketId,
        string apiKey,
        CancellationToken cancellationToken
    ) => PostCarrierManifestAsync(marketId, "refresh", apiKey, cancellationToken);

    public Task<ColonizationFleetCarrier> SetFleetCarrierLocationAsync(
        long marketId,
        string systemName,
        string apiKey,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemName);
        return PostCarrierManifestAsync(
            marketId,
            $"location/{Uri.EscapeDataString(systemName.Trim())}",
            apiKey,
            cancellationToken
        );
    }

    private async Task<ColonizationFleetCarrier> PostCarrierManifestAsync(
        long marketId,
        string operation,
        string apiKey,
        CancellationToken cancellationToken
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(marketId);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            CreateUri($"api/fc/{marketId.ToString(CultureInfo.InvariantCulture)}/{operation}")
        );
        request.Headers.TryAddWithoutValidation(RccKeyHeader, apiKey.Trim());
        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        return await ReadRequiredAsync<ColonizationFleetCarrier>(
                response,
                "update the Fleet Carrier manifest",
                cancellationToken
            )
            .ConfigureAwait(false);
    }
}
