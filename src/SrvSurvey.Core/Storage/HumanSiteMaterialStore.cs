using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Settlements;

namespace SrvSurvey.Core.Storage;

public sealed class HumanSiteMaterialStore
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim>
        FileLocks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };
    private readonly string dataDirectory;
    private readonly TimeProvider timeProvider;

    public HumanSiteMaterialStore(
        string dataDirectory,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        this.dataDirectory = Path.GetFullPath(dataDirectory);
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<HumanSiteMaterialLoadResult> LoadActiveAsync(
        HumanSiteMaterialContext context,
        CancellationToken cancellationToken = default)
    {
        ValidateContext(context);
        var folder = GetFolder(context);
        var path = FindLatestPath(folder, context);
        if (path is null)
        {
            return new HumanSiteMaterialLoadResult(
                GetNewPath(folder, context),
                false,
                false,
                HumanSiteMaterialSurvey.Empty,
                null,
                []);
        }

        var result = await ReadAsync(path, cancellationToken)
            .ConfigureAwait(false);
        if (result.Root is null)
        {
            return new HumanSiteMaterialLoadResult(
                path,
                true,
                false,
                null,
                result.Error,
                []);
        }

        var warnings = new List<string>();
        var survey = ReadSurvey(result.Root, warnings);
        if (survey.Completed)
        {
            return new HumanSiteMaterialLoadResult(
                GetNewPath(folder, context),
                false,
                false,
                HumanSiteMaterialSurvey.Empty,
                null,
                warnings);
        }

        return new HumanSiteMaterialLoadResult(
            path,
            true,
            true,
            survey,
            null,
            warnings);
    }

    public async Task<HumanSiteMaterialMutationResult> AppendAsync(
        HumanSiteMaterialContext context,
        IEnumerable<HumanSiteCollectedMaterial> materials,
        CancellationToken cancellationToken = default)
    {
        ValidateContext(context);
        ArgumentNullException.ThrowIfNull(materials);
        var additions = materials.ToArray();
        if (additions.Length == 0)
        {
            var current = await LoadActiveAsync(context, cancellationToken)
                .ConfigureAwait(false);
            return new HumanSiteMaterialMutationResult(
                current.Path,
                0,
                current.Survey ?? HumanSiteMaterialSurvey.Empty);
        }

        var folder = GetFolder(context);
        Directory.CreateDirectory(folder);
        var sessionLockKey = GetSessionLockKey(folder, context);
        var fileLock = FileLocks.GetOrAdd(
            sessionLockKey,
            static _ => new SemaphoreSlim(1, 1));
        await fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var latest = FindLatestPath(folder, context);
            var path = latest ?? GetNewPath(folder, context);
            var root = new JsonObject();
            if (latest is not null)
            {
                var read = await ReadAsync(latest, cancellationToken)
                    .ConfigureAwait(false);
                if (read.Root is null)
                {
                    throw new InvalidDataException(read.Error);
                }

                if (ReadBoolean(read.Root, "completed") == true)
                {
                    path = GetNewPath(folder, context, avoidExisting: true);
                }
                else
                {
                    root = read.Root;
                }
            }

            ApplyContext(root, context);
            var locations = GetOrCreateArray(root, "matLocations");
            var countMats = GetOrCreateObject(root, "countMats");
            var countTypes = GetOrCreateObject(root, "countTypes");
            var total = Math.Max(0, ReadInt32(root, "totalMatCount") ?? 0);
            var addedCount = 0;
            foreach (var material in additions)
            {
                if (!material.Offset.IsFinite
                    || string.IsNullOrWhiteSpace(material.Name)
                    || string.IsNullOrWhiteSpace(material.Type)
                    || material.Count <= 0)
                {
                    continue;
                }

                total += material.Count;
                Increment(countMats, material.Name, material.Count);
                Increment(countTypes, material.Type, material.Count);
                locations.Add(SerializeLocation(material));
                addedCount++;
            }

            root["totalMatCount"] = total;
            root["completed"] = false;
            await WriteAsync(path, root, cancellationToken)
                .ConfigureAwait(false);
            var warnings = new List<string>();
            return new HumanSiteMaterialMutationResult(
                path,
                addedCount,
                ReadSurvey(root, warnings));
        }
        finally
        {
            fileLock.Release();
        }
    }

    public async Task<HumanSiteMaterialMutationResult> CompleteAsync(
        HumanSiteMaterialContext context,
        CancellationToken cancellationToken = default)
    {
        ValidateContext(context);
        var folder = GetFolder(context);
        var sessionLockKey = GetSessionLockKey(folder, context);
        var fileLock = FileLocks.GetOrAdd(
            sessionLockKey,
            static _ => new SemaphoreSlim(1, 1));
        await fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = FindLatestPath(folder, context);
            if (path is null)
            {
                return new HumanSiteMaterialMutationResult(
                    GetNewPath(folder, context),
                    0,
                    HumanSiteMaterialSurvey.Empty with { Completed = true });
            }

            var read = await ReadAsync(path, cancellationToken)
                .ConfigureAwait(false);
            if (read.Root is null)
            {
                throw new InvalidDataException(read.Error);
            }

            read.Root["completed"] = true;
            await WriteAsync(path, read.Root, cancellationToken)
                .ConfigureAwait(false);
            var warnings = new List<string>();
            return new HumanSiteMaterialMutationResult(
                path,
                0,
                ReadSurvey(read.Root, warnings));
        }
        finally
        {
            fileLock.Release();
        }
    }

    public async Task<HumanSiteMaterialMutationResult> SetThreatLevelAsync(
        HumanSiteMaterialContext context,
        int threatLevel,
        CancellationToken cancellationToken = default)
    {
        ValidateContext(context);
        var folder = GetFolder(context);
        Directory.CreateDirectory(folder);
        var sessionLockKey = GetSessionLockKey(folder, context);
        var fileLock = FileLocks.GetOrAdd(
            sessionLockKey,
            static _ => new SemaphoreSlim(1, 1));
        await fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var latest = FindLatestPath(folder, context);
            var path = latest ?? GetNewPath(folder, context);
            var root = new JsonObject();
            if (latest is not null)
            {
                var read = await ReadAsync(latest, cancellationToken)
                    .ConfigureAwait(false);
                if (read.Root is null)
                {
                    throw new InvalidDataException(read.Error);
                }

                if (ReadBoolean(read.Root, "completed") == true)
                {
                    path = GetNewPath(folder, context, avoidExisting: true);
                }
                else
                {
                    root = read.Root;
                }
            }

            ApplyContext(root, context);
            root["threatLevel"] = threatLevel;
            root["completed"] = false;
            await WriteAsync(path, root, cancellationToken)
                .ConfigureAwait(false);
            var warnings = new List<string>();
            return new HumanSiteMaterialMutationResult(
                path,
                0,
                ReadSurvey(root, warnings));
        }
        finally
        {
            fileLock.Release();
        }
    }

    private static HumanSiteMaterialSurvey ReadSurvey(
        JsonObject root,
        List<string> warnings)
    {
        var materials = new List<HumanSiteCollectedMaterial>();
        if (root["matLocations"] is JsonArray locations)
        {
            foreach (var node in locations)
            {
                if (node is JsonValue value
                    && value.TryGetValue<string>(out var text)
                    && TryParseLocation(text, out var material))
                {
                    materials.Add(material!);
                }
                else
                {
                    warnings.Add(
                        "A saved settlement material location was invalid and was ignored.");
                }
            }
        }

        return new HumanSiteMaterialSurvey(
            ReadBoolean(root, "completed") ?? false,
            ReadInt32(root, "threatLevel") ?? -1,
            Math.Max(0, ReadInt32(root, "totalMatCount") ?? 0),
            ReadCounts(root["countMats"]),
            ReadCounts(root["countTypes"]),
            ReadCounts(root["countBuildings"]),
            materials);
    }

    private static bool TryParseLocation(
        string? text,
        out HumanSiteCollectedMaterial? material)
    {
        material = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('_');
        if (parts.Length < 4
            || !double.TryParse(
                parts[^2],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var x)
            || !double.TryParse(
                parts[^1],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var y)
            || !double.IsFinite(x)
            || !double.IsFinite(y))
        {
            return false;
        }

        var name = parts[0];
        var type = string.Join('_', parts.Skip(1).Take(parts.Length - 3));
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(type))
        {
            return false;
        }

        material = new HumanSiteCollectedMaterial(
            name,
            null,
            type,
            1,
            new HumanSiteMapPoint(x, y),
            null);
        return true;
    }

    private static string SerializeLocation(HumanSiteCollectedMaterial material)
    {
        return string.Join(
            "_",
            material.Name,
            material.Type,
            material.Offset.X.ToString(CultureInfo.InvariantCulture),
            material.Offset.Y.ToString(CultureInfo.InvariantCulture));
    }

    private static Dictionary<string, int> ReadCounts(JsonNode? node)
    {
        if (node is not JsonObject counts)
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        return counts
            .Where(pair => ReadInt32(pair.Value) is > 0)
            .ToDictionary(
                pair => pair.Key,
                pair => ReadInt32(pair.Value)!.Value,
                StringComparer.OrdinalIgnoreCase);
    }

    private static void ApplyContext(
        JsonObject root,
        HumanSiteMaterialContext context)
    {
        root["name"] = context.Site.Name;
        root["marketId"] = context.Site.MarketId;
        root["systemAddress"] = context.Site.SystemAddress;
        root["bodyId"] = context.Site.BodyId;
        root["factionName"] = context.Site.FactionName;
        root["stationGovernment"] = context.Site.Government;
        root["stationEconomy"] = context.Site.EconomyToken;
        root["subType"] = context.Site.SubType;
    }

    private string GetFolder(HumanSiteMaterialContext context)
    {
        return Path.Combine(dataDirectory, "footMatStats", context.FrontierId);
    }

    private static string? FindLatestPath(
        string folder,
        HumanSiteMaterialContext context)
    {
        return !Directory.Exists(folder)
            ? null
            : Directory.EnumerateFiles(
                    folder,
                    $"{context.Site.SystemAddress}-{context.Site.MarketId}-*.json")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .LastOrDefault();
    }

    private static string GetSessionLockKey(
        string folder,
        HumanSiteMaterialContext context)
    {
        return Path.Combine(
            folder,
            $"{context.Site.SystemAddress}-{context.Site.MarketId}");
    }

    private string GetNewPath(
        string folder,
        HumanSiteMaterialContext context,
        bool avoidExisting = false)
    {
        var timestamp = timeProvider.GetUtcNow()
            .ToString("yyyy-MM-dd HHmmss", CultureInfo.InvariantCulture);
        var stem = $"{context.Site.SystemAddress}-{context.Site.MarketId}-{timestamp}";
        var path = Path.Combine(folder, stem + ".json");
        if (!avoidExisting || !File.Exists(path))
        {
            return path;
        }

        var suffix = 1;
        while (true)
        {
            path = Path.Combine(folder, $"{stem}_{suffix}.json");
            if (!File.Exists(path))
            {
                return path;
            }

            suffix++;
        }
    }

    private static async Task<(JsonObject? Root, string? Error)> ReadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken)
                .ConfigureAwait(false);
            var root = JsonNode.Parse(json) as JsonObject;
            return root is null
                ? (null, "The settlement material survey is not a JSON object.")
                : (root, null);
        }
        catch (JsonException exception)
        {
            return (null, exception.Message);
        }
    }

    private static async Task WriteAsync(
        string path,
        JsonObject root,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(
                    temporaryPath,
                    root.ToJsonString(JsonOptions),
                    cancellationToken)
                .ConfigureAwait(false);
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

    private static JsonArray GetOrCreateArray(
        JsonObject root,
        string propertyName)
    {
        if (root[propertyName] is JsonArray value)
        {
            return value;
        }

        value = [];
        root[propertyName] = value;
        return value;
    }

    private static JsonObject GetOrCreateObject(
        JsonObject root,
        string propertyName)
    {
        if (root[propertyName] is JsonObject value)
        {
            return value;
        }

        value = [];
        root[propertyName] = value;
        return value;
    }

    private static void Increment(JsonObject counts, string name, int count)
    {
        counts[name] = Math.Max(0, ReadInt32(counts[name]) ?? 0) + count;
    }

    private static int? ReadInt32(JsonObject root, string propertyName)
    {
        return ReadInt32(root[propertyName]);
    }

    private static int? ReadInt32(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<int>(out var number))
        {
            return number;
        }

        return value.TryGetValue<string>(out var text)
            && int.TryParse(
                text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out number)
                ? number
                : null;
    }

    private static bool? ReadBoolean(JsonObject root, string propertyName)
    {
        return root[propertyName] is JsonValue value
            && value.TryGetValue<bool>(out var result)
                ? result
                : null;
    }

    private static void ValidateContext(HumanSiteMaterialContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Site);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.FrontierId);
        if (context.Site.SystemAddress <= 0 || context.Site.MarketId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(context),
                "The material survey requires a system address and market ID.");
        }
    }
}

public sealed record HumanSiteMaterialContext(
    string FrontierId,
    HumanSiteLiveSnapshot Site);

public sealed record HumanSiteMaterialSurvey(
    bool Completed,
    int ThreatLevel,
    int TotalMaterialCount,
    IReadOnlyDictionary<string, int> CountByMaterial,
    IReadOnlyDictionary<string, int> CountByType,
    IReadOnlyDictionary<string, int> CountByBuilding,
    IReadOnlyList<HumanSiteCollectedMaterial> Materials)
{
    public static HumanSiteMaterialSurvey Empty { get; } = new(
        false,
        -1,
        0,
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
        []);
}

public sealed record HumanSiteMaterialLoadResult(
    string Path,
    bool Exists,
    bool IsActive,
    HumanSiteMaterialSurvey? Survey,
    string? Error,
    IReadOnlyList<string> Warnings);

public sealed record HumanSiteMaterialMutationResult(
    string Path,
    int AddedLocationCount,
    HumanSiteMaterialSurvey Survey);
