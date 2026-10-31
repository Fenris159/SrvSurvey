using Avalonia.Threading;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class RouteAutoCopyCoordinator : IDisposable
{
    private readonly RouteWorkspaceViewModel standardRoute;
    private readonly IReadOnlyList<RouteWorkspaceViewModel> carrierRoutes;
    private RouteWorkspaceViewModel? preferredCarrier;
    private readonly IBoxelSearchSession boxel;

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "CA2213:Disposable fields should be disposed",
        Justification = "The gate may still be released by an in-flight clipboard operation during disposal."
    )]
    private readonly SemaphoreSlim ownershipGate = new(1, 1);
    private long claimVersion;
    private bool disposed;

    public RouteAutoCopyCoordinator(
        RouteWorkspaceViewModel standardRoute,
        RouteWorkspaceViewModel fleetCarrierRoute,
        IBoxelSearchSession boxel,
        RouteWorkspaceViewModel? squadronCarrierRoute = null
    )
    {
        this.standardRoute = standardRoute;
        carrierRoutes = squadronCarrierRoute is null ? [fleetCarrierRoute] : [fleetCarrierRoute, squadronCarrierRoute];
        this.boxel = boxel;
        standardRoute.AutoCopySelected += OnRouteAutoCopySelected;
        foreach (RouteWorkspaceViewModel carrier in carrierRoutes)
        {
            carrier.AutoCopySelected += OnRouteAutoCopySelected;
        }
        boxel.Changed += OnBoxelChanged;
    }

    public Task ClaimAsync(RouteWorkspaceViewModel source)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!ReferenceEquals(source, standardRoute) && !carrierRoutes.Contains(source))
        {
            throw new ArgumentException("The route workspace is not managed by this coordinator.", nameof(source));
        }

        return ClaimRouteAsync(source, Interlocked.Increment(ref claimVersion));
    }

    public Task ClaimAsync(IBoxelSearchSession source)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!ReferenceEquals(source, boxel))
        {
            throw new ArgumentException("The boxel search is not managed by this coordinator.", nameof(source));
        }

        return ClaimBoxelAsync(source, Interlocked.Increment(ref claimVersion));
    }

    public async Task ReconcileAsync()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (boxel.Current.Search.AutoCopy)
        {
            await ClaimAsync(boxel);
            return;
        }

        if (CanOwnAutoCopy(standardRoute))
        {
            await ClaimAsync(standardRoute);
            return;
        }

        RouteWorkspaceViewModel? carrier =
            preferredCarrier is not null && CanOwnAutoCopy(preferredCarrier)
                ? preferredCarrier
                : carrierRoutes.FirstOrDefault(CanOwnAutoCopy);
        if (carrier is not null)
        {
            await ClaimAsync(carrier);
            return;
        }

        long version = Interlocked.Increment(ref claimVersion);
        await ownershipGate.WaitAsync();
        try
        {
            await DisableRoutesAsync([standardRoute, .. carrierRoutes], version);
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
        standardRoute.AutoCopySelected -= OnRouteAutoCopySelected;
        foreach (RouteWorkspaceViewModel carrier in carrierRoutes)
        {
            carrier.AutoCopySelected -= OnRouteAutoCopySelected;
        }
        boxel.Changed -= OnBoxelChanged;
    }

    /// <summary>One map opening writes one destination even when both carrier auto-copy settings are enabled.</summary>
    public bool CanAutoCopyOnMapOpen(RouteWorkspaceViewModel route) =>
        route.ShouldAutoCopyNextHop
        && (
            ReferenceEquals(route, standardRoute)
            || ReferenceEquals(
                route,
                preferredCarrier is { ShouldAutoCopyNextHop: true }
                    ? preferredCarrier
                    : carrierRoutes.FirstOrDefault(carrier => carrier.ShouldAutoCopyNextHop)
            )
        );

    private async Task ClaimRouteAsync(RouteWorkspaceViewModel source, long version)
    {
        if (!CanOwnAutoCopy(source))
        {
            return;
        }

        await ownershipGate.WaitAsync();
        try
        {
            if (version != Volatile.Read(ref claimVersion) || !CanOwnAutoCopy(source))
            {
                return;
            }

            if (carrierRoutes.Contains(source))
            {
                preferredCarrier = source;
            }

            IEnumerable<RouteWorkspaceViewModel> competingRoutes = ReferenceEquals(source, standardRoute)
                ? carrierRoutes
                : [standardRoute];
            await DisableRoutesAsync(competingRoutes, version);
            if (version != Volatile.Read(ref claimVersion))
            {
                return;
            }

            if (boxel.Current.Search.AutoCopy)
            {
                await boxel.ExecuteAsync(new SetBoxelAutoCopy(false));
            }
        }
        finally
        {
            ownershipGate.Release();
        }
    }

    private async Task ClaimBoxelAsync(IBoxelSearchSession source, long version)
    {
        if (!CanOwnAutoCopy(source))
        {
            return;
        }

        await ownershipGate.WaitAsync();
        try
        {
            if (version != Volatile.Read(ref claimVersion) || !CanOwnAutoCopy(source))
            {
                return;
            }

            await DisableRoutesAsync([standardRoute, .. carrierRoutes], version);
        }
        finally
        {
            ownershipGate.Release();
        }
    }

    private async Task DisableRoutesAsync(IEnumerable<RouteWorkspaceViewModel> routes, long version)
    {
        foreach (RouteWorkspaceViewModel route in routes)
        {
            if (version != Volatile.Read(ref claimVersion))
            {
                return;
            }
            if (route.AutoCopy)
            {
                await route.DisableAutoCopyForCompetingRouteAsync();
            }
        }
    }

    private async void OnRouteAutoCopySelected(object? sender, EventArgs eventArgs)
    {
        if (disposed || sender is not RouteWorkspaceViewModel source || !CanOwnAutoCopy(source))
        {
            return;
        }

        await ClaimAfterPropertyChangeAsync(source);
    }

    private async void OnBoxelChanged(object? sender, BoxelSearchSessionChangedEventArgs eventArgs)
    {
        if (
            disposed
            || !ReferenceEquals(sender, boxel)
            || eventArgs.Previous.Search.AutoCopy
            || !eventArgs.Current.Search.AutoCopy
        )
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            await ClaimAfterPropertyChangeAsync(boxel);
            return;
        }

        Dispatcher.UIThread.Post(async () => await ClaimAfterPropertyChangeAsync(boxel));
    }

    internal async Task ClaimAfterPropertyChangeAsync(RouteWorkspaceViewModel source)
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

    internal async Task ClaimAfterPropertyChangeAsync(IBoxelSearchSession source)
    {
        try
        {
            await ClaimAsync(source);
        }
        catch (ObjectDisposedException) when (disposed)
        {
            // Disposal can race an already-dispatched property notification.
        }
    }

    private static bool CanOwnAutoCopy(RouteWorkspaceViewModel route)
    {
        return route.HasRoute && route.AutoCopy;
    }

    private static bool CanOwnAutoCopy(IBoxelSearchSession boxelSearch)
    {
        return boxelSearch.Current.Search.AutoCopy;
    }
}
