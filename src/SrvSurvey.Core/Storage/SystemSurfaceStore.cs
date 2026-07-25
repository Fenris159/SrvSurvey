using System.Globalization;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Storage;

public sealed class SystemSurfaceStore
{
    private readonly LegacySystemDataFileStore fileStore;

    public SystemSurfaceStore(string dataDirectory)
    {
        fileStore = new LegacySystemDataFileStore(dataDirectory);
    }

    public async Task<SystemSurfaceLoadResult> LoadBodyAsync(
        SystemSurfaceContext context,
        CancellationToken cancellationToken = default)
    {
        ValidateContext(context);
        var result = await fileStore.LoadAsync(
                ToFileContext(context),
                cancellationToken)
            .ConfigureAwait(false);
        if (result.Root is null)
        {
            return result.Exists
                ? new SystemSurfaceLoadResult(
                    result.Path,
                    true,
                    false,
                    null,
                    result.Error,
                    [])
                : new SystemSurfaceLoadResult(
                    result.Path,
                    false,
                    false,
                    CreateEmptySnapshot(context),
                    null,
                    []);
        }

        var warnings = new List<string>();
        var body = FindBody(result.Root, context);
        if (body is null)
        {
            return new SystemSurfaceLoadResult(
                result.Path,
                true,
                false,
                CreateEmptySnapshot(context),
                null,
                warnings);
        }

        return new SystemSurfaceLoadResult(
            result.Path,
            true,
            true,
            ReadSnapshot(body, context, warnings),
            null,
            warnings);
    }

    public async Task<string> SetLastTouchdownAsync(
        SystemSurfaceContext context,
        SurfaceCoordinate? location,
        CancellationToken cancellationToken = default)
    {
        ValidateContext(context);
        return await fileStore.UpdateAsync(
                ToFileContext(context),
                root =>
                {
                    var body = GetOrCreateBody(root, context);
                    if (location is null)
                    {
                        body.Remove("lastTouchdown");
                    }
                    else
                    {
                        body["lastTouchdown"] = WriteCoordinate(location.Value);
                    }
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<SurfaceBookmarkMutationResult> AddBookmarkAsync(
        SystemSurfaceContext context,
        string name,
        SurfaceCoordinate location,
        double minimumSeparationMeters = 20,
        CancellationToken cancellationToken = default)
    {
        ValidateContext(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!double.IsFinite(minimumSeparationMeters)
            || minimumSeparationMeters < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumSeparationMeters));
        }

        var outcome = SurfaceBookmarkMutation.Added;
        var path = await fileStore.UpdateAsync(
                ToFileContext(context),
                root =>
                {
                    var body = GetOrCreateBody(root, context);
                    var bookmarks = GetOrCreateObject(body, "bookmarks");
                    var locations = GetOrCreateArray(bookmarks, name);
                    var existing = locations
                        .Select(ReadCoordinate)
                        .Where(coordinate => coordinate is not null)
                        .Select(coordinate => coordinate!.Value);
                    if (existing.Any(coordinate => IsWithin(
                            coordinate,
                            location,
                            context.RadiusMeters,
                            minimumSeparationMeters)))
                    {
                        outcome = SurfaceBookmarkMutation.TooClose;
                        return;
                    }

                    locations.Add(WriteCoordinate(location));
                },
                cancellationToken)
            .ConfigureAwait(false);
        return new SurfaceBookmarkMutationResult(path, outcome);
    }

    public async Task<string> RemoveBookmarkGroupAsync(
        SystemSurfaceContext context,
        string name,
        CancellationToken cancellationToken = default)
    {
        ValidateContext(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return await fileStore.UpdateAsync(
                ToFileContext(context),
                root =>
                {
                    var body = FindBody(root, context);
                    if (body?["bookmarks"] is not JsonObject bookmarks)
                    {
                        return;
                    }

                    bookmarks.Remove(name);
                    if (bookmarks.Count == 0)
                    {
                        body.Remove("bookmarks");
                    }
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<SurfaceBookmarkMutationResult> RemoveBookmarkAsync(
        SystemSurfaceContext context,
        string name,
        SurfaceCoordinate location,
        bool nearest = true,
        double? maximumDistanceMeters = null,
        CancellationToken cancellationToken = default)
    {
        ValidateContext(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (maximumDistanceMeters is { } maximum
            && (!double.IsFinite(maximum) || maximum < 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumDistanceMeters));
        }

        var outcome = SurfaceBookmarkMutation.NotFound;
        var path = await fileStore.UpdateAsync(
                ToFileContext(context),
                root =>
                {
                    var body = FindBody(root, context);
                    if (body?["bookmarks"] is not JsonObject bookmarks
                        || bookmarks[name] is not JsonArray locations)
                    {
                        return;
                    }

                    var candidates = locations
                        .Select((node, index) => new
                        {
                            Index = index,
                            Coordinate = ReadCoordinate(node),
                        })
                        .Where(candidate => candidate.Coordinate is not null)
                        .Select(candidate => new
                        {
                            candidate.Index,
                            Distance = GetDistance(
                                candidate.Coordinate!.Value,
                                location,
                                context.RadiusMeters),
                        })
                        .OrderBy(candidate => candidate.Distance)
                        .ToArray();
                    if (candidates.Length == 0)
                    {
                        return;
                    }

                    var selected = nearest ? candidates[0] : candidates[^1];
                    if (maximumDistanceMeters is { } limit
                        && selected.Distance >= limit)
                    {
                        return;
                    }

                    locations.RemoveAt(selected.Index);
                    if (locations.Count == 0)
                    {
                        bookmarks.Remove(name);
                    }

                    if (bookmarks.Count == 0)
                    {
                        body.Remove("bookmarks");
                    }

                    outcome = SurfaceBookmarkMutation.Removed;
                },
                cancellationToken)
            .ConfigureAwait(false);
        return new SurfaceBookmarkMutationResult(path, outcome);
    }

    public async Task<string> AppendBioScansAsync(
        SystemSurfaceContext context,
        IReadOnlyList<SurfaceBioScan> scans,
        CancellationToken cancellationToken = default)
    {
        ValidateContext(context);
        ArgumentNullException.ThrowIfNull(scans);
        foreach (var scan in scans)
        {
            ArgumentNullException.ThrowIfNull(scan);
            if (!double.IsFinite(scan.RadiusMeters) || scan.RadiusMeters <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(scans));
            }

            ArgumentException.ThrowIfNullOrWhiteSpace(scan.Status);
        }

        return await fileStore.UpdateAsync(
                ToFileContext(context),
                root =>
                {
                    var body = GetOrCreateBody(root, context);
                    var bioScans = GetOrCreateArray(body, "bioScans");
                    foreach (var scan in scans)
                    {
                        if (bioScans.Any(node => IsSameScan(node, scan)))
                        {
                            continue;
                        }

                        bioScans.Add(WriteBioScan(scan));
                    }
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<SurfaceDeathMarkResult> MarkBioScansDiedAsync(
        string frontierId,
        IReadOnlyList<string> scannedBioEntryIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(frontierId);
        ArgumentNullException.ThrowIfNull(scannedBioEntryIds);
        var warnings = new List<string>();
        var claims = new HashSet<SurfaceBioScanClaim>();
        foreach (var value in scannedBioEntryIds)
        {
            if (TryParseBioScanClaim(value, out var claim))
            {
                claims.Add(claim);
            }
            else
            {
                warnings.Add(
                    $"Unclaimed biological scan ID '{value}' is malformed and "
                        + "was not applied to surface history.");
            }
        }

        var markedScanCount = 0;
        var changedFileCount = 0;
        foreach (var systemGroup in claims.GroupBy(claim => claim.SystemAddress))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var systemAddress = systemGroup.Key;
            var claimsByBody = systemGroup
                .GroupBy(claim => claim.BodyId)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(claim => claim.EntryId).ToHashSet());
            var fileContext = new LegacySystemDataFileContext(
                frontierId,
                null,
                systemAddress.ToString(CultureInfo.InvariantCulture),
                systemAddress,
                null);
            var result = await fileStore.UpdateExistingAsync(
                    fileContext,
                    root =>
                    {
                        var marked = MarkSystemBioScansDied(
                            root,
                            claimsByBody);
                        markedScanCount += marked;
                        return marked > 0;
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                warnings.Add(result.Error);
            }

            if (result.Changed)
            {
                changedFileCount++;
            }
        }

        return new SurfaceDeathMarkResult(
            markedScanCount,
            changedFileCount,
            warnings);
    }

    private static int MarkSystemBioScansDied(
        JsonObject root,
        IReadOnlyDictionary<int, HashSet<long>> claimsByBody)
    {
        if (root["bodies"] is not JsonArray bodies)
        {
            return 0;
        }

        var markedScanCount = 0;
        foreach (var body in bodies.OfType<JsonObject>())
        {
            if (GetInt32(body["id"]) is not { } bodyId
                || !claimsByBody.TryGetValue(bodyId, out var entryIds)
                || body["bioScans"] is not JsonArray scans)
            {
                continue;
            }

            foreach (var scan in scans.OfType<JsonObject>())
            {
                if (GetInt64(scan["entryId"]) is not { } entryId
                    || !entryIds.Contains(entryId)
                    || string.Equals(
                        GetString(scan["status"]),
                        "Abandoned",
                        StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        GetString(scan["status"]),
                        "Died",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                scan["status"] = "Died";
                markedScanCount++;
            }
        }

        return markedScanCount;
    }

    private static bool TryParseBioScanClaim(
        string value,
        out SurfaceBioScanClaim claim)
    {
        claim = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split(
            '_',
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 5
            || !long.TryParse(
                parts[0],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var systemAddress)
            || !int.TryParse(
                parts[1],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var bodyId)
            || !long.TryParse(
                parts[2],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var entryId)
            || systemAddress <= 0
            || bodyId < 0
            || entryId <= 0)
        {
            return false;
        }

        claim = new SurfaceBioScanClaim(systemAddress, bodyId, entryId);
        return true;
    }

    private static SystemSurfaceBodySnapshot ReadSnapshot(
        JsonObject body,
        SystemSurfaceContext context,
        List<string> warnings)
    {
        var bookmarks = ReadBookmarks(body, warnings);
        var scans = ReadBioScans(body, warnings);
        var touchdown = ReadCoordinate(body["lastTouchdown"]);
        if (body["lastTouchdown"] is not null && touchdown is null)
        {
            warnings.Add("The saved touchdown coordinates are invalid and were ignored.");
        }

        return new SystemSurfaceBodySnapshot(
            GetInt32(body["id"]) ?? context.BodyId,
            GetString(body["name"]) ?? context.BodyName,
            GetDouble(body["radius"]) ?? context.RadiusMeters,
            touchdown,
            bookmarks,
            scans);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<SurfaceCoordinate>>
        ReadBookmarks(JsonObject body, List<string> warnings)
    {
        if (body["bookmarks"] is null)
        {
            return new Dictionary<string, IReadOnlyList<SurfaceCoordinate>>(
                StringComparer.Ordinal);
        }

        if (body["bookmarks"] is not JsonObject bookmarks)
        {
            warnings.Add("The saved bookmarks are not a JSON object and were ignored.");
            return new Dictionary<string, IReadOnlyList<SurfaceCoordinate>>(
                StringComparer.Ordinal);
        }

        var result = new Dictionary<string, IReadOnlyList<SurfaceCoordinate>>(
            StringComparer.Ordinal);
        foreach (var pair in bookmarks)
        {
            if (pair.Value is not JsonArray locations)
            {
                warnings.Add(
                    $"Bookmark group '{pair.Key}' is not an array and was ignored.");
                continue;
            }

            var parsed = new List<SurfaceCoordinate>();
            foreach (var node in locations)
            {
                if (ReadCoordinate(node) is { } coordinate)
                {
                    parsed.Add(coordinate);
                }
                else
                {
                    warnings.Add(
                        $"An invalid coordinate in bookmark group '{pair.Key}' was ignored.");
                }
            }

            result[pair.Key] = parsed;
        }

        return result;
    }

    private static IReadOnlyList<SurfaceBioScan> ReadBioScans(
        JsonObject body,
        List<string> warnings)
    {
        if (body["bioScans"] is null)
        {
            return [];
        }

        if (body["bioScans"] is not JsonArray scans)
        {
            warnings.Add("The saved biological scans are not an array and were ignored.");
            return [];
        }

        var result = new List<SurfaceBioScan>();
        foreach (var node in scans)
        {
            if (node is not JsonObject scan
                || ReadCoordinate(scan["location"]) is not { } location)
            {
                warnings.Add("A biological scan with invalid coordinates was ignored.");
                continue;
            }

            var radius = GetDouble(scan["radius"]) ?? 50;
            if (!double.IsFinite(radius) || radius <= 0)
            {
                warnings.Add("A biological scan with an invalid radius was ignored.");
                continue;
            }

            result.Add(new SurfaceBioScan(
                location,
                radius,
                GetString(scan["genus"]) ?? string.Empty,
                GetString(scan["species"]) ?? string.Empty,
                GetString(scan["status"]) ?? "Active",
                GetInt64(scan["entryId"]) ?? 0,
                GetString(scan["body"])));
        }

        return result;
    }

    private static JsonObject? FindBody(
        JsonObject root,
        SystemSurfaceContext context)
    {
        if (root["bodies"] is not JsonArray bodies)
        {
            return null;
        }

        return bodies
                .OfType<JsonObject>()
                .FirstOrDefault(body => GetInt32(body["id"]) == context.BodyId)
            ?? bodies
                .OfType<JsonObject>()
                .FirstOrDefault(body => string.Equals(
                    GetString(body["name"]),
                    context.BodyName,
                    StringComparison.OrdinalIgnoreCase));
    }

    private static JsonObject GetOrCreateBody(
        JsonObject root,
        SystemSurfaceContext context)
    {
        JsonArray bodies;
        if (root["bodies"] is null)
        {
            bodies = [];
            root["bodies"] = bodies;
        }
        else if (root["bodies"] is JsonArray existingBodies)
        {
            bodies = existingBodies;
        }
        else
        {
            throw new InvalidDataException(
                "The legacy system body's collection is malformed and was not overwritten.");
        }

        var body = FindBody(root, context);
        if (body is not null)
        {
            return body;
        }

        body = new JsonObject
        {
            ["name"] = context.BodyName,
            ["id"] = context.BodyId,
        };
        if (context.RadiusMeters > 0)
        {
            body["radius"] = context.RadiusMeters;
        }

        bodies.Add(body);
        return body;
    }

    private static JsonObject GetOrCreateObject(
        JsonObject owner,
        string propertyName)
    {
        if (owner[propertyName] is null)
        {
            var created = new JsonObject();
            owner[propertyName] = created;
            return created;
        }

        return owner[propertyName] as JsonObject
            ?? throw new InvalidDataException(
                $"The legacy '{propertyName}' value is malformed and was not overwritten.");
    }

    private static JsonArray GetOrCreateArray(
        JsonObject owner,
        string propertyName)
    {
        if (owner[propertyName] is null)
        {
            var created = new JsonArray();
            owner[propertyName] = created;
            return created;
        }

        return owner[propertyName] as JsonArray
            ?? throw new InvalidDataException(
                $"The legacy '{propertyName}' value is malformed and was not overwritten.");
    }

    private static JsonObject WriteCoordinate(SurfaceCoordinate location)
    {
        return new JsonObject
        {
            ["lat"] = location.Latitude,
            ["long"] = location.Longitude,
        };
    }

    private static JsonObject WriteBioScan(SurfaceBioScan scan)
    {
        var result = new JsonObject
        {
            ["location"] = WriteCoordinate(scan.Location),
            ["radius"] = scan.RadiusMeters,
            ["genus"] = scan.Genus,
            ["species"] = scan.Species,
            ["status"] = scan.Status,
        };
        if (scan.EntryId != 0)
        {
            result["entryId"] = scan.EntryId;
        }

        if (!string.IsNullOrWhiteSpace(scan.BodyName))
        {
            result["body"] = scan.BodyName;
        }

        return result;
    }

    private static bool IsSameScan(JsonNode? node, SurfaceBioScan scan)
    {
        return node is JsonObject existing
            && ReadCoordinate(existing["location"]) == scan.Location
            && string.Equals(
                GetString(existing["species"]),
                scan.Species,
                StringComparison.Ordinal);
    }

    private static SurfaceCoordinate? ReadCoordinate(JsonNode? node)
    {
        if (node is not JsonObject coordinate
            || GetDouble(coordinate["lat"]) is not { } latitude
            || GetDouble(coordinate["long"]) is not { } longitude)
        {
            return null;
        }

        try
        {
            return new SurfaceCoordinate(latitude, longitude);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static bool IsWithin(
        SurfaceCoordinate first,
        SurfaceCoordinate second,
        double radiusMeters,
        double minimumSeparationMeters)
    {
        if (radiusMeters <= 0)
        {
            return first == second;
        }

        return GetDistance(first, second, radiusMeters)
            < minimumSeparationMeters;
    }

    private static double GetDistance(
        SurfaceCoordinate first,
        SurfaceCoordinate second,
        double radiusMeters)
    {
        return radiusMeters > 0
            ? SurfaceNavigation.GetDistance(first, second, radiusMeters)
            : first == second
                ? 0
                : double.PositiveInfinity;
    }

    private static string? GetString(JsonNode? node)
    {
        return node is JsonValue value
            && value.TryGetValue<string>(out var result)
                ? result
                : null;
    }

    private static int? GetInt32(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<int>(out var result))
        {
            return result;
        }

        return value.TryGetValue<string>(out var text)
            && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out result)
                ? result
                : null;
    }

    private static long? GetInt64(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<long>(out var result))
        {
            return result;
        }

        return value.TryGetValue<string>(out var text)
            && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out result)
                ? result
                : null;
    }

    private static double? GetDouble(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<double>(out var result))
        {
            return result;
        }

        return value.TryGetValue<string>(out var text)
            && double.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out result)
                ? result
                : null;
    }

    private static SystemSurfaceBodySnapshot CreateEmptySnapshot(
        SystemSurfaceContext context)
    {
        return new SystemSurfaceBodySnapshot(
            context.BodyId,
            context.BodyName,
            context.RadiusMeters,
            null,
            new Dictionary<string, IReadOnlyList<SurfaceCoordinate>>(
                StringComparer.Ordinal),
            []);
    }

    private static LegacySystemDataFileContext ToFileContext(
        SystemSurfaceContext context)
    {
        return new LegacySystemDataFileContext(
            context.FrontierId,
            context.CommanderName,
            context.SystemName,
            context.SystemAddress,
            context.StarPosition);
    }

    private static void ValidateContext(SystemSurfaceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.BodyName);
        if (context.BodyId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(context));
        }

        if (!double.IsFinite(context.RadiusMeters)
            || context.RadiusMeters < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(context));
        }
    }

    private readonly record struct SurfaceBioScanClaim(
        long SystemAddress,
        int BodyId,
        long EntryId);
}

public sealed record SystemSurfaceContext(
    string FrontierId,
    string? CommanderName,
    string SystemName,
    long SystemAddress,
    GalacticCoordinate? StarPosition,
    int BodyId,
    string BodyName,
    double RadiusMeters);

public sealed record SystemSurfaceBodySnapshot(
    int BodyId,
    string BodyName,
    double RadiusMeters,
    SurfaceCoordinate? LastTouchdown,
    IReadOnlyDictionary<string, IReadOnlyList<SurfaceCoordinate>> Bookmarks,
    IReadOnlyList<SurfaceBioScan> BioScans);

public sealed record SurfaceBioScan(
    SurfaceCoordinate Location,
    double RadiusMeters,
    string Genus,
    string Species,
    string Status,
    long EntryId,
    string? BodyName);

public sealed record SystemSurfaceLoadResult(
    string Path,
    bool FileExists,
    bool BodyExists,
    SystemSurfaceBodySnapshot? Snapshot,
    string? Error,
    IReadOnlyList<string> Warnings)
{
    public bool IsSuccess => Snapshot is not null;
}

public sealed record SurfaceBookmarkMutationResult(
    string Path,
    SurfaceBookmarkMutation Mutation);

public sealed record SurfaceDeathMarkResult(
    int MarkedScanCount,
    int ChangedFileCount,
    IReadOnlyList<string> Warnings);

public enum SurfaceBookmarkMutation
{
    Added,
    TooClose,
    Removed,
    NotFound,
}
