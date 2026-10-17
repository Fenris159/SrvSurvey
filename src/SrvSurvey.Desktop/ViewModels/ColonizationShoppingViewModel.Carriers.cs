using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class ColonizationShoppingViewModel
{
    private IReadOnlyList<ColonizationFleetCarrier> linkedCarriers = [];
    private bool useCarrierDifference = true;
    private long selectedCarrierMarketId;

    public IReadOnlyList<ColonizationCarrierChoice> CarrierOptions { get; private set; } = [new(0, "ALL")];

    public bool UseCarrierDifference
    {
        get => useCarrierDifference;
        set
        {
            if (Set(ref useCarrierDifference, value))
            {
                ReconcileCarrierNeeds();
            }
        }
    }

    public ColonizationCarrierChoice SelectedCarrier
    {
        get => CarrierOptions.FirstOrDefault(choice => choice.MarketId == selectedCarrierMarketId) ?? CarrierOptions[0];
        set
        {
            if (value is not null && Set(ref selectedCarrierMarketId, value.MarketId))
            {
                Changed(nameof(SelectedCarrier));
                ReconcileCarrierNeeds();
            }
        }
    }

    internal void UpdateCarriers(IReadOnlyList<ColonizationFleetCarrier> carriers, long? preferredMarketId = null)
    {
        if (disposed)
        {
            return;
        }
        if (preferredMarketId.HasValue)
        {
            selectedCarrierMarketId = preferredMarketId.Value;
        }
        SetCarriers(carriers);
        ReconcileCarrierNeeds();
    }

    internal bool SelectPlanningCarrier(long marketId)
    {
        if (disposed)
        {
            return false;
        }
        SelectedCarrier = CarrierOptions.FirstOrDefault(choice => choice.MarketId == marketId) ?? CarrierOptions[0];
        return true;
    }

    private void SetCarriers(IReadOnlyList<ColonizationFleetCarrier> carriers)
    {
        linkedCarriers = carriers;
        CarrierOptions = ColonizationCarrierChoice.CreateOptions(carriers);
        if (!CarrierOptions.Any(choice => choice.MarketId == selectedCarrierMarketId))
        {
            selectedCarrierMarketId = 0;
        }
        Changed(nameof(CarrierOptions));
        Changed(nameof(SelectedCarrier));
    }

    private IReadOnlyList<ColonizationShoppingNeed> EffectiveNeeds(IEnumerable<ColonizationShoppingNeed> needs) =>
        UseCarrierDifference
            ? ColonizationShopping.SubtractCarrierCargo(needs, linkedCarriers, selectedCarrierMarketId)
            : needs.ToArray();

    private void ReconcileCarrierNeeds()
    {
        plan = new ColonizationShoppingPlan(EffectiveNeeds(Needs.Select(need => need.Need)), quotes, plan.Snapshot());
        FilterResults();
        SaveWorkspace();
    }
}
