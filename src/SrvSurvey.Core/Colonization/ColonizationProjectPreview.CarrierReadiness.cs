namespace SrvSurvey.Core.Colonization;

/// <summary>One carrier's proportional share of cargo usable against the report's remaining requirements.</summary>
public sealed record ColonizationPreviewCarrierReadiness(ColonizationPreviewCarrier Carrier, long? Ready);

public sealed partial class ColonizationProjectPreview
{
    /// <summary>Caps each commodity at its need, distributing coverage proportionally without counting surplus or shared carriers twice.</summary>
    public IReadOnlyList<ColonizationPreviewCarrierReadiness> GetCarrierReadiness()
    {
        if (ReadyOnCarriers is null)
        {
            return Carriers.Select(carrier => new ColonizationPreviewCarrierReadiness(carrier, null)).ToArray();
        }
        long[] ready = new long[Carriers.Count];
        foreach (ColonizationPreviewCommodity row in Rows.Where(row => row.Need > 0))
        {
            AllocateReadiness(row, ready);
        }
        return Carriers
            .Select((carrier, index) => new ColonizationPreviewCarrierReadiness(carrier, ready[index]))
            .ToArray();
    }

    private void AllocateReadiness(ColonizationPreviewCommodity row, long[] ready)
    {
        long available = row.CarrierQuantities.Sum(quantity => (long)Math.Max(0, quantity ?? 0));
        if (available == 0)
        {
            return;
        }
        long required = Math.Min(row.Need, available);
        decimal[] shares = row
            .CarrierQuantities.Select(quantity => (decimal)required * Math.Max(0, quantity ?? 0) / available)
            .ToArray();
        long[] wholeTonnes = shares.Select(share => (long)decimal.Floor(share)).ToArray();
        int remainder = checked((int)(required - wholeTonnes.Sum()));
        foreach (
            int index in Enumerable
                .Range(0, shares.Length)
                .OrderByDescending(index => shares[index] - wholeTonnes[index])
                .ThenBy(index => Carriers[index].MarketId)
                .Take(remainder)
        )
        {
            wholeTonnes[index]++;
        }
        for (int index = 0; index < ready.Length; index++)
        {
            ready[index] += wholeTonnes[index];
        }
    }
}
