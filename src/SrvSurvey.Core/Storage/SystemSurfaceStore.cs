using System.Globalization;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Storage;

public sealed class SystemSurfaceStore
{
    private const string ActiveStatus = "Active";
    private const string AbandonedStatus = "Abandoned";
    private const string BodyCollectionProperty = "bodies";
    private const string BodyIdProperty = "id";
    private const string BodyNameProperty = "name";
    private const string BodyRadiusProperty = "radius";
    private const string BodyStatusProperty = "status";
    private const string BioScansProperty = "bioScans";
    private const string BookmarksProperty = "bookmarks";
    private const string DiedStatus = "Died";
    private const string EntryIdProperty = "entryId";
    private const string GenusProperty = "genus";
    private const string LastTouchdownProperty = "lastTouchdown";
    private const string LatitudeProperty = "lat";
    private const string LocationProperty = "location";
    private const string LongitudeProperty = "long";
    private const string OrbitingBodyProperty = "body";
    private const string SpeciesProperty = "species";

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
                        body.Remove(LastTouchdownProperty);
                    }
                    else
                    {
                        body[LastTouchdownProperty] = WriteCoordinate(location.Value);
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
        name = LegacySurfaceBookmarkNames.Canonicalize(name);
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
                    var bookmarks = GetOrCreateObject(body, BookmarksProperty);
                    NormalizeLegacyBookmarkKeys(bookmarks);
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
        name = LegacySurfaceBookmarkNames.Canonicalize(name);
        return await fileStore.UpdateAsync(
                ToFileContext(context),
                root =>
                {
                    var body = FindBody(root, context);
                    if (body?[BookmarksProperty] is not JsonObject bookmarks)
                    {
                        return;
                    }

                    NormalizeLegacyBookmarkKeys(bookmarks);
                    bookmarks.Remove(name);
                    if (bookmarks.Count == 0)
                    {
                        body.Remove(BookmarksProperty);
                    }
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<string> ClearBookmarksAsync(
        SystemSurfaceContext context,
        CancellationToken cancellationToken = default)
    {
        ValidateContext(context);
        return await fileStore.UpdateAsync(
                ToFileContext(context),
                root => FindBody(root, context)?.Remove(BookmarksProperty),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<SurfaceBookmarkMutationResult> ToggleBookmarkGroupAsync(
        SystemSurfaceContext context,
        string name,
        SurfaceCoordinate location,
        CancellationToken cancellationToken = default)
    {
        ValidateContext(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        name = LegacySurfaceBookmarkNames.Canonicalize(name);
        var outcome = SurfaceBookmarkMutation.Added;
        var path = await fileStore.UpdateAsync(
                ToFileContext(context),
                root =>
                {
                    var body = GetOrCreateBody(root, context);
                    if (body[BookmarksProperty] is not JsonObject existing)
                    {
                        var bookmarks = GetOrCreateObject(body, BookmarksProperty);
                        bookmarks[name] = new JsonArray(WriteCoordinate(location));
                        return;
                    }

                    NormalizeLegacyBookmarkKeys(existing);
                    if (!existing.Remove(name))
                    {
                        existing[name] = new JsonArray(WriteCoordinate(location));
                        return;
                    }

                    if (existing.Count > 0)
                    {
                        outcome = SurfaceBookmarkMutation.Removed;
                        return;
                    }

                    body.Remove(BookmarksProperty);
                    outcome = SurfaceBookmarkMutation.Removed;
                },
                cancellationToken)
            .ConfigureAwait(false);
        return new SurfaceBookmarkMutationResult(path, outcome);
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
        name = LegacySurfaceBookmarkNames.Canonicalize(name);
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
                    if (TryRemoveBookmark(
                        root,
                        context,
                        name,
                        location,
                        nearest,
                        maximumDistanceMeters))
                    {
                        outcome = SurfaceBookmarkMutation.Removed;
                    }
                },
                cancellationToken)
            .ConfigureAwait(false);
        return new SurfaceBookmarkMutationResult(path, outcome);
    }

    private static bool TryRemoveBookmark(
        JsonObject root,
        SystemSurfaceContext context,
        string name,
        SurfaceCoordinate location,
        bool nearest,
        double? maximumDistanceMeters)
    {
        var body = FindBody(root, context);
        if (body?[BookmarksProperty] is not JsonObject bookmarks)
        {
            return false;
        }

        NormalizeLegacyBookmarkKeys(bookmarks);
        if (bookmarks[name] is not JsonArray locations)
        {
            return false;
        }

        var selectedIndex = FindBookmarkIndex(
            locations,
            location,
            context.RadiusMeters,
            nearest,
            maximumDistanceMeters);
        if (selectedIndex is null)
        {
            return false;
        }

        locations.RemoveAt(selectedIndex.Value);
        if (locations.Count == 0)
        {
            bookmarks.Remove(name);
        }

        if (bookmarks.Count == 0)
        {
            body.Remove(BookmarksProperty);
        }

        return true;
    }

    private static int? FindBookmarkIndex(
        JsonArray locations,
        SurfaceCoordinate location,
        double radiusMeters,
        bool nearest,
        double? maximumDistanceMeters)
    {
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
                    radiusMeters),
            })
            .OrderBy(candidate => candidate.Distance)
            .ToArray();
        if (candidates.Length == 0)
        {
            return null;
        }

        var selected = nearest ? candidates[0] : candidates[^1];
        if (maximumDistanceMeters is { } limit
            && selected.Distance >= limit)
        {
            return null;
        }

        return selected.Index;
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
                    var bioScans = GetOrCreateArray(body, BioScansProperty);
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
        Dictionary<int, HashSet<long>> claimsByBody)
    {
        if (root[BodyCollectionProperty] is not JsonArray bodies)
        {
            return 0;
        }

        var markedScanCount = 0;
        foreach (var body in bodies.OfType<JsonObject>())
        {
            if (GetInt32(body[BodyIdProperty]) is not { } bodyId
                || !claimsByBody.TryGetValue(bodyId, out var entryIds)
                || body[BioScansProperty] is not JsonArray scans)
            {
                continue;
            }

            foreach (var scan in scans.OfType<JsonObject>())
            {
                if (GetInt64(scan[EntryIdProperty]) is not { } entryId
                    || !entryIds.Contains(entryId)
                    || string.Equals(
                        GetString(scan[BodyStatusProperty]),
                        AbandonedStatus,
                        StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        GetString(scan[BodyStatusProperty]),
                        DiedStatus,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                scan[BodyStatusProperty] = DiedStatus;
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
        var touchdown = ReadCoordinate(body[LastTouchdownProperty]);
        if (body[LastTouchdownProperty] is not null && touchdown is null)
        {
            warnings.Add("The saved touchdown coordinates are invalid and were ignored.");
        }

        return new SystemSurfaceBodySnapshot(
            GetInt32(body[BodyIdProperty]) ?? context.BodyId,
            GetString(body[BodyNameProperty]) ?? context.BodyName,
            GetDouble(body[BodyRadiusProperty]) ?? context.RadiusMeters,
            touchdown,
            bookmarks,
            scans);
    }

    private static Dictionary<string, IReadOnlyList<SurfaceCoordinate>>
        ReadBookmarks(JsonObject body, List<string> warnings)
    {
        if (body[BookmarksProperty] is null)
        {
            return new Dictionary<string, IReadOnlyList<SurfaceCoordinate>>(
                StringComparer.Ordinal);
        }

        if (body[BookmarksProperty] is not JsonObject bookmarks)
        {
            warnings.Add("The saved bookmarks are not a JSON object and were ignored.");
            return new Dictionary<string, IReadOnlyList<SurfaceCoordinate>>(
                StringComparer.Ordinal);
        }

        var normalizedBookmarks = bookmarks.DeepClone().AsObject();
        NormalizeLegacyBookmarkKeys(normalizedBookmarks);
        var result = new Dictionary<string, IReadOnlyList<SurfaceCoordinate>>(
            StringComparer.Ordinal);
        foreach (var pair in normalizedBookmarks)
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

            var name = LegacySurfaceBookmarkNames.Canonicalize(pair.Key);
            if (result.TryGetValue(name, out var existing))
            {
                result[name] = [.. existing, .. parsed];
            }
            else
            {
                result[name] = parsed;
            }
        }

        return result;
    }

    private static void NormalizeLegacyBookmarkKeys(JsonObject bookmarks)
    {
        foreach (var pair in bookmarks.ToArray())
        {
            var canonical = LegacySurfaceBookmarkNames.Canonicalize(pair.Key);
            if (string.Equals(canonical, pair.Key, StringComparison.Ordinal)
                || pair.Value is not JsonArray legacyLocations)
            {
                continue;
            }

            if (bookmarks[canonical] is null)
            {
                bookmarks.Remove(pair.Key);
                bookmarks[canonical] = legacyLocations;
                continue;
            }

            if (bookmarks[canonical] is not JsonArray canonicalLocations)
            {
                continue;
            }

            foreach (var location in legacyLocations)
            {
                canonicalLocations.Add(location?.DeepClone());
            }

            bookmarks.Remove(pair.Key);
        }
    }

    private static List<SurfaceBioScan> ReadBioScans(
        JsonObject body,
        List<string> warnings)
    {
        if (body[BioScansProperty] is null)
        {
            return [];
        }

        if (body[BioScansProperty] is not JsonArray scans)
        {
            warnings.Add("The saved biological scans are not an array and were ignored.");
            return [];
        }

        var result = new List<SurfaceBioScan>();
        foreach (var node in scans)
        {
            if (node is not JsonObject scan
                || ReadCoordinate(scan[LocationProperty]) is not { } location)
            {
                warnings.Add("A biological scan with invalid coordinates was ignored.");
                continue;
            }

            var radius = GetDouble(scan[BodyRadiusProperty]) ?? 50;
            if (!double.IsFinite(radius) || radius <= 0)
            {
                warnings.Add("A biological scan with an invalid radius was ignored.");
                continue;
            }

            result.Add(new SurfaceBioScan(
                location,
                radius,
                GetString(scan[GenusProperty]) ?? string.Empty,
                GetString(scan[SpeciesProperty]) ?? string.Empty,
                GetString(scan[BodyStatusProperty]) ?? ActiveStatus,
                GetInt64(scan[EntryIdProperty]) ?? 0,
                GetString(scan[OrbitingBodyProperty])));
        }

        return result;
    }

    private static JsonObject? FindBody(
        JsonObject root,
        SystemSurfaceContext context)
    {
        if (root[BodyCollectionProperty] is not JsonArray bodies)
        {
            return null;
        }

        return bodies
            .OfType<JsonObject>()
            .FirstOrDefault(body => GetInt32(body[BodyIdProperty]) == context.BodyId)
            ?? bodies
                .OfType<JsonObject>()
                .FirstOrDefault(body => string.Equals(
                    GetString(body[BodyNameProperty]),
                    context.BodyName,
                    StringComparison.OrdinalIgnoreCase));
    }

    private static JsonObject GetOrCreateBody(
        JsonObject root,
        SystemSurfaceContext context)
    {
        JsonArray bodies;
        if (root[BodyCollectionProperty] is null)
        {
            bodies = [];
            root[BodyCollectionProperty] = bodies;
        }
        else if (root[BodyCollectionProperty] is JsonArray existingBodies)
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
            [BodyNameProperty] = context.BodyName,
            [BodyIdProperty] = context.BodyId,
        };
        if (context.RadiusMeters > 0)
        {
            body[BodyRadiusProperty] = context.RadiusMeters;
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
            [LatitudeProperty] = location.Latitude,
            [LongitudeProperty] = location.Longitude,
        };
    }

    private static JsonObject WriteBioScan(SurfaceBioScan scan)
    {
        var result = new JsonObject
        {
            [LocationProperty] = WriteCoordinate(scan.Location),
            [BodyRadiusProperty] = scan.RadiusMeters,
            [GenusProperty] = scan.Genus,
            [SpeciesProperty] = scan.Species,
            [BodyStatusProperty] = scan.Status,
        };
        if (scan.EntryId != 0)
        {
            result[EntryIdProperty] = scan.EntryId;
        }

        if (!string.IsNullOrWhiteSpace(scan.BodyName))
        {
            result[OrbitingBodyProperty] = scan.BodyName;
        }

        return result;
    }

    private static bool IsSameScan(JsonNode? node, SurfaceBioScan scan)
    {
        return node is JsonObject existing
            && ReadCoordinate(existing[LocationProperty]) == scan.Location
            && string.Equals(
                GetString(existing[SpeciesProperty]),
                scan.Species,
                StringComparison.Ordinal);
    }

    private static SurfaceCoordinate? ReadCoordinate(JsonNode? node)
    {
        if (node is not JsonObject coordinate
            || GetDouble(coordinate[LatitudeProperty]) is not { } latitude
            || GetDouble(coordinate[LongitudeProperty]) is not { } longitude)
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
            : (first == second) switch
            {
                true => 0,
                false => double.PositiveInfinity
            };
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
