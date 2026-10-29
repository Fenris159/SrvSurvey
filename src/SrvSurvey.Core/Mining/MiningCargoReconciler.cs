using System.Text.Json;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Mining;

/// <summary>Persists the cargo baseline and non-mining movements independently of journal replay batches.</summary>
public sealed record MiningCargoReconciliationState
{
    public int? LastCount { get; set; }
    public DateTimeOffset? LastSnapshotAt { get; set; }
    public long ExternalDelta { get; set; }
    public Guid? OwnerId { get; set; }
    public Guid? CarryoverSessionId { get; set; }
    public bool Carryover { get; set; }
    public long Balance { get; set; }
    public bool HasRefineryEvidence { get; set; }
    public bool EstimatedBaseline { get; set; }
    public string Mineral { get; set; } = "";
}

/// <summary>Reconciles gross refinery production with observed ship cargo, excluding purchases, transfers and consumption.</summary>
internal sealed class MiningCargoReconciler(MiningCargoReconciliationState state, Func<Guid, MiningSession?> resolve)
{
    private const string CountProperty = "Count";

    public void Observe(JournalEventEnvelope entry, MiningSession? owner, bool carryover, bool credit)
    {
        if (entry.Timestamp is not { } at || at < state.LastSnapshotAt)
        {
            return;
        }
        SelectOwner(owner, carryover);
        JsonElement data = entry.Payload;
        if (entry.EventName == "Cargo")
        {
            ObserveCargo(data, at, owner, credit);
            return;
        }
        if (IsCargoBoundary(entry.EventName, data))
        {
            Rebase(owner, at);
            return;
        }
        state.ExternalDelta += ExternalChange(entry.EventName, data);
        if (entry.EventName == "MiningRefined" && owner is not null && credit)
        {
            if (!carryover && state.LastCount is null)
            {
                owner.EstimatedCargoBaseline = true;
            }
            state.HasRefineryEvidence = true;
            state.Mineral = MiningCommodityName.Canonical(MiningJson.Text(data, "Type"));
            if (carryover)
            {
                owner.RefineryCarryover.Add(new(at, state.Mineral, 1, false));
            }
        }
    }

    internal void SelectOwner(MiningSession? owner, bool carryover)
    {
        if (state.OwnerId == owner?.Id && state.Carryover == carryover)
        {
            return;
        }
        // Unconfirmed output at a boundary is retained as an estimate, never counted again as carryover.
        if (state.OwnerId is { } previousId && resolve(previousId) is { } previous && state.LastCount is { } count)
        {
            int pending = state.Carryover
                ? previous.RefineryCarryover.Sum(c => c.Count) - (previous.ConfirmedCarryoverTons ?? 0)
                : (int)previous.RefinedTons - (previous.ConfirmedRefinedTons ?? 0);
            state.LastCount = (int)Math.Clamp((long)count + pending, 0, int.MaxValue);
        }
        state.OwnerId = owner?.Id;
        state.Carryover = carryover;
        state.Balance = carryover ? owner?.RefineryCarryover.Sum(c => c.Count) ?? 0 : (long)(owner?.RefinedTons ?? 0);
        state.HasRefineryEvidence = false;
        state.EstimatedBaseline = state.LastCount is null;
        state.Mineral = "";
        if (owner is not null && !carryover && state.EstimatedBaseline)
        {
            owner.EstimatedCargoBaseline = true;
        }
    }

    private static bool IsCargoBoundary(string name, JsonElement data) =>
        name
            is "Died"
                or "ShipyardBuy"
                or "ShipyardSwap"
                or "CargoDepot"
                or "MissionAccepted"
                or "MissionCompleted"
                or "MissionAbandoned"
                or "MissionFailed"
        || name == "Synthesis" && MiningJson.Text(data, "Name").Contains("Limpet", StringComparison.OrdinalIgnoreCase);

    private void Rebase(MiningSession? owner, DateTimeOffset at)
    {
        // These operations can change cargo without an exact journal quantity. Preserve
        // confirmed gross yield and take the next cargo reading as a new baseline.
        if (owner is not null && !state.Carryover)
        {
            owner.EstimatedCargoBaseline |= owner.HasUnconfirmedYield;
        }
        state.LastCount = null;
        state.LastSnapshotAt = at;
        state.ExternalDelta = 0;
        state.HasRefineryEvidence = false;
    }

    private void ObserveCargo(JsonElement data, DateTimeOffset at, MiningSession? owner, bool credit)
    {
        string vessel = MiningJson.Text(data, "Vessel");
        if (
            (vessel.Length > 0 && vessel != "Ship")
            || !data.TryGetProperty(CountProperty, out JsonElement value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out int count)
            || count < 0
        )
        {
            return;
        }
        if (state.LastCount is { } before && owner is not null && credit)
        {
            // Cargo can lag a limpet launch. Keep signed deltas across snapshots so a late
            // consumption update cannot lose (or invent) a ton at the first refinery event.
            state.Balance += count - before - state.ExternalDelta;
            if (state.HasRefineryEvidence)
            {
                Reconcile(owner, (int)Math.Clamp(state.Balance, 0, int.MaxValue), at);
            }
        }
        if (state.LastCount is null && owner is not null)
        {
            // The first reading cannot separate pre-existing cargo from earlier production.
            // Retain that provisional yield and reconcile only subsequent movements.
            state.Balance = state.Carryover ? owner.RefineryCarryover.Sum(c => c.Count) : (long)owner.RefinedTons;
        }
        state.LastCount = count;
        state.LastSnapshotAt = at;
        state.ExternalDelta = 0;
    }

    private void Reconcile(MiningSession session, int total, DateTimeOffset at)
    {
        List<MiningCollectionEntry> entries = state.Carryover ? session.RefineryCarryover : session.Collections;
        int observed = entries.Where(c => !c.Engineering).Sum(c => c.Count);
        int excess = observed - total;
        for (int i = entries.Count - 1; i >= 0 && excess > 0; i--)
        {
            MiningCollectionEntry entry = entries[i];
            if (entry.Engineering)
            {
                continue;
            }
            int removed = Math.Min(entry.Count, excess);
            if (removed == entry.Count)
            {
                entries.RemoveAt(i);
            }
            else
            {
                entries[i] = entry with { Count = entry.Count - removed };
            }
            excess -= removed;
        }
        if (total > observed && state.Mineral.Length > 0)
        {
            entries.Add(new(at, state.Mineral, total - observed, false));
        }
        if (state.Carryover)
        {
            session.ConfirmedCarryoverTons = total;
        }
        else
        {
            session.ConfirmedRefinedTons = total;
        }
        // Bare Cargo records establish total tonnage, not the mineral split of simultaneous mixed output.
        session.EstimatedMineralBreakdown |= entries
            .Where(c => !c.Engineering)
            .Select(c => MiningCommodityName.Canonical(c.Name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Skip(1)
            .Any();
    }

    private static long ExternalChange(string name, JsonElement data) =>
        name switch
        {
            "LaunchDrone" => -1,
            "BuyDrones" or "MarketBuy" => Quantity(data, CountProperty),
            "SellDrones" or "EjectCargo" or "MarketSell" => -Quantity(data, CountProperty),
            "CollectCargo" => 1,
            "CargoTransfer" => MiningJson.Array(data, "Transfers").Sum(TransferChange),
            "ColonisationContribution" => -MiningJson.Array(data, "Contributions").Sum(c => Quantity(c, "Amount")),
            _ => 0,
        };

    private static long TransferChange(JsonElement data) =>
        MiningJson.Text(data, "Direction") switch
        {
            "toship" => Quantity(data, CountProperty),
            "tocarrier" or "tosrv" => -Quantity(data, CountProperty),
            _ => 0,
        };

    private static long Quantity(JsonElement data, string key) =>
        data.TryGetProperty(key, out JsonElement value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out int count)
            ? Math.Max(0, count)
            : 0;
}
