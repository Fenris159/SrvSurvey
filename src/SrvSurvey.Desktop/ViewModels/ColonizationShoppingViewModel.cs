using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Input;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

/// <summary>Owns the embedded market-search snapshot and its local filters.</summary>
public sealed partial class ColonizationShoppingViewModel : WorkspaceObservable, IDisposable
{
    private const string DistanceSort = "Distance";
    private const string StationSort = "Station";
    private const string MatchesSort = "Most commodities available";
    private const string SystemSort = "System";
    private const string ArrivalSort = "Arrival";

    private readonly IColonizationMarketSearch search;
    private readonly Func<string?> currentSystem;
    private readonly bool ownsSearch;
    private readonly Action<ColonizationShoppingState>? saveState;
    private readonly Func<CancellationToken, Task<IReadOnlyList<ColonizationProject>>>? refreshProjects;
    private DateTimeOffset? completedAt;
    private string completedSystem = string.Empty;
    private int completedRadius;
    private IReadOnlyList<string> searchedCommodities = [];
    private readonly CancellationTokenSource lifetime = new();
    private IReadOnlyList<ColonizationMarketQuote> quotes = [];
    private string referenceSystem;
    private string radiusText = "100";
    private string maximumAgeText = "7";
    private string maximumArrivalText = "0";
    private string minimumPad = "Large";
    private string sort = DistanceSort;
    private bool excludeCarriers = true;
    private bool excludeSurface;
    private bool requireFullSupply;
    private bool showShortSupply = true;
    private bool includeOutdatedLocalMarkets = true;
    private bool useSpansh = true;
    private bool useArdent = true;
    private bool isWishListLocked;
    private bool disposed;
    private int ownedSearchDisposed;
    private bool updatingCommoditySelection;
    private int searchVersion;
    private bool showSearchCriteria = true;
    private bool reverseSort;
    private ColonizationShoppingPlan plan;
    private IReadOnlyList<string> appliedPurchaseIds = [];
    private readonly HashSet<string> expandedMarkets = new(StringComparer.Ordinal);
    private IReadOnlyList<string> warningEntries = [];

    public bool ShowSearchCriteria
    {
        get => showSearchCriteria;
        set => Set(ref showSearchCriteria, value);
    }

    public bool IsWishListLocked
    {
        get => isWishListLocked;
        set
        {
            if (!disposed && Set(ref isWishListLocked, value))
            {
                if (!value)
                {
                    RebuildPlan(Needs.Select(need => need.Need), plan.Snapshot());
                    FilterResults();
                }
                SaveWorkspace();
            }
        }
    }

    public ICommand ClearWishListCommand { get; }

    public void ClearWishList() => RemoveWishListItems(plan.Snapshot());

    public string ExpandAllText => Results.Any(row => row.IsExpanded) ? "Collapse all" : "Expand all";
    public string StationSortLabel => SortLabel(StationSort, StationSort);
    public string MatchesSortLabel => SortLabel(MatchesSort, "Commodities");
    public string SystemSortLabel => SortLabel(SystemSort, SystemSort);
    public string DistanceSortLabel => SortLabel(DistanceSort, DistanceSort);
    public string ArrivalSortLabel => SortLabel(ArrivalSort, ArrivalSort);

    private string SortLabel(string column, string label)
    {
        if (Sort != column)
        {
            return label;
        }

        bool descending = (column == MatchesSort) != reverseSort;
        return label + (descending ? " ▾" : " ▴");
    }

    public void SortBy(string column)
    {
        if (Sort == column)
        {
            reverseSort = !reverseSort;
            FilterResults();
        }
        else
        {
            Sort = column;
        }
    }

    public void ToggleAllExpanded()
    {
        bool expand = !Results.Any(row => row.IsExpanded);
        foreach (ShoppingMarketRowViewModel row in Results)
        {
            row.IsExpanded = expand;
        }
    }

    public void ToggleCommodity(string commodity)
    {
        ShoppingNeedViewModel? need = Needs.FirstOrDefault(need => need.Need.Commodity == commodity);
        need?.IsSelected = !need.IsSelected;
    }

    /// <summary>Updates every commodity filter together without rebuilding the market rows once per tag.</summary>
    public void SetAllCommoditiesSelected(bool selected)
    {
        if (disposed)
        {
            return;
        }
        updatingCommoditySelection = true;
        try
        {
            foreach (ShoppingNeedViewModel need in Needs)
            {
                need.IsSelected = selected;
            }
        }
        finally
        {
            updatingCommoditySelection = false;
        }
        FilterResults();
        SaveWorkspace();
    }

    private void OnWishListChanged(IReadOnlyList<ColonizationWishListItem> items, bool selected)
    {
        if (selected)
        {
            plan.Select(items);
        }
        else
        {
            plan.Remove(items);
        }
        FilterResults();
        SaveWorkspace();
    }

    /// <summary>Returns removed ShoppingList quantities to the market search.</summary>
    internal bool RemoveWishListItems(IReadOnlyList<ColonizationWishListItem> items)
    {
        if (disposed)
        {
            return false;
        }
        plan.Remove(items);
        FilterResults();
        SaveWorkspace();
        return true;
    }

    /// <summary>Keeps market tags and subsequent saves consistent with purchases applied to the shared list.</summary>
    internal void ApplyPurchasedWishList(ColonizationShoppingState state)
    {
        appliedPurchaseIds = state.AppliedPurchaseIds;
        RebuildPlan(Needs.Select(need => need.Need), state.WishList, state.WishListQuotes);
        FilterResults();
        SaveWorkspace();
    }

    /// <summary>Discards saved search results while respecting the ShoppingList lock.</summary>
    public void ClearSearch()
    {
        searchVersion++;
        quotes = [];
        ResetPlan();
        expandedMarkets.Clear();
        completedAt = null;
        searchedCommodities = [];
        SetWarnings([]);
        Status = "Search cleared. Choose criteria to search again.";
        ShowSearchCriteria = true;
        FilterResults();
        SaveWorkspace();
        NotifySearch();
    }

    private void OnExpansionChanged(string key, bool expanded)
    {
        if (expanded)
        {
            expandedMarkets.Add(key);
        }
        else
        {
            expandedMarkets.Remove(key);
        }

        Changed(nameof(ExpandAllText));
    }

    public ColonizationShoppingViewModel(
        IEnumerable<ColonizationProject> projects,
        Func<string?> currentSystem,
        IColonizationMarketSearch? search = null,
        bool ownsProvidedSearch = false,
        ColonizationShoppingState? restoredState = null,
        Action<ColonizationShoppingState>? saveState = null,
        Func<CancellationToken, Task<IReadOnlyList<ColonizationProject>>>? refreshProjects = null
    )
    {
        this.saveState = saveState;
        this.refreshProjects = refreshProjects;
        this.currentSystem = currentSystem ?? throw new ArgumentNullException(nameof(currentSystem));
        this.search = search ?? new ColonizationMarketSearchClient();
        ownsSearch = search is null || ownsProvidedSearch;
        referenceSystem = currentSystem() ?? string.Empty;
        ColonizationProject[] selected = projects.ToArray();
        UpdateProjectSystems(selected);
        ProjectIds = selected.Select(project => project.BuildId).ToArray();
        ProjectSummary = string.Join(", ", selected.Select(project => project.BuildName));
        IReadOnlyList<ColonizationShoppingNeed> aggregate = ColonizationShopping.Aggregate(selected);
        Needs = aggregate.Select(need => new ShoppingNeedViewModel(need)).ToArray();
        plan = new ColonizationShoppingPlan(aggregate, []);
        ClearWishListCommand = new WorkspaceCommand(ClearWishList);
        VisibleNeeds = Needs;

        if (restoredState is not null)
        {
            Restore(restoredState);
        }
        foreach (ShoppingNeedViewModel need in Needs)
        {
            need.PropertyChanged += OnNeedChanged;
        }
    }

    internal IReadOnlyList<string> ProjectIds { get; }
    public IReadOnlyList<ShoppingNeedViewModel> Needs { get; private set; }
    public IReadOnlyList<ShoppingNeedViewModel> VisibleNeeds { get; private set; }
    public bool IsFullyPlanned => Needs.Count > 0 && VisibleNeeds.Count == 0;
    public string ProjectSummary { get; private set; }
    public bool HasSearchResults => completedAt.HasValue;
    public string SearchButtonText => HasSearchResults ? "Refresh" : "Search Markets";
    public string ResultsReferenceSystem => HasSearchResults ? completedSystem : ReferenceSystem;
    public string NeedsText => string.Join(", ", VisibleNeeds.Select(need => need.Label));
    public string TotalText =>
        $"{Needs.Sum(need => need.Remaining):N0} tonnes remaining across {VisibleNeeds.Count:N0} commodities";
    public ICommand? OpenWishListCommand { get; init; }
    public IReadOnlyList<ShoppingMarketRowViewModel> Results { get; private set; } = [];

    public double ResultTableMinimumHeight => 33 * Math.Min(20, Results.Count) + 1;
    public bool IsBusy { get; private set; }
    public bool CanSearch =>
        !disposed && !IsBusy && (UseSpansh || UseArdent) && (Needs.Count > 0 || refreshProjects is not null);
    public string Status { get; private set; } = "Choose a reference system and search market data.";
    public string Warnings => string.Join(" ", warningEntries);

    private void SetWarnings(IEnumerable<string> warnings)
    {
        warningEntries = warnings
            .Where(warning => !string.IsNullOrWhiteSpace(warning))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Changed(nameof(Warnings));
    }

    private void AddWarnings(IEnumerable<string> warnings) => SetWarnings(warningEntries.Concat(warnings));

    public string ResultSummary =>
        $"{Results.Count:N0} markets · {Results.Select(row => row.System).Distinct(StringComparer.OrdinalIgnoreCase).Count():N0} systems";
    public static IReadOnlyList<string> PadOptions { get; } = ["Any", "Small", "Medium", "Large"];

    public static IReadOnlyList<string> SortOptions { get; } =
    [DistanceSort, MatchesSort, StationSort, SystemSort, ArrivalSort];

    public bool ShowShortSupply
    {
        get => showShortSupply;
        set
        {
            if (Set(ref showShortSupply, value))
            {
                FilterResults();
            }
        }
    }

    /// <summary>Allows clearly marked old market estimates in the reference system.</summary>
    public bool IncludeOutdatedLocalMarkets
    {
        get => includeOutdatedLocalMarkets;
        set
        {
            if (Set(ref includeOutdatedLocalMarkets, value))
            {
                InvalidateSearch();
                SaveWorkspace();
            }
        }
    }

    private int MarketAgeDays => int.TryParse(MaximumAgeText, out int days) && days is >= 1 and <= 30 ? days : 7;

    public bool UseSpansh
    {
        get => useSpansh;
        set
        {
            if (Set(ref useSpansh, value))
            {
                InvalidateSearch();
                SaveWorkspace();
            }
        }
    }

    public bool UseArdent
    {
        get => useArdent;
        set
        {
            if (Set(ref useArdent, value))
            {
                InvalidateSearch();
                SaveWorkspace();
            }
        }
    }

    public string ReferenceSystem
    {
        get => referenceSystem;
        set
        {
            if (Set(ref referenceSystem, value))
            {
                InvalidateSearch();
            }
        }
    }
    public string RadiusText
    {
        get => radiusText;
        set
        {
            if (Set(ref radiusText, value))
            {
                InvalidateSearch();
            }
        }
    }
    public string MaximumAgeText
    {
        get => maximumAgeText;
        set
        {
            if (Set(ref maximumAgeText, value))
            {
                InvalidateSearch();
            }
        }
    }
    public string MaximumArrivalText
    {
        get => maximumArrivalText;
        set
        {
            if (Set(ref maximumArrivalText, value))
            {
                FilterResults();
            }
        }
    }
    public string MinimumPad
    {
        get => minimumPad;
        set
        {
            if (Set(ref minimumPad, value))
            {
                FilterResults();
            }
        }
    }
    public string Sort
    {
        get => sort;
        set
        {
            if (Set(ref sort, value))
            {
                reverseSort = false;
                FilterResults();
            }
        }
    }
    public bool ExcludeCarriers
    {
        get => excludeCarriers;
        set
        {
            if (Set(ref excludeCarriers, value))
            {
                FilterResults();
            }
        }
    }
    public bool ExcludeSurface
    {
        get => excludeSurface;
        set
        {
            if (Set(ref excludeSurface, value))
            {
                FilterResults();
            }
        }
    }
    public bool RequireFullSupply
    {
        get => requireFullSupply;
        set
        {
            if (Set(ref requireFullSupply, value))
            {
                FilterResults();
            }
        }
    }

    public void UseCurrentLocation() => ReferenceSystem = currentSystem() ?? string.Empty;

    /// <summary>Fetches all required commodities so toggling the comma-separated list needs no more requests.</summary>
    public async Task SearchAsync()
    {
        if (!CanSearch)
        {
            return;
        }

        if (
            string.IsNullOrWhiteSpace(ReferenceSystem)
            || !int.TryParse(RadiusText, out int radius)
            || radius is < 1 or > 500
            || !int.TryParse(MaximumAgeText, out int maximumAge)
            || maximumAge is < 1 or > 30
        )
        {
            Status = "Enter a system, a radius from 1 to 500 ly, and a market age from 1 to 30 days.";
            Changed(nameof(Status));
            return;
        }
        IsBusy = true;
        ResetPlan();
        FilterResults();
        SaveWorkspace();
        int version = searchVersion;
        var query = new ColonizationMarketQuery(ReferenceSystem.Trim(), radius, [], maximumAge)
        {
            IncludeOutdatedLocalMarkets = IncludeOutdatedLocalMarkets,
            UseSpansh = UseSpansh,
            UseArdent = UseArdent,
        };
        try
        {
            if (!await RefreshNeedsForSearchAsync(version))
            {
                return;
            }
            query = query with { Needs = Needs.Select(need => need.Need).ToArray() };
            IReadOnlyList<string> localWarnings = [];
            if (ExcludeLocalCommodities)
            {
                ColonizationMarketSearchResult local = await SearchLocalMarketsAsync(maximumAge);
                if (disposed || version != searchVersion)
                {
                    return;
                }
                localMarketQuotes = local.Quotes;
                localWarnings = local.Warnings;
                AddWarnings(localWarnings);
                FilterResults();
                SaveWorkspace();
            }
            Status = "Searching selected market data sources… This can take a long time.";
            Changed(nameof(Status));
            ColonizationMarketSearchResult result = await search.SearchAsync(query, lifetime.Token);
            if (disposed || version != searchVersion)
            {
                return;
            }

            CompleteSearch(
                query,
                result with
                {
                    Warnings = localWarnings.Concat(result.Warnings).Distinct().ToArray(),
                }
            );
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            // Closing the shopping window cancels the search and discards late results.
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
            ReportSearchFailure(version);
        }
        finally
        {
            FinishSearch();
        }
    }

    private void ReportSearchFailure(int version)
    {
        if (disposed || version != searchVersion)
        {
            return;
        }
        Status = HasSearchResults
            ? "Refresh failed. Previous results are still shown; try again."
            : "Market search failed. Try searching again.";
    }

    private async Task<bool> RefreshNeedsForSearchAsync(int version)
    {
        Status = refreshProjects is null
            ? "Searching selected market data sources… This can take a long time."
            : "Refreshing project needs from Raven Colonial…";
        NotifySearch();
        if (refreshProjects is not null)
        {
            IReadOnlyList<ColonizationProject> updated = await refreshProjects(lifetime.Token);
            if (disposed || version != searchVersion)
            {
                return false;
            }
            UpdateProjects(updated);
        }
        if (Needs.Count == 0)
        {
            ClearSearch();
            Status = "The selected projects have no remaining commodity needs.";
            return false;
        }
        return true;
    }

    private void CompleteSearch(ColonizationMarketQuery query, ColonizationMarketSearchResult result)
    {
        quotes = result.Quotes;
        ResetPlan();
        completedAt = DateTimeOffset.Now;
        completedSystem = query.ReferenceSystem;
        completedRadius = query.Radius;
        searchedCommodities = query.Needs.Select(need => need.Commodity).ToArray();
        SetWarnings(result.Warnings);
        WarnAboutUnsearchedNeeds();
        Status = CompletedStatus();
        expandedMarkets.Clear();
        FilterResults();
        if (Results.Count > 0)
        {
            Results[0].IsExpanded = true;
        }

        ShowSearchCriteria = false;
        SaveWorkspace();
    }

    private void InvalidateSearch()
    {
        searchVersion++;
        Status = "Search criteria changed. Refresh to load markets for these criteria.";
        FilterResults();
        NotifySearch();
    }

    /// <summary>Captures successful data independently of pending requests so closing during refresh retains it.</summary>
    internal ColonizationShoppingState CaptureState() =>
        new()
        {
            ReferenceSystem = ReferenceSystem,
            RadiusText = RadiusText,
            MaximumAgeText = MaximumAgeText,
            MaximumArrivalText = MaximumArrivalText,
            MinimumPad = MinimumPad,
            Sort = Sort,
            ReverseSort = reverseSort,
            ExcludeCarriers = ExcludeCarriers,
            ExcludeSurface = ExcludeSurface,
            RequireFullSupply = RequireFullSupply,
            ShowShortSupply = ShowShortSupply,
            ExcludeLocalCommodities = ExcludeLocalCommodities,
            IncludeOutdatedLocalMarkets = IncludeOutdatedLocalMarkets,
            UseSpansh = UseSpansh,
            UseArdent = UseArdent,
            LocalMarketQuotes = localMarketQuotes,
            UseCarrierDifference = UseCarrierDifference,
            HasCarrierDifferencePreference = true,
            SelectedCarrierMarketId = selectedCarrierMarketId,
            ShowSearchCriteria = ShowSearchCriteria,
            ExcludedCommodities = Needs
                .Where(need => !need.IsManuallySelected)
                .Select(need => need.Need.Commodity)
                .ToArray(),
            ExpandedMarkets = expandedMarkets.ToArray(),
            SearchedCommodities = searchedCommodities,
            Quotes = quotes,
            WishList = plan.Snapshot(),
            IsWishListLocked = IsWishListLocked,
            WishListQuotes = plan.SnapshotQuotes(),
            AppliedPurchaseIds = appliedPurchaseIds,
            Warnings = Warnings,
            WarningEntries = warningEntries,
            CompletedAt = completedAt,
            CompletedSystem = completedSystem,
            CompletedRadius = completedRadius,
        };

    private void Restore(ColonizationShoppingState state)
    {
        appliedPurchaseIds = state.AppliedPurchaseIds;
        isWishListLocked = state.IsWishListLocked;
        referenceSystem = state.ReferenceSystem;
        radiusText = state.RadiusText;
        maximumAgeText = state.MaximumAgeText;
        maximumArrivalText = state.MaximumArrivalText;
        minimumPad = state.MinimumPad;
        sort = state.Sort == "Most matches" ? MatchesSort : state.Sort;
        reverseSort = state.ReverseSort;
        excludeCarriers = state.ExcludeCarriers;
        excludeSurface = state.ExcludeSurface;
        requireFullSupply = state.RequireFullSupply;
        showShortSupply = state.ShowShortSupply;
        excludeLocalCommodities = state.ExcludeLocalCommodities;
        includeOutdatedLocalMarkets = state.IncludeOutdatedLocalMarkets;
        useSpansh = state.UseSpansh;
        useArdent = state.UseArdent;
        localMarketQuotes = state.LocalMarketQuotes;
        useCarrierDifference = state.EffectiveUseCarrierDifference;
        selectedCarrierMarketId = state.SelectedCarrierMarketId;
        showSearchCriteria = state.ShowSearchCriteria;
        quotes = state.Quotes;
        SetWarnings(
            (state.WarningEntries.Count > 0 ? state.WarningEntries : [state.Warnings]).Select(warning =>
                LegacySpanshWarnings()
                    .Replace(warning, "Previous Spansh results were truncated. Refresh to load more markets. ")
                    .Trim()
            )
        );
        completedAt = state.CompletedAt;
        completedSystem = state.CompletedSystem;
        completedRadius = state.CompletedRadius;
        searchedCommodities = state.SearchedCommodities;
        expandedMarkets.UnionWith(state.ExpandedMarkets);
        RebuildPlan(Needs.Select(need => need.Need), state.WishList, state.WishListQuotes);
        foreach (ShoppingNeedViewModel need in Needs)
        {
            need.IsSelected = !state.ExcludedCommodities.Contains(need.Need.Commodity, StringComparer.Ordinal);
        }
        if (HasSearchResults)
        {
            Status = CompletedStatus();
            WarnAboutUnsearchedNeeds();
        }
        FilterResults();
    }

    [GeneratedRegex(
        @"(?:Spansh reached the 100-station limit for .*?; narrow the radius for more complete results\.\s*)+"
    )]
    private static partial Regex LegacySpanshWarnings();

    private void WarnAboutUnsearchedNeeds()
    {
        if (
            HasSearchResults
            && Needs.Any(need => !searchedCommodities.Contains(need.Need.Commodity, StringComparer.Ordinal))
            && !Warnings.Contains("newly required commodities", StringComparison.Ordinal)
        )
        {
            AddWarnings(["Project needs changed. Refresh to search for newly required commodities."]);
        }
    }

    /// <summary>Reconciles live project needs with saved market stock and caps existing allocations.</summary>
    internal void UpdateProjects(
        IEnumerable<ColonizationProject> projects,
        IReadOnlyList<ColonizationFleetCarrier>? carriers = null
    )
    {
        if (disposed)
        {
            return;
        }
        if (carriers is not null)
        {
            SetCarriers(carriers);
        }
        string[] excluded = Needs.Where(need => !need.IsManuallySelected).Select(need => need.Need.Commodity).ToArray();
        foreach (ShoppingNeedViewModel need in Needs)
        {
            need.PropertyChanged -= OnNeedChanged;
        }
        ColonizationProject[] selected = projects.ToArray();
        UpdateProjectSystems(selected);
        IReadOnlyList<ColonizationShoppingNeed> aggregate = ColonizationShopping.Aggregate(selected);
        RebuildPlan(aggregate, plan.Snapshot());
        Needs = aggregate
            .Select(need => new ShoppingNeedViewModel(need)
            {
                IsSelected = !excluded.Contains(need.Commodity, StringComparer.Ordinal),
            })
            .ToArray();
        foreach (ShoppingNeedViewModel need in Needs)
        {
            need.PropertyChanged += OnNeedChanged;
        }
        ProjectSummary = string.Join(", ", selected.Select(project => project.BuildName));
        WarnAboutUnsearchedNeeds();
        Changed(nameof(Needs));
        Changed(nameof(ProjectSummary));
        FilterResults();
        NotifySearch();
        SaveWorkspace();
    }

    private string CompletedStatus() =>
        $"Saved results near {completedSystem} within {completedRadius:N0} ly · {completedAt?.LocalDateTime:g}. Refresh for current stock.";

    internal void SaveWorkspace()
    {
        try
        {
            saveState?.Invoke(CaptureState());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AddWarnings(["The search could not be saved to disk."]);
        }
    }

    private void OnNeedChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!updatingCommoditySelection && args.PropertyName == nameof(ShoppingNeedViewModel.IsSelected))
        {
            FilterResults();
        }
    }

    private void ResetPlan() => RebuildPlan(Needs.Select(need => need.Need), IsWishListLocked ? plan.Snapshot() : []);

    private void RebuildPlan(
        IEnumerable<ColonizationShoppingNeed> needs,
        IReadOnlyList<ColonizationWishListItem> selections,
        IReadOnlyList<ColonizationMarketQuote>? retainedQuotes = null
    )
    {
        plan = new ColonizationShoppingPlan(
            EffectiveNeeds(needs),
            ColonizationShopping.Merge(quotes.Concat(retainedQuotes ?? plan.SnapshotQuotes())),
            selections,
            preserveSelections: IsWishListLocked
        );
    }

    private void UpdateRemainingNeeds()
    {
        foreach (ShoppingNeedViewModel need in Needs)
        {
            need.SetRemaining(plan.Remaining(need.Need.Commodity));
        }
        VisibleNeeds = Needs.Where(need => need.Remaining > 0).ToArray();
        Changed(nameof(VisibleNeeds));
        Changed(nameof(NeedsText));
        Changed(nameof(TotalText));
        Changed(nameof(IsFullyPlanned));
    }

    /// <summary>Applies Raven-style any-commodity matching, then compares each market's stock to the aggregate need.</summary>
    private void FilterResults()
    {
        UpdateRemainingNeeds();
        ApplyLocalCommodityExclusions();
        ColonizationShoppingNeed[] active = Needs
            .Where(need => need.IsSelected)
            .Select(need => need.Need with { Required = need.Remaining })
            .ToArray();
        IReadOnlyList<ColonizationWishListItem> wishList = plan.Snapshot();
        IEnumerable<ShoppingMarketRowViewModel> rows = FilterMarketQuotes(ColonizationShopping.Merge(quotes))
            .GroupBy(ColonizationShopping.MarketKey)
            .Select(group =>
            {
                var row = new ShoppingMarketRowViewModel(
                    group.ToArray(),
                    active,
                    expandedMarkets.Contains(group.Key),
                    OnExpansionChanged,
                    OnWishListChanged,
                    ShowShortSupply,
                    wishList
                );
                row.ApplyMarketAge(MarketAgeDays);
                return row;
            })
            .Where(row => row.Matches > 0 && (!RequireFullSupply || row.CanFulfillAll));
        IEnumerable<ShoppingMarketRowViewModel> ordered = Sort switch
        {
            MatchesSort => rows.OrderByDescending(row => row.Matches).ThenBy(row => row.Distance),
            StationSort => rows.OrderBy(row => row.Station, StringComparer.OrdinalIgnoreCase),
            SystemSort => rows.OrderBy(row => row.System, StringComparer.OrdinalIgnoreCase),
            ArrivalSort => rows.OrderBy(row => row.Arrival),
            _ => rows.OrderBy(row => row.Distance).ThenBy(row => row.Station, StringComparer.OrdinalIgnoreCase),
        };
        Results = (reverseSort ? ordered.Reverse() : ordered).ToArray();
        Changed(nameof(ExpandAllText));
        Changed(nameof(StationSortLabel));
        Changed(nameof(MatchesSortLabel));
        Changed(nameof(SystemSortLabel));
        Changed(nameof(DistanceSortLabel));
        Changed(nameof(ArrivalSortLabel));
        Changed(nameof(Results));
        Changed(nameof(ResultTableMinimumHeight));
        Changed(nameof(ResultSummary));
    }

    private IEnumerable<ColonizationMarketQuote> FilterMarketQuotes(
        IEnumerable<ColonizationMarketQuote> source,
        bool forLocalExclusion = false
    )
    {
        int pad = MinimumPad switch
        {
            "Large" => 3,
            "Medium" => 2,
            "Small" => 1,
            _ => 0,
        };
        bool validArrival =
            double.TryParse(MaximumArrivalText, NumberStyles.Number, CultureInfo.CurrentCulture, out double arrival)
            && double.IsFinite(arrival)
            && arrival >= 0;
        DateTimeOffset cutoff = DateTimeOffset.UtcNow.AddDays(-MarketAgeDays);
        return source.Where(quote =>
            quote.PadSize >= pad
            && (
                UseSpansh && string.Equals(quote.Source, "Spansh", StringComparison.OrdinalIgnoreCase)
                || UseArdent && string.Equals(quote.Source, "Ardent", StringComparison.OrdinalIgnoreCase)
            )
            && (!ExcludeCarriers || !quote.StationType.Contains("Carrier", StringComparison.OrdinalIgnoreCase))
            && (forLocalExclusion || !ExcludeSurface || !IsSurface(quote.StationType))
            && validArrival
            && (arrival == 0 || quote.Arrival <= arrival)
            && (
                forLocalExclusion
                || IncludeOutdatedLocalMarkets
                || !string.Equals(quote.System, ResultsReferenceSystem, StringComparison.OrdinalIgnoreCase)
                || quote.UpdatedAt >= cutoff
            )
        );
    }

    private static bool IsSurface(string type) =>
        type.Contains("Planetary", StringComparison.OrdinalIgnoreCase)
        || type.Contains("Settlement", StringComparison.OrdinalIgnoreCase)
        || type.Contains("Crater", StringComparison.OrdinalIgnoreCase)
        || type.Contains("Surface", StringComparison.OrdinalIgnoreCase);

    private void NotifySearch()
    {
        Changed(nameof(HasSearchResults));
        Changed(nameof(SearchButtonText));
        Changed(nameof(ResultsReferenceSystem));
        Changed(nameof(IsBusy));
        Changed(nameof(CanSearch));
        Changed(nameof(CanExcludeLocalCommodities));
        Changed(nameof(ExcludeLocalCommodities));
        Changed(nameof(Status));
        Changed(nameof(Warnings));
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        SaveWorkspace();
        disposed = true;
        lifetime.Cancel();
        lifetime.Dispose();
        foreach (ShoppingNeedViewModel need in Needs)
        {
            need.PropertyChanged -= OnNeedChanged;
        }

        if (!IsBusy)
        {
            DisposeOwnedSearch();
        }
    }

    private void DisposeOwnedSearch()
    {
        if (ownsSearch && search is IDisposable disposable && Interlocked.Exchange(ref ownedSearchDisposed, 1) == 0)
        {
            disposable.Dispose();
        }
    }
}
