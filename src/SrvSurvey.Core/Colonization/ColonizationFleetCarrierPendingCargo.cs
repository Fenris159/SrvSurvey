namespace SrvSurvey.Core.Colonization;

/// <summary>
/// Helpers for EDMC-style dock cargo baselines: queue journal deltas while an
/// async Market.json / server baseline is in flight, then replay them.
/// Does not change Market.json partial-replacement semantics (Market.json is not
/// a full hold manifest).
/// </summary>
public static class ColonizationFleetCarrierPendingCargo
{
    public static bool NeedsServerBaseline(IReadOnlyDictionary<string, int>? cargo)
    {
        return cargo is null || cargo.Count == 0 || !cargo.Values.Any(quantity => quantity > 0);
    }

    public static void MergeDelta(IDictionary<string, int> pending, IReadOnlyDictionary<string, int> delta)
    {
        ArgumentNullException.ThrowIfNull(pending);
        ArgumentNullException.ThrowIfNull(delta);
        foreach (KeyValuePair<string, int> pair in delta)
        {
            string commodity = ColonizationConstructionState.NormalizeCommodityName(pair.Key);
            if (commodity.Length == 0 || pair.Value == 0)
            {
                continue;
            }

            pending.TryGetValue(commodity, out int current);
            long updated = (long)current + pair.Value;
            if (updated is < int.MinValue or > int.MaxValue)
            {
                throw new InvalidDataException("Queued Fleet Carrier cargo delta exceeds supported counts.");
            }

            if (updated == 0)
            {
                pending.Remove(commodity);
            }
            else
            {
                pending[commodity] = (int)updated;
            }
        }
    }
}
