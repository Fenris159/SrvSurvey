using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class ColonizationViewModel
{
    private long planningCarrierMarketId;
    private IReadOnlyList<ColonizationFleetCarrier>? planningCarriers;
    private bool updatingPlanningCarriers;
    private ColonizationCarrierSelectionViewModel? carrierSelection;

    public ColonizationCarrierSelectionViewModel CarrierSelection =>
        carrierSelection ??= new(OnPlanningCarrierSelectionChanged);

    public IReadOnlyList<ColonizationCarrierChoice> PlanningCarrierOptions { get; private set; } = [new(0, "ALL")];

    public ColonizationCarrierChoice SelectedPlanningCarrier
    {
        get =>
            PlanningCarrierOptions.FirstOrDefault(choice => choice.MarketId == planningCarrierMarketId)
            ?? PlanningCarrierOptions[0];
        set
        {
            if (value is not null)
            {
                SelectedPlanningCarrierMarketId = value.MarketId;
            }
        }
    }

    /// <summary>Keeps the saved carrier identity available while its dropdown options are loading.</summary>
    public long SelectedPlanningCarrierMarketId
    {
        get => planningCarrierMarketId;
        set
        {
            if (!updatingPlanningCarriers)
            {
                CarrierSelection.Restore(null, value);
            }
        }
    }

    private void RestorePlanningCarrier()
    {
        updatingPlanningCarriers = true;
        try
        {
            long legacy = CommanderName is null ? 0 : settingsStore.LoadPlanningCarrier(CommanderName);
            CarrierSelection.Restore(
                CommanderName is null ? null : settingsStore.LoadPlanningCarrierIds(CommanderName),
                legacy
            );
            planningCarrierMarketId = CarrierSelection.LegacyMarketId;
            planningCarriers = null;
        }
        finally
        {
            updatingPlanningCarriers = false;
        }
        UpdatePlanningCarriers();
    }

    private void OnPlanningCarrierSelectionChanged()
    {
        if (updatingPlanningCarriers)
        {
            return;
        }
        planningCarrierMarketId = CarrierSelection.LegacyMarketId;
        OnPropertyChanged(nameof(SelectedPlanningCarrierMarketId));
        OnPropertyChanged(nameof(SelectedPlanningCarrier));
        SavePlanningCarrier();
        UpdateCommodityPlan();
        ApplyPlanningCarrierToShopping();
    }

    private void UpdatePlanningCarriers()
    {
        if (!ReferenceEquals(planningCarriers, LinkedFleetCarriers))
        {
            IReadOnlyList<long>? previous = CarrierSelection.CapturePreference();
            long legacy = CarrierSelection.LegacyMarketId;
            planningCarriers = LinkedFleetCarriers;
            ProjectEditor.UpdateCarrierOptions(LinkedFleetCarriers);
            PlanningCarrierOptions = ColonizationCarrierChoice.CreateOptions(LinkedFleetCarriers);
            updatingPlanningCarriers = true;
            try
            {
                CarrierSelection.UpdateCarriers(LinkedFleetCarriers, pruneMissing: hasCurrentShoppingProjects);
                planningCarrierMarketId = CarrierSelection.LegacyMarketId;
                OnPropertyChanged(nameof(PlanningCarrierOptions));
                OnPropertyChanged(nameof(SelectedPlanningCarrierMarketId));
                OnPropertyChanged(nameof(SelectedPlanningCarrier));
            }
            finally
            {
                updatingPlanningCarriers = false;
            }
            if (hasCurrentShoppingProjects && !CarrierSelection.Matches(previous, legacy))
            {
                SavePlanningCarrier();
                ApplyPlanningCarrierToShopping();
            }
        }
        UpdateProjectCarrierCoverage();
        RaiseProjectCarrierCommandStates();
    }

    private void UpdateProjectCarrierCoverage()
    {
        bool changed = false;
        foreach (ColonizationProjectRowViewModel row in Projects)
        {
            changed |= row.UpdateCarrierCoverage(
                ColonizationShopping.CarrierCoveragePercent(
                    row.Project,
                    LinkedFleetCarriers,
                    selectedMarketIds: CarrierSelection.CaptureSelectedIds()
                )
            );
        }
        if (
            changed
            && projectSorter.Indicator(nameof(ColonizationProjectRowViewModel.CarrierCoveragePercent)).Length > 0
        )
        {
            ApplyProjectFilters();
        }
    }

    private void ApplyPlanningCarrierToShopping()
    {
        if (
            shoppingWorkspace?.SelectPlanningCarriers(CarrierSelection.CapturePreference(), planningCarrierMarketId)
            == true
        )
        {
            return;
        }
        if (shoppingSession?.Search is { } search)
        {
            shoppingSession = shoppingSession with
            {
                Search = search with
                {
                    SelectedCarrierMarketId = planningCarrierMarketId,
                    SelectedCarrierMarketIds = CarrierSelection.CapturePreference(),
                },
            };
            ReconcileWishList();
            SaveShoppingSession("The updated carrier selection could not be saved to disk.");
        }
    }

    private void SavePlanningCarrier()
    {
        if (CommanderName is null)
        {
            return;
        }
        try
        {
            settingsStore.SavePlanningCarrier(
                CommanderName,
                planningCarrierMarketId,
                CarrierSelection.CapturePreference()
            );
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StatusMessage = "The carrier selection could not be saved to disk.";
        }
    }
}
