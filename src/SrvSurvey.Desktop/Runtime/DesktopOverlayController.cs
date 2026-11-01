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

    /// <summary>Changes this session's manual gate without allowing a retired controller to affect active overlays.</summary>
    public void SetManualSuppression(bool suppressed)
    {
        if (disposed)
        {
            return;
        }
        manuallySuppressed = suppressed;
        SynchronizeSuppression();
    }

    /// <summary>Establishes priority and session gates before any hosted overlay can register or present.</summary>
    public static void PreparePresentation(MainWindowViewModel viewModel, OverlayWindowRegistry registry)
    {
        registry.SetPriorityFacts(PriorityFacts(viewModel));
        registry.SetGlobalSuppression(
            false,
            viewModel.OverlayBehavior.ShouldSuppressForSuit,
            viewModel.OverlayBehavior.ShouldSuppressForSession
        );
    }

    private void SynchronizePriority() => registry.SetPriorityFacts(PriorityFacts(viewModel));

    /// <summary>Derives presentation priorities from forced survey panes and the colonization suppression preference.</summary>
    private static OverlayPriorityFacts PriorityFacts(MainWindowViewModel viewModel)
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
        return facts;
    }

    /// <summary>Applies session and manual gates to combat, adding suit suppression only for other gameplay panels.</summary>
    private void SynchronizeSuppression()
    {
        bool suit = viewModel.OverlayBehavior.ShouldSuppressForSuit;
        bool session = viewModel.OverlayBehavior.ShouldSuppressForSession;
        registry.SetGlobalSuppression(manuallySuppressed, suit, session);
        combat?.SetSuppressed(manuallySuppressed || session);
        suppressOtherPanels?.Invoke(manuallySuppressed || session || suit);
    }

    /// <summary>Refreshes priority facts only when a preference that contributes to them changes.</summary>
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

    /// <summary>Reconciles live suppression when session readiness or suit-related preferences change.</summary>
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

    /// <summary>Detaches preference subscriptions so a retired session cannot overwrite current presentation policy.</summary>
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
