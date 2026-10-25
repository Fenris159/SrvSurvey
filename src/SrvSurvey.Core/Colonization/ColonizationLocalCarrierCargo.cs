using SrvSurvey.Core.Frontier;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Colonization;

/// <summary>Independent local cargo totals: full CAPI baselines followed by journal transactions, never Raven edits.</summary>
public sealed class ColonizationLocalCarrierCargo
{
    private readonly List<CarrierCargo> carriers = [];

    public event Action? Changed;

    public void Clear()
    {
        carriers.Clear();
        Changed?.Invoke();
    }

    public void Observe(FrontierCarrierSnapshot? carrier, DateTimeOffset fetchedAt, bool isDocked = false)
    {
        if (carrier is null || string.IsNullOrWhiteSpace(carrier.Callsign))
        {
            return;
        }
        CarrierCargo local = FindOrCreate(carrier.Market?.Id, carrier.Callsign);
        // Frontier can lag behind live transactions. Retain the journal projection while docked.
        if (
            local.BaselineAt >= fetchedAt
            || isDocked && local.BaselineAt.HasValue && (local.Deltas.Count > 0 || local.Markets.Count > 0)
        )
        {
            return;
        }
        local.PreferMarket = isDocked;
        local.BaselineAt = fetchedAt;
        local.Baseline = Totals(carrier.Cargo);

        local.Recalculate();
        Changed?.Invoke();
    }

    /// <summary>Uses Market.json stock only for listed goods; an unlisted good is not a confirmed zero.</summary>
    public void Observe(MarketSnapshot market)
    {
        ArgumentNullException.ThrowIfNull(market);
        if (market.MarketId <= 0 || !market.StationType.Contains("Carrier", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        CarrierCargo local = FindOrCreate(market.MarketId, market.StationName);
        local.PreferMarket = true;
        foreach (
            MarketItem item in market.Items.Where(item =>
                item.Stock >= 0 && (item.Producer || item.Stock > 0) && !string.IsNullOrWhiteSpace(item.Name)
            )
        )
        {
            string key = Key(item.Name);
            if (!local.Markets.TryGetValue(key, out MarketBaseline? previous) || market.Timestamp > previous.At)
            {
                local.Markets[key] = new(market.Timestamp, item.Stock);
            }
        }
        local.Recalculate();
        Changed?.Invoke();
    }

    public void Observe(JournalEventEnvelope journalEvent, ColonizationDockingSnapshot? dock, bool inMainShip)
    {
        if (
            dock is null
            || journalEvent.Timestamp is not { } timestamp
            || journalEvent.EventName is not ("MarketBuy" or "MarketSell" or "CargoTransfer")
        )
        {
            return;
        }
        IReadOnlyDictionary<string, int> delta;
        try
        {
            delta = ColonizationFleetCarrierCargoSynchronizer.CreateJournalAdjustment(
                journalEvent,
                dock,
                inMainShip,
                preferShipCargoDiffForSquadron: false
            );
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException)
        {
            return;
        }
        if (delta.Count == 0)
        {
            return;
        }
        CarrierCargo local = FindOrCreate(dock.MarketId, dock.StationName);
        string identity = journalEvent.SourceRecordIdentity ?? journalEvent.RawJson;
        if (local.Deltas.Any(change => change.Identity == identity))
        {
            return;
        }
        local.Deltas.Add(new(identity, timestamp, delta));
        local.Recalculate();
        Changed?.Invoke();
    }

    /// <summary>A missing good in a known complete manifest is zero; an unknown manifest or inconsistent count is unavailable.</summary>
    public long? GetQuantity(ColonizationFleetCarrier carrier, string commodity)
    {
        CarrierCargo? local = Find(carrier.MarketId, carrier.Name);
        string key = Key(commodity);
        if (local is null || local.GetBaselineTimestamp(key) is null)
        {
            return null;
        }
        long quantity = local.Current.GetValueOrDefault(key);
        return quantity >= 0 ? quantity : null;
    }

    /// <summary>Counts each physical commodity once when Raven also contains legacy aliases for it.</summary>
    public long? GetTotal(ColonizationFleetCarrier carrier, IEnumerable<string> commodities)
    {
        long total = 0;
        foreach (string key in commodities.Select(Key).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            long? quantity = GetQuantity(carrier, key);
            if (quantity is null)
            {
                return null;
            }
            total += quantity.Value;
        }
        return total;
    }

    private CarrierCargo? Find(long? marketId, string callsign) =>
        carriers.FirstOrDefault(carrier => marketId is > 0 && carrier.MarketId == marketId)
        ?? carriers.FirstOrDefault(carrier =>
            (!carrier.MarketId.HasValue || marketId is null || marketId <= 0)
            && carrier.Callsign.Equals(callsign.Trim(), StringComparison.OrdinalIgnoreCase)
        );

    private CarrierCargo FindOrCreate(long? marketId, string callsign)
    {
        CarrierCargo? carrier = Find(marketId, callsign);
        if (carrier is null)
        {
            carrier = new CarrierCargo(callsign.Trim());
            carriers.Add(carrier);
        }
        if (marketId is > 0)
        {
            carrier.MarketId = marketId;
        }
        return carrier;
    }

    private static Dictionary<string, long> Totals(IEnumerable<FrontierInventorySnapshot> cargo) =>
        cargo
            .Where(item => item.Quantity >= 0 && !string.IsNullOrWhiteSpace(item.Name))
            .GroupBy(item => Key(item.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(item => (long)item.Quantity),
                StringComparer.OrdinalIgnoreCase
            );

    private static string Key(string commodity) =>
        ColonizationManifestCommodity
            .ForKey(ColonizationMarketCommodities.Key(ColonizationConstructionState.NormalizeCommodityName(commodity)))
            .Key;

    /// <summary>Lists local inventory identities without treating an unknown baseline as a zero count.</summary>
    public IReadOnlyList<string> GetCommodities(ColonizationFleetCarrier carrier) =>
        Find(carrier.MarketId, carrier.Name)?.Current.Where(item => item.Value > 0).Select(item => item.Key).ToArray()
        ?? [];

    private sealed record CargoDelta(
        string Identity,
        DateTimeOffset Timestamp,
        IReadOnlyDictionary<string, int> Counts
    );

    private sealed record MarketBaseline(DateTimeOffset At, long Quantity);

    private sealed class CarrierCargo(string callsign)
    {
        public string Callsign { get; } = callsign;
        public long? MarketId { get; set; }
        public DateTimeOffset? BaselineAt { get; set; }
        public bool PreferMarket { get; set; }
        public Dictionary<string, MarketBaseline> Markets { get; } = new(StringComparer.OrdinalIgnoreCase);

        public DateTimeOffset? GetBaselineTimestamp(string key) =>
            Markets.TryGetValue(key, out MarketBaseline? market)
            && (PreferMarket || BaselineAt is null || market.At > BaselineAt)
                ? market.At
                : BaselineAt;

        public Dictionary<string, long> Baseline { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, long> Current { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<CargoDelta> Deltas { get; } = [];

        public void Recalculate()
        {
            Current = new(Baseline, StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, MarketBaseline> market in Markets)
            {
                if (GetBaselineTimestamp(market.Key) == market.Value.At)
                {
                    Current[market.Key] = market.Value.Quantity;
                }
            }
            foreach (CargoDelta delta in Deltas)
            {
                foreach (KeyValuePair<string, int> item in delta.Counts)
                {
                    string key = Key(item.Key);
                    if (GetBaselineTimestamp(key) is { } baseline && delta.Timestamp <= baseline)
                    {
                        continue;
                    }
                    Current[key] = Current.GetValueOrDefault(key) + item.Value;
                }
            }
            Deltas.RemoveAll(delta =>
                delta.Counts.Keys.All(key =>
                    GetBaselineTimestamp(Key(key)) is { } baseline && delta.Timestamp <= baseline
                )
            );
        }
    }
}
