using System.ComponentModel;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class RouteAutoCopyCoordinator : IDisposable
{
    private readonly RouteWorkspaceViewModel standardRoute;
    private readonly RouteWorkspaceViewModel fleetCarrierRoute;
    private readonly SemaphoreSlim ownershipGate = new(1, 1);
    private bool disposed;

    public RouteAutoCopyCoordinator(
        RouteWorkspaceViewModel standardRoute,
        RouteWorkspaceViewModel fleetCarrierRoute)
    {
        this.standardRoute = standardRoute;
        this.fleetCarrierRoute = fleetCarrierRoute;
        standardRoute.PropertyChanged += OnRoutePropertyChanged;
        fleetCarrierRoute.PropertyChanged += OnRoutePropertyChanged;
    }

    public async Task ClaimAsync(RouteWorkspaceViewModel source)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!ReferenceEquals(source, standardRoute)
            && !ReferenceEquals(source, fleetCarrierRoute))
        {
            throw new ArgumentException(
                "The route workspace is not managed by this coordinator.",
                nameof(source));
        }

        if (!CanOwnAutoCopy(source))
        {
            return;
        }

        await ownershipGate.WaitAsync();
        try
        {
            if (!CanOwnAutoCopy(source))
            {
                return;
            }

            var other = ReferenceEquals(source, standardRoute)
                ? fleetCarrierRoute
                : standardRoute;
            if (other.AutoCopy)
            {
                await other.DisableAutoCopyForCompetingRouteAsync();
            }
        }
        finally
        {
            ownershipGate.Release();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        standardRoute.PropertyChanged -= OnRoutePropertyChanged;
        fleetCarrierRoute.PropertyChanged -= OnRoutePropertyChanged;
    }

    private void OnRoutePropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (disposed
            || eventArgs.PropertyName is not (
                nameof(RouteWorkspaceViewModel.AutoCopy)
                or nameof(RouteWorkspaceViewModel.HasSavedRoute)
                or nameof(RouteWorkspaceViewModel.IsActive)
                or nameof(RouteWorkspaceViewModel.ShouldAutoCopyNextHop))
            || sender is not RouteWorkspaceViewModel source
            || !CanOwnAutoCopy(source))
        {
            return;
        }

        _ = ClaimAfterPropertyChangeAsync(source);
    }

    internal async Task ClaimAfterPropertyChangeAsync(
        RouteWorkspaceViewModel source)
    {
        try
        {
            await ClaimAsync(source);
        }
        catch (ObjectDisposedException) when (disposed)
        {
            // Disposal can race a PropertyChanged notification that was already
            // dispatched. Shutdown must not surface that expected race as an
            // unhandled exception on the UI synchronization context.
        }
    }

    private static bool CanOwnAutoCopy(RouteWorkspaceViewModel route)
    {
        return route.HasSavedRoute && route.ShouldAutoCopyNextHop;
    }
}
