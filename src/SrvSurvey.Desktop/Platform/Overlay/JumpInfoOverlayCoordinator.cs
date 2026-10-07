using System.ComponentModel;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class JumpInfoOverlayCoordinator : IDisposable
{
    private readonly JumpInfoViewModel jumpInfo;
    private readonly JumpInfoOverlayViewModel viewModel;
    private readonly HostedOverlayWindow hostedWindow;
    private bool isSuppressed;
    private bool disposed;

    public JumpInfoOverlayCoordinator(
        JumpInfoViewModel jumpInfo,
        OverlayPresentationSession presentationSession,
        SystemNicknameViewModel? systemNicknames = null
    )
    {
        this.jumpInfo = jumpInfo ?? throw new ArgumentNullException(nameof(jumpInfo));
        ArgumentNullException.ThrowIfNull(presentationSession);
        hostedWindow = presentationSession.HostPassiveWindow(
            new PassiveOverlayWindowDefinition(
                "PlotJumpInfo",
                _ => CreateWindow(),
                (gameBounds, windowSize) => OverlayWindowPlacement.TopCenter(gameBounds, windowSize),
                ApplyPreparation
            )
            {
                Tick = OnTick,
                BeginPresentation = BeginPresentation,
                EndPresentation = jumpInfo.EndOverlayPresentation,
            }
        );
        viewModel = new JumpInfoOverlayViewModel(jumpInfo, hostedWindow.Capabilities, systemNicknames);
        hostedWindow.VisibilityChanged += OnHostedVisibilityChanged;
        jumpInfo.PropertyChanged += OnJumpInfoPropertyChanged;
        SynchronizeIntent();
    }

    public event EventHandler? VisibilityChanged;

    public bool IsVisible => hostedWindow.IsVisible;

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
        jumpInfo.PropertyChanged -= OnJumpInfoPropertyChanged;
        hostedWindow.VisibilityChanged -= OnHostedVisibilityChanged;
        hostedWindow.Dispose();
        viewModel.Dispose();
    }

    private JumpInfoOverlayWindow CreateWindow() => new(viewModel);

    private void ApplyPreparation(OverlayPreparationResult preparation)
    {
        viewModel.ApplyPreparation(preparation);
    }

    private void OnTick()
    {
        jumpInfo.AdvanceTimedTransitions();
        SynchronizeIntent();
    }

    private bool BeginPresentation()
    {
        jumpInfo.BeginOverlayPresentation();
        return jumpInfo.ShouldShow;
    }

    private void OnJumpInfoPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(JumpInfoViewModel.ShouldShow))
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

        hostedWindow.Reconcile(!isSuppressed && jumpInfo.ShouldShow);
    }

    private void OnHostedVisibilityChanged(object? sender, EventArgs eventArgs)
    {
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }
}
