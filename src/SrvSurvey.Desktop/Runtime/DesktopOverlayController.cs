using System.ComponentModel;
using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Runtime;

/// <summary>Coordinates gameplay overlay priorities, suppression, and persistent visibility shortcuts.</summary>
internal sealed class DesktopOverlayController : IDisposable
{
    private readonly MainWindowViewModel viewModel;
    private readonly OverlayWindowRegistry registry;
    private readonly CombatOverlayCoordinator? combat;
    private readonly ColonizationCommodityOverlayCoordinator? colony;
    private readonly Action<bool>? suppressOtherPanels;
    private bool manuallySuppressed;
    private bool disposed;

    public DesktopOverlayController(
        MainWindowViewModel viewModel,
        OverlayWindowRegistry registry,
        CombatOverlayCoordinator? combat = null,
        ColonizationCommodityOverlayCoordinator? colony = null,
        Action<bool>? suppressOtherPanels = null
    )
    {
        this.viewModel = viewModel;
        this.registry = registry;
        this.combat = combat;
        this.colony = colony;
        this.suppressOtherPanels = suppressOtherPanels;
        viewModel.SystemSurvey.PropertyChanged += OnPriorityChanged;
        viewModel.Combat.PropertyChanged += OnPriorityChanged;
        viewModel.OverlayBehavior.PropertyChanged += OnBehaviorChanged;
        SynchronizePriority();
        SynchronizeSuppression();
    }

    /// <summary>Returns null for actions owned by another workspace and otherwise reports whether the action was handled.</summary>
    public bool? TryExecute(GlobalInputAction action)
    {
        if (disposed)
        {
            return false;
        }
        switch (action)
        {
            case GlobalInputAction.ToggleAllVisibility:
                SetManualSuppression(!manuallySuppressed);
                return true;
            case GlobalInputAction.ToggleColonizationCommoditiesVisibility:
            case GlobalInputAction.ShowColonyShopping:
                if (colony is null)
                {
                    return false;
                }
                colony.ToggleVisibility(viewModel.OverlayPanelVisibility);
                return true;
            case GlobalInputAction.ToggleCollapseCoveredColonyGroups:
                ColonizationViewModel colonization = viewModel.Colonization;
                colonization.CollapseCoveredCommodityGroups = !colonization.CollapseCoveredCommodityGroups;
                return true;
            default:
                return null;
        }
    }

    public void SetManualSuppression(bool suppressed)
    {
        if (disposed)
        {
            return;
        }
        manuallySuppressed = suppressed;
        SynchronizeSuppression();
    }

    private void SynchronizePriority()
    {
        OverlayPriorityFacts facts = OverlayPriorityFacts.None;
        if (viewModel.SystemSurvey.IsFssInfoForced)
        {
            facts |= OverlayPriorityFacts.FssInfoForced;
        }
        if (viewModel.SystemSurvey.IsBodyInfoForced)
        {
            facts |= OverlayPriorityFacts.BodyInfoForced;
        }
        if (viewModel.Combat.SuppressForActiveBuildProjects)
        {
            facts |= OverlayPriorityFacts.HideCombatForColonization;
        }
        registry.SetPriorityFacts(facts);
    }

    private void SynchronizeSuppression()
    {
        bool suit = viewModel.OverlayBehavior.ShouldSuppressForSuit;
        bool session = viewModel.OverlayBehavior.ShouldSuppressForSession;
        registry.SetGlobalSuppression(manuallySuppressed, suit, session);
        combat?.SetSuppressed(manuallySuppressed || session);
        suppressOtherPanels?.Invoke(manuallySuppressed || session || suit);
    }

    private void OnPriorityChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (
            args.PropertyName
            is nameof(SystemSurveyViewModel.IsFssInfoForced)
                or nameof(SystemSurveyViewModel.IsBodyInfoForced)
                or nameof(CombatViewModel.SuppressForActiveBuildProjects)
        )
        {
            SynchronizePriority();
        }
    }

    private void OnBehaviorChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (
            args.PropertyName
            is nameof(OverlayBehaviorViewModel.ShouldSuppressForSuit)
                or nameof(OverlayBehaviorViewModel.ShouldSuppressForSession)
                or nameof(OverlayBehaviorViewModel.HideInDominatorSuit)
                or nameof(OverlayBehaviorViewModel.HideInMaverickSuit)
        )
        {
            SynchronizeSuppression();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        viewModel.SystemSurvey.PropertyChanged -= OnPriorityChanged;
        viewModel.Combat.PropertyChanged -= OnPriorityChanged;
        viewModel.OverlayBehavior.PropertyChanged -= OnBehaviorChanged;
    }
}
