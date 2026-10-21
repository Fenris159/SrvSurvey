using System.Text.Json;
using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class ColonizationShoppingViewModel
{
    private bool excludeLocalCommodities;
    private string[] projectSystems = [];
    private IReadOnlyList<ColonizationMarketQuote> localMarketQuotes = [];

    /// <summary>Whether sufficient local stock is excluded from the commodity filters.</summary>
    public bool ExcludeLocalCommodities => excludeLocalCommodities;

    /// <summary>Allows an idle workspace to check local markets or restore its commodity selections.</summary>
    public bool CanExcludeLocalCommodities =>
        !disposed
        && !IsBusy
        && (ExcludeLocalCommodities || (UseSpansh || UseArdent) && projectSystems.Length > 0 && Needs.Count > 0);

    /// <summary>Checks selected project systems before excluding needs, preserving manual choices and the ShoppingList.</summary>
    public async Task ToggleLocalCommoditiesAsync()
    {
        if (!CanExcludeLocalCommodities)
        {
            return;
        }
        if (ExcludeLocalCommodities)
        {
            excludeLocalCommodities = false;
            FilterResults();
            SaveWorkspace();
            NotifySearch();
            return;
        }
        IsBusy = true;
        Status = "Checking markets in the selected projects’ systems…";
        NotifySearch();
        int version = searchVersion;
        try
        {
            ColonizationMarketSearchResult result = await SearchLocalMarketsAsync(MarketAgeDays);
            if (disposed || version != searchVersion)
            {
                return;
            }
            localMarketQuotes = result.Quotes;
            excludeLocalCommodities = true;
            AddWarnings(result.Warnings);
            FilterResults();
            Status =
                $"Excluded {Needs.Count(need => need.IsLocallyExcluded):N0} commodities covered by local markets. Turn off Exclude Local Commodities to restore selections.";
            SaveWorkspace();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            // A replaced workspace must not apply a late market response.
        }
        catch (Exception exception)
            when (exception
                    is HttpRequestException
                        or IOException
                        or JsonException
                        or InvalidDataException
                        or OperationCanceledException
            )
        {
            if (!disposed && version == searchVersion)
            {
                Status = "Local market check failed. Commodity selections were kept; try again.";
            }
        }
        finally
        {
            FinishSearch();
        }
    }

    private async Task<ColonizationMarketSearchResult> SearchLocalMarketsAsync(int maximumAge)
    {
        CancellationToken token = lifetime.Token;
        ColonizationShoppingNeed[] needs = Needs
            .Where(need => need.Remaining > 0)
            .Select(need => need.Need with { Required = need.Remaining })
            .ToArray();
        var results = new List<ColonizationMarketSearchResult>();
        foreach (string[] batch in projectSystems.Chunk(3))
        {
            token.ThrowIfCancellationRequested();
            results.AddRange(
                await Task.WhenAll(
                    batch.Select(system =>
                        search.SearchAsync(
                            new ColonizationMarketQuery(system, 1, needs, maximumAge)
                            {
                                LocalOnly = true,
                                UseSpansh = UseSpansh,
                                UseArdent = UseArdent,
                            },
                            token
                        )
                    )
                )
            );
        }
        return new ColonizationMarketSearchResult(
            ColonizationShopping.Merge(results.SelectMany(result => result.Quotes)),
            results.SelectMany(result => result.Warnings).Distinct().ToArray()
        );
    }

    private void UpdateProjectSystems(IEnumerable<ColonizationProject> projects) =>
        projectSystems = projects
            .Where(project => !project.IsComplete)
            .Select(project => project.SystemName.Trim())
            .Where(system => system.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private void ApplyLocalCommodityExclusions()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var allocated = plan.Snapshot().ToDictionary(item => item.Key(), item => item.Quantity);
        var stock = FilterMarketQuotes(ColonizationShopping.Merge(localMarketQuotes), forLocalExclusion: true)
            .Where(quote =>
                quote.Price > 0
                && projectSystems.Contains(quote.System, StringComparer.OrdinalIgnoreCase)
                && quote.UpdatedAt > DateTimeOffset.MinValue
                && quote.UpdatedAt <= now.AddMinutes(5)
            )
            .GroupBy(quote => quote.Commodity, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group =>
                    group.Sum(quote =>
                        Math.Max(
                            0,
                            quote.Stock
                                - allocated.GetValueOrDefault((ColonizationShopping.MarketKey(quote), quote.Commodity))
                        )
                    ),
                StringComparer.Ordinal
            );
        updatingCommoditySelection = true;
        try
        {
            foreach (ShoppingNeedViewModel need in Needs)
            {
                need.SetLocallyExcluded(
                    ExcludeLocalCommodities
                        && need.Remaining > 0
                        && stock.GetValueOrDefault(need.Need.Commodity) >= need.Remaining
                );
            }
        }
        finally
        {
            updatingCommoditySelection = false;
        }
    }

    private void FinishSearch()
    {
        IsBusy = false;
        NotifySearch();
        if (disposed)
        {
            DisposeOwnedSearch();
        }
    }
}
