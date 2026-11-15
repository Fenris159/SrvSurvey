using System.Globalization;
using System.Net.Http.Json;

namespace SrvSurvey.Core.Colonization;

public interface IRavenColonialBodyClient
{
    Task<IReadOnlyList<ColonizationSystemBody>> UpdateSystemBodiesAsync(
        long systemAddress,
        IReadOnlyList<ColonizationSystemBody> bodies,
        CancellationToken cancellationToken = default
    );
}

public sealed partial class RavenColonialClient : IRavenColonialBodyClient
{
    /// <summary>Replaces the selected system's body catalog without publishing its sites.</summary>
    public async Task<IReadOnlyList<ColonizationSystemBody>> UpdateSystemBodiesAsync(
        long systemAddress,
        IReadOnlyList<ColonizationSystemBody> bodies,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(systemAddress);
        ArgumentNullException.ThrowIfNull(bodies);
        ColonizationBodyCatalog.Validate(bodies);
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            CreateUri($"api/v2/system/{systemAddress.ToString(CultureInfo.InvariantCulture)}/bodies")
        )
        {
            Content = JsonContent.Create(bodies, options: JsonOptions),
        };
        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        List<ColonizationSystemBody> result = await ReadRequiredAsync<List<ColonizationSystemBody>>(
                response,
                "update the system body catalog",
                cancellationToken
            )
            .ConfigureAwait(false);
        ColonizationBodyCatalog.Validate(result);
        return result;
    }
}
