using System.ComponentModel;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class CombatOverlayCoordinator : IDisposable
{
    private readonly CombatViewModel combat;
    private readonly CombatOverlayViewModel viewModel;
    private readonly HostedOverlayWindow footCombatWindow;
    private readonly HostedOverlayWindow massacreWindow;
    private bool isSuppressed;
    private bool disposed;

    public CombatOverlayCoordinator(CombatViewModel combat, OverlayPresentationSession presentationSession)
    {
        this.combat = combat ?? throw new ArgumentNullException(nameof(combat));
        ArgumentNullException.ThrowIfNull(presentationSession);
        footCombatWindow = presentationSession.HostPassiveWindow(
            new PassiveOverlayWindowDefinition(
                "PlotFootCombat",
                _ => CreateFootCombatWindow(),
                (gameBounds, windowSize) => OverlayWindowPlacement.TopLeft(gameBounds, windowSize, 8),
                ApplyPreparation
            )
            {
                Tick = SynchronizeIntent,
            }
        );
        massacreWindow = presentationSession.HostPassiveWindow(
            new PassiveOverlayWindowDefinition(
                "PlotMassacre",
                _ => CreateMassacreWindow(),
                (gameBounds, windowSize) => OverlayWindowPlacement.TopRight(gameBounds, windowSize, 8),
                ApplyPreparation
            )
        );
        viewModel = new CombatOverlayViewModel(combat, footCombatWindow.Capabilities);
        footCombatWindow.VisibilityChanged += OnHostedVisibilityChanged;
        massacreWindow.VisibilityChanged += OnHostedVisibilityChanged;
        combat.PropertyChanged += OnCombatPropertyChanged;
        SynchronizeIntent();
    }

    public event EventHandler? VisibilityChanged;

    public bool IsVisible => IsFootCombatVisible || IsMassacreVisible;

    public bool IsFootCombatVisible => footCombatWindow.IsVisible;

    public bool IsMassacreVisible => massacreWindow.IsVisible;

    public bool IsSuppressed => isSuppressed;

    public void SetSuppressed(bool value)
    {
        if (disposed || value == isSuppressed)
        {
            return;
        }

        isSuppressed = value;
        SynchronizeIntent();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        combat.PropertyChanged -= OnCombatPropertyChanged;
        footCombatWindow.VisibilityChanged -= OnHostedVisibilityChanged;
        massacreWindow.VisibilityChanged -= OnHostedVisibilityChanged;
        footCombatWindow.Dispose();
        massacreWindow.Dispose();
    }

    private void OnCombatPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (
            eventArgs.PropertyName
            is nameof(CombatViewModel.ShouldShowFootCombat)
                or nameof(CombatViewModel.ShouldShowMassacreMissions)
        )
        {
            SynchronizeIntent();
        }
    }

    private void SynchronizeIntent()
    {
        if (disposed)
        {
            return;
        }

        footCombatWindow.Reconcile(!isSuppressed && combat.ShouldShowFootCombat);
        massacreWindow.Reconcile(!isSuppressed && combat.ShouldShowMassacreMissions);
    }

    private FootCombatOverlayWindow CreateFootCombatWindow() => new(viewModel);

    private MassacreMissionsOverlayWindow CreateMassacreWindow() => new(viewModel);

    private void ApplyPreparation(OverlayPreparationResult preparation)
    {
        viewModel.ApplyPreparation(preparation);
    }

    private void OnHostedVisibilityChanged(object? sender, EventArgs eventArgs)
    {
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }
}
