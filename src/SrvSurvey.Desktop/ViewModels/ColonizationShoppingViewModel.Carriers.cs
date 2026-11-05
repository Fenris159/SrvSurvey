using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class ColonizationShoppingViewModel
{
    private IReadOnlyList<ColonizationFleetCarrier> linkedCarriers = [];
    private bool useCarrierDifference = true;
    private long selectedCarrierMarketId;
    private bool updatingCarrierSelection;
    private ColonizationCarrierSelectionViewModel? carrierSelection;

    public ColonizationCarrierSelectionViewModel CarrierSelection =>
        carrierSelection ??= new(OnCarrierSelectionChanged);

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
            if (value is not null)
            {
                CarrierSelection.Restore(null, value.MarketId);
            }
        }
    }

    /// <summary>Refreshes cargo and carrier choices while optionally applying a legacy single/all preference.</summary>
    internal void UpdateCarriers(IReadOnlyList<ColonizationFleetCarrier> carriers, long? preferredMarketId = null)
    {
        if (disposed)
        {
            return;
        }
        if (preferredMarketId.HasValue)
        {
            RestoreCarrierSelection(null, preferredMarketId.Value);
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

    /// <summary>Installs planning choices before reconciling cargo needs, preserving explicit empty and partial selections.</summary>
    internal void UpdatePlanningCarriers(
        IReadOnlyList<ColonizationFleetCarrier> carriers,
        IReadOnlyList<long>? ids,
        long legacyMarketId = 0
    )
    {
        if (disposed)
        {
            return;
        }
        RestoreCarrierSelection(ids, legacyMarketId);
        SetCarriers(carriers);
        ReconcileCarrierNeeds();
    }

    /// <summary>Synchronizes checked identities while this search still owns an active workspace.</summary>
    internal bool SelectPlanningCarriers(IReadOnlyList<long>? ids, long legacyMarketId = 0)
    {
        if (disposed)
        {
            return false;
        }
        CarrierSelection.Restore(ids, legacyMarketId);
        return true;
    }

    private void RestoreCarrierSelection(IReadOnlyList<long>? ids, long legacy)
    {
        updatingCarrierSelection = true;
        try
        {
            CarrierSelection.Restore(ids, legacy);
            selectedCarrierMarketId = CarrierSelection.LegacyMarketId;
        }
        finally
        {
            updatingCarrierSelection = false;
        }
    }

    /// <summary>Recalculates carrier-adjusted needs and persists a selection outside guarded roster refreshes.</summary>
    private void OnCarrierSelectionChanged()
    {
        if (!updatingCarrierSelection && !disposed)
        {
            selectedCarrierMarketId = CarrierSelection.LegacyMarketId;
            Changed(nameof(SelectedCarrier));
            ReconcileCarrierNeeds();
        }
    }

    private void SetCarriers(IReadOnlyList<ColonizationFleetCarrier> carriers)
    {
        linkedCarriers = carriers;
        CarrierOptions = ColonizationCarrierChoice.CreateOptions(carriers);
        updatingCarrierSelection = true;
        try
        {
            CarrierSelection.UpdateCarriers(carriers);
            selectedCarrierMarketId = CarrierSelection.LegacyMarketId;
        }
        finally
        {
            updatingCarrierSelection = false;
        }
        Changed(nameof(CarrierOptions));
        Changed(nameof(SelectedCarrier));
    }

    /// <summary>Subtracts inventory from checked carriers only when carrier difference is enabled.</summary>
    private IReadOnlyList<ColonizationShoppingNeed> EffectiveNeeds(IEnumerable<ColonizationShoppingNeed> needs) =>
        UseCarrierDifference
            ? ColonizationShopping.SubtractCarrierCargo(
                needs,
                linkedCarriers,
                selectedMarketIds: CarrierSelection.CaptureSelectedIds()
            )
            : needs.ToArray();

    private void ReconcileCarrierNeeds()
    {
        RebuildPlan(Needs.Select(need => need.Need), plan.Snapshot());
        FilterResults();
        SaveWorkspace();
    }
}
