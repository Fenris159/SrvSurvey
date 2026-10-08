namespace SrvSurvey.Core.Search;

/// <summary>The Powerplay goal and system filters a Powerplay mining search was started with.</summary>
public sealed record PowerplaySearchFilters(
    string Reference,
    string Objective,
    string PledgedPower,
    string OpposingPower
)
{
    public string PowerState { get; init; } = "";
    public string Security { get; init; } = "";
    public string Allegiance { get; init; } = "";
    public string Government { get; init; } = "";
    public string State { get; init; } = "";
    public string Economy { get; init; } = "";
    public long MinimumPopulation { get; init; }
    public IReadOnlyList<string> States { get; init; } = [];
    public bool ForceIncludeReference { get; init; }
    public int ResultLimit { get; init; } = 30;
    public long MinimumDemand { get; init; }
    public long MaximumDemand { get; init; } = 90_000;
    public TimeSpan? MarketFreshness { get; init; }
    public string PadSize { get; init; } = PowerplayPlan.AnyPower;

    public bool IsAcquire => Objective == PowerplayPlan.Acquire;

    public bool MatchesPowerplay(MiningSystemResult system) =>
        PowerplayPlan.Matches(Objective, system, PledgedPower, OpposingPower)
        && PowerplayPlan.MatchesOppositionCount(Objective, OpposingPower, system, PledgedPower);

    public bool MatchesSelectedStates(MiningSystemResult target) =>
        !States.Any(state => !IsAny(state)) || States.Any(state => Same(state, target.State));

    public bool MatchesForcedTarget(MiningSystemResult target) =>
        MatchesSelectedStates(target)
        && MatchesOptional(Security, target.Security)
        && MatchesOptional(Allegiance, target.Allegiance)
        && MatchesOptional(Government, target.Government)
        && MatchesOptional(Economy, target.Economy)
        && target.Population >= MinimumPopulation;

    public static bool IsAny(string value) =>
        value.Length == 0 || value.Equals(PowerplayPlan.AnyPower, StringComparison.OrdinalIgnoreCase);

    internal static bool Same(string left, string right) => left.Equals(right, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesOptional(string expected, string actual) => IsAny(expected) || Same(expected, actual);
}

/// <summary>Finds a pledged power's Acquire supporters, the targets inside their reach, and those targets' markets.</summary>
public sealed class PowerplayAcquireSearch(IMiningSearchProvider provider, PowerplaySearchFilters filters)
{
    public const string FortifiedState = "Fortified";
    public const string StrongholdState = "Stronghold";

    public PowerplaySearchFilters Filters => filters;

    public async Task<MiningSystemResult[]> SupportersAsync(bool requirePosition, CancellationToken cancellationToken)
    {
        IEnumerable<MiningSystemResult> supporters = (
            await provider.FindAcquireSupportersAsync(
                filters.Reference,
                filters.PledgedPower,
                FortifiedState,
                cancellationToken
            )
        ).Concat(
            await provider.FindAcquireSupportersAsync(
                filters.Reference,
                filters.PledgedPower,
                StrongholdState,
                cancellationToken
            )
        );
        if (requirePosition)
        {
            supporters = supporters.Where(system => system.Position is not null);
        }

        return supporters
            .OrderBy(system => system.Distance ?? double.MaxValue)
            .DistinctBy(system => system.System, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public MiningSystemQuery BubbleQuery(MiningSystemResult supporter, int page) =>
        new(
            supporter.System,
            PowerplayPlan.AcquisitionReachLy(supporter.PowerState),
            filters.Security.Trim(),
            filters.Allegiance.Trim(),
            filters.Government.Trim(),
            PowerplaySearchFilters.IsAny(filters.State) ? "" : filters.State.Trim(),
            filters.Economy.Trim(),
            "",
            "",
            filters.MinimumPopulation,
            page,
            PowerplayPlan.Acquire
        );

    public Task<MiningSystemPage> BubblePageAsync(MiningSystemQuery query, CancellationToken cancellationToken) =>
        PowerplaySearchFilters.IsAny(filters.PowerState)
        && !filters.States.Any(state => !PowerplaySearchFilters.IsAny(state))
            ? provider.FindAcquireCandidatePageAsync(query, cancellationToken)
            : provider.FindSystemPageAsync(query, cancellationToken);

    /// <summary>Reads every page of a supporter's reach bubble and keeps the systems the power can acquire.</summary>
    public async Task<IReadOnlyList<MiningSystemResult>> BubbleTargetsAsync(
        MiningSystemResult supporter,
        CancellationToken cancellationToken
    )
    {
        var targets = new List<MiningSystemResult>();
        int page = 0;
        bool hasMore = true;
        while (hasMore)
        {
            MiningSystemPage bubble = await BubblePageAsync(BubbleQuery(supporter, page), cancellationToken);
            targets.AddRange(
                bubble.Systems.Where(candidate => PowerplayPlan.IsAcquisitionTarget(candidate, filters.PowerState))
            );
            hasMore = bubble.HasMore;
            page++;
        }

        return targets;
    }

    /// <summary>Finds the pledged power's Fortified and Stronghold systems whose reach includes a target.</summary>
    public async Task<HashSet<string>> NearbySourcesAsync(string target, CancellationToken cancellationToken)
    {
        var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int page = 0;
        bool hasMore = true;
        while (hasMore)
        {
            MiningSystemPage nearby = await provider.FindSystemPageAsync(
                new MiningSystemQuery(target, PowerplayPlan.StrongholdReachLy, Power: filters.PledgedPower, Page: page),
                cancellationToken
            );
            foreach (MiningSystemResult supporter in nearby.Systems)
            {
                if (
                    PowerplayPlan.SamePower(supporter.Power, filters.PledgedPower)
                    && supporter.PowerState is FortifiedState or StrongholdState
                    && supporter.Distance <= PowerplayPlan.AcquisitionReachLy(supporter.PowerState)
                )
                {
                    sources.Add(supporter.System);
                }
            }

            hasMore = nearby.HasMore;
            page++;
        }

        return sources;
    }

    /// <summary>The reference system when it can be acquired and matches the system filters.</summary>
    public async Task<MiningSystemResult?> ForcedReferenceTargetAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<MiningSystemResult> found = await provider.FindSystemsByNameAsync(
            filters.Reference,
            [filters.Reference],
            cancellationToken
        );
        return found.FirstOrDefault(system =>
            PowerplaySearchFilters.Same(system.System, filters.Reference)
            && PowerplayPlan.IsAcquisitionTarget(system, filters.PowerState)
            && filters.MatchesForcedTarget(system)
        );
    }

    public async Task<MiningMarketResult[]> ImportsAsync(
        string system,
        IReadOnlyList<string> materials,
        CancellationToken cancellationToken
    )
    {
        var query = new MiningMarketQuery(
            system,
            "Any",
            false,
            ExcludeCarriers: true,
            SystemOnly: true,
            MinimumDemand: filters.MinimumDemand,
            MaximumDemand: filters.MaximumDemand,
            MaximumAge: filters.MarketFreshness,
            PadSize: filters.PadSize
        );
        (MiningMarketResult[] quotes, _) = await MiningSystemImports.FindAsync(
            provider,
            filters.Reference,
            system,
            query,
            new MiningImportFilter(
                materials,
                filters.MinimumDemand,
                filters.MaximumDemand,
                filters.MarketFreshness,
                filters.PadSize
            ),
            cancellationToken
        );
        return quotes;
    }
}

/// <summary>
/// Sell-market rules for a Powerplay planetary search. Acquire searches continue through supporter
/// reach bubbles page by page until the surface search has enough sell systems.
/// </summary>
public sealed class PowerplayPlanetarySellMarkets
{
    private sealed class AcquireMarketCursor(MiningSystemResult[] supporters, GalacticCoordinate? origin)
    {
        public MiningSystemResult[] Supporters { get; } = supporters;
        public GalacticCoordinate? Origin { get; } = origin;
        public Queue<(MiningSystemResult Target, MiningSystemResult Supporter, bool FromCache)> Targets { get; } =
            new();
        public HashSet<string> Seen { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int SupporterIndex { get; set; }
        public MiningSystemResult? CurrentSupporter { get; set; }
        public int BubblePage { get; set; }
        public bool ReferenceChecked { get; set; }
    }

    private readonly IMiningSearchProvider provider;
    private readonly PowerplaySearchFilters filters;
    private readonly PowerplayAcquireSearch acquire;
    private readonly Dictionary<string, HashSet<string>> acquisitionSources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SurfaceSellSystemDetails> sellSystemDetails = new(
        StringComparer.OrdinalIgnoreCase
    );

    public PowerplayPlanetarySellMarkets(IMiningSearchProvider provider, PowerplaySearchFilters filters)
    {
        this.provider = provider;
        this.filters = filters;
        acquire = new PowerplayAcquireSearch(provider, filters);
    }

    public async Task<SurfaceSellMarketRules> RulesAsync(
        Func<CancellationToken, Task<GalacticCoordinate?>> resolveOrigin,
        CancellationToken cancellationToken
    )
    {
        var rules = new SurfaceSellMarketRules
        {
            SellSystemDetailsFor = system => sellSystemDetails.GetValueOrDefault(system),
            MiningSystemsForSell = system =>
                filters.IsAcquire
                    ? acquisitionSources.GetValueOrDefault(system)
                        ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>([system], StringComparer.OrdinalIgnoreCase),
            EligibleSellSystemsAsync = EligibleSellSystemsAsync,
        };
        if (!filters.IsAcquire)
        {
            return rules;
        }

        AcquireMarketCursor cursor = await CreateCursorAsync(resolveOrigin, cancellationToken);
        return rules with
        {
            AdditionalMarketQuotesAsync = (materials, token) => FindAcquireQuotesAsync(cursor, materials, token),
        };
    }

    private async Task<AcquireMarketCursor> CreateCursorAsync(
        Func<CancellationToken, Task<GalacticCoordinate?>> resolveOrigin,
        CancellationToken token
    )
    {
        MiningSystemResult[] supporters = await acquire.SupportersAsync(requirePosition: false, token);
        GalacticCoordinate? origin = await resolveOrigin(token);
        if (origin is null)
        {
            IReadOnlyList<MiningSystemResult> reference = await provider.FindSystemsByNameAsync(
                filters.Reference,
                [filters.Reference],
                token
            );
            origin = reference
                .FirstOrDefault(system => PowerplaySearchFilters.Same(system.System, filters.Reference))
                ?.Position;
        }

        return new AcquireMarketCursor(supporters, origin);
    }

    private async Task<IReadOnlyList<MiningMarketResult>> FindAcquireQuotesAsync(
        AcquireMarketCursor cursor,
        IReadOnlyList<string> materials,
        CancellationToken token
    )
    {
        if (filters.ForceIncludeReference && !cursor.ReferenceChecked)
        {
            cursor.ReferenceChecked = true;
            IReadOnlyList<MiningMarketResult> forced = await FindForcedReferenceQuotesAsync(cursor, materials, token);
            if (forced.Count > 0)
            {
                return forced;
            }
        }

        return await FindAdditionalMarketsAsync(cursor, materials, token);
    }

    private async Task<IReadOnlyList<MiningMarketResult>> FindForcedReferenceQuotesAsync(
        AcquireMarketCursor cursor,
        IReadOnlyList<string> materials,
        CancellationToken token
    )
    {
        if (await acquire.ForcedReferenceTargetAsync(token) is not { } target)
        {
            return [];
        }

        HashSet<string> sources = await acquire.NearbySourcesAsync(target.System, token);
        if (sources.Count == 0)
        {
            return [];
        }

        MiningMarketResult[] imports = await acquire.ImportsAsync(target.System, materials, token);
        if (imports.Length == 0)
        {
            return [];
        }

        acquisitionSources[target.System] = sources;
        sellSystemDetails[target.System] = DetailsFor(target, 0);
        cursor.Seen.Add(target.System);
        return imports.Select(quote => quote with { Distance = 0 }).ToArray();
    }

    private async Task<IReadOnlyList<MiningMarketResult>> FindAdditionalMarketsAsync(
        AcquireMarketCursor cursor,
        IReadOnlyList<string> materials,
        CancellationToken token
    )
    {
        List<MiningMarketResult> quotes;
        do
        {
            IReadOnlyList<MiningSystemResult> candidates = await CollectMarketBatchAsync(cursor, token);
            if (candidates.Count == 0)
            {
                return [];
            }

            quotes = [];
            foreach (string system in candidates.Select(target => target.System))
            {
                MiningMarketResult[] imports = await acquire.ImportsAsync(system, materials, token);
                double? distance = sellSystemDetails[system].DistanceLy;
                quotes.AddRange(imports.Select(quote => quote with { Distance = distance }));
            }
        } while (quotes.Count == 0);

        return quotes;
    }

    private async Task<IReadOnlyList<MiningSystemResult>> CollectMarketBatchAsync(
        AcquireMarketCursor cursor,
        CancellationToken token
    )
    {
        var candidates = new List<MiningSystemResult>();
        bool exhausted = filters.ResultLimit <= 0;
        while (candidates.Count == 0 && !exhausted)
        {
            var cachedCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            exhausted = await FillMarketCandidatesAsync(cursor, candidates, cachedCandidates, token);

            await RefreshCachedCandidatesAsync(candidates, cachedCandidates, token);
        }
        return candidates;
    }

    private async Task<bool> FillMarketCandidatesAsync(
        AcquireMarketCursor cursor,
        List<MiningSystemResult> candidates,
        HashSet<string> cachedCandidates,
        CancellationToken token
    )
    {
        while (candidates.Count < filters.ResultLimit)
        {
            if (!await FillTargetQueueAsync(cursor, token))
            {
                return true;
            }
            (MiningSystemResult target, MiningSystemResult supporter, bool fromCache) = cursor.Targets.Dequeue();
            if (!filters.MatchesSelectedStates(target))
            {
                continue;
            }

            var sources = cursor
                .Supporters.Where(source =>
                    source.Position is { } origin
                    && target.Position is { } position
                    && origin.DistanceTo(position) <= PowerplayPlan.AcquisitionReachLy(source.PowerState)
                )
                .Select(source => source.System)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            sources.Add(supporter.System);
            acquisitionSources[target.System] = sources;
            double? distance = PowerplayPlan.TravelDistance(cursor.Origin, target.Position);
            if (distance is null)
            {
                IReadOnlyList<MiningSystemResult> located = await provider.FindSystemsByNameAsync(
                    filters.Reference,
                    [target.System],
                    token
                );
                distance = located
                    .FirstOrDefault(system => PowerplaySearchFilters.Same(system.System, target.System))
                    ?.Distance;
            }

            sellSystemDetails[target.System] = DetailsFor(target, distance);
            candidates.Add(target);
            if (fromCache)
            {
                cachedCandidates.Add(target.System);
            }
        }
        return false;
    }

    private async Task RefreshCachedCandidatesAsync(
        List<MiningSystemResult> candidates,
        HashSet<string> cachedCandidates,
        CancellationToken token
    )
    {
        if (cachedCandidates.Count == 0)
        {
            return;
        }

        try
        {
            IReadOnlyList<MiningSystemResult> live = await provider.FindSystemsByNameAsync(
                filters.Reference,
                cachedCandidates.ToArray(),
                token
            );
            var liveByName = live.ToDictionary(system => system.System, StringComparer.OrdinalIgnoreCase);
            for (int index = candidates.Count - 1; index >= 0; index--)
            {
                MiningSystemResult candidate = candidates[index];
                if (!liveByName.TryGetValue(candidate.System, out MiningSystemResult? current))
                {
                    continue;
                }

                if (!PowerplayPlan.IsAcquisitionTarget(current, filters.PowerState))
                {
                    acquisitionSources.Remove(candidate.System);
                    sellSystemDetails.Remove(candidate.System);
                    candidates.RemoveAt(index);
                    continue;
                }

                candidates[index] = current with { Distance = candidate.Distance };
                SurfaceSellSystemDetails details = sellSystemDetails[candidate.System];
                sellSystemDetails[candidate.System] = details with
                {
                    PowerState = current.PowerState,
                    FactionState = current.State,
                    Powers = current.NearbyPowers.Count > 0 ? current.NearbyPowers : [current.Power],
                    Conflict = current.Conflict,
                    ControllingPower = current.Power,
                    ControlProgress = current.ControlProgress,
                };
            }
        }
        catch (Exception ex) when (MiningProviderFailure.Is(ex))
        {
            // Candidate geometry remains useful when a live progress lookup fails.
        }
    }

    private async Task<bool> FillTargetQueueAsync(AcquireMarketCursor cursor, CancellationToken token)
    {
        while (cursor.Targets.Count == 0)
        {
            if (cursor.CurrentSupporter is null)
            {
                if (cursor.SupporterIndex >= cursor.Supporters.Length)
                {
                    return false;
                }

                cursor.CurrentSupporter = cursor.Supporters[cursor.SupporterIndex++];
                cursor.BubblePage = 0;
            }

            MiningSystemResult supporter = cursor.CurrentSupporter;
            MiningSystemPage bubble = await acquire.BubblePageAsync(
                acquire.BubbleQuery(supporter, cursor.BubblePage),
                token
            );
            foreach (
                MiningSystemResult target in bubble.Systems.Where(target =>
                    PowerplayPlan.IsAcquisitionTarget(target, filters.PowerState)
                    && (PowerplayPlan.TravelDistance(supporter.Position, target.Position) ?? target.Distance)
                        <= PowerplayPlan.AcquisitionReachLy(supporter.PowerState)
                    && cursor.Seen.Add(target.System)
                )
            )
            {
                cursor.Targets.Enqueue((target, supporter, bubble.FromCache));
            }

            cursor.BubblePage++;
            if (!bubble.HasMore)
            {
                cursor.CurrentSupporter = null;
            }
        }

        return true;
    }

    private async Task<IReadOnlySet<string>> EligibleSellSystemsAsync(
        IReadOnlyList<string> names,
        CancellationToken token
    )
    {
        var eligible = names.Where(sellSystemDetails.ContainsKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] unresolved = names.Where(name => !eligible.Contains(name)).ToArray();
        if (unresolved.Length == 0)
        {
            return eligible;
        }

        IReadOnlyList<MiningSystemResult> found = await provider.FindSystemsByNameAsync(
            filters.Reference,
            unresolved,
            token
        );
        string[] states = filters.States.Where(state => !PowerplaySearchFilters.IsAny(state)).ToArray();
        foreach (MiningSystemResult system in found)
        {
            if (
                filters.IsAcquire
                && PowerplayPlan.IsAcquisitionTarget(system, filters.PowerState)
                && !acquisitionSources.ContainsKey(system.System)
            )
            {
                HashSet<string> sources = await acquire.NearbySourcesAsync(system.System, token);
                if (sources.Count > 0)
                {
                    acquisitionSources[system.System] = sources;
                }
            }

            if (!IsEligibleSellSystem(system, states))
            {
                continue;
            }

            eligible.Add(system.System);
            sellSystemDetails[system.System] = DetailsFor(system, system.Distance);
        }

        return eligible;
    }

    private bool IsEligibleSellSystem(MiningSystemResult system, string[] states)
    {
        if (states.Length > 0 && !states.Any(state => PowerplaySearchFilters.Same(state, system.State)))
        {
            return false;
        }

        if (filters.IsAcquire)
        {
            return PowerplayPlan.IsAcquisitionTarget(system, filters.PowerState)
                && acquisitionSources.ContainsKey(system.System);
        }

        return filters.MatchesPowerplay(system)
            && (
                PowerplaySearchFilters.IsAny(filters.PowerState)
                || PowerplaySearchFilters.Same(system.PowerState, filters.PowerState)
            );
    }

    private static SurfaceSellSystemDetails DetailsFor(MiningSystemResult system, double? distance) =>
        new(
            system.PowerState,
            system.State,
            system.NearbyPowers.Count > 0 ? system.NearbyPowers : [system.Power],
            distance
        )
        {
            Conflict = system.Conflict,
            ControllingPower = system.Power,
            ControlProgress = system.ControlProgress,
        };
}
