using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Journeys;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The store is application-scoped and its semaphore may still have in-flight waiters.")]
public sealed class JourneyStore(string dataDirectory)
{
    private const string JsonFileExtension = ".json";
    private const string CodexNewPropertyName = "codexNew";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string dataDirectory = GetFullPath(dataDirectory);
    private readonly SemaphoreSlim saveLock = new(1, 1);

    public async Task<JourneyCatalogResult> LoadAllAsync(
        string frontierId,
        CancellationToken cancellationToken = default)
    {
        ValidateFolderName(frontierId, nameof(frontierId));
        var directory = GetJourneyDirectory(frontierId);
        if (!Directory.Exists(directory))
        {
            return new JourneyCatalogResult([], []);
        }

        var journeys = new List<JourneyDocument>();
        var errors = new List<string>();
        foreach (var path in Directory.EnumerateFiles(
                     directory,
                     "*" + JsonFileExtension,
                     SearchOption.TopDirectoryOnly)
                 .Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await LoadPathAsync(
                    frontierId,
                    path,
                    cancellationToken)
                .ConfigureAwait(false);
            if (result.Journey is not null)
            {
                journeys.Add(result.Journey);
            }
            else if (result.Error is not null)
            {
                errors.Add(result.Error);
            }
        }

        return new JourneyCatalogResult(
            journeys
                .OrderByDescending(journey => journey.StartTime)
                .ThenBy(journey => journey.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            errors);
    }

    public Task<JourneyLoadResult> LoadAsync(
        string frontierId,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        ValidateFolderName(frontierId, nameof(frontierId));
        var normalizedFileName = NormalizeFileName(fileName);
        var path = Path.Combine(
            GetJourneyDirectory(frontierId),
            normalizedFileName + JsonFileExtension);
        return LoadPathAsync(frontierId, path, cancellationToken);
    }

    public async Task<JourneyDocument> CreateAsync(
        JourneyCreationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateFolderName(request.FrontierId, nameof(request.FrontierId));
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException(
                "The journey name cannot be blank.",
                nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.StartingJournal))
        {
            throw new ArgumentException(
                "The starting journal file is required.",
                nameof(request));
        }

        var fileName = request.StartingEventTimestamp.ToString(
            "yyyyMMdd_HHmmss",
            CultureInfo.InvariantCulture);
        var path = Path.Combine(
            GetJourneyDirectory(request.FrontierId),
            fileName + JsonFileExtension);
        var legacyStartTime = request.StartingEventTimestamp.AddMilliseconds(-10);
        var journey = new JourneyDocument(
            fileName,
            path,
            request.FrontierId,
            request.CommanderName,
            request.Name.Trim(),
            request.Description ?? string.Empty,
            Path.GetFileName(request.StartingJournal),
            legacyStartTime,
            null,
            legacyStartTime,
            []);

        await saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(path))
            {
                throw new InvalidOperationException(
                    "A journey already starts at that journal event: "
                        + Path.GetFileName(path));
            }

            await WriteJourneyAsync(path, [], journey, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            saveLock.Release();
        }

        return journey;
    }

    public async Task SaveAsync(
        JourneyDocument journey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journey);
        ValidateFolderName(journey.FrontierId, nameof(journey));
        var fileName = NormalizeFileName(journey.FileName);
        var path = Path.Combine(
            GetJourneyDirectory(journey.FrontierId),
            fileName + JsonFileExtension);
        await saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            JsonObject root;
            if (File.Exists(path))
            {
                var readResult = await ReadObjectAsync(path, cancellationToken)
                    .ConfigureAwait(false);
                root = readResult.Root
                    ?? throw new InvalidDataException(
                        "The journey file is malformed and was not overwritten: "
                            + readResult.Error);
            }
            else
            {
                root = [];
            }

            await WriteJourneyAsync(path, root, journey, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            saveLock.Release();
        }
    }

    public async Task<bool> IncrementNoteCountAsync(
        string frontierId,
        string journeyFileName,
        long systemAddress,
        CancellationToken cancellationToken = default)
    {
        var result = await LoadAsync(
                frontierId,
                journeyFileName,
                cancellationToken)
            .ConfigureAwait(false);
        if (result.Journey is not { } journey)
        {
            throw new InvalidDataException(
                result.Error ?? "The active journey could not be loaded.");
        }

        var visits = journey.VisitedSystems.ToArray();
        var index = Array.FindLastIndex(
            visits,
            visit => visit.StarSystem.SystemAddress == systemAddress);
        if (index < 0)
        {
            return false;
        }

        var visit = visits[index];
        visits[index] = visit with
        {
            Counts = visit.Counts with
            {
                Notes = checked(visit.Counts.Notes + 1),
            },
        };
        await SaveAsync(
                journey with { VisitedSystems = visits },
                cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    private static async Task<JourneyLoadResult> LoadPathAsync(
        string frontierId,
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return new JourneyLoadResult(path, false, null, null);
        }

        var readResult = await ReadObjectAsync(path, cancellationToken)
            .ConfigureAwait(false);
        if (readResult.Root is null)
        {
            return new JourneyLoadResult(path, true, null, readResult.Error);
        }

        try
        {
            return new JourneyLoadResult(
                path,
                true,
                ParseJourney(frontierId, path, readResult.Root),
                null);
        }
        catch (InvalidDataException exception)
        {
            return new JourneyLoadResult(path, true, null, exception.Message);
        }
    }

    private static JourneyDocument ParseJourney(
        string frontierId,
        string path,
        JsonObject root)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        var startTime = GetDateTimeOffset(root, "startTime")
            ?? throw new InvalidDataException(
                $"The journey {path} has no valid startTime.");
        var visits = new List<JourneySystemVisit>();
        if (root["visitedSystems"] is JsonArray visitedSystems)
        {
            for (var index = 0; index < visitedSystems.Count; index++)
            {
                if (visitedSystems[index] is not JsonObject visit)
                {
                    throw new InvalidDataException(
                        $"The journey {path} has an invalid visitedSystems[{index}].");
                }

                visits.Add(ParseVisit(path, index, visit));
            }
        }

        return new JourneyDocument(
            fileName,
            path,
            GetString(root, "fid") ?? frontierId,
            GetString(root, "commander") ?? string.Empty,
            GetString(root, "name") ?? fileName,
            GetString(root, "description") ?? string.Empty,
            GetString(root, "startingJournal") ?? string.Empty,
            startTime,
            GetDateTimeOffset(root, "endTime"),
            GetDateTimeOffset(root, "watermark") ?? startTime,
            visits);
    }

    private static JourneySystemVisit ParseVisit(
        string path,
        int index,
        JsonObject root)
    {
        var starSystem = ParseStarReference(root["starRef"])
            ?? throw new InvalidDataException(
                $"The journey {path} has an invalid visitedSystems[{index}].starRef.");
        var arrived = GetDateTimeOffset(root, "arrived")
            ?? throw new InvalidDataException(
                $"The journey {path} has an invalid visitedSystems[{index}].arrived.");
        return new JourneySystemVisit(
            starSystem,
            arrived,
            GetDateTimeOffset(root, "departed"),
            ParseCounts(root["count"] as JsonObject),
            ReadStringIntDictionary(root, "landedOn"),
            ReadInt64Set(root, "codexScanned"),
            ReadInt32Set(root, "bodiesScanned"),
            ReadStringSet(root, CodexNewPropertyName),
            ReadStringIntDictionary(root, "subCats"),
            ReadStringIntDictionary(root, "saaSignals"),
            ReadStringIntDictionary(root, "fssSignals"));
    }

    private static JourneySystemReference? ParseStarReference(JsonNode? node)
    {
        if (node is JsonValue value
            && value.TryGetValue<string>(out var compact))
        {
            var parts = compact.Split('|');
            if (parts.Length != 5
                || !long.TryParse(
                    parts[1],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var address)
                || !double.TryParse(
                    parts[2],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var x)
                || !double.TryParse(
                    parts[3],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var y)
                || !double.TryParse(
                    parts[4],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var z)
                || string.IsNullOrWhiteSpace(parts[0]))
            {
                return null;
            }

            return TryCreateStarReference(parts[0], address, x, y, z);
        }

        if (node is not JsonObject legacy)
        {
            return null;
        }

        var name = GetString(legacy, "name");
        var systemAddress = GetInt64(legacy, "id64");
        var legacyX = GetDouble(legacy, "x");
        var legacyY = GetDouble(legacy, "y");
        var legacyZ = GetDouble(legacy, "z");
        return string.IsNullOrWhiteSpace(name)
            || systemAddress is null
            || legacyX is null
            || legacyY is null
            || legacyZ is null
                ? null
                : TryCreateStarReference(
                    name,
                    systemAddress.Value,
                    legacyX.Value,
                    legacyY.Value,
                    legacyZ.Value);
    }

    private static JourneySystemReference? TryCreateStarReference(
        string name,
        long systemAddress,
        double x,
        double y,
        double z)
    {
        try
        {
            return new JourneySystemReference(
                name,
                systemAddress,
                new GalacticCoordinate(x, y, z));
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static JourneyCounts ParseCounts(JsonObject? root)
    {
        if (root is null)
        {
            return JourneyCounts.Empty;
        }

        return new JourneyCounts(
            GetInt32(root, "bodyScans") ?? 0,
            GetInt32(root, "dss") ?? 0,
            GetInt32(root, CodexNewPropertyName) ?? 0,
            GetInt32(root, "organic") ?? 0,
            GetInt32(root, "touchdown") ?? 0,
            GetInt32(root, "bodyCount") ?? 0,
            GetInt32(root, "screenshots") ?? 0,
            GetInt32(root, "notes") ?? 0,
            GetInt32(root, "rewardBio") ?? 0,
            GetInt32(root, "rewardExp") ?? 0,
            GetInt32(root, "stars") ?? 0);
    }

    private static async Task WriteJourneyAsync(
        string path,
        JsonObject root,
        JourneyDocument journey,
        CancellationToken cancellationToken)
    {
        root["fid"] = journey.FrontierId;
        root["commander"] = journey.CommanderName;
        root["name"] = journey.Name;
        root["description"] = journey.Description;
        root["startingJournal"] = journey.StartingJournal;
        root["startTime"] = journey.StartTime;
        if (journey.EndTime is { } endTime)
        {
            root["endTime"] = endTime;
        }
        else
        {
            root.Remove("endTime");
        }

        root["watermark"] = journey.Watermark;
        root["visitedSystems"] = MergeVisits(
            root["visitedSystems"] as JsonArray,
            journey.VisitedSystems);
        await WriteObjectAsync(path, root, cancellationToken).ConfigureAwait(false);
    }

    private static JsonArray MergeVisits(
        JsonArray? existing,
        IReadOnlyList<JourneySystemVisit> visits)
    {
        var existingRows = existing?
            .Select((node, index) => new ExistingVisit(
                index,
                node as JsonObject,
                node is JsonObject row ? GetVisitIdentity(row) : null))
            .ToArray() ?? [];
        var used = new HashSet<int>();
        var result = new JsonArray();
        foreach (var visit in visits)
        {
            var identity = GetVisitIdentity(visit);
            var match = existingRows.FirstOrDefault(candidate =>
                !used.Contains(candidate.Index)
                && string.Equals(
                    candidate.Identity,
                    identity,
                    StringComparison.Ordinal));
            JsonObject row;
            if (match?.Root is not null)
            {
                used.Add(match.Index);
                row = match.Root.DeepClone().AsObject();
            }
            else
            {
                row = [];
            }

            WriteVisit(row, visit);
            result.Add(row);
        }

        return result;
    }

    private static void WriteVisit(JsonObject root, JourneySystemVisit visit)
    {
        var star = visit.StarSystem;
        root["starRef"] = string.Join(
            '|',
            star.Name,
            star.SystemAddress.ToString(CultureInfo.InvariantCulture),
            star.Position.X.ToString(CultureInfo.InvariantCulture),
            star.Position.Y.ToString(CultureInfo.InvariantCulture),
            star.Position.Z.ToString(CultureInfo.InvariantCulture));
        root["arrived"] = visit.Arrived;
        if (visit.Departed is { } departed)
        {
            root["departed"] = departed;
        }
        else
        {
            root.Remove("departed");
        }

        var counts = root["count"] is JsonObject existingCounts
            ? existingCounts
            : [];
        WriteCounts(counts, visit.Counts);
        root["count"] = counts;
        WriteDictionary(root, "landedOn", visit.LandedOn);
        WriteSet(root, "codexScanned", visit.CodexScanned);
        WriteSet(root, "bodiesScanned", visit.BodiesScanned);
        WriteSet(root, CodexNewPropertyName, visit.CodexNew);
        WriteDictionary(root, "subCats", visit.SubCategories);
        WriteDictionary(root, "saaSignals", visit.SurfaceSignals);
        WriteDictionary(root, "fssSignals", visit.FssSignals);
    }

    private static void WriteCounts(JsonObject root, JourneyCounts counts)
    {
        root["bodyScans"] = counts.BodyScans;
        root["dss"] = counts.DetailedSurfaceScans;
        root[CodexNewPropertyName] = counts.NewCodexEntries;
        root["organic"] = counts.Organisms;
        root["touchdown"] = counts.Touchdowns;
        root["bodyCount"] = counts.BodyCount;
        root["screenshots"] = counts.Screenshots;
        root["notes"] = counts.Notes;
        root["rewardBio"] = counts.ExobiologyRewards;
        root["rewardExp"] = counts.ExplorationRewards;
        root["stars"] = counts.Stars;
    }

    private static void WriteDictionary(
        JsonObject root,
        string propertyName,
        IReadOnlyDictionary<string, int>? values)
    {
        if (values is null)
        {
            root.Remove(propertyName);
            return;
        }

        var result = new JsonObject();
        foreach (var (key, value) in values.OrderBy(
                     entry => entry.Key,
                     StringComparer.Ordinal))
        {
            result[key] = value;
        }

        root[propertyName] = result;
    }

    private static void WriteSet<T>(
        JsonObject root,
        string propertyName,
        IReadOnlySet<T>? values)
    {
        if (values is null)
        {
            root.Remove(propertyName);
            return;
        }

        var array = new JsonArray();
        foreach (var value in values.Order())
        {
            array.Add(JsonValue.Create(value));
        }

        root[propertyName] = array;
    }

    private static string GetVisitIdentity(JourneySystemVisit visit)
    {
        return $"{visit.StarSystem.SystemAddress}|{visit.Arrived:O}";
    }

    private static string? GetVisitIdentity(JsonObject visit)
    {
        var star = ParseStarReference(visit["starRef"]);
        var arrived = GetDateTimeOffset(visit, "arrived");
        return star is null || arrived is null
            ? null
            : $"{star.SystemAddress}|{arrived.Value:O}";
    }

    private string GetJourneyDirectory(string frontierId)
    {
        return Path.Combine(dataDirectory, "journey", frontierId);
    }

    private static string NormalizeFileName(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ValidateFolderName(fileName, nameof(fileName));
        var normalized = fileName.EndsWith(JsonFileExtension, StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(fileName)
            : fileName;
        ValidateFolderName(normalized, nameof(fileName));
        return normalized;
    }

    private static void ValidateFolderName(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "The folder name cannot be empty.",
                parameterName);
        }

        if (value is "." or ".."
            || !string.Equals(
                Path.GetFileName(value),
                value,
                StringComparison.Ordinal)
            || value.IndexOfAny(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
        {
            throw new ArgumentException(
                "The value must be a file or folder name, not a path.",
                parameterName);
        }
    }

    private static string GetFullPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path);
    }

    private static string? GetString(JsonObject root, string propertyName)
    {
        return root[propertyName] is JsonValue value
            && value.TryGetValue<string>(out var result)
                ? result
                : null;
    }

    private static int? GetInt32(JsonObject root, string propertyName)
    {
        var value = GetInt64(root, propertyName);
        return value is >= int.MinValue and <= int.MaxValue
            ? (int)value.Value
            : null;
    }

    private static long? GetInt64(JsonObject root, string propertyName)
    {
        if (root[propertyName] is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<long>(out var result))
        {
            return result;
        }

        return value.TryGetValue<double>(out var number)
            && number is >= long.MinValue and <= long.MaxValue
                ? Convert.ToInt64(number)
                : null;
    }

    private static double? GetDouble(JsonObject root, string propertyName)
    {
        if (root[propertyName] is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<double>(out var result))
        {
            return result;
        }

        return value.TryGetValue<long>(out var integer) ? integer : null;
    }

    private static DateTimeOffset? GetDateTimeOffset(
        JsonObject root,
        string propertyName)
    {
        if (root[propertyName] is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<DateTimeOffset>(out var result))
        {
            return result;
        }

        return value.TryGetValue<string>(out var text)
            && DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out result)
                ? result
                : null;
    }

    private static Dictionary<string, int>? ReadStringIntDictionary(
        JsonObject root,
        string propertyName)
    {
        if (root[propertyName] is not JsonObject dictionary)
        {
            return null;
        }

        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (key, node) in dictionary)
        {
            if (node is JsonValue value
                && value.TryGetValue<int>(out var number))
            {
                result[key] = number;
            }
        }

        return result;
    }

    private static HashSet<long>? ReadInt64Set(
        JsonObject root,
        string propertyName)
    {
        if (root[propertyName] is not JsonArray array)
        {
            return null;
        }

        return array
            .OfType<JsonValue>()
            .Select(value => value.TryGetValue<long>(out var number)
                ? number
                : (long?)null)
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .ToHashSet();
    }

    private static HashSet<int>? ReadInt32Set(
        JsonObject root,
        string propertyName)
    {
        if (root[propertyName] is not JsonArray array)
        {
            return null;
        }

        return array
            .OfType<JsonValue>()
            .Select(value => value.TryGetValue<int>(out var number)
                ? number
                : (int?)null)
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .ToHashSet();
    }

    private static HashSet<string>? ReadStringSet(
        JsonObject root,
        string propertyName)
    {
        if (root[propertyName] is not JsonArray array)
        {
            return null;
        }

        return array
            .OfType<JsonValue>()
            .Select(value => value.TryGetValue<string>(out var text) ? text : null)
            .Where(text => text is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
    }

    private static async Task<JsonObjectReadResult> ReadObjectAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var node = await JsonNode.ParseAsync(
                    stream,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return node is JsonObject root
                ? new JsonObjectReadResult(root, null)
                : new JsonObjectReadResult(
                    null,
                    $"{path} does not contain a JSON object.");
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException)
        {
            return new JsonObjectReadResult(
                null,
                $"Could not read {path}: {exception.Message}");
        }
    }

    private static async Task WriteObjectAsync(
        string path,
        JsonObject root,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException(
                $"The journey path has no parent directory: {path}");
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             16 * 1024,
                             FileOptions.Asynchronous))
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

    private sealed record ExistingVisit(
        int Index,
        JsonObject? Root,
        string? Identity);

    private sealed record JsonObjectReadResult(JsonObject? Root, string? Error);
}
