using System.Globalization;

namespace SrvSurvey.Core.Colonization;

/// <summary>Links a Fleet Carrier to an existing Raven build project.</summary>
public interface IRavenColonialProjectCarrierClient
{
    Task LinkProjectCarrierAsync(string buildId, long marketId, CancellationToken cancellationToken = default);

    Task UnlinkProjectCarrierAsync(string buildId, long marketId, CancellationToken cancellationToken = default);
}

public sealed partial class RavenColonialClient : IRavenColonialProjectCarrierClient
{
    public Task LinkProjectCarrierAsync(string buildId, long marketId, CancellationToken cancellationToken = default) =>
        ChangeProjectCarrierAsync(buildId, marketId, HttpMethod.Put, cancellationToken);

    public Task UnlinkProjectCarrierAsync(
        string buildId,
        long marketId,
        CancellationToken cancellationToken = default
    ) => ChangeProjectCarrierAsync(buildId, marketId, HttpMethod.Delete, cancellationToken);

    private Task ChangeProjectCarrierAsync(
        string buildId,
        long marketId,
        HttpMethod method,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(buildId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(marketId);
        return SendWithoutResponseAsync(
            method,
            $"api/project/{Uri.EscapeDataString(buildId.Trim())}/fc/{marketId.ToString(CultureInfo.InvariantCulture)}",
            content: null,
            "update a colonisation project's Fleet Carrier link",
            cancellationToken
        );
    }
}
