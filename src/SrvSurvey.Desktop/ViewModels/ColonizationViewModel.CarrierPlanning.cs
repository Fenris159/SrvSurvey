using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class ColonizationViewModel
{
    private long planningCarrierMarketId;
    private IReadOnlyList<ColonizationFleetCarrier>? planningCarriers;
    private bool updatingPlanningCarriers;

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
            if (updatingPlanningCarriers || !SetField(ref planningCarrierMarketId, value))
            {
                return;
            }
            OnPropertyChanged(nameof(SelectedPlanningCarrier));
            SavePlanningCarrier();
            UpdateCommodityPlan();
            ApplyPlanningCarrierToShopping();
        }
    }

    private void RestorePlanningCarrier()
    {
        planningCarrierMarketId = CommanderName is null ? 0 : settingsStore.LoadPlanningCarrier(CommanderName);
        planningCarriers = null;
        UpdatePlanningCarriers();
    }

    private void UpdatePlanningCarriers()
    {
        if (!ReferenceEquals(planningCarriers, LinkedFleetCarriers))
        {
            planningCarriers = LinkedFleetCarriers;
            PlanningCarrierOptions = ColonizationCarrierChoice.CreateOptions(LinkedFleetCarriers);
            bool reset =
                hasCurrentShoppingProjects
                && !PlanningCarrierOptions.Any(choice => choice.MarketId == planningCarrierMarketId);
            if (reset)
            {
                planningCarrierMarketId = 0;
            }
            updatingPlanningCarriers = true;
            try
            {
                OnPropertyChanged(nameof(PlanningCarrierOptions));
                OnPropertyChanged(nameof(SelectedPlanningCarrierMarketId));
                OnPropertyChanged(nameof(SelectedPlanningCarrier));
            }
            finally
            {
                updatingPlanningCarriers = false;
            }
            if (reset)
            {
                SavePlanningCarrier();
                ApplyPlanningCarrierToShopping();
            }
        }
        UpdateProjectCarrierCoverage();
    }

    private void UpdateProjectCarrierCoverage()
    {
        foreach (ColonizationProjectRowViewModel row in Projects)
        {
            row.UpdateCarrierCoverage(
                ColonizationShopping.CarrierCoveragePercent(row.Project, LinkedFleetCarriers, planningCarrierMarketId)
            );
        }
    }

    private void ApplyPlanningCarrierToShopping()
    {
        if (shoppingWorkspace?.SelectPlanningCarrier(planningCarrierMarketId) == true)
        {
            return;
        }
        if (shoppingSession?.Search is { } search)
        {
            shoppingSession = shoppingSession with
            {
                Search = search with { SelectedCarrierMarketId = planningCarrierMarketId },
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
            settingsStore.SavePlanningCarrier(CommanderName, planningCarrierMarketId);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StatusMessage = "The carrier selection could not be saved to disk.";
        }
    }
}
