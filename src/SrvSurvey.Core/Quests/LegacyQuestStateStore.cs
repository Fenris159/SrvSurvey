using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace SrvSurvey.Core.Quests;

public sealed class LegacyQuestStateStore
{
    private static readonly JsonSerializerOptions PortableJsonOptions = new()
    {
        AllowTrailingCommas = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new JsonStringEnumConverter(),
        },
    };

    private readonly SemaphoreSlim saveLock = new(1, 1);
    private readonly string questDirectory;

    public LegacyQuestStateStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        questDirectory = Path.Combine(
            Path.GetFullPath(dataDirectory),
            "quests");
    }

    public LegacyQuestStateLoadResult Load(string frontierId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(frontierId);
        if (ContainsPathSeparator(frontierId))
        {
            throw new ArgumentException(
                "The Frontier ID cannot contain path separators.",
                nameof(frontierId));
        }

        var statePath = Path.Combine(questDirectory, frontierId + ".json");

        try
        {
            statePath = FindFile(frontierId + ".json") ?? statePath;
            if (!File.Exists(statePath))
            {
                return new LegacyQuestStateLoadResult(
                    statePath,
                    false,
                    new LegacyCommanderQuestState(
                        frontierId,
                        null,
                        null),
                    [],
                    null);
            }

            var root = ParseObject(statePath);
            var warnings = new List<string>();
            var storedFrontierId = GetString(root, "fid") ?? frontierId;
            if (!string.Equals(
                    storedFrontierId,
                    frontierId,
                    StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add(
                    $"Quest state FID '{storedFrontierId}' does not match '{frontierId}'.");
            }

            var reference = ParseReference(root["devRef"], warnings);
            LegacyQuestProgress? devQuest = null;
            if (root["devQuest"] is JsonObject progress)
            {
                if (reference is null)
                {
                    warnings.Add(
                        "A development quest state exists without a devRef identity.");
                }
                else
                {
                    var portableDefinition = ParsePortableDefinition(
                        progress["quest"],
                        reference,
                        warnings);
                    var definition = portableDefinition is null
                        ? LoadDefinition(reference, warnings)
                        : null;
                    devQuest = ParseProgress(
                        reference,
                        definition,
                        progress,
                        warnings) with
                    {
                        PortableDefinition = portableDefinition,
                    };
                }
            }
            else if (reference is not null)
            {
                warnings.Add(
                    $"Development quest '{reference}' has no local progress object.");
            }

            return new LegacyQuestStateLoadResult(
                statePath,
                true,
                new LegacyCommanderQuestState(
                    storedFrontierId,
                    GetString(root, "cmdr"),
                    devQuest),
                warnings,
                null);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException
                or InvalidDataException
                or FormatException
                or OverflowException)
        {
            return new LegacyQuestStateLoadResult(
                statePath,
                true,
                null,
                [],
                exception.Message);
        }
    }

    public async Task<LegacyQuestStateSaveResult> SaveDevelopmentQuestAsync(
        string frontierId,
        string? commanderName,
        RavenCommanderQuest? progress,
        CancellationToken cancellationToken = default)
    {
        return await SaveDevelopmentQuestCoreAsync(
                frontierId,
                commanderName,
                progress,
                replaceExistingProgress: false,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<LegacyQuestStateSaveResult> ReplaceDevelopmentQuestAsync(
        string frontierId,
        string? commanderName,
        RavenCommanderQuest progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(progress);
        return await SaveDevelopmentQuestCoreAsync(
                frontierId,
                commanderName,
                progress,
                replaceExistingProgress: true,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<LegacyQuestStateSaveResult> SaveDevelopmentQuestCoreAsync(
        string frontierId,
        string? commanderName,
        RavenCommanderQuest? progress,
        bool replaceExistingProgress,
        CancellationToken cancellationToken)
    {
        ValidateFrontierId(frontierId);
        if (progress is not null)
        {
            ValidateProgressIdentity(progress);
        }

        await saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(questDirectory);
            var path = FindFile(frontierId + ".json")
                ?? Path.Combine(questDirectory, frontierId + ".json");
            JsonObject root;
            if (File.Exists(path))
            {
                try
                {
                    root = ParseObject(path);
                }
                catch (JsonException exception)
                {
                    throw new InvalidDataException(
                        "The legacy quest state is malformed and was not overwritten.",
                        exception);
                }
            }
            else
            {
                root = [];
            }

            root["fid"] = frontierId;
            if (!string.IsNullOrWhiteSpace(commanderName))
            {
                root["cmdr"] = commanderName.Trim();
            }

            if (progress is null)
            {
                root.Remove("devRef");
                root.Remove("devQuest");
            }
            else
            {
                root["devRef"] = progress.Reference.ToString();
                var questRoot = replaceExistingProgress
                    ? new JsonObject()
                    : root["devQuest"] switch
                    {
                        null => new JsonObject(),
                        JsonObject existing => existing,
                        _ => throw new InvalidDataException(
                            "The legacy development quest state is not a JSON object and was not overwritten."),
                    };
                root["devQuest"] = questRoot;
                MergeProgress(questRoot, progress);
            }

            var backupPath = File.Exists(path)
                ? await CreateVerifiedBackupAsync(path, frontierId, cancellationToken)
                    .ConfigureAwait(false)
                : null;
            await WriteVerifiedAsync(path, root, cancellationToken)
                .ConfigureAwait(false);
            return new LegacyQuestStateSaveResult(
                path,
                backupPath,
                progress is not null);
        }
        finally
        {
            saveLock.Release();
        }
    }

    private static void MergeProgress(
        JsonObject root,
        RavenCommanderQuest progress)
    {
        MergeExtensionData(root, progress.ExtensionData);
        if (progress.Quest is null)
        {
            root.Remove("quest");
        }
        else
        {
            root["quest"] = JsonSerializer.SerializeToNode(
                progress.Quest,
                PortableJsonOptions);
        }

        root["objectives"] = ToStringObject(progress.Objectives);
        SetOrRemove(root, "startTime", progress.StartTime);
        SetOrRemove(root, "endTime", progress.EndTime);
        if (progress.Paused)
        {
            root["paused"] = true;
        }
        else
        {
            root.Remove("paused");
        }

        root["tags"] = new JsonArray(
            progress.Tags
                .Select(value => (JsonNode?)JsonValue.Create(value))
                .ToArray());
        root["bodyLocations"] = ToStringObject(progress.BodyLocations);
        root["chapters"] = MergeById(
            root["chapters"],
            progress.Chapters,
            chapter => chapter.Id,
            MergeChapter);
        root["msgs"] = MergeById(
            root["msgs"],
            progress.Messages,
            message => message.Id,
            MergeMessage);
        root["vars"] = ToJsonObject(progress.Variables);
        root["keptLasts"] = ToJsonObject(progress.KeptJournalEvents);
        root["routes"] = MergeById(
            root["routes"],
            progress.Routes,
            route => route.Id,
            MergeRoute);
    }

    private static void MergeChapter(
        JsonObject root,
        RavenQuestChapterState chapter)
    {
        MergeExtensionData(root, chapter.ExtensionData);
        root["id"] = chapter.Id;
        SetOrRemove(root, "startTime", chapter.StartTime);
        SetOrRemove(root, "endTime", chapter.EndTime);
        root["vars"] = ToJsonObject(chapter.Variables);
    }

    private static void MergeMessage(
        JsonObject root,
        RavenQuestMessage message)
    {
        MergeExtensionData(root, message.ExtensionData);
        root["id"] = message.Id;
        SetOrRemove(
            root,
            "received",
            message.Received == default
                ? (DateTimeOffset?)null
                : message.Received);
        SetOrRemove(root, "from", message.From);
        SetOrRemove(root, "subject", message.Subject);
        SetOrRemove(root, "body", message.Body);
        SetOrRemove(root, "chapter", message.Chapter);
        if (message.Actions is null)
        {
            root.Remove("actions");
        }
        else
        {
            root["actions"] = new JsonArray(
                message.Actions
                    .Select(value => (JsonNode?)JsonValue.Create(value))
                    .ToArray());
        }

        if (message.Read)
        {
            root["read"] = true;
        }
        else
        {
            root.Remove("read");
        }

        SetOrRemove(root, "replied", message.Replied);
    }

    private static void MergeRoute(
        JsonObject root,
        RavenQuestRoute route)
    {
        MergeExtensionData(root, route.ExtensionData);
        root["id"] = route.Id;
        root["w"] = route.Width;
        var waypoints = new JsonArray();
        foreach (var waypoint in route.Waypoints)
        {
            waypoints.Add(new JsonArray(
                waypoint
                    .Select(value => (JsonNode?)JsonValue.Create(value))
                    .ToArray()));
        }

        root["wp"] = waypoints;
    }

    private static JsonArray MergeById<T>(
        JsonNode? existing,
        IEnumerable<T> values,
        Func<T, string> getId,
        Action<JsonObject, T> merge)
    {
        var existingById = new Dictionary<string, JsonObject>(
            StringComparer.Ordinal);
        var unidentified = new List<JsonNode>();
        if (existing is JsonArray array)
        {
            foreach (var item in array)
            {
                if (item is JsonObject child
                    && GetString(child, "id") is { } id
                    && !existingById.ContainsKey(id))
                {
                    existingById[id] = child;
                }
                else if (item is not null)
                {
                    unidentified.Add(item.DeepClone());
                }
            }
        }

        var result = new JsonArray();
        foreach (var value in values)
        {
            var id = getId(value);
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            var child = existingById.TryGetValue(id, out var prior)
                ? prior.DeepClone().AsObject()
                : new JsonObject();
            merge(child, value);
            result.Add(child);
        }

        foreach (var child in unidentified)
        {
            result.Add(child);
        }

        return result;
    }

    private static JsonObject ToStringObject(
        IReadOnlyDictionary<string, string> values)
    {
        var result = new JsonObject();
        foreach (var pair in values)
        {
            result[pair.Key] = pair.Value;
        }

        return result;
    }

    private static JsonObject ToJsonObject(
        IReadOnlyDictionary<string, JsonElement> values)
    {
        var result = new JsonObject();
        foreach (var pair in values)
        {
            result[pair.Key] = JsonNode.Parse(pair.Value.GetRawText());
        }

        return result;
    }

    private static void MergeExtensionData(
        JsonObject root,
        IReadOnlyDictionary<string, JsonElement> extensionData)
    {
        foreach (var pair in extensionData)
        {
            root[pair.Key] = JsonNode.Parse(pair.Value.GetRawText());
        }
    }

    private static void SetOrRemove<T>(
        JsonObject root,
        string name,
        T? value)
    {
        if (value is null)
        {
            root.Remove(name);
        }
        else
        {
            root[name] = JsonValue.Create(value);
        }
    }

    private async Task<string> CreateVerifiedBackupAsync(
        string path,
        string frontierId,
        CancellationToken cancellationToken)
    {
        var backupDirectory = Path.Combine(
            questDirectory,
            "quest-state-backups");
        Directory.CreateDirectory(backupDirectory);
        var safeFrontierId = string.Concat(frontierId.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character)
                ? '_'
                : character));
        var backupPath = Path.Combine(
            backupDirectory,
            $"{safeFrontierId}-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffffffZ}-{Guid.NewGuid():N}.json");
        File.Copy(path, backupPath, false);
        try
        {
            var sourceHash = await ComputeSha256Async(path, cancellationToken)
                .ConfigureAwait(false);
            var backupHash = await ComputeSha256Async(
                    backupPath,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(sourceHash, backupHash))
            {
                throw new IOException(
                    "The legacy quest state backup did not match its source.");
            }

            return backupPath;
        }
        catch
        {
            File.Delete(backupPath);
            throw;
        }
    }

    private static async Task<byte[]> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await SHA256.HashDataAsync(stream, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task WriteVerifiedAsync(
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
                             FileOptions.Asynchronous))
            {
                await using var writer = new Utf8JsonWriter(
                    stream,
                    new JsonWriterOptions
                    {
                        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                        Indented = true,
                    });
                root.WriteTo(writer);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            var verified = ParseObject(temporaryPath);
            if (!JsonNode.DeepEquals(root, verified))
            {
                throw new InvalidDataException(
                    "The legacy quest state could not be verified before saving.");
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

    private static void ValidateFrontierId(string frontierId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(frontierId);
        if (ContainsPathSeparator(frontierId))
        {
            throw new ArgumentException(
                "The Frontier ID cannot contain path separators.",
                nameof(frontierId));
        }
    }

    private static void ValidateProgressIdentity(RavenCommanderQuest progress)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(progress.Publisher);
        ArgumentException.ThrowIfNullOrWhiteSpace(progress.Id);
        if (progress.Publisher.Contains('|', StringComparison.Ordinal)
            || progress.Id.Contains('|', StringComparison.Ordinal)
            || ContainsPathSeparator(progress.Id)
            || !double.IsFinite(progress.Version))
        {
            throw new ArgumentException(
                "The development quest has an invalid identity.",
                nameof(progress));
        }
    }

    private LegacyQuestDefinition? LoadDefinition(
        LegacyQuestReference reference,
        ICollection<string> warnings)
    {
        if (ContainsPathSeparator(reference.Id))
        {
            warnings.Add(
                $"Development quest id '{reference.Id}' contains a path separator.");
            return null;
        }

        var fileName = $"dev-{reference.Id}.json";
        var path = FindFile(fileName);
        if (path is null)
        {
            warnings.Add(
                $"Development quest definition '{fileName}' is missing.");
            return null;
        }

        try
        {
            var root = ParseObject(path);
            var publisher = GetRequiredString(root, "publisher", path);
            var id = GetRequiredString(root, "id", path);
            var version = GetRequiredDouble(root, "ver", path);
            if (!string.Equals(
                    publisher,
                    reference.Publisher,
                    StringComparison.Ordinal)
                || !string.Equals(id, reference.Id, StringComparison.Ordinal)
                || version != reference.Version)
            {
                warnings.Add(
                    $"Development quest definition identity '{publisher}|{id}|{version.ToString(CultureInfo.InvariantCulture)}' "
                    + $"does not match '{reference}'.");
            }

            return new LegacyQuestDefinition(
                publisher,
                id,
                version,
                GetRequiredString(root, "title", path),
                GetString(root, "subTitle"),
                GetString(root, "desc"),
                GetStringSet(root["tags"]),
                ParseDuration(root["duration"], warnings),
                GetStringSet(root["onlySquadrons"]),
                GetStringSet(root["onlyCmdrs"]),
                GetBoolean(root, "hidden") ?? false,
                GetStringMap(root["objectives"]),
                GetStringMap(root["strings"]),
                ParseMessageDefinitions(root["msgs"], warnings),
                GetRequiredString(root, "firstChapter", path),
                GetStringMap(root["chapters"]),
                path);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException
                or InvalidDataException)
        {
            warnings.Add(
                $"Development quest definition '{fileName}' could not be loaded: "
                + exception.Message);
            return null;
        }
    }

    private static RavenQuestDefinition? ParsePortableDefinition(
        JsonNode? node,
        LegacyQuestReference reference,
        ICollection<string> warnings)
    {
        if (node is null)
        {
            return null;
        }

        if (node is not JsonObject)
        {
            warnings.Add(
                "The embedded development quest definition is not a JSON object.");
            return null;
        }

        try
        {
            var definition = node.Deserialize<RavenQuestDefinition>(
                PortableJsonOptions);
            if (definition is null)
            {
                warnings.Add(
                    "The embedded development quest definition contains JSON null.");
                return null;
            }

            if (!string.Equals(
                    definition.Publisher,
                    reference.Publisher,
                    StringComparison.Ordinal)
                || !string.Equals(
                    definition.Id,
                    reference.Id,
                    StringComparison.Ordinal)
                || definition.Version != reference.Version)
            {
                warnings.Add(
                    $"Embedded development quest definition identity '{definition.Reference}' does not match '{reference}'.");
                return null;
            }

            return definition;
        }
        catch (JsonException exception)
        {
            warnings.Add(
                "The embedded development quest definition could not be loaded: "
                    + exception.Message);
            return null;
        }
    }

    private static LegacyQuestProgress ParseProgress(
        LegacyQuestReference reference,
        LegacyQuestDefinition? definition,
        JsonObject root,
        ICollection<string> warnings)
    {
        var objectives = new Dictionary<string, LegacyQuestObjective>(
            StringComparer.Ordinal);
        if (root["objectives"] is JsonObject objectiveRoot)
        {
            foreach (var entry in objectiveRoot)
            {
                if (entry.Value is not JsonValue value
                    || !value.TryGetValue<string>(out var text)
                    || !TryParseObjective(text, out var objective))
                {
                    warnings.Add(
                        $"Quest objective '{entry.Key}' has an invalid state and was ignored.");
                    continue;
                }

                objectives[entry.Key] = objective!;
            }
        }

        var messages = ParseDeliveredMessages(
            root["msgs"],
            definition,
            warnings);
        var chapters = ParseChapters(root["chapters"], warnings);
        return new LegacyQuestProgress(
            reference,
            definition,
            GetDateTimeOffset(root, "startTime"),
            GetDateTimeOffset(root, "endTime"),
            GetBoolean(root, "paused") ?? false,
            objectives,
            GetStringSet(root["tags"]),
            ParseBodyLocations(root["bodyLocations"], warnings),
            chapters,
            messages,
            ParseRoutes(root["routes"], warnings),
            GetJsonMap(root["vars"]),
            GetJsonMap(root["keptLasts"]));
    }

    private static IReadOnlyList<LegacyQuestMessage> ParseDeliveredMessages(
        JsonNode? node,
        LegacyQuestDefinition? definition,
        ICollection<string> warnings)
    {
        if (node is not JsonArray array)
        {
            return [];
        }

        var messages = new List<LegacyQuestMessage>();
        foreach (var item in array)
        {
            if (item is not JsonObject root
                || GetString(root, "id") is not { } id)
            {
                warnings.Add("A delivered quest message has no id and was ignored.");
                continue;
            }

            var declared = definition?.Messages.FirstOrDefault(message =>
                string.Equals(message.Id, id, StringComparison.Ordinal));
            messages.Add(new LegacyQuestMessage(
                id,
                GetDateTimeOffset(root, "received"),
                GetString(root, "from") ?? declared?.From,
                GetString(root, "subject") ?? declared?.Subject,
                GetString(root, "body") ?? declared?.Body,
                GetString(root, "chapter"),
                GetStringArray(root["actions"]),
                GetBoolean(root, "read") ?? false,
                GetString(root, "replied")));
        }

        return messages;
    }

    private static IReadOnlyList<LegacyQuestMessageDefinition> ParseMessageDefinitions(
        JsonNode? node,
        ICollection<string> warnings)
    {
        if (node is not JsonArray array)
        {
            return [];
        }

        var messages = new List<LegacyQuestMessageDefinition>();
        foreach (var item in array)
        {
            if (item is not JsonObject root
                || GetString(root, "id") is not { } id
                || GetString(root, "from") is not { } from
                || GetString(root, "body") is not { } body)
            {
                warnings.Add(
                    "A quest message definition is incomplete and was ignored.");
                continue;
            }

            messages.Add(new LegacyQuestMessageDefinition(
                id,
                from,
                GetString(root, "subject"),
                body,
                GetStringMap(root["actions"]),
                GetStringSet(root["tags"])));
        }

        return messages;
    }

    private static IReadOnlyList<LegacyQuestChapter> ParseChapters(
        JsonNode? node,
        ICollection<string> warnings)
    {
        if (node is not JsonArray array)
        {
            return [];
        }

        var chapters = new List<LegacyQuestChapter>();
        foreach (var item in array)
        {
            if (item is not JsonObject root
                || GetString(root, "id") is not { } id)
            {
                warnings.Add("A quest chapter has no id and was ignored.");
                continue;
            }

            chapters.Add(new LegacyQuestChapter(
                id,
                GetDateTimeOffset(root, "startTime"),
                GetDateTimeOffset(root, "endTime"),
                GetJsonMap(root["vars"])));
        }

        return chapters;
    }

    private static IReadOnlyDictionary<string, LegacyQuestBodyLocation>
        ParseBodyLocations(JsonNode? node, ICollection<string> warnings)
    {
        var locations = new Dictionary<string, LegacyQuestBodyLocation>(
            StringComparer.Ordinal);
        if (node is not JsonObject root)
        {
            return locations;
        }

        foreach (var entry in root)
        {
            if (entry.Value is not JsonValue value
                || !value.TryGetValue<string>(out var text))
            {
                warnings.Add(
                    $"Quest body location '{entry.Key}' is invalid and was ignored.");
                continue;
            }

            var parts = text.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length != 3
                || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
                || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude)
                || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var radius)
                || !double.IsFinite(latitude)
                || !double.IsFinite(longitude)
                || !double.IsFinite(radius))
            {
                warnings.Add(
                    $"Quest body location '{entry.Key}' is invalid and was ignored.");
                continue;
            }

            locations[entry.Key] = new LegacyQuestBodyLocation(
                latitude,
                longitude,
                radius);
        }

        return locations;
    }

    private static IReadOnlyList<LegacyQuestRoute> ParseRoutes(
        JsonNode? node,
        ICollection<string> warnings)
    {
        if (node is not JsonArray array)
        {
            return [];
        }

        var routes = new List<LegacyQuestRoute>();
        foreach (var item in array)
        {
            if (item is not JsonObject root
                || GetString(root, "id") is not { } id
                || GetDouble(root, "w") is not { } width
                || root["wp"] is not JsonArray waypointArray)
            {
                warnings.Add("A quest route is invalid and was ignored.");
                continue;
            }

            var waypoints = new List<IReadOnlyList<double>>();
            foreach (var waypoint in waypointArray)
            {
                if (waypoint is not JsonArray coordinates)
                {
                    continue;
                }

                var values = coordinates
                    .Select(value => value is JsonValue number
                        && number.TryGetValue<double>(out var coordinate)
                        && double.IsFinite(coordinate)
                            ? (double?)coordinate
                            : null)
                    .ToArray();
                if (values.Length > 0 && values.All(value => value is not null))
                {
                    waypoints.Add(values.Select(value => value!.Value).ToArray());
                }
            }

            routes.Add(new LegacyQuestRoute(id, width, waypoints));
        }

        return routes;
    }

    private static LegacyQuestReference? ParseReference(
        JsonNode? node,
        ICollection<string> warnings)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonValue value
            && value.TryGetValue<string>(out var text))
        {
            var parts = text.Split(
                '|',
                StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries);
            if (parts.Length == 3
                && double.TryParse(
                    parts[2],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var version)
                && double.IsFinite(version))
            {
                return new LegacyQuestReference(parts[0], parts[1], version);
            }
        }
        else if (node is JsonObject root
            && GetString(root, "publisher") is { } publisher
            && GetString(root, "id") is { } id
            && GetDouble(root, "ver") is { } version)
        {
            return new LegacyQuestReference(publisher, id, version);
        }

        warnings.Add("The development quest reference is invalid.");
        return null;
    }

    private string? FindFile(string fileName)
    {
        if (ContainsPathSeparator(fileName))
        {
            return null;
        }

        if (!Directory.Exists(questDirectory))
        {
            return null;
        }

        var exact = Path.Combine(questDirectory, fileName);
        if (File.Exists(exact))
        {
            return exact;
        }

        return Directory.EnumerateFiles(questDirectory, "*.json")
            .FirstOrDefault(path => string.Equals(
                Path.GetFileName(path),
                fileName,
                StringComparison.OrdinalIgnoreCase));
    }

    private static JsonObject ParseObject(string path)
    {
        return JsonNode.Parse(
                File.ReadAllText(path),
                documentOptions: new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                })
            as JsonObject
            ?? throw new InvalidDataException($"'{path}' is not a JSON object.");
    }

    private static bool TryParseObjective(
        string value,
        out LegacyQuestObjective? objective)
    {
        objective = null;
        var parts = value.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length is not 1 and not 3
            || !Enum.TryParse<LegacyQuestObjectiveState>(
                parts[0],
                ignoreCase: false,
                out var state))
        {
            return false;
        }

        var current = 0;
        var total = 0;
        if (parts.Length == 3
            && (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out current)
                || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out total)))
        {
            return false;
        }

        objective = new LegacyQuestObjective(state, current, total);
        return true;
    }

    private static IReadOnlyDictionary<string, string> GetStringMap(JsonNode? node)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (node is not JsonObject root)
        {
            return result;
        }

        foreach (var entry in root)
        {
            if (entry.Value is JsonValue value
                && value.TryGetValue<string>(out var text))
            {
                result[entry.Key] = text;
            }
        }

        return result;
    }

    private static IReadOnlySet<string> GetStringSet(JsonNode? node)
    {
        return node is JsonArray array
            ? array
                .Select(value => value is JsonValue item
                    && item.TryGetValue<string>(out var text)
                        ? text
                        : null)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
    }

    private static IReadOnlyList<string> GetStringArray(JsonNode? node)
    {
        if (node is not JsonArray array)
        {
            return [];
        }

        var result = new List<string>();
        foreach (var item in array)
        {
            if (item is JsonValue value
                && value.TryGetValue<string>(out var text)
                && !string.IsNullOrWhiteSpace(text))
            {
                result.Add(text);
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<string, JsonElement> GetJsonMap(
        JsonNode? node)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (node is not JsonObject root)
        {
            return result;
        }

        foreach (var entry in root)
        {
            result[entry.Key] = JsonSerializer.SerializeToElement(entry.Value);
        }

        return result;
    }

    private static LegacyQuestDuration ParseDuration(
        JsonNode? node,
        ICollection<string> warnings)
    {
        if (node is null)
        {
            return LegacyQuestDuration.Unknown;
        }

        if (node is JsonValue value
            && value.TryGetValue<string>(out var text)
            && Enum.TryParse<LegacyQuestDuration>(text, true, out var duration))
        {
            return duration;
        }

        warnings.Add("The development quest duration is invalid; Unknown was used.");
        return LegacyQuestDuration.Unknown;
    }

    private static bool ContainsPathSeparator(string value)
    {
        return value.IndexOfAny(['/', '\\']) >= 0;
    }

    private static string GetRequiredString(
        JsonObject root,
        string name,
        string path)
    {
        return GetString(root, name)
            ?? throw new InvalidDataException(
                $"'{path}' has no quest {name}.");
    }

    private static double GetRequiredDouble(
        JsonObject root,
        string name,
        string path)
    {
        return GetDouble(root, name)
            ?? throw new InvalidDataException(
                $"'{path}' has no quest {name}.");
    }

    private static string? GetString(JsonObject root, string name)
    {
        return root[name] is JsonValue value
            && value.TryGetValue<string>(out var result)
            && !string.IsNullOrWhiteSpace(result)
                ? result
                : null;
    }

    private static bool? GetBoolean(JsonObject root, string name)
    {
        return root[name] is JsonValue value
            && value.TryGetValue<bool>(out var result)
                ? result
                : null;
    }

    private static double? GetDouble(JsonObject root, string name)
    {
        return root[name] is JsonValue value
            && value.TryGetValue<double>(out var result)
            && double.IsFinite(result)
                ? result
                : null;
    }

    private static DateTimeOffset? GetDateTimeOffset(
        JsonObject root,
        string name)
    {
        return root[name] is JsonValue value
            && value.TryGetValue<DateTimeOffset>(out var result)
                ? result
                : null;
    }
}

public sealed record LegacyQuestStateLoadResult(
    string Path,
    bool Exists,
    LegacyCommanderQuestState? Data,
    IReadOnlyList<string> Warnings,
    string? Error);

public sealed record LegacyQuestStateSaveResult(
    string Path,
    string? BackupPath,
    bool HasDevelopmentQuest);

public sealed record LegacyCommanderQuestState(
    string FrontierId,
    string? CommanderName,
    LegacyQuestProgress? DevelopmentQuest);

public sealed record LegacyQuestReference(
    string Publisher,
    string Id,
    double Version)
{
    public override string ToString()
    {
        return $"{Publisher}|{Id}|{Version.ToString(CultureInfo.InvariantCulture)}";
    }
}

public sealed record LegacyQuestDefinition(
    string Publisher,
    string Id,
    double Version,
    string Title,
    string? Subtitle,
    string? Description,
    IReadOnlySet<string> Tags,
    LegacyQuestDuration Duration,
    IReadOnlySet<string> OnlySquadrons,
    IReadOnlySet<string> OnlyCommanders,
    bool Hidden,
    IReadOnlyDictionary<string, string> Objectives,
    IReadOnlyDictionary<string, string> Strings,
    IReadOnlyList<LegacyQuestMessageDefinition> Messages,
    string FirstChapter,
    IReadOnlyDictionary<string, string> Chapters,
    string Path);

public enum LegacyQuestDuration
{
    Unknown,
    Short,
    Medium,
    Long,
    Extended,
}

public sealed record LegacyQuestMessageDefinition(
    string Id,
    string From,
    string? Subject,
    string Body,
    IReadOnlyDictionary<string, string> Actions,
    IReadOnlySet<string> Tags);

public sealed record LegacyQuestProgress(
    LegacyQuestReference Reference,
    LegacyQuestDefinition? Definition,
    DateTimeOffset? StartTime,
    DateTimeOffset? EndTime,
    bool Paused,
    IReadOnlyDictionary<string, LegacyQuestObjective> Objectives,
    IReadOnlySet<string> Tags,
    IReadOnlyDictionary<string, LegacyQuestBodyLocation> BodyLocations,
    IReadOnlyList<LegacyQuestChapter> Chapters,
    IReadOnlyList<LegacyQuestMessage> Messages,
    IReadOnlyList<LegacyQuestRoute> Routes,
    IReadOnlyDictionary<string, JsonElement> Variables,
    IReadOnlyDictionary<string, JsonElement> KeptJournalEvents)
{
    public RavenQuestDefinition? PortableDefinition { get; init; }

    public int UnreadMessageCount => Messages.Count(message => !message.Read);
}

public sealed record LegacyQuestObjective(
    LegacyQuestObjectiveState State,
    int Current,
    int Total);

public enum LegacyQuestObjectiveState
{
    hidden,
    visible,
    complete,
    failed,
}

public sealed record LegacyQuestBodyLocation(
    double Latitude,
    double Longitude,
    double Radius);

public sealed record LegacyQuestChapter(
    string Id,
    DateTimeOffset? StartTime,
    DateTimeOffset? EndTime,
    IReadOnlyDictionary<string, JsonElement> Variables)
{
    public bool IsActive => StartTime is not null && EndTime is null;
}

public sealed record LegacyQuestMessage(
    string Id,
    DateTimeOffset? Received,
    string? From,
    string? Subject,
    string? Body,
    string? Chapter,
    IReadOnlyList<string> Actions,
    bool Read,
    string? Replied);

public sealed record LegacyQuestRoute(
    string Id,
    double Width,
    IReadOnlyList<IReadOnlyList<double>> Waypoints);
