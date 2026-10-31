using System.Text.Json.Nodes;

namespace SrvSurvey.Core.Routes;

public sealed partial class FollowRouteStore
{
    private readonly FollowRouteCarrierKind? carrierWorkspace;

    private FollowRouteStore(FollowRouteStore library, FollowRouteCarrierKind? carrier)
    {
        dataDirectory = library.dataDirectory;
        routeKind = library.routeKind;
        saveLock = library.saveLock;
        carrierWorkspace = carrier;
    }

    /// <summary>Shares saved itineraries and their write lock while keeping each carrier's working state separate.</summary>
    public FollowRouteStore ForCarrier(FollowRouteCarrierKind carrier)
    {
        if (routeKind != FollowRouteKind.FleetCarrier || !Enum.IsDefined(carrier))
        {
            throw new ArgumentException(
                "Choose a carrier workspace from a fleet-carrier route library.",
                nameof(carrier)
            );
        }
        return new(this, carrier);
    }

    private FollowRouteCarrierKind ParseWorkspaceCarrierKind(string path, JsonObject root, FollowRouteKind kind)
    {
        if (kind != FollowRouteKind.FleetCarrier)
        {
            return FollowRouteCarrierKind.Personal;
        }
        FollowRouteCarrierKind parsed = ParseCarrierKind(path, GetString(root, "carrierKind"));
        return carrierWorkspace ?? parsed;
    }

    private string GetWorkspaceDirectory(string frontierId) =>
        carrierWorkspace is { } carrier
            ? Path.Combine(GetNamedDirectory(frontierId), ".workspaces", carrier.ToString())
            : GetNamedDirectory(frontierId);

    private string GetCarrierProgressPath(FollowRouteDocument route) =>
        Path.Combine(GetWorkspaceDirectory(route.FrontierId), "Progress", Path.GetFileName(route.FilePath));

    private Task WriteCarrierProgressAsync(FollowRouteDocument route, CancellationToken cancellationToken)
    {
        var root = new JsonObject();
        WriteRoute(root, route with { CarrierKind = carrierWorkspace!.Value });
        return WriteObjectAsync(GetCarrierProgressPath(route), root, cancellationToken);
    }

    private async Task SynchronizeCarrierLibraryEntryAsync(
        string frontierId,
        string fileName,
        string? replacement,
        string? name,
        CancellationToken cancellationToken
    )
    {
        if (routeKind != FollowRouteKind.FleetCarrier)
        {
            return;
        }
        string library = GetNamedDirectory(frontierId);
        IEnumerable<string> directories = Enum.GetValues<FollowRouteCarrierKind>()
            .Select(carrier => Path.Combine(library, ".workspaces", carrier.ToString()));
        foreach (string directory in directories.Prepend(library))
        {
            UpdateCarrierProgressFile(directory, fileName, replacement);
            if (carrierWorkspace is null && directory == library)
            {
                continue;
            }
            await UpdateCarrierSelectionAsync(directory, fileName, replacement, name, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static void UpdateCarrierProgressFile(string directory, string fileName, string? replacement)
    {
        string path = Path.Combine(directory, "Progress", fileName);
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!File.Exists(path) || string.Equals(fileName, replacement, comparison))
        {
            return;
        }
        if (replacement is null)
        {
            File.Delete(path);
        }
        else
        {
            File.Move(path, Path.Combine(directory, "Progress", replacement), true);
        }
    }

    private static async Task UpdateCarrierSelectionAsync(
        string directory,
        string fileName,
        string? replacement,
        string? name,
        CancellationToken cancellationToken
    )
    {
        string path = Path.Combine(directory, WorkspaceFileName);
        if (!File.Exists(path))
        {
            return;
        }
        JsonObjectReadResult read = await ReadObjectAsync(path, cancellationToken).ConfigureAwait(false);
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (
            read.Root is not { } root
            || !string.Equals(GetString(root, SelectedFilePropertyName), fileName, comparison)
        )
        {
            return;
        }
        WriteOptional(root, SelectedFilePropertyName, replacement);
        root.Remove("legacy");
        if (name is not null && root[DraftPropertyName] is JsonObject draft)
        {
            draft["name"] = name;
        }
        await WriteObjectAsync(path, root, cancellationToken).ConfigureAwait(false);
    }

    private async Task<FollowRouteLoadResult> LoadCarrierWorkspaceRouteAsync(
        string frontierId,
        string path,
        bool migrateCarrierState,
        CancellationToken cancellationToken
    )
    {
        FollowRouteLoadResult baseline = await LoadFromPathAsync(frontierId, path, cancellationToken)
            .ConfigureAwait(false);
        if (carrierWorkspace is not { } carrier || baseline.Route is not { } route)
        {
            return baseline;
        }
        route = route with { CarrierKind = carrier };
        if (migrateCarrierState)
        {
            return baseline with { Route = route };
        }

        string progressPath = GetCarrierProgressPath(route);
        if (!File.Exists(progressPath))
        {
            return baseline with { Route = ResetCarrierProgress(route) };
        }
        FollowRouteLoadResult progress = await LoadFromPathAsync(frontierId, progressPath, cancellationToken)
            .ConfigureAwait(false);
        if (progress.Route is not { } saved)
        {
            return baseline with { Route = null, Error = progress.Error };
        }
        bool sameHops = JsonNode.DeepEquals(MergeHops(null, route.Hops), MergeHops(null, saved.Hops));
        return baseline with
        {
            Route = route with
            {
                IsActive = saved.IsActive,
                AutoCopy = saved.AutoCopy,
                LastReachedIndex = sameHops ? saved.LastReachedIndex : -1,
                RepeatMode = saved.RepeatMode,
                IsReversed = saved.IsReversed,
                RestockOnlyWhenLow = saved.RestockOnlyWhenLow,
            },
        };
    }

    private static FollowRouteDocument ResetCarrierProgress(FollowRouteDocument route) =>
        route with
        {
            LastReachedIndex = -1,
            IsActive = true,
            AutoCopy = false,
            IsReversed = false,
        };

    /// <summary>Recovers the former shared workspace for the matching carrier without letting the other consume it.</summary>
    private async Task<WorkspaceSelectionReadResult> ReadLegacyCarrierSelectionAsync(
        string frontierId,
        CancellationToken cancellationToken
    )
    {
        var legacy = new FollowRouteStore(this, null);
        WorkspaceSelectionReadResult selection = await legacy
            .ReadSelectionAsync(frontierId, cancellationToken)
            .ConfigureAwait(false);
        if (!selection.Exists || selection.Error is not null)
        {
            return selection;
        }
        FollowRouteCarrierKind? carrier;
        if (selection.Draft is { } draft)
        {
            try
            {
                carrier = ParseCarrierKind(GetWorkspacePath(frontierId), GetString(draft, "carrierKind"));
            }
            catch (InvalidDataException exception)
            {
                return selection with { Error = exception.Message };
            }
        }
        else
        {
            FollowRouteLoadResult loaded = await legacy
                .LoadSelectionAsync(frontierId, selection, cancellationToken)
                .ConfigureAwait(false);
            carrier = loaded.Route?.CarrierKind;
        }
        return carrier == carrierWorkspace
            ? selection with
            {
                MigrateCarrierState = true,
            }
            : new(false, null, false, null);
    }
}
