using System.Globalization;
using System.Text.Json;
using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Colonization;

/// <summary>Searches both public providers concurrently, retaining usable results when one provider fails.</summary>
public sealed class ColonizationMarketSearchClient : IColonizationMarketSearch, IDisposable
{
    private const int SpanshPageSize = 100;
    private static readonly HttpClient SharedClient = CreateSharedClient();
    private readonly ArdentApi ardent;
    private readonly SpanshApi spansh;

    public ColonizationMarketSearchClient(HttpClient? client = null)
    {
        HttpClient http = client ?? SharedClient;
        ardent = new ArdentApi(http, maximumConcurrentRequests: 3);
        spansh = new SpanshApi(http);
    }

    public void Dispose() => ardent.Dispose();

    public async Task<ColonizationMarketSearchResult> SearchAsync(
        ColonizationMarketQuery query,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        if (query.Needs.Count == 0)
        {
            return new ColonizationMarketSearchResult([], []);
        }
        var searches = new List<Task<ProviderSearchResult>>();
        if (query.UseSpansh)
        {
            searches.Add(
                RunProviderAsync(
                    "Spansh",
                    result => SearchSpanshAsync(query, result, cancellationToken),
                    cancellationToken
                )
            );
        }
        if (query.UseArdent)
        {
            searches.Add(
                RunProviderAsync(
                    "Ardent",
                    result => SearchArdentAsync(query, result, cancellationToken),
                    cancellationToken
                )
            );
        }
        ProviderSearchResult[] results = await Task.WhenAll(searches).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!results.Any(result => result.IsComplete || result.Quotes.Count > 0))
        {
            throw new HttpRequestException(
                "The selected market data sources returned no usable data. Try refreshing again."
            );
        }
        IReadOnlyList<ColonizationMarketQuote> quotes = ColonizationShopping.Merge(
            results.SelectMany(result => result.Quotes)
        );
        var warnings = results.SelectMany(result => result.Warnings).ToList();
        if (quotes.Any(quote => quote.UpdatedAt < DateTimeOffset.UtcNow.AddDays(-query.MaximumAgeDays)))
        {
            warnings.Add(
                $"Local markets include outdated stock older than {query.MaximumAgeDays} days. These are estimates; verify stock before travelling."
            );
        }
        return new ColonizationMarketSearchResult(quotes, warnings);
    }

    private static async Task<ProviderSearchResult> RunProviderAsync(
        string source,
        Func<ProviderSearchResult, Task> search,
        CancellationToken cancellationToken
    )
    {
        var result = new ProviderSearchResult();
        try
        {
            await search(result).ConfigureAwait(false);
            result.IsComplete = true;
            if (result.LimitReached)
            {
                result.Warnings.Add(
                    $"{source} reached its result limit. Fetched results were kept; narrow the radius for more complete results."
                );
            }
        }
        catch (Exception exception)
            when (!cancellationToken.IsCancellationRequested
                && exception
                    is HttpRequestException
                        or IOException
                        or JsonException
                        or InvalidDataException
                        or OperationCanceledException
            )
        {
            result.Warnings.Clear();
            result.Warnings.Add($"{source} search was incomplete. Fetched results were kept; refresh to retry.");
        }
        return result;
    }

    private sealed class ProviderSearchResult
    {
        public List<ColonizationMarketQuote> Quotes { get; } = [];
        public List<string> Warnings { get; } = [];
        public bool IsComplete { get; set; }
        public bool LimitReached { get; set; }
    }

    private async Task SearchArdentAsync(
        ColonizationMarketQuery query,
        ProviderSearchResult result,
        CancellationToken cancellationToken
    )
    {
        var needs = query.Needs.ToDictionary(need => need.Commodity, StringComparer.Ordinal);
        string system = Uri.EscapeDataString(query.ReferenceSystem.Trim());
        string localRoute =
            query.LocalOnly || query.IncludeOutdatedLocalMarkets
                ? $"system/name/{system}/commodities"
                : $"system/name/{system}/commodities/exports?minVolume=1&maxDaysAgo={query.MaximumAgeDays}";
        try
        {
            using JsonDocument local = await ardent
                .GetAsync(localRoute, ArdentApi.MaximumResponseBytes, "Colonization local markets", cancellationToken)
                .ConfigureAwait(false);
            ReadArdent(local, query, needs, result.Quotes, localSystem: true);
        }
        catch (Exception exception)
            when (!query.LocalOnly
                && !cancellationToken.IsCancellationRequested
                && exception
                    is HttpRequestException
                        or IOException
                        or JsonException
                        or InvalidDataException
                        or OperationCanceledException
            )
        {
            result.Warnings.Add("Ardent local market data was unavailable; nearby results were still loaded.");
        }
        if (query.LocalOnly)
        {
            return;
        }
        // Keep a small request window: independent commodities can overlap without flooding Ardent.
        var sync = new Lock();
        await Parallel
            .ForEachAsync(
                query.Needs,
                new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = cancellationToken },
                async (need, token) =>
                {
                    string route =
                        ArdentRoutes.NearbyCommodity(
                            query.ReferenceSystem,
                            need.Commodity,
                            "exports",
                            1,
                            query.MaximumAgeDays,
                            query.Radius.ToString(CultureInfo.InvariantCulture),
                            excludeFleetCarriers: false
                        ) + "&sort=distance";
                    using JsonDocument document = await ardent
                        .GetAsync(route, ArdentApi.MaximumResponseBytes, "Colonization nearby markets", token)
                        .ConfigureAwait(false);
                    lock (sync)
                    {
                        ReadArdent(document, query, needs, result.Quotes, localSystem: false);
                        if (document.RootElement.GetArrayLength() >= 1000)
                        {
                            result.LimitReached = true;
                        }
                    }
                }
            )
            .ConfigureAwait(false);
    }

    private static void ReadArdent(
        JsonDocument document,
        ColonizationMarketQuery query,
        Dictionary<string, ColonizationShoppingNeed> needs,
        List<ColonizationMarketQuote> quotes,
        bool localSystem
    )
    {
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Unexpected Ardent market response.");
        }
        foreach (JsonElement row in document.RootElement.EnumerateArray())
        {
            string commodity = ColonizationMarketCommodities.Key(MiningJson.Text(row, "commodityName"));
            if (!needs.ContainsKey(commodity))
            {
                continue;
            }
            double distance = localSystem ? 0 : MiningJson.Number(row, "distance");
            var quote = new ColonizationMarketQuote(
                MiningJson.Text(row, "systemName"),
                MiningJson.Text(row, "stationName"),
                Integer(row, "marketId"),
                MiningJson.Text(row, "stationType"),
                (int)MiningJson.Number(row, "maxLandingPadSize"),
                distance,
                MiningJson.Number(row, "distanceToArrival"),
                commodity,
                Integer(row, "stock"),
                Integer(row, "buyPrice"),
                Timestamp(MiningJson.Text(row, "updatedAt")),
                "Ardent"
            );
            AddValid(quote, query, quotes);
        }
    }

    private async Task SearchSpanshAsync(
        ColonizationMarketQuery query,
        ProviderSearchResult result,
        CancellationToken cancellationToken
    )
    {
        string[] commodities = query
            .Needs.Select(need => ColonizationMarketCommodities.DisplayName(need.Commodity))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var filters = new Dictionary<string, object>
        {
            ["distance"] = new { min = 0, max = query.LocalOnly ? 1 : query.Radius },
            ["marketplace"] = new[]
            {
                new
                {
                    commodity = new { value = commodities },
                    supply = new { comparison = "<=>", value = new[] { 1, int.MaxValue } },
                },
            },
        };
        // A single OR search returns each station once, including all its matching commodities.
        // Page sequentially to avoid flooding the provider when many build commodities are selected.
        string searchReference = string.Empty;
        int pageSize = SpanshPageSize;
        int offset = 0;
        while (offset < SpanshRoutes.SearchResultLimit)
        {
            try
            {
                using JsonDocument document = await ReadSpanshPageAsync(
                        query,
                        filters,
                        searchReference,
                        offset / pageSize,
                        pageSize,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                if (searchReference.Length == 0)
                {
                    searchReference = MiningJson.Text(document.RootElement, "search_reference");
                }
                JsonElement rows = SpanshRows(document);
                foreach (JsonElement station in rows.EnumerateArray())
                {
                    ReadSpansh(station, query, result.Quotes);
                }
                offset += rows.GetArrayLength();
                long total = Integer(document.RootElement, "count");
                if (rows.GetArrayLength() < pageSize || (total > 0 && offset >= total))
                {
                    return;
                }
            }
            catch (InvalidDataException) when (pageSize > SpanshRoutes.PageSize(SpanshRoutes.Stations))
            {
                // Large station outfitting inventories can exceed the bounded response size.
                // Restart only the rejected page at a smaller size, keeping earlier quotes.
                pageSize = Math.Max(SpanshRoutes.PageSize(SpanshRoutes.Stations), pageSize / 2);
                offset -= offset % pageSize;
                searchReference = string.Empty;
            }
        }
        result.LimitReached = true;
    }

    private Task<JsonDocument> ReadSpanshPageAsync(
        ColonizationMarketQuery query,
        Dictionary<string, object> filters,
        string searchReference,
        int page,
        int pageSize,
        CancellationToken cancellationToken
    ) =>
        searchReference.Length > 0
            ? spansh.RecallAsync(SpanshRoutes.Stations, searchReference, page, cancellationToken)
            : spansh.SearchAsync(
                new SpanshSearchRequest(SpanshRoutes.Stations, query.ReferenceSystem, filters, page)
                {
                    PageSize = pageSize,
                },
                cancellationToken
            );

    private static JsonElement SpanshRows(JsonDocument document)
    {
        if (
            document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("results", out JsonElement rows)
            || rows.ValueKind != JsonValueKind.Array
        )
        {
            throw new JsonException("Unexpected Spansh market response.");
        }
        return rows;
    }

    private static void ReadSpansh(
        JsonElement station,
        ColonizationMarketQuery query,
        List<ColonizationMarketQuote> quotes
    )
    {
        foreach (JsonElement item in MiningJson.Array(station, "market"))
        {
            string commodity = ColonizationMarketCommodities.Key(MiningJson.Text(item, "commodity"));
            if (!query.Needs.Any(need => need.Commodity == commodity))
            {
                continue;
            }
            int pad = PadSize(station);
            var quote = new ColonizationMarketQuote(
                MiningJson.Text(station, "system_name"),
                MiningJson.Text(station, "name"),
                Integer(station, "market_id"),
                MiningJson.Text(station, "type"),
                pad,
                MiningJson.Number(station, "distance"),
                MiningJson.Number(station, "distance_to_arrival"),
                commodity,
                Integer(item, "supply"),
                Integer(item, "buy_price"),
                Timestamp(MiningJson.Text(station, "market_updated_at")),
                "Spansh"
            );
            AddValid(quote, query, quotes);
        }
    }

    private static int PadSize(JsonElement station)
    {
        if (MiningJson.Number(station, "large_pads") > 0)
        {
            return 3;
        }

        if (MiningJson.Number(station, "medium_pads") > 0)
        {
            return 2;
        }

        return MiningJson.Number(station, "small_pads") > 0 ? 1 : 0;
    }

    private static void AddValid(
        ColonizationMarketQuote quote,
        ColonizationMarketQuery query,
        List<ColonizationMarketQuote> quotes
    )
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (
            quote.Stock > 0
            && quote.Price > 0
            && quote.System.Length > 0
            && quote.Station.Length > 0
            && quote.Distance >= 0
            && quote.Distance <= query.Radius
            && (
                !query.LocalOnly
                || string.Equals(quote.System, query.ReferenceSystem.Trim(), StringComparison.OrdinalIgnoreCase)
            )
            && quote.UpdatedAt > DateTimeOffset.MinValue
            && (
                query.LocalOnly
                || quote.UpdatedAt >= now.AddDays(-query.MaximumAgeDays)
                || query.IncludeOutdatedLocalMarkets
                    && string.Equals(quote.System, query.ReferenceSystem.Trim(), StringComparison.OrdinalIgnoreCase)
            )
            && quote.UpdatedAt <= now.AddMinutes(5)
        )
        {
            quotes.Add(quote);
        }
    }

    private static long Integer(JsonElement row, string name) =>
        row.ValueKind == JsonValueKind.Object
        && row.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out long number)
            ? number
            : 0;

    private static DateTimeOffset Timestamp(string value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out DateTimeOffset time
        )
            ? time
            : DateTimeOffset.MinValue;

    private static HttpClient CreateSharedClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SrvSurvey-Avalonia/1.0");
        return client;
    }
}
