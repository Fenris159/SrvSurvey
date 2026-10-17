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
        if (local.BaselineAt >= fetchedAt || isDocked && local.BaselineAt.HasValue && local.Deltas.Count > 0)
        {
            return;
        }
        local.BaselineAt = fetchedAt;
        local.Baseline = Totals(carrier.Cargo);
        local.Deltas.RemoveAll(change => change.Timestamp <= fetchedAt);
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
        if (timestamp <= local.BaselineAt || local.Deltas.Any(change => change.Identity == identity))
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
        if (local?.BaselineAt is null)
        {
            return null;
        }
        long quantity = local.Current.GetValueOrDefault(Key(commodity));
        return quantity >= 0 ? quantity : null;
    }

    /// <summary>Counts each physical commodity once when Raven also contains legacy aliases for it.</summary>
    public long? GetTotal(ColonizationFleetCarrier carrier, IEnumerable<string> commodities)
    {
        CarrierCargo? local = Find(carrier.MarketId, carrier.Name);
        if (local?.BaselineAt is null)
        {
            return null;
        }
        long total = 0;
        foreach (string key in commodities.Select(Key).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            long quantity = local.Current.GetValueOrDefault(key);
            if (quantity < 0)
            {
                return null;
            }
            total += quantity;
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
        ColonizationMarketCommodities.Key(ColonizationConstructionState.NormalizeCommodityName(commodity));

    private sealed record CargoDelta(
        string Identity,
        DateTimeOffset Timestamp,
        IReadOnlyDictionary<string, int> Counts
    );

    private sealed class CarrierCargo(string callsign)
    {
        public string Callsign { get; } = callsign;
        public long? MarketId { get; set; }
        public DateTimeOffset? BaselineAt { get; set; }
        public Dictionary<string, long> Baseline { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, long> Current { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<CargoDelta> Deltas { get; } = [];

        public void Recalculate()
        {
            Current = new(Baseline, StringComparer.OrdinalIgnoreCase);
            foreach (CargoDelta delta in Deltas)
            {
                foreach (KeyValuePair<string, int> item in delta.Counts)
                {
                    string key = Key(item.Key);
                    Current[key] = Current.GetValueOrDefault(key) + item.Value;
                }
            }
        }
    }
}
