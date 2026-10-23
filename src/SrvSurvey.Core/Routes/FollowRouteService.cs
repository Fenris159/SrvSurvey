namespace SrvSurvey.Core.Routes;

public sealed class FollowRouteService(FollowRouteStore store)
{
    /// <summary>Retains a working itinerary without adding or overwriting a saved route.</summary>
    public Task SaveWorkspaceAsync(
        FollowRouteDocument route,
        bool hasSavedRoute,
        CancellationToken cancellationToken = default
    ) => store.SaveWorkspaceAsync(route, hasSavedRoute, cancellationToken);

    public Task<FollowRouteLoadResult> LoadAsync(string frontierId, CancellationToken cancellationToken = default)
    {
        return store.LoadAsync(frontierId, cancellationToken);
    }

    public Task<IReadOnlyList<FollowRouteCatalogEntry>> ListAsync(
        string frontierId,
        CancellationToken cancellationToken = default
    )
    {
        return store.ListAsync(frontierId, cancellationToken);
    }

    public Task<FollowRouteLoadResult> LoadNamedAsync(
        string frontierId,
        string fileName,
        bool isLegacy,
        CancellationToken cancellationToken = default
    )
    {
        return store.LoadNamedAsync(frontierId, fileName, isLegacy, cancellationToken);
    }

    public Task<FollowRouteLoadResult> ReloadAsync(
        FollowRouteDocument route,
        CancellationToken cancellationToken = default
    )
    {
        return store.ReloadAsync(route, cancellationToken);
    }

    public Task<FollowRouteDocument> CreateNewAsync(string frontierId, CancellationToken cancellationToken = default)
    {
        return store.CreateNewAsync(frontierId, cancellationToken);
    }

    public Task<FollowRouteDocument> SaveAsAsync(
        FollowRouteDocument route,
        string name,
        CancellationToken cancellationToken = default
    )
    {
        return store.SaveAsAsync(route, name, cancellationToken);
    }

    public async Task<FollowRouteDocument> SaveProgressAsync(
        FollowRouteDocument route,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(route);
        FollowRouteDocument updated = PrepareActivation(route, currentSystemAddress: null);
        await store.SaveProgressAsync(updated, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    public Task<FollowRouteDocument> SaveNotesAsync(
        FollowRouteDocument route,
        string? notes,
        CancellationToken cancellationToken = default
    )
    {
        return store.SaveNotesAsync(route, notes, cancellationToken);
    }

    public Task<FollowRouteDocument> SaveNotesAsync(
        string frontierId,
        string fileName,
        bool isLegacy,
        string? notes,
        CancellationToken cancellationToken = default
    )
    {
        return store.SaveNotesAsync(frontierId, fileName, isLegacy, notes, cancellationToken);
    }

    public Task<FollowRouteDocument> SetFavoriteAsync(
        string frontierId,
        string fileName,
        bool isLegacy,
        bool isFavorite,
        CancellationToken cancellationToken = default
    )
    {
        return store.SetFavoriteAsync(frontierId, fileName, isLegacy, isFavorite, cancellationToken);
    }

    public Task<FollowRouteDocument> ImportAsync(
        string frontierId,
        string sourcePath,
        CancellationToken cancellationToken = default
    )
    {
        return store.ImportAsync(frontierId, sourcePath, cancellationToken);
    }

    public Task<IReadOnlyList<string>> ExportAsync(
        string frontierId,
        IReadOnlyList<FollowRouteCatalogEntry> routes,
        string destinationDirectory,
        CancellationToken cancellationToken = default
    )
    {
        return store.ExportAsync(frontierId, routes, destinationDirectory, cancellationToken);
    }

    public Task<IReadOnlyList<string>> ExportSpanshAsync(
        string frontierId,
        IReadOnlyList<FollowRouteCatalogEntry> routes,
        string destinationDirectory,
        CancellationToken cancellationToken = default
    )
    {
        return store.ExportSpanshAsync(frontierId, routes, destinationDirectory, cancellationToken);
    }

    public Task<IReadOnlyList<string>> ExportCsvAsync(
        string frontierId,
        IReadOnlyList<FollowRouteCatalogEntry> routes,
        string destinationDirectory,
        CancellationToken cancellationToken = default
    )
    {
        return store.ExportCsvAsync(frontierId, routes, destinationDirectory, cancellationToken);
    }

    public Task<FollowRouteRenameResult> RenameAsync(
        string frontierId,
        string fileName,
        bool isLegacy,
        string name,
        CancellationToken cancellationToken = default
    )
    {
        return store.RenameAsync(frontierId, fileName, isLegacy, name, cancellationToken);
    }

    public Task<string> DeleteNamedAsync(
        string frontierId,
        string fileName,
        bool isLegacy,
        CancellationToken cancellationToken = default
    )
    {
        return store.DeleteNamedAsync(frontierId, fileName, isLegacy, cancellationToken);
    }

    public Task<string> DeleteAsync(FollowRouteDocument route, CancellationToken cancellationToken = default)
    {
        return store.DeleteAsync(route, cancellationToken);
    }

    public async Task<FollowRouteDocument> ReplaceAsync(
        FollowRouteDocument route,
        IReadOnlyList<FollowRouteHop> hops,
        int lastReachedIndex,
        bool isActive,
        bool autoCopy,
        long? currentSystemAddress = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(hops);
        FollowRouteDocument updated = route with
        {
            Hops = hops.ToArray(),
            LastReachedIndex = NormalizeLastIndex(lastReachedIndex, hops.Count),
            IsActive = isActive,
            AutoCopy = autoCopy,
        };
        updated = PrepareActivation(updated, currentSystemAddress);
        await store.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    public async Task<FollowRouteDocument> SetActiveAsync(
        FollowRouteDocument route,
        bool isActive,
        long? currentSystemAddress = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(route);
        FollowRouteDocument updated = isActive
            ? PrepareActivation(route with { IsActive = true }, currentSystemAddress)
            : route with
            {
                IsActive = false,
            };
        await store.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    public async Task<FollowRouteDocument> SetAutoCopyAsync(
        FollowRouteDocument route,
        bool autoCopy,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(route);
        FollowRouteDocument updated = route with { AutoCopy = autoCopy };
        await store.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    public async Task<FollowRouteDocument> SetProgressAsync(
        FollowRouteDocument route,
        int lastReachedIndex,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(route);
        FollowRouteDocument updated = FollowRouteProgression.SetProgress(route, lastReachedIndex);
        await store.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    public async Task<FollowRouteDocument> SetBioTargetCompletedAsync(
        FollowRouteDocument route,
        int hopIndex,
        int targetIndex,
        bool isCompleted,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(route);
        if (hopIndex < 0 || hopIndex >= route.Hops.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(hopIndex));
        }

        int sourceIndex = route.GetSourceIndex(hopIndex);
        FollowRouteHop hop = route.Hops[sourceIndex];
        if (targetIndex < 0 || targetIndex >= hop.BioTargets.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(targetIndex));
        }

        if (hop.BioTargets[targetIndex].IsCompleted == isCompleted)
        {
            return route;
        }

        FollowRouteBioTarget[] targets = hop.BioTargets.ToArray();
        targets[targetIndex] = targets[targetIndex] with { IsCompleted = isCompleted };
        FollowRouteHop[] hops = route.Hops.ToArray();
        hops[sourceIndex] = hop with { Bio = targets };
        FollowRouteDocument updated = route with { Hops = hops };
        await store.SaveProgressAsync(updated, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    /// <summary>Advances and saves a library route when the next destination is reached.</summary>
    public Task<FollowRouteArrivalResult> ApplyArrivalAsync(
        FollowRouteDocument route,
        string systemName,
        long? systemAddress,
        CancellationToken cancellationToken = default
    ) => ApplyArrivalAsync(route, systemName, systemAddress, persist: true, cancellationToken);

    /// <summary>Advances a working route, optionally persisting its saved library document.</summary>
    public async Task<FollowRouteArrivalResult> ApplyArrivalAsync(
        FollowRouteDocument route,
        string systemName,
        long? systemAddress,
        bool persist,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(route);
        if (!route.IsActive || route.Hops.Count == 0)
        {
            return new FollowRouteArrivalResult(route, false, null);
        }

        int startIndex = Math.Clamp(route.LastReachedIndex, 0, route.Hops.Count);
        int reachedIndex = FindHopIndex(route.GetNavigationHops(), startIndex, systemName, systemAddress);
        if (reachedIndex < 0 || (reachedIndex != route.Hops.Count - 1 && reachedIndex != route.LastReachedIndex + 1))
        {
            return new FollowRouteArrivalResult(route, false, null);
        }

        if (route.RepeatMode != FollowRouteRepeatMode.None && route.LastReachedIndex < 0 && reachedIndex != 0)
        {
            return new FollowRouteArrivalResult(route, false, null);
        }

        FollowRouteDocument updated = FollowRouteProgression.SetProgress(route, reachedIndex);
        bool restarted = reachedIndex == route.Hops.Count - 1 && updated.IsActive;
        if (persist)
        {
            await store.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
        }
        return new FollowRouteArrivalResult(updated, true, reachedIndex, restarted);
    }

    private static FollowRouteDocument PrepareActivation(FollowRouteDocument route, long? currentSystemAddress)
    {
        int lastReachedIndex = NormalizeLastIndex(route.LastReachedIndex, route.Hops.Count);
        if (route.IsActive && route.Hops.Count > 0 && lastReachedIndex >= route.Hops.Count - 1)
        {
            route = FollowRouteProgression.SetProgress(route, lastReachedIndex);
            lastReachedIndex = route.LastReachedIndex;
        }
        if (!route.IsActive || route.Hops.Count == 0 || lastReachedIndex >= route.Hops.Count - 1)
        {
            return route with { LastReachedIndex = lastReachedIndex, IsActive = false };
        }

        if (
            lastReachedIndex == -1
            && route.Hops.Count > 1
            && currentSystemAddress is { } address
            && route.GetNavigationHop(0).SystemAddress == address
        )
        {
            lastReachedIndex = 0;
        }

        return route with
        {
            LastReachedIndex = lastReachedIndex,
            IsActive = true,
        };
    }

    private static int NormalizeLastIndex(int value, int hopCount)
    {
        return hopCount == 0 ? -1 : Math.Clamp(value, -1, hopCount - 1);
    }

    private static int FindHopIndex(
        IReadOnlyList<FollowRouteHop> hops,
        int startIndex,
        string systemName,
        long? systemAddress
    )
    {
        for (int index = startIndex; index < hops.Count; index++)
        {
            FollowRouteHop hop = hops[index];
            if (
                (systemAddress is not null && hop.SystemAddress == systemAddress)
                || string.Equals(hop.Name, systemName, StringComparison.OrdinalIgnoreCase)
            )
            {
                return index;
            }
        }

        return -1;
    }
}
