using SrvSurvey.Core.Mining;

namespace SrvSurvey.Core.Search;

/// <summary>The commodities, demand, freshness, and landing pad a sell station must match.</summary>
public sealed record MiningImportFilter(
    IReadOnlyList<string> Materials,
    long MinimumDemand,
    long MaximumDemand,
    TimeSpan? MaximumAge,
    string PadSize
)
{
    /// <summary>Spansh fallback quotes must also report a positive price and demand.</summary>
    public bool RequireQuotedSale { get; init; }

    public bool Includes(string commodity) => Materials.Any(material => MiningCommodityName.Same(material, commodity));

    public bool MatchesFallback(MiningMarketResult quote) =>
        Includes(quote.Commodity)
        && (!RequireQuotedSale || (quote.Price > 0 && quote.Demand > 0))
        && quote.Demand >= MinimumDemand
        && (MaximumDemand == 0 || quote.Demand <= MaximumDemand)
        && (MaximumAge is null || quote.Updated >= DateTimeOffset.UtcNow - MaximumAge)
        && MatchesPad(quote, PadSize);

    public static bool MatchesPad(MiningMarketResult quote, string padSize) =>
        padSize switch
        {
            "L" => quote.LargePad == true,
            "M" => quote.QuotedPad is "Medium",
            "S" => quote.QuotedPad is "Small",
            _ => true,
        };
}

/// <summary>Finds a system's station imports from Ardent, falling back to Spansh station commodities.</summary>
public static class MiningSystemImports
{
    public static async Task<(MiningMarketResult[] Quotes, bool UsedFallback)> FindAsync(
        IMiningSearchProvider provider,
        string spanshReference,
        string system,
        MiningMarketQuery query,
        MiningImportFilter filter,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<MiningMarketResult> imports;
        try
        {
            imports = await provider.FindSystemImportsAsync(system, query, cancellationToken);
        }
        catch (Exception ex) when (MiningProviderFailure.Is(ex))
        {
            imports = [];
        }

        MiningMarketResult[] matching = imports.Where(quote => filter.Includes(quote.Commodity)).ToArray();
        if (matching.Length > 0)
        {
            return (matching, false);
        }

        IReadOnlyList<MiningMarketResult> fallback = await provider.FindSpanshSystemCommoditiesAsync(
            spanshReference,
            system,
            cancellationToken
        );
        return (fallback.Where(filter.MatchesFallback).ToArray(), true);
    }
}
