using System.Globalization;
using SrvSurvey.Core.Network;

namespace SrvSurvey.Core.Colonization;

public interface IColonizationBodyRecoveryClient
{
    Task<IReadOnlyList<ColonizationSystemBody>> RecoverAsync(
        long systemAddress,
        string systemName,
        IReadOnlyList<ColonizationSystemBody> knownBodies,
        CancellationToken cancellationToken = default
    );
}

public sealed class ColonizationBodyRecoveryClient : IColonizationBodyRecoveryClient
{
    private static readonly HttpClient SharedClient = new();
    private const int MaximumResponseBytes = 16 * 1024 * 1024;
    private readonly HttpClient client;

    public ColonizationBodyRecoveryClient(HttpClient? client = null) => this.client = client ?? SharedClient;

    /// <summary>Requires valid responses from both sources before proposing a replacement catalog.</summary>
    public async Task<IReadOnlyList<ColonizationSystemBody>> RecoverAsync(
        long systemAddress,
        string systemName,
        IReadOnlyList<ColonizationSystemBody> knownBodies,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(systemAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(systemName);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        string id = systemAddress.ToString(CultureInfo.InvariantCulture);
        Task<System.Text.Json.JsonDocument> spansh = FetchAsync(
            new Uri($"https://spansh.co.uk/api/dump/{id}"),
            deadline.Token
        );
        Task<System.Text.Json.JsonDocument> edsm = FetchAsync(
            new Uri($"https://www.edsm.net/api-system-v1/bodies?systemId64={id}"),
            deadline.Token
        );
        try
        {
            await Task.WhenAll(spansh, edsm).ConfigureAwait(false);
            return ColonizationBodyCatalog.FromProviders(
                spansh.Result.RootElement,
                edsm.Result.RootElement,
                systemAddress,
                systemName,
                knownBodies
            );
        }
        finally
        {
            if (spansh.IsCompletedSuccessfully)
            {
                spansh.Result.Dispose();
            }
            if (edsm.IsCompletedSuccessfully)
            {
                edsm.Result.Dispose();
            }
        }
    }

    private async Task<System.Text.Json.JsonDocument> FetchAsync(Uri uri, CancellationToken token)
    {
        using HttpResponseMessage response = await client
            .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await BoundedHttpContent
            .ReadJsonDocumentAsync(response.Content, MaximumResponseBytes, "External body catalog", token)
            .ConfigureAwait(false);
    }
}
