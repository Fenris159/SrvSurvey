using SrvSurvey.Core.Search;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class RouteAutoCopyCoordinator : IDisposable
{
    private readonly RouteWorkspaceViewModel standardRoute;
    private readonly RouteWorkspaceViewModel fleetCarrierRoute;
    private readonly IBoxelSearchSession boxel;
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "CA2213:Disposable fields should be disposed",
        Justification = "The gate may still be released by an in-flight clipboard operation during disposal.")]
    private readonly SemaphoreSlim ownershipGate = new(1, 1);
    private long claimVersion;
    private bool disposed;

    public RouteAutoCopyCoordinator(
        RouteWorkspaceViewModel standardRoute,
        RouteWorkspaceViewModel fleetCarrierRoute,
        IBoxelSearchSession boxel)
    {
        this.standardRoute = standardRoute;
        this.fleetCarrierRoute = fleetCarrierRoute;
        this.boxel = boxel;
        standardRoute.AutoCopySelected += OnRouteAutoCopySelected;
        fleetCarrierRoute.AutoCopySelected += OnRouteAutoCopySelected;
        boxel.Changed += OnBoxelChanged;
    }

    public Task ClaimAsync(RouteWorkspaceViewModel source)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!ReferenceEquals(source, standardRoute)
            && !ReferenceEquals(source, fleetCarrierRoute))
        {
            throw new ArgumentException(
                "The route workspace is not managed by this coordinator.",
                nameof(source));
        }

        return ClaimRouteAsync(
            source,
            Interlocked.Increment(ref claimVersion));
    }

    public Task ClaimAsync(IBoxelSearchSession source)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!ReferenceEquals(source, boxel))
        {
            throw new ArgumentException(
                "The boxel search is not managed by this coordinator.",
                nameof(source));
        }

        return ClaimBoxelAsync(
            source,
            Interlocked.Increment(ref claimVersion));
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

        if (CanOwnAutoCopy(fleetCarrierRoute))
        {
            await ClaimAsync(fleetCarrierRoute);
            return;
        }

        var version = Interlocked.Increment(ref claimVersion);
        await ownershipGate.WaitAsync();
        try
        {
            if (version != Volatile.Read(ref claimVersion))
            {
                return;
            }

            if (standardRoute.AutoCopy)
            {
                await standardRoute.DisableAutoCopyForCompetingRouteAsync();
            }

            if (version != Volatile.Read(ref claimVersion))
            {
                return;
            }

            if (fleetCarrierRoute.AutoCopy)
            {
                await fleetCarrierRoute.DisableAutoCopyForCompetingRouteAsync();
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
        standardRoute.AutoCopySelected -= OnRouteAutoCopySelected;
        fleetCarrierRoute.AutoCopySelected -= OnRouteAutoCopySelected;
        boxel.Changed -= OnBoxelChanged;
    }

    private async Task ClaimRouteAsync(
        RouteWorkspaceViewModel source,
        long version)
    {
        if (!CanOwnAutoCopy(source))
        {
            return;
        }

        await ownershipGate.WaitAsync();
        try
        {
            if (version != Volatile.Read(ref claimVersion)
                || !CanOwnAutoCopy(source))
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

    private async Task ClaimBoxelAsync(
        IBoxelSearchSession source,
        long version)
    {
        if (!CanOwnAutoCopy(source))
        {
            return;
        }

        await ownershipGate.WaitAsync();
        try
        {
            if (version != Volatile.Read(ref claimVersion)
                || !CanOwnAutoCopy(source))
            {
                return;
            }

            if (standardRoute.AutoCopy)
            {
                await standardRoute.DisableAutoCopyForCompetingRouteAsync();
            }

            if (version != Volatile.Read(ref claimVersion))
            {
                return;
            }

            if (fleetCarrierRoute.AutoCopy)
            {
                await fleetCarrierRoute.DisableAutoCopyForCompetingRouteAsync();
            }
        }
        finally
        {
            ownershipGate.Release();
        }
    }

    private async void OnRouteAutoCopySelected(object? sender, EventArgs eventArgs)
    {
        if (disposed
            || sender is not RouteWorkspaceViewModel source
            || !CanOwnAutoCopy(source))
        {
            return;
        }

        await ClaimAfterPropertyChangeAsync(source);
    }

    private async void OnBoxelChanged(
        object? sender,
        BoxelSearchSessionChangedEventArgs eventArgs)
    {
        if (disposed
            || !ReferenceEquals(sender, boxel)
            || eventArgs.Previous.Search.AutoCopy
            || !eventArgs.Current.Search.AutoCopy)
        {
            return;
        }

        await ClaimAfterPropertyChangeAsync(boxel);
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

    internal async Task ClaimAfterPropertyChangeAsync(
        IBoxelSearchSession source)
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
        return route.HasSavedRoute && route.AutoCopy;
    }

    private static bool CanOwnAutoCopy(IBoxelSearchSession boxelSearch)
    {
        return boxelSearch.Current.Search.AutoCopy;
    }
}
