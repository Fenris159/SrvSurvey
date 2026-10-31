using SrvSurvey.Core.Routes;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class RouteWorkspaceViewModel
{
    private RouteWorkspaceViewModel? carrierLibraryPeer;

    private RouteWorkspaceViewModel? SharedCarrierWorkspace =>
        carrierLibraryPeer is { } peer
        && HasProfile
        && string.Equals(frontierId, peer.frontierId, StringComparison.OrdinalIgnoreCase)
            ? peer
            : null;

    internal void ShareCarrierLibraryWith(RouteWorkspaceViewModel peer)
    {
        ArgumentNullException.ThrowIfNull(peer);
        if (!IsFleetCarrierWorkspace || !peer.IsFleetCarrierWorkspace || ReferenceEquals(this, peer))
        {
            throw new ArgumentException(
                "Choose another carrier workspace to share the itinerary library.",
                nameof(peer)
            );
        }
        carrierLibraryPeer = peer;
        peer.carrierLibraryPeer = this;
    }

    internal Task RefreshSharedCarrierCatalogAsync() =>
        carrierLibraryPeer is null ? Task.CompletedTask : RefreshCatalogAsync();

    internal async Task NotifySharedCarrierRouteDeletedAsync(string filePath)
    {
        if (SharedCarrierWorkspace is not { } peer)
        {
            return;
        }
        if (peer.IsLoadedSavedRoute(filePath))
        {
            await peer.HandleLoadedRouteDeletedAsync();
        }
        else
        {
            await peer.RefreshCatalogAsync(refreshPeer: false);
        }
    }

    private async Task NotifySharedCarrierRenameAsync(FollowRouteRenameResult result)
    {
        if (SharedCarrierWorkspace is { } peer)
        {
            await peer.HandleRouteRenamedAsync(result, notifyPeer: false);
        }
    }

    internal void ApplySharedCarrierFavorite(string path, bool favorite) =>
        SharedCarrierWorkspace?.ApplyExternalFavorite(path, favorite);

    internal async Task ApplySharedCarrierNotesAsync(string path, string? notes)
    {
        if (SharedCarrierWorkspace is { } peer)
        {
            if (peer.IsLoadedSavedRoute(path) && peer.loadedRoute is { } current)
            {
                bool preserveDraft = peer.HasNotesChanges;
                string? normalized = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
                peer.loadedRoute = current with { Notes = normalized };
                if (!preserveDraft)
                {
                    peer.draftNotes = normalized;
                }
                peer.RaiseRouteMetadataProperties();
                await peer.RetainWorkspaceAsync();
            }
            await peer.RefreshCatalogAsync(refreshPeer: false);
        }
    }
}
