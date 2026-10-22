using System.Net;
using System.Text;

namespace SrvSurvey.Core.Colonization;

/// <summary>Reads the authenticated Nexus index and individual snapshots without publishing changes.</summary>
public interface IRavenColonialNexusReader
{
    Task<IReadOnlyList<ColonizationNexusSummary>> GetCommanderNexusesAsync(
        string commander,
        string apiKey,
        CancellationToken cancellationToken = default
    );

    Task<ColonizationNexus?> GetNexusAsync(
        string id,
        string commander,
        string apiKey,
        CancellationToken cancellationToken = default
    );
}

public sealed partial class RavenColonialClient : IRavenColonialNexusReader
{
    public async Task<IReadOnlyList<ColonizationNexusSummary>> GetCommanderNexusesAsync(
        string commander,
        string apiKey,
        CancellationToken cancellationToken = default
    )
    {
        using HttpRequestMessage request = CreateNexusRequest("api/Cmdr/nexus", commander, apiKey);
        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        ColonizationNexusSummary[] entries = await ReadRequiredAsync<ColonizationNexusSummary[]>(
                response,
                "load commander Nexus projects",
                cancellationToken
            )
            .ConfigureAwait(false);
        return entries
            .OfType<ColonizationNexusSummary>()
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Id) && !string.IsNullOrWhiteSpace(entry.Name))
            .DistinctBy(entry => entry.Id, StringComparer.Ordinal)
            .OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public async Task<ColonizationNexus?> GetNexusAsync(
        string id,
        string commander,
        string apiKey,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        using HttpRequestMessage request = CreateNexusRequest(
            $"api/Chain/{Uri.EscapeDataString(id.Trim())}",
            commander,
            apiKey
        );
        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        ColonizationNexus nexus = await ReadRequiredAsync<ColonizationNexus>(
                response,
                "load Nexus details",
                cancellationToken
            )
            .ConfigureAwait(false);
        if (!string.Equals(nexus.Id, id.Trim(), StringComparison.Ordinal) || string.IsNullOrWhiteSpace(nexus.Name))
        {
            throw new InvalidDataException("Raven Colonial returned an invalid Nexus snapshot.");
        }
        return NormalizeNexus(nexus);
    }

    private static ColonizationNexus NormalizeNexus(ColonizationNexus nexus)
    {
        if (
            (nexus.Systems ?? [])
                .OfType<ColonizationNexusSystem>()
                .Any(system => system.SystemAddress <= 0 || string.IsNullOrWhiteSpace(system.Name))
        )
        {
            throw new InvalidDataException("Raven Colonial returned invalid Nexus system data.");
        }
        return nexus with
        {
            Commanders = (nexus.Commanders ?? []).Where(name => !string.IsNullOrWhiteSpace(name)).ToArray(),
            FleetCarriers = (nexus.FleetCarriers ?? [])
                .OfType<ColonizationFleetCarrier>()
                .Where(carrier => carrier.MarketId > 0 && carrier.Cargo is not null)
                .DistinctBy(carrier => carrier.MarketId)
                .ToArray(),
            Systems = (nexus.Systems ?? [])
                .OfType<ColonizationNexusSystem>()
                .Select(system =>
                    system with
                    {
                        FleetCarrierIds = system.FleetCarrierIds ?? [],
                        Builds = (system.Builds ?? [])
                            .OfType<ColonizationProject>()
                            .Where(build => !string.IsNullOrWhiteSpace(build.BuildId))
                            .ToArray(),
                        BuildTypes = system.BuildTypes ?? [],
                    }
                )
                .ToArray(),
            Hubs = nexus.Hubs ?? [],
        };
    }

    private HttpRequestMessage CreateNexusRequest(string path, string commander, string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commander);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        var request = new HttpRequestMessage(HttpMethod.Get, CreateUri(path));
        request.Headers.TryAddWithoutValidation(RccKeyHeader, apiKey.Trim());
        request.Headers.TryAddWithoutValidation(
            "rcc-cmdr0",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(commander.Trim()))
        );
        return request;
    }
}
