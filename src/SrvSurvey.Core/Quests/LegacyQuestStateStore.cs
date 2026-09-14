using System.Globalization;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace SrvSurvey.Core.Quests;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The store is coordinator-scoped and its semaphore may still have in-flight waiters."
)]
public sealed class LegacyQuestStateStore
{
    private const string JsonExtension = ".json";
    private const string DevQuestProperty = "devQuest";
    private const string StartTimeProperty = "startTime";
    private const string EndTimeProperty = "endTime";
    private const string ChaptersProperty = "chapters";
    private const string ActionsProperty = "actions";
    private static readonly JsonSerializerOptions PortableJsonOptions = new()
    {
        AllowTrailingCommas = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly SemaphoreSlim saveLock = new(1, 1);
    private readonly string questDirectory;

    public LegacyQuestStateStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        questDirectory = Path.Combine(Path.GetFullPath(dataDirectory), "quests");
    }

    public LegacyQuestStateLoadResult Load(string frontierId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(frontierId);
        if (ContainsPathSeparator(frontierId))
        {
            throw new ArgumentException("The Frontier ID cannot contain path separators.", nameof(frontierId));
        }

        string statePath = Path.Combine(questDirectory, frontierId + JsonExtension);

        try
        {
            statePath = FindFile(frontierId + JsonExtension) ?? statePath;
            if (!File.Exists(statePath))
            {
                return new LegacyQuestStateLoadResult(
                    statePath,
                    false,
                    new LegacyCommanderQuestState(frontierId, null, null),
                    [],
                    null
                );
            }

            JsonObject root = ParseObject(statePath);
            var warnings = new List<string>();
            string storedFrontierId = GetString(root, "fid") ?? frontierId;
            if (!string.Equals(storedFrontierId, frontierId, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"Quest state FID '{storedFrontierId}' does not match '{frontierId}'.");
            }

            LegacyQuestReference? reference = ParseReference(root["devRef"], warnings);
            LegacyQuestProgress? devQuest = null;
            if (root[DevQuestProperty] is JsonObject progress)
            {
                if (reference is null)
                {
                    warnings.Add("A development quest state exists without a devRef identity.");
                }
                else
                {
                    RavenQuestDefinition? portableDefinition = ParsePortableDefinition(
                        progress["quest"],
                        reference,
                        warnings
                    );
                    LegacyQuestDefinition? definition = portableDefinition is null
                        ? LoadDefinition(reference, warnings)
                        : null;
                    devQuest = ParseProgress(reference, definition, progress, warnings) with
                    {
                        PortableDefinition = portableDefinition,
                    };
                }
            }
            else if (reference is not null)
            {
                warnings.Add($"Development quest '{reference}' has no local progress object.");
            }

            return new LegacyQuestStateLoadResult(
                statePath,
                true,
                new LegacyCommanderQuestState(storedFrontierId, GetString(root, "cmdr"), devQuest),
                warnings,
                null
            );
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or JsonException
                        or InvalidDataException
                        or FormatException
                        or OverflowException
            )
        {
            return new LegacyQuestStateLoadResult(statePath, true, null, [], exception.Message);
        }
    }

    public async Task<LegacyQuestStateSaveResult> SaveDevelopmentQuestAsync(
        string frontierId,
        string? commanderName,
        RavenCommanderQuest? progress,
        CancellationToken cancellationToken = default
    )
    {
        return await SaveDevelopmentQuestCoreAsync(
                frontierId,
                commanderName,
                progress,
                replaceExistingProgress: false,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async Task<LegacyQuestStateSaveResult> ReplaceDevelopmentQuestAsync(
        string frontierId,
        string? commanderName,
        RavenCommanderQuest progress,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(progress);
        return await SaveDevelopmentQuestCoreAsync(
                frontierId,
                commanderName,
                progress,
                replaceExistingProgress: true,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private async Task<LegacyQuestStateSaveResult> SaveDevelopmentQuestCoreAsync(
        string frontierId,
        string? commanderName,
        RavenCommanderQuest? progress,
        bool replaceExistingProgress,
        CancellationToken cancellationToken
    )
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
            string path =
                FindFile(frontierId + JsonExtension) ?? Path.Combine(questDirectory, frontierId + JsonExtension);
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
                        exception
                    );
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
                root.Remove(DevQuestProperty);
            }
            else
            {
                root["devRef"] = progress.Reference.ToString();
                JsonObject questRoot = replaceExistingProgress
                    ? []
                    : root[DevQuestProperty] switch
                    {
                        null => [],
                        JsonObject existing => existing,
                        _ => throw new InvalidDataException(
                            "The legacy development quest state is not a JSON object and was not overwritten."
                        ),
                    };
                root[DevQuestProperty] = questRoot;
                MergeProgress(questRoot, progress);
            }

            string? backupPath = File.Exists(path)
                ? await CreateVerifiedBackupAsync(path, frontierId, cancellationToken).ConfigureAwait(false)
                : null;
            await WriteVerifiedAsync(path, root, cancellationToken).ConfigureAwait(false);
            return new LegacyQuestStateSaveResult(path, backupPath, progress is not null);
        }
        finally
        {
            saveLock.Release();
        }
    }

    private static void MergeProgress(JsonObject root, RavenCommanderQuest progress)
    {
        MergeExtensionData(root, progress.ExtensionData);
        if (progress.Quest is null)
        {
            root.Remove("quest");
        }
        else
        {
            root["quest"] = JsonSerializer.SerializeToNode(progress.Quest, PortableJsonOptions);
        }

        root["objectives"] = ToStringObject(progress.Objectives);
        SetOrRemove(root, StartTimeProperty, progress.StartTime);
        SetOrRemove(root, EndTimeProperty, progress.EndTime);
        if (progress.Paused)
        {
            root["paused"] = true;
        }
        else
        {
            root.Remove("paused");
        }

        root["tags"] = new JsonArray(progress.Tags.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
        root["bodyLocations"] = ToStringObject(progress.BodyLocations);
        root[ChaptersProperty] = MergeById(
            root[ChaptersProperty],
            progress.Chapters,
            chapter => chapter.Id,
            MergeChapter
        );
        root["msgs"] = MergeById(root["msgs"], progress.Messages, message => message.Id, MergeMessage);
        root["vars"] = ToJsonObject(progress.Variables);
        root["keptLasts"] = ToJsonObject(progress.KeptJournalEvents);
        root["routes"] = MergeById(root["routes"], progress.Routes, route => route.Id, MergeRoute);
    }

    private static void MergeChapter(JsonObject root, RavenQuestChapterState chapter)
    {
        MergeExtensionData(root, chapter.ExtensionData);
        root["id"] = chapter.Id;
        SetOrRemove(root, StartTimeProperty, chapter.StartTime);
        SetOrRemove(root, EndTimeProperty, chapter.EndTime);
        root["vars"] = ToJsonObject(chapter.Variables);
    }

    private static void MergeMessage(JsonObject root, RavenQuestMessage message)
    {
        MergeExtensionData(root, message.ExtensionData);
        root["id"] = message.Id;
        SetOrRemove(root, "received", message.Received == default ? (DateTimeOffset?)null : message.Received);
        SetOrRemove(root, "from", message.From);
        SetOrRemove(root, "subject", message.Subject);
        SetOrRemove(root, "body", message.Body);
        SetOrRemove(root, "chapter", message.Chapter);
        if (message.Actions is null)
        {
            root.Remove(ActionsProperty);
        }
        else
        {
            root[ActionsProperty] = new JsonArray(
                message.Actions.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()
            );
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

    private static void MergeRoute(JsonObject root, RavenQuestRoute route)
    {
        MergeExtensionData(root, route.ExtensionData);
        root["id"] = route.Id;
        root["w"] = route.Width;
        var waypoints = new JsonArray();
        foreach (double[] waypoint in route.Waypoints)
        {
            waypoints.Add(new JsonArray(waypoint.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()));
        }

        root["wp"] = waypoints;
    }

    private static JsonArray MergeById<T>(
        JsonNode? existing,
        IEnumerable<T> values,
        Func<T, string> getId,
        Action<JsonObject, T> merge
    )
    {
        var existingById = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        var unidentified = new List<JsonNode>();
        if (existing is JsonArray array)
        {
            foreach (JsonNode? item in array)
            {
                if (item is JsonObject child && GetString(child, "id") is { } id && !existingById.ContainsKey(id))
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
        foreach (T? value in values)
        {
            string id = getId(value);
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            JsonObject child = existingById.TryGetValue(id, out JsonObject? prior) ? prior.DeepClone().AsObject() : [];
            merge(child, value);
            result.Add(child);
        }

        foreach (JsonNode child in unidentified)
        {
            result.Add(child);
        }

        return result;
    }

    private static JsonObject ToStringObject(IReadOnlyDictionary<string, string> values)
    {
        var result = new JsonObject();
        foreach (KeyValuePair<string, string> pair in values)
        {
            result[pair.Key] = pair.Value;
        }

        return result;
    }

    private static JsonObject ToJsonObject(IReadOnlyDictionary<string, JsonElement> values)
    {
        var result = new JsonObject();
        foreach (KeyValuePair<string, JsonElement> pair in values)
        {
            result[pair.Key] = JsonNode.Parse(pair.Value.GetRawText());
        }

        return result;
    }

    private static void MergeExtensionData(JsonObject root, IReadOnlyDictionary<string, JsonElement> extensionData)
    {
        foreach (KeyValuePair<string, JsonElement> pair in extensionData)
        {
            root[pair.Key] = JsonNode.Parse(pair.Value.GetRawText());
        }
    }

    private static void SetOrRemove<T>(JsonObject root, string name, T? value)
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
        CancellationToken cancellationToken
    )
    {
        string backupDirectory = Path.Combine(questDirectory, "quest-state-backups");
        Directory.CreateDirectory(backupDirectory);
        string safeFrontierId = string.Concat(
            frontierId.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)
        );
        string backupPath = Path.Combine(
            backupDirectory,
            $"{safeFrontierId}-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffffffZ}-{Guid.NewGuid():N}.json"
        );
        File.Copy(path, backupPath, false);
        try
        {
            byte[] sourceHash = await ComputeSha256Async(path, cancellationToken).ConfigureAwait(false);
            byte[] backupHash = await ComputeSha256Async(backupPath, cancellationToken).ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(sourceHash, backupHash))
            {
                throw new IOException("The legacy quest state backup did not match its source.");
            }

            return backupPath;
        }
        catch
        {
            File.Delete(backupPath);
            throw;
        }
    }

    private static async Task<byte[]> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan
        );
        return await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteVerifiedAsync(string path, JsonObject root, CancellationToken cancellationToken)
    {
        string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (
                var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    16 * 1024,
                    FileOptions.Asynchronous
                )
            )
            {
                await using var writer = new Utf8JsonWriter(
                    stream,
                    new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, Indented = true }
                );
                root.WriteTo(writer);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            JsonObject verified = ParseObject(temporaryPath);
            if (!JsonNode.DeepEquals(root, verified))
            {
                throw new InvalidDataException("The legacy quest state could not be verified before saving.");
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
            throw new ArgumentException("The Frontier ID cannot contain path separators.", nameof(frontierId));
        }
    }

    private static void ValidateProgressIdentity(RavenCommanderQuest progress)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(progress.Publisher);
        ArgumentException.ThrowIfNullOrWhiteSpace(progress.Id);
        if (
            progress.Publisher.Contains('|', StringComparison.Ordinal)
            || progress.Id.Contains('|', StringComparison.Ordinal)
            || ContainsPathSeparator(progress.Id)
            || !double.IsFinite(progress.Version)
        )
        {
            throw new ArgumentException("The development quest has an invalid identity.", nameof(progress));
        }
    }

    private LegacyQuestDefinition? LoadDefinition(LegacyQuestReference reference, List<string> warnings)
    {
        if (ContainsPathSeparator(reference.Id))
        {
            warnings.Add($"Development quest id '{reference.Id}' contains a path separator.");
            return null;
        }

        string fileName = $"dev-{reference.Id}.json";
        string? path = FindFile(fileName);
        if (path is null)
        {
            warnings.Add($"Development quest definition '{fileName}' is missing.");
            return null;
        }

        try
        {
            JsonObject root = ParseObject(path);
            string publisher = GetRequiredString(root, "publisher", path);
            string id = GetRequiredString(root, "id", path);
            double version = GetRequiredDouble(root, "ver", path);
            if (
                !string.Equals(publisher, reference.Publisher, StringComparison.Ordinal)
                || !string.Equals(id, reference.Id, StringComparison.Ordinal)
                || version.CompareTo(reference.Version) != 0
            )
            {
                warnings.Add(
                    $"Development quest definition identity '{publisher}|{id}|{version.ToString(CultureInfo.InvariantCulture)}' "
                        + $"does not match '{reference}'."
                );
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
                GetStringMap(root[ChaptersProperty]),
                path
            );
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            warnings.Add($"Development quest definition '{fileName}' could not be loaded: " + exception.Message);
            return null;
        }
    }

    private static RavenQuestDefinition? ParsePortableDefinition(
        JsonNode? node,
        LegacyQuestReference reference,
        List<string> warnings
    )
    {
        if (node is null)
        {
            return null;
        }

        if (node is not JsonObject)
        {
            warnings.Add("The embedded development quest definition is not a JSON object.");
            return null;
        }

        try
        {
            RavenQuestDefinition? definition = node.Deserialize<RavenQuestDefinition>(PortableJsonOptions);
            if (definition is null)
            {
                warnings.Add("The embedded development quest definition contains JSON null.");
                return null;
            }

            if (
                !string.Equals(definition.Publisher, reference.Publisher, StringComparison.Ordinal)
                || !string.Equals(definition.Id, reference.Id, StringComparison.Ordinal)
                || definition.Version.CompareTo(reference.Version) != 0
            )
            {
                warnings.Add(
                    $"Embedded development quest definition identity '{definition.Reference}' does not match '{reference}'."
                );
                return null;
            }

            return definition;
        }
        catch (JsonException exception)
        {
            warnings.Add("The embedded development quest definition could not be loaded: " + exception.Message);
            return null;
        }
    }

    private static LegacyQuestProgress ParseProgress(
        LegacyQuestReference reference,
        LegacyQuestDefinition? definition,
        JsonObject root,
        List<string> warnings
    )
    {
        var objectives = new Dictionary<string, LegacyQuestObjective>(StringComparer.Ordinal);
        if (root["objectives"] is JsonObject objectiveRoot)
        {
            foreach (KeyValuePair<string, JsonNode?> entry in objectiveRoot)
            {
                if (
                    entry.Value is not JsonValue value
                    || !value.TryGetValue<string>(out string? text)
                    || !TryParseObjective(text, out LegacyQuestObjective? objective)
                )
                {
                    warnings.Add($"Quest objective '{entry.Key}' has an invalid state and was ignored.");
                    continue;
                }

                objectives[entry.Key] = objective!;
            }
        }

        List<LegacyQuestMessage> messages = ParseDeliveredMessages(root["msgs"], definition, warnings);
        List<LegacyQuestChapter> chapters = ParseChapters(root[ChaptersProperty], warnings);
        return new LegacyQuestProgress(
            reference,
            definition,
            GetDateTimeOffset(root, StartTimeProperty),
            GetDateTimeOffset(root, EndTimeProperty),
            GetBoolean(root, "paused") ?? false,
            objectives,
            GetStringSet(root["tags"]),
            ParseBodyLocations(root["bodyLocations"], warnings),
            chapters,
            messages,
            ParseRoutes(root["routes"], warnings),
            GetJsonMap(root["vars"]),
            GetJsonMap(root["keptLasts"])
        );
    }

    private static List<LegacyQuestMessage> ParseDeliveredMessages(
        JsonNode? node,
        LegacyQuestDefinition? definition,
        List<string> warnings
    )
    {
        if (node is not JsonArray array)
        {
            return [];
        }

        var messages = new List<LegacyQuestMessage>();
        foreach (JsonNode? item in array)
        {
            if (item is not JsonObject root || GetString(root, "id") is not { } id)
            {
                warnings.Add("A delivered quest message has no id and was ignored.");
                continue;
            }

            LegacyQuestMessageDefinition? declared = definition?.Messages.FirstOrDefault(message =>
                string.Equals(message.Id, id, StringComparison.Ordinal)
            );
            messages.Add(
                new LegacyQuestMessage(
                    id,
                    GetDateTimeOffset(root, "received"),
                    GetString(root, "from") ?? declared?.From,
                    GetString(root, "subject") ?? declared?.Subject,
                    GetString(root, "body") ?? declared?.Body,
                    GetString(root, "chapter"),
                    GetStringArray(root[ActionsProperty]),
                    GetBoolean(root, "read") ?? false,
                    GetString(root, "replied")
                )
            );
        }

        return messages;
    }

    private static List<LegacyQuestMessageDefinition> ParseMessageDefinitions(JsonNode? node, List<string> warnings)
    {
        if (node is not JsonArray array)
        {
            return [];
        }

        var messages = new List<LegacyQuestMessageDefinition>();
        foreach (JsonNode? item in array)
        {
            if (
                item is not JsonObject root
                || GetString(root, "id") is not { } id
                || GetString(root, "from") is not { } from
                || GetString(root, "body") is not { } body
            )
            {
                warnings.Add("A quest message definition is incomplete and was ignored.");
                continue;
            }

            messages.Add(
                new LegacyQuestMessageDefinition(
                    id,
                    from,
                    GetString(root, "subject"),
                    body,
                    GetStringMap(root[ActionsProperty]),
                    GetStringSet(root["tags"])
                )
            );
        }

        return messages;
    }

    private static List<LegacyQuestChapter> ParseChapters(JsonNode? node, List<string> warnings)
    {
        if (node is not JsonArray array)
        {
            return [];
        }

        var chapters = new List<LegacyQuestChapter>();
        foreach (JsonNode? item in array)
        {
            if (item is not JsonObject root || GetString(root, "id") is not { } id)
            {
                warnings.Add("A quest chapter has no id and was ignored.");
                continue;
            }

            chapters.Add(
                new LegacyQuestChapter(
                    id,
                    GetDateTimeOffset(root, StartTimeProperty),
                    GetDateTimeOffset(root, EndTimeProperty),
                    GetJsonMap(root["vars"])
                )
            );
        }

        return chapters;
    }

    private static Dictionary<string, LegacyQuestBodyLocation> ParseBodyLocations(JsonNode? node, List<string> warnings)
    {
        var locations = new Dictionary<string, LegacyQuestBodyLocation>(StringComparer.Ordinal);
        if (node is not JsonObject root)
        {
            return locations;
        }

        foreach (KeyValuePair<string, JsonNode?> entry in root)
        {
            if (entry.Value is not JsonValue value || !value.TryGetValue<string>(out string? text))
            {
                warnings.Add($"Quest body location '{entry.Key}' is invalid and was ignored.");
                continue;
            }

            string[] parts = text.Split(',', StringSplitOptions.TrimEntries);
            if (
                parts.Length != 3
                || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double latitude)
                || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double longitude)
                || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double radius)
                || !double.IsFinite(latitude)
                || !double.IsFinite(longitude)
                || !double.IsFinite(radius)
            )
            {
                warnings.Add($"Quest body location '{entry.Key}' is invalid and was ignored.");
                continue;
            }

            locations[entry.Key] = new LegacyQuestBodyLocation(latitude, longitude, radius);
        }

        return locations;
    }

    private static List<LegacyQuestRoute> ParseRoutes(JsonNode? node, List<string> warnings)
    {
        if (node is not JsonArray array)
        {
            return [];
        }

        var routes = new List<LegacyQuestRoute>();
        foreach (JsonNode? item in array)
        {
            if (!TryParseRoute(item, out LegacyQuestRoute? route) || route is null)
            {
                warnings.Add("A quest route is invalid and was ignored.");
                continue;
            }

            routes.Add(route);
        }

        return routes;
    }

    private static bool TryParseRoute(JsonNode? item, out LegacyQuestRoute? route)
    {
        route = null;
        if (item is not JsonObject root || GetString(root, "id") is not { } id || GetDouble(root, "w") is not { } width)
        {
            return false;
        }

        if (root["wp"] is not JsonArray waypointArray)
        {
            route = new LegacyQuestRoute(id, width, []);
            return true;
        }

        route = new LegacyQuestRoute(id, width, ParseWaypoints(waypointArray));
        return true;
    }

    private static List<IReadOnlyList<double>> ParseWaypoints(JsonArray waypointArray)
    {
        var waypoints = new List<IReadOnlyList<double>>(waypointArray.Count);
        foreach (JsonNode? waypoint in waypointArray)
        {
            if (
                waypoint is not JsonArray coordinates
                || !TryReadWaypoint(coordinates, out IReadOnlyList<double>? values)
            )
            {
                continue;
            }

            waypoints.Add(values);
        }

        return waypoints;
    }

    private static bool TryReadWaypoint(JsonArray coordinates, out IReadOnlyList<double> values)
    {
        values = [];
        double?[] parsed = coordinates
            .Select(value =>
                value is JsonValue number
                && number.TryGetValue<double>(out double coordinate)
                && double.IsFinite(coordinate)
                    ? (double?)coordinate
                    : null
            )
            .ToArray();
        if (parsed.Length == 0 || parsed.Any(value => value is null))
        {
            return false;
        }

        values = parsed.Select(value => value!.Value).ToArray();
        return true;
    }

    private static LegacyQuestReference? ParseReference(JsonNode? node, List<string> warnings)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonValue value && value.TryGetValue<string>(out string? text))
        {
            string[] parts = text.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (
                parts.Length == 3
                && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double version)
                && double.IsFinite(version)
            )
            {
                return new LegacyQuestReference(parts[0], parts[1], version);
            }
        }
        else if (
            node is JsonObject root
            && GetString(root, "publisher") is { } publisher
            && GetString(root, "id") is { } id
            && GetDouble(root, "ver") is { } version
        )
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

        string exact = Path.Combine(questDirectory, fileName);
        if (File.Exists(exact))
        {
            return exact;
        }

        return Directory
            .EnumerateFiles(questDirectory, "*.json")
            .FirstOrDefault(path =>
                string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase)
            );
    }

    private static JsonObject ParseObject(string path)
    {
        return JsonNode.Parse(
                File.ReadAllText(path),
                documentOptions: new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                }
            ) as JsonObject
            ?? throw new InvalidDataException($"'{path}' is not a JSON object.");
    }

    private static bool TryParseObjective(string value, out LegacyQuestObjective? objective)
    {
        objective = null;
        string[] parts = value.Split(',', StringSplitOptions.TrimEntries);
        if (
            parts.Length is not 1 and not 3
            || !Enum.TryParse<LegacyQuestObjectiveState>(
                parts[0],
                ignoreCase: false,
                out LegacyQuestObjectiveState state
            )
        )
        {
            return false;
        }

        int current = 0;
        int total = 0;
        if (
            parts.Length == 3
            && (
                !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out current)
                || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out total)
            )
        )
        {
            return false;
        }

        objective = new LegacyQuestObjective(state, current, total);
        return true;
    }

    private static Dictionary<string, string> GetStringMap(JsonNode? node)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (node is not JsonObject root)
        {
            return result;
        }

        foreach (KeyValuePair<string, JsonNode?> entry in root)
        {
            if (entry.Value is JsonValue value && value.TryGetValue<string>(out string? text))
            {
                result[entry.Key] = text;
            }
        }

        return result;
    }

    private static HashSet<string> GetStringSet(JsonNode? node)
    {
        return node is JsonArray array
            ? array
                .Select(value => value is JsonValue item && item.TryGetValue<string>(out string? text) ? text : null)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
    }

    private static List<string> GetStringArray(JsonNode? node)
    {
        if (node is not JsonArray array)
        {
            return [];
        }

        var result = new List<string>();
        foreach (JsonNode? item in array)
        {
            if (
                item is JsonValue value
                && value.TryGetValue<string>(out string? text)
                && !string.IsNullOrWhiteSpace(text)
            )
            {
                result.Add(text);
            }
        }

        return result;
    }

    private static Dictionary<string, JsonElement> GetJsonMap(JsonNode? node)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (node is not JsonObject root)
        {
            return result;
        }

        foreach (KeyValuePair<string, JsonNode?> entry in root)
        {
            result[entry.Key] = JsonSerializer.SerializeToElement(entry.Value);
        }

        return result;
    }

    private static LegacyQuestDuration ParseDuration(JsonNode? node, List<string> warnings)
    {
        if (node is null)
        {
            return LegacyQuestDuration.Unknown;
        }

        if (
            node is JsonValue value
            && value.TryGetValue<string>(out string? text)
            && Enum.TryParse<LegacyQuestDuration>(text, true, out LegacyQuestDuration duration)
        )
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

    private static string GetRequiredString(JsonObject root, string name, string path)
    {
        return GetString(root, name) ?? throw new InvalidDataException($"'{path}' has no quest {name}.");
    }

    private static double GetRequiredDouble(JsonObject root, string name, string path)
    {
        return GetDouble(root, name) ?? throw new InvalidDataException($"'{path}' has no quest {name}.");
    }

    private static string? GetString(JsonObject root, string name)
    {
        return
            root[name] is JsonValue value
            && value.TryGetValue<string>(out string? result)
            && !string.IsNullOrWhiteSpace(result)
            ? result
            : null;
    }

    private static bool? GetBoolean(JsonObject root, string name)
    {
        return root[name] is JsonValue value && value.TryGetValue<bool>(out bool result) ? result : null;
    }

    private static double? GetDouble(JsonObject root, string name)
    {
        return root[name] is JsonValue value && value.TryGetValue<double>(out double result) && double.IsFinite(result)
            ? result
            : null;
    }

    private static DateTimeOffset? GetDateTimeOffset(JsonObject root, string name)
    {
        return
            root[name] is JsonValue value && value.TryGetValue<DateTimeOffset>(out global::System.DateTimeOffset result)
            ? result
            : null;
    }
}

public sealed record LegacyQuestStateLoadResult(
    string Path,
    bool Exists,
    LegacyCommanderQuestState? Data,
    IReadOnlyList<string> Warnings,
    string? Error
);

public sealed record LegacyQuestStateSaveResult(string Path, string? BackupPath, bool HasDevelopmentQuest);

public sealed record LegacyCommanderQuestState(
    string FrontierId,
    string? CommanderName,
    LegacyQuestProgress? DevelopmentQuest
);

public sealed record LegacyQuestReference(string Publisher, string Id, double Version)
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
    string Path
);

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
    IReadOnlySet<string> Tags
);

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
    IReadOnlyDictionary<string, JsonElement> KeptJournalEvents
)
{
    public RavenQuestDefinition? PortableDefinition { get; init; }

    public int UnreadMessageCount => Messages.Count(message => !message.Read);
}

public sealed record LegacyQuestObjective(LegacyQuestObjectiveState State, int Current, int Total);

public enum LegacyQuestObjectiveState
{
    hidden,
    visible,
    complete,
    failed,
}

public sealed record LegacyQuestBodyLocation(double Latitude, double Longitude, double Radius);

public sealed record LegacyQuestChapter(
    string Id,
    DateTimeOffset? StartTime,
    DateTimeOffset? EndTime,
    IReadOnlyDictionary<string, JsonElement> Variables
)
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
    string? Replied
);

public sealed record LegacyQuestRoute(string Id, double Width, IReadOnlyList<IReadOnlyList<double>> Waypoints);
