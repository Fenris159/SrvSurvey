using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SrvSurvey.Core.Search;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The store is application-scoped and its semaphore may still have in-flight waiters.")]
public sealed class BoxelSurveyStatsStore
{
    public const string StoreDirectoryName = "boxelSurveyStats";
    private const string IndexFileName = "index.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly char[] InvalidFileNameCharacters = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    private readonly string rootDirectory;
    private readonly SemaphoreSlim writeLock = new(1, 1);

    public BoxelSurveyStatsStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        rootDirectory = Path.Combine(
            Path.GetFullPath(dataDirectory),
            StoreDirectoryName);
    }

    public async Task<IReadOnlyList<BoxelSurveyIndexEntry>> ListIndexAsync(
        string frontierId,
        CancellationToken cancellationToken = default)
    {
        var catalog = await LoadCatalogAsync(frontierId, cancellationToken)
            .ConfigureAwait(false);
        return catalog.Index;
    }

    public async Task<BoxelSurveyStatsCatalog> LoadCatalogAsync(
        string frontierId,
        CancellationToken cancellationToken = default)
    {
        ValidateFileName(frontierId, nameof(frontierId));
        var directory = GetCommanderDirectory(frontierId);
        var indexPath = Path.Combine(directory, IndexFileName);
        if (File.Exists(indexPath))
        {
            try
            {
                return await ReadCatalogAsync(frontierId, indexPath, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or InvalidDataException
                    or JsonException)
            {
                // Fall through and recover from per-boxel files.
            }
        }

        return await RecoverCatalogAsync(frontierId, directory, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<BoxelSurveyBoxelDocument?> LoadBoxelAsync(
        string frontierId,
        string prefix,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ValidateFileName(frontierId, nameof(frontierId));
        var catalog = await LoadCatalogAsync(frontierId, cancellationToken)
            .ConfigureAwait(false);
        var path = ResolveBoxelPath(frontierId, prefix, catalog);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return await ReadDocumentAsync(frontierId, path, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or JsonException)
        {
            return null;
        }
    }

    public async Task SaveBoxelAsync(
        string frontierId,
        BoxelSurveyBoxelDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(document.Prefix);
        ValidateFileName(frontierId, nameof(frontierId));

        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = GetCommanderDirectory(frontierId);
            Directory.CreateDirectory(directory);
            var catalog = await LoadCatalogAsync(frontierId, cancellationToken)
                .ConfigureAwait(false);
            var path = ResolveBoxelPath(frontierId, document.Prefix, catalog);
            await WriteJsonAsync(
                    path,
                    WriteDocument(frontierId, document),
                    cancellationToken)
                .ConfigureAwait(false);

            var snapshot = CreateSnapshot(document);
            var entries = catalog.Index
                .Where(entry => !string.Equals(
                    entry.Prefix,
                    document.Prefix,
                    StringComparison.Ordinal))
                .Append(snapshot.ToIndexEntry())
                .OrderBy(entry => entry.Prefix, StringComparer.Ordinal)
                .ToArray();
            var updated = new BoxelSurveyStatsCatalog(
                frontierId,
                BoxelSurveyStatsCatalog.CurrentSchemaVersion,
                DateTimeOffset.UtcNow,
                entries);
            await WriteJsonAsync(
                    Path.Combine(directory, IndexFileName),
                    WriteCatalog(updated),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            writeLock.Release();
        }
    }

    public static string SanitizePrefix(string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        var characters = prefix
            .Select(character =>
                character < 32 || InvalidFileNameCharacters.Contains(character)
                    ? '_'
                    : character)
            .ToArray();
        var safe = new string(characters).Trim().Trim('.');
        return string.IsNullOrWhiteSpace(safe) ? "boxel" : safe;
    }

    private async Task<BoxelSurveyStatsCatalog> RecoverCatalogAsync(
        string frontierId,
        string directory,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory))
        {
            return BoxelSurveyStatsCatalog.Empty(frontierId);
        }

        var entries = new List<BoxelSurveyIndexEntry>();
        foreach (var path in Directory.EnumerateFiles(
                     directory,
                     "*.json",
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(
                    Path.GetFileName(path),
                    IndexFileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var document = await ReadDocumentAsync(
                        frontierId,
                        path,
                        cancellationToken)
                    .ConfigureAwait(false);
                entries.Add(CreateSnapshot(document).ToIndexEntry());
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or InvalidDataException
                    or JsonException)
            {
                // One damaged boxel file must not make the index unavailable.
            }
        }

        return new BoxelSurveyStatsCatalog(
            frontierId,
            BoxelSurveyStatsCatalog.CurrentSchemaVersion,
            DateTimeOffset.UtcNow,
            entries
                .OrderBy(entry => entry.Prefix, StringComparer.Ordinal)
                .ToArray());
    }

    private string GetCommanderDirectory(string frontierId)
        => Path.Combine(rootDirectory, frontierId);

    private string ResolveBoxelPath(
        string frontierId,
        string prefix,
        BoxelSurveyStatsCatalog catalog)
    {
        var directory = GetCommanderDirectory(frontierId);
        var preferred = Path.Combine(directory, SanitizePrefix(prefix) + ".json");
        var collision = catalog.Index.Any(entry =>
            !string.Equals(entry.Prefix, prefix, StringComparison.Ordinal)
            && string.Equals(
                SanitizePrefix(entry.Prefix),
                SanitizePrefix(prefix),
                StringComparison.OrdinalIgnoreCase));
        if (!collision)
        {
            return preferred;
        }

        var identity = catalog.Index.FirstOrDefault(entry =>
            string.Equals(entry.Prefix, prefix, StringComparison.Ordinal));
        var suffix = identity?.BoxelId64
            ?? (BoxelAddress.TryParse(prefix + "0", out var boxel)
                && boxel is not null
                && boxel.WithSystemNumber(0).TryGetSystemAddress(out var encoded)
                    ? encoded
                    : 0);
        return Path.Combine(
            directory,
            $"{SanitizePrefix(prefix)}-{suffix.ToString("x", CultureInfo.InvariantCulture)}.json");
    }

    private static BoxelSurveyBoxelSnapshot CreateSnapshot(
        BoxelSurveyBoxelDocument document)
    {
        var state = new BoxelSurveyStatsState();
        state.ImportDocument(document);
        return state.TryGet(document.Prefix, out var snapshot)
            ? snapshot
            : BoxelSurveyBoxelSnapshot.Empty;
    }

    private static async Task<BoxelSurveyStatsCatalog> ReadCatalogAsync(
        string frontierId,
        string path,
        CancellationToken cancellationToken)
    {
        var root = await ReadObjectAsync(path, cancellationToken).ConfigureAwait(false);
        var storedFrontierId = GetString(root, "frontierId");
        if (!string.IsNullOrWhiteSpace(storedFrontierId)
            && !string.Equals(
                storedFrontierId,
                frontierId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The boxel survey index belongs to a different commander profile.");
        }

        var entries = new List<BoxelSurveyIndexEntry>();
        if (root["entries"] is JsonArray array)
        {
            foreach (var node in array.OfType<JsonObject>())
            {
                var prefix = GetString(node, "prefix");
                if (string.IsNullOrWhiteSpace(prefix))
                {
                    continue;
                }

                var massText = GetString(node, "massCode");
                entries.Add(new BoxelSurveyIndexEntry(
                    prefix,
                    string.IsNullOrWhiteSpace(massText)
                        ? BoxelAddress.MinimumMassCode
                        : char.ToLowerInvariant(massText[0]),
                    GetInt64(node, "boxelId64"),
                    GetDateTimeOffset(node, "lastVisited"),
                    GetInt32(node, "visitedSystemCount") ?? 0,
                    GetInt32(node, "impliedPopulation") ?? 0,
                    GetInt32(node, "fssCompleteCount") ?? 0,
                    GetInt32(node, "navBeaconCount") ?? 0,
                    GetDouble(node, "minHeliumPercent"),
                    GetDouble(node, "maxHeliumPercent"),
                    GetInt64(node, "currentValue") ?? 0,
                    GetInt64(node, "mappedPotentialValue") ?? 0));
            }
        }

        return new BoxelSurveyStatsCatalog(
            frontierId,
            GetInt32(root, "version") ?? BoxelSurveyStatsCatalog.CurrentSchemaVersion,
            GetDateTimeOffset(root, "updatedAt") ?? DateTimeOffset.UtcNow,
            entries);
    }

    private static async Task<BoxelSurveyBoxelDocument> ReadDocumentAsync(
        string frontierId,
        string path,
        CancellationToken cancellationToken)
    {
        var root = await ReadObjectAsync(path, cancellationToken).ConfigureAwait(false);
        var storedFrontierId = GetString(root, "frontierId");
        if (!string.IsNullOrWhiteSpace(storedFrontierId)
            && !string.Equals(
                storedFrontierId,
                frontierId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The boxel survey file belongs to a different commander profile.");
        }

        var prefix = GetString(root, "prefix");
        if (string.IsNullOrWhiteSpace(prefix))
        {
            throw new InvalidDataException("The boxel survey file does not have a prefix.");
        }

        var systems = new List<BoxelSurveySystemContribution>();
        if (root["systems"] is JsonArray array)
        {
            foreach (var node in array.OfType<JsonObject>())
            {
                var generatedName = GetString(node, "generatedName");
                if (string.IsNullOrWhiteSpace(generatedName))
                {
                    continue;
                }

                systems.Add(new BoxelSurveySystemContribution(
                    generatedName,
                    GetInt64(node, "systemAddress") ?? 0,
                    GetInt32(node, "n2") ?? 0,
                    GetDateTimeOffset(node, "lastVisited"),
                    GetInt32(node, "fssDiscoveryBodyCount") ?? 0,
                    GetBoolean(node, "allBodiesFound") ?? false,
                    GetBoolean(node, "navBeaconScanned") ?? false,
                    GetDouble(node, "minHeliumPercent"),
                    GetDouble(node, "maxHeliumPercent"),
                    GetInt64(node, "scanValue") ?? 0,
                    GetInt64(node, "currentValue") ?? 0,
                    GetInt64(node, "mappedPotentialValue") ?? 0,
                    ReadBodies(node)));
            }
        }

        return new BoxelSurveyBoxelDocument(
            prefix,
            GetInt64(root, "boxelId64"),
            GetDateTimeOffset(root, "lastVisited"),
            GetDouble(root, "minHeliumPercent"),
            GetDouble(root, "maxHeliumPercent"),
            systems);
    }

    private static IReadOnlyList<BoxelSurveyBodyContribution> ReadBodies(JsonObject system)
    {
        if (system["bodies"] is not JsonArray array)
        {
            return [];
        }

        var bodies = new List<BoxelSurveyBodyContribution>();
        foreach (var node in array.OfType<JsonObject>())
        {
            var bodyId = GetInt32(node, "bodyId");
            if (bodyId is null or < 0)
            {
                continue;
            }

            var classified = GetInt32(node, "class") is { } raw
                && Enum.IsDefined(typeof(BoxelPlanetClass), raw)
                    ? (BoxelPlanetClass)raw
                    : BoxelPlanetClass.Unknown;
            bodies.Add(new BoxelSurveyBodyContribution(
                bodyId.Value,
                classified,
                GetBoolean(node, "terraformable") ?? false,
                GetBoolean(node, "landable") ?? false,
                GetBoolean(node, "atmospheric") ?? false,
                GetDouble(node, "massEm") ?? 0,
                GetDouble(node, "heliumPercent"),
                GetInt32(node, "scanValue") ?? 0,
                GetInt32(node, "currentValue") ?? 0,
                GetInt32(node, "mappedPotentialValue") ?? 0,
                GetBoolean(node, "wasDiscovered") ?? false,
                GetBoolean(node, "wasMapped") ?? false,
                GetBoolean(node, "dssComplete") ?? false,
                GetBoolean(node, "dssEfficiencyBonus") ?? false));
        }

        return bodies;
    }

    private static JsonObject WriteCatalog(BoxelSurveyStatsCatalog catalog)
    {
        var entries = new JsonArray();
        foreach (var entry in catalog.Index)
        {
            entries.Add(new JsonObject
            {
                ["prefix"] = entry.Prefix,
                ["massCode"] = entry.MassCode.ToString(),
                ["boxelId64"] = entry.BoxelId64,
                ["lastVisited"] = WriteDate(entry.LastVisited),
                ["visitedSystemCount"] = entry.VisitedSystemCount,
                ["impliedPopulation"] = entry.ImpliedPopulation,
                ["fssCompleteCount"] = entry.FssCompleteCount,
                ["navBeaconCount"] = entry.NavBeaconCount,
                ["minHeliumPercent"] = entry.MinHeliumPercent,
                ["maxHeliumPercent"] = entry.MaxHeliumPercent,
                ["currentValue"] = entry.CurrentValue,
                ["mappedPotentialValue"] = entry.MappedPotentialValue,
            });
        }

        return new JsonObject
        {
            ["version"] = catalog.SchemaVersion,
            ["frontierId"] = catalog.FrontierId,
            ["updatedAt"] = catalog.UpdatedAt,
            ["entries"] = entries,
        };
    }

    private static JsonObject WriteDocument(
        string frontierId,
        BoxelSurveyBoxelDocument document)
    {
        var systems = new JsonArray();
        foreach (var system in document.Systems)
        {
            var bodies = new JsonArray();
            foreach (var body in system.Bodies)
            {
                bodies.Add(new JsonObject
                {
                    ["bodyId"] = body.BodyId,
                    ["class"] = (int)body.Class,
                    ["terraformable"] = body.Terraformable,
                    ["landable"] = body.Landable,
                    ["atmospheric"] = body.Atmospheric,
                    ["massEm"] = body.MassEm,
                    ["heliumPercent"] = body.HeliumPercent,
                    ["scanValue"] = body.ScanValue,
                    ["currentValue"] = body.CurrentValue,
                    ["mappedPotentialValue"] = body.MappedPotentialValue,
                    ["wasDiscovered"] = body.WasDiscovered,
                    ["wasMapped"] = body.WasMapped,
                    ["dssComplete"] = body.DssComplete,
                    ["dssEfficiencyBonus"] = body.DssEfficiencyBonus,
                });
            }

            systems.Add(new JsonObject
            {
                ["generatedName"] = system.GeneratedName,
                ["systemAddress"] = system.SystemAddress,
                ["n2"] = system.N2,
                ["lastVisited"] = WriteDate(system.LastVisited),
                ["fssDiscoveryBodyCount"] = system.FssDiscoveryBodyCount,
                ["allBodiesFound"] = system.AllBodiesFound,
                ["navBeaconScanned"] = system.NavBeaconScanned,
                ["minHeliumPercent"] = system.MinHeliumPercent,
                ["maxHeliumPercent"] = system.MaxHeliumPercent,
                ["scanValue"] = system.ScanValue,
                ["currentValue"] = system.CurrentValue,
                ["mappedPotentialValue"] = system.MappedPotentialValue,
                ["bodies"] = bodies,
            });
        }

        return new JsonObject
        {
            ["version"] = BoxelSurveyStatsCatalog.CurrentSchemaVersion,
            ["frontierId"] = frontierId,
            ["prefix"] = document.Prefix,
            ["boxelId64"] = document.BoxelId64,
            ["lastVisited"] = WriteDate(document.LastVisited),
            ["minHeliumPercent"] = document.MinHeliumPercent,
            ["maxHeliumPercent"] = document.MaxHeliumPercent,
            ["systems"] = systems,
        };
    }

    private static JsonNode? WriteDate(DateTimeOffset? value)
        => value is null ? null : JsonValue.Create(value.Value);

    private static async Task<JsonObject> ReadObjectAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false) as JsonObject
            ?? throw new InvalidDataException("The boxel survey file did not contain a JSON object.");
    }

    private static async Task WriteJsonAsync(
        string path,
        JsonObject root,
        CancellationToken cancellationToken)
    {
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             16 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                        stream,
                        root,
                        SerializerOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void ValidateFileName(string value, string parameterName)
    {
        if (value is null)
        {
            throw new ArgumentNullException(parameterName);
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "The value cannot be an empty string or composed entirely of whitespace.",
                parameterName);
        }

        if (!string.Equals(value, Path.GetFileName(value), StringComparison.Ordinal)
            || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("The file name is invalid.", parameterName);
        }
    }

    private static string? GetString(JsonObject root, string propertyName)
        => root[propertyName] is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : null;

    private static bool? GetBoolean(JsonObject root, string propertyName)
        => root[propertyName] is JsonValue value && value.TryGetValue<bool>(out var flag)
            ? flag
            : null;

    private static int? GetInt32(JsonObject root, string propertyName)
    {
        if (root[propertyName] is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<int>(out var number))
        {
            return number;
        }

        return value.TryGetValue<long>(out var wider)
            ? (int)wider
            : null;
    }

    private static long? GetInt64(JsonObject root, string propertyName)
    {
        if (root[propertyName] is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<long>(out var number))
        {
            return number;
        }

        return value.TryGetValue<int>(out var smaller) ? smaller : null;
    }

    private static double? GetDouble(JsonObject root, string propertyName)
        => root[propertyName] is JsonValue value && value.TryGetValue<double>(out var number)
            ? number
            : null;

    private static DateTimeOffset? GetDateTimeOffset(JsonObject root, string propertyName)
    {
        if (root[propertyName] is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<DateTimeOffset>(out var stamp))
        {
            return stamp;
        }

        return value.TryGetValue<string>(out var text)
            && DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed)
                ? parsed
                : null;
    }
}
