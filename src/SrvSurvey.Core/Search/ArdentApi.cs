using System.Text.Json;
using SrvSurvey.Core.Network;

namespace SrvSurvey.Core.Search;

/// <summary>
/// Ardent Insight routes. The service documents no enforced quota and asks callers to be respectful.
/// </summary>
public static class ArdentRoutes
{
    public static string NearbyCommodity(
        string system,
        string commodity,
        string direction,
        long minimumVolume,
        int maximumDaysAgo,
        string radiusLightYears,
        bool excludeFleetCarriers
    )
    {
        string route =
            $"system/name/{Escape(system)}/commodity/name/{Escape(commodity)}/nearby/{direction}?minVolume={minimumVolume}&maxDaysAgo={maximumDaysAgo}&maxDistance={radiusLightYears}";
        return excludeFleetCarriers ? route + "&fleetCarriers=false" : route;
    }

    public static string GalaxyCommodity(
        string commodity,
        string direction,
        long minimumVolume,
        int maximumDaysAgo,
        bool excludeFleetCarriers
    )
    {
        string route =
            $"commodity/name/{Escape(commodity)}/{direction}?minVolume={minimumVolume}&maxDaysAgo={maximumDaysAgo}";
        return excludeFleetCarriers ? route + "&fleetCarriers=false" : route;
    }

    public static string SystemCommodity(string system, string commodity, int maximumDaysAgo) =>
        $"system/name/{Escape(system)}/commodity/name/{Escape(commodity)}?maxDaysAgo={maximumDaysAgo}";

    public static string SystemImports(string system, int maximumDaysAgo) =>
        $"system/name/{Escape(system)}/commodities/imports?maxDaysAgo={maximumDaysAgo}";

    public const string Commodities = "commodities";

    public static string CommoditySummary(string commodity) => $"commodity/name/{Escape(commodity)}";

    public static string CurrentCommodityImporters(string commodity) =>
        GalaxyCommodity(commodity, "imports", 1, 2, excludeFleetCarriers: true);

    public static string NearestMaterialTrader(string system, int minimumPadSize) =>
        $"system/name/{Escape(system)}/nearest/material-trader?minLandingPadSize={minimumPadSize}";

    public static string SystemName(string query) => $"search/system/name/{Escape(query)}";

    private static string Escape(string value) => Uri.EscapeDataString(value.Trim());
}

public sealed class ArdentApi
{
    public const int MaximumResponseBytes = 8 * 1024 * 1024;
    public static readonly Uri Origin = new("https://api.ardent-insight.com/v2/");
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly HttpClient client;
    private readonly Uri origin;
    private readonly Action<string, Exception>? onFailure;

    public ArdentApi(HttpClient client, Uri? origin = null, Action<string, Exception>? onFailure = null)
    {
        this.client = client;
        this.origin = origin ?? Origin;
        this.onFailure = onFailure;
    }

    public async Task<JsonDocument> GetAsync(
        string relativePath,
        int maximumBytes,
        string responseLabel,
        CancellationToken cancellationToken = default
    )
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using HttpResponseMessage response = await client
                .GetAsync(new Uri(origin, relativePath), HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await BoundedHttpContent
                .ReadJsonDocumentAsync(
                    response.Content,
                    Math.Min(maximumBytes, MaximumResponseBytes),
                    responseLabel,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or InvalidDataException)
        {
            Report(relativePath, ex);
            throw;
        }
        finally
        {
            Gate.Release();
        }
    }

    private void Report(string route, Exception exception)
    {
        try
        {
            onFailure?.Invoke(route, exception);
        }
        catch (Exception)
        {
            // Diagnostics must never replace the provider failure.
        }
    }
}
