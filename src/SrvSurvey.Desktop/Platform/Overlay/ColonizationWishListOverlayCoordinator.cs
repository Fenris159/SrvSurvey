using System.ComponentModel;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

/// <summary>Shows the saved shopping plan through the shared passive overlay lifecycle.</summary>
public sealed class ColonizationWishListOverlayCoordinator : IDisposable
{
    private readonly ColonizationWishListViewModel viewModel;
    private readonly HostedOverlayWindow hostedWindow;
    private bool suppressed;
    private bool disposed;

    public ColonizationWishListOverlayCoordinator(
        ColonizationWishListViewModel viewModel,
        OverlayPresentationSession presentationSession
    )
    {
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        ArgumentNullException.ThrowIfNull(presentationSession);
        hostedWindow = presentationSession.HostPassiveWindow(
            new PassiveOverlayWindowDefinition(
                "PlotColonizationWishList",
                _ => new ColonizationWishListOverlayWindow(viewModel),
                (bounds, size) => new(bounds.X, bounds.Y + Math.Max(0, (bounds.Height - size.Height) / 2))
            )
        );
        viewModel.PropertyChanged += OnWishListChanged;
        SynchronizeIntent();
    }

    public bool IsVisible => hostedWindow.IsVisible;

    public void SetSuppressed(bool value)
    {
        if (disposed || suppressed == value)
        {
            return;
        }
        suppressed = value;
        SynchronizeIntent();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        viewModel.PropertyChanged -= OnWishListChanged;
        hostedWindow.Dispose();
    }

    private void OnWishListChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ColonizationWishListViewModel.HasItems))
        {
            SynchronizeIntent();
        }
    }

    private void SynchronizeIntent() => hostedWindow.Reconcile(!suppressed && viewModel.HasItems);
}
