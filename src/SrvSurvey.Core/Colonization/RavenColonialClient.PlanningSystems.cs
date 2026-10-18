using System.Text;

namespace SrvSurvey.Core.Colonization;

/// <summary>Reads the authenticated commander's saved Raven system plans independently of game location.</summary>
public interface IRavenColonialPlanningSystemReader
{
    Task<IReadOnlyList<ColonizationSystemRecord>> GetCommanderPlanningSystemsAsync(
        string commander,
        string apiKey,
        CancellationToken cancellationToken = default
    );
}

public sealed partial class RavenColonialClient : IRavenColonialPlanningSystemReader
{
    public async Task<IReadOnlyList<ColonizationSystemRecord>> GetCommanderPlanningSystemsAsync(
        string commander,
        string apiKey,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commander);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        using var request = new HttpRequestMessage(HttpMethod.Get, CreateUri("api/v2/system/snapshots/"));
        request.Headers.TryAddWithoutValidation(RccKeyHeader, apiKey.Trim());
        request.Headers.TryAddWithoutValidation(
            "rcc-cmdr0",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(commander.Trim()))
        );
        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        ColonizationSystemRecord[] systems = await ReadRequiredAsync<ColonizationSystemRecord[]>(
                response,
                "load commander system plans",
                cancellationToken
            )
            .ConfigureAwait(false);
        return systems
            .OfType<ColonizationSystemRecord>()
            .Where(system =>
                system.SystemAddress > 0 && !string.IsNullOrWhiteSpace(system.Name) && system.Sites is not null
            )
            .ToArray();
    }
}
