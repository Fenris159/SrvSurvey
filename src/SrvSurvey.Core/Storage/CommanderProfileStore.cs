using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Combat;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Guardian;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Storage;

public sealed class CommanderProfileStore(string profileDirectory)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly SemaphoreSlim saveLock = new(1, 1);

    public string ProfileDirectory { get; } = Path.GetFullPath(profileDirectory);

    public string GetProfilePath(string frontierId, bool isOdyssey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(frontierId);
        var mode = isOdyssey ? "live" : "legacy";
        return Path.Combine(ProfileDirectory, $"{frontierId}-{mode}.json");
    }

    public async Task<CommanderProfileLoadResult> LoadAsync(
        string frontierId,
        bool isOdyssey,
        CancellationToken cancellationToken = default)
    {
        var path = GetProfilePath(frontierId, isOdyssey);
        if (!File.Exists(path))
        {
            return new CommanderProfileLoadResult(
                path,
                false,
                new CommanderProfileData(
                    frontierId,
                    null,
                    isOdyssey,
                    ExplorationSnapshot.Empty,
                    ExobiologySnapshot.Empty,
                    SphereLimitSnapshot.Empty,
                    BoxelSearchSnapshot.Empty,
                    RamTahSnapshot.Empty,
                    CombatSnapshot.Empty),
                null);
        }

        var readResult = await ReadObjectAsync(path, cancellationToken)
            .ConfigureAwait(false);
        if (readResult.Root is null)
        {
            return new CommanderProfileLoadResult(
                path,
                true,
                null,
                readResult.Error);
        }

        var root = readResult.Root;
        var data = new CommanderProfileData(
            GetString(root, "fid") ?? frontierId,
            GetString(root, "commander"),
            GetBoolean(root, "isOdyssey") ?? isOdyssey,
            new ExplorationSnapshot(
                GetInt64(root, "explRewards") ?? 0,
                GetDouble(root, "distanceTravelled") ?? 0,
                GetInt32(root, "countJumps") ?? 0,
                GetInt32(root, "countScans") ?? 0,
                GetInt32(root, "countDSS") ?? 0,
                GetInt32(root, "countLanded") ?? 0),
            ReadExobiology(root),
            ReadSphereLimit(root),
            ReadBoxelSearch(root),
            ReadRamTah(root),
            ReadCombat(root),
            GetString(root, "rccApiKey"),
            GetString(root, "activeJourney"),
            GetString(root, "inaraApiKey"));
        return new CommanderProfileLoadResult(path, true, data, null);
    }

    public async Task SaveExplorationAsync(
        string frontierId,
        string? commanderName,
        bool isOdyssey,
        ExplorationSnapshot exploration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exploration);
        await SaveFieldsAsync(
            frontierId,
            commanderName,
            isOdyssey,
            root =>
            {
                root["explRewards"] = exploration.EstimatedRewards;
                root["distanceTravelled"] = exploration.DistanceTravelled;
                root["countJumps"] = exploration.JumpCount;
                root["countScans"] = exploration.ScanCount;
                root["countDSS"] = exploration.DetailedSurfaceScanCount;
                root["countLanded"] = exploration.LandedBodyCount;
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveExobiologyAsync(
        string frontierId,
        string? commanderName,
        bool isOdyssey,
        ExobiologySnapshot exobiology,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exobiology);
        await SaveFieldsAsync(
            frontierId,
            commanderName,
            isOdyssey,
            root =>
            {
                root["lastOrganicScan"] = exobiology.LastOrganicScan;
                WriteBioSample(root, "scanOne", exobiology.ScanOne);
                WriteBioSample(root, "scanTwo", exobiology.ScanTwo);
                root["organicRewards"] = exobiology.OrganicRewards;
                var scannedIds = new JsonArray();
                foreach (var entry in exobiology.ScannedBioEntryIds)
                {
                    scannedIds.Add(entry);
                }

                root["scannedBioEntryIds"] = scannedIds;
                root["countRadicoidaUnica"] = exobiology.CountRadicoidaUnica;
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveSphereLimitAsync(
        string frontierId,
        string? commanderName,
        bool isOdyssey,
        SphereLimitSnapshot sphereLimit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sphereLimit);
        await SaveFieldsAsync(
            frontierId,
            commanderName,
            isOdyssey,
            root => WriteSphereLimit(root, sphereLimit),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveBoxelSearchAsync(
        string frontierId,
        string? commanderName,
        bool isOdyssey,
        BoxelSearchSnapshot boxelSearch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(boxelSearch);
        await SaveFieldsAsync(
            frontierId,
            commanderName,
            isOdyssey,
            root => WriteBoxelSearch(root, boxelSearch),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveRamTahAsync(
        string frontierId,
        string? commanderName,
        bool isOdyssey,
        RamTahSnapshot ramTah,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ramTah);
        await SaveFieldsAsync(
            frontierId,
            commanderName,
            isOdyssey,
            root => WriteRamTah(root, ramTah),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveCombatAsync(
        string frontierId,
        string? commanderName,
        bool isOdyssey,
        CombatSnapshot combat,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(combat);
        await SaveFieldsAsync(
            frontierId,
            commanderName,
            isOdyssey,
            root => WriteCombat(root, combat),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveRavenColonialApiKeyAsync(
        string frontierId,
        string? commanderName,
        bool isOdyssey,
        string? apiKey,
        CancellationToken cancellationToken = default)
    {
        var normalized = string.IsNullOrWhiteSpace(apiKey)
            ? null
            : apiKey.Trim();
        await SaveFieldsAsync(
            frontierId,
            commanderName,
            isOdyssey,
            root =>
            {
                if (normalized is null)
                {
                    root.Remove("rccApiKey");
                }
                else
                {
                    root["rccApiKey"] = normalized;
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveInaraApiKeyAsync(
        string frontierId,
        string? commanderName,
        bool isOdyssey,
        string? apiKey,
        CancellationToken cancellationToken = default)
    {
        var normalized = string.IsNullOrWhiteSpace(apiKey)
            ? null
            : apiKey.Trim();
        await SaveFieldsAsync(
            frontierId,
            commanderName,
            isOdyssey,
            root =>
            {
                if (normalized is null)
                {
                    root.Remove("inaraApiKey");
                }
                else
                {
                    root["inaraApiKey"] = normalized;
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveActiveJourneyAsync(
        string frontierId,
        string? commanderName,
        bool isOdyssey,
        string? journeyFileName,
        CancellationToken cancellationToken = default)
    {
        var normalized = string.IsNullOrWhiteSpace(journeyFileName)
            ? null
            : journeyFileName.Trim();
        await SaveFieldsAsync(
            frontierId,
            commanderName,
            isOdyssey,
            root =>
            {
                if (normalized is null)
                {
                    root.Remove("activeJourney");
                }
                else
                {
                    root["activeJourney"] = normalized;
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task SaveFieldsAsync(
        string frontierId,
        string? commanderName,
        bool isOdyssey,
        Action<JsonObject> update,
        CancellationToken cancellationToken)
    {
        await saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = GetProfilePath(frontierId, isOdyssey);
            JsonObject root;
            if (File.Exists(path))
            {
                var readResult = await ReadObjectAsync(path, cancellationToken)
                    .ConfigureAwait(false);
                root = readResult.Root
                    ?? throw new InvalidDataException(
                        $"The commander profile is malformed and was not overwritten: "
                            + readResult.Error);
            }
            else
            {
                root = [];
            }

            root["fid"] = frontierId;
            if (!string.IsNullOrWhiteSpace(commanderName))
            {
                root["commander"] = commanderName;
            }

            root["isOdyssey"] = isOdyssey;
            update(root);

            Directory.CreateDirectory(ProfileDirectory);
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
        finally
        {
            saveLock.Release();
        }
    }

    private static ExobiologySnapshot ReadExobiology(JsonObject root)
    {
        return new ExobiologySnapshot(
            GetString(root, "lastOrganicScan"),
            ReadBioSample(root, "scanOne"),
            ReadBioSample(root, "scanTwo"),
            GetInt64(root, "organicRewards") ?? 0,
            ReadStringArray(root, "scannedBioEntryIds"),
            GetInt32(root, "countRadicoidaUnica") ?? 0);
    }

    private static SphereLimitSnapshot ReadSphereLimit(JsonObject root)
    {
        if (root["sphereLimit"] is not JsonObject sphere)
        {
            return SphereLimitSnapshot.Empty;
        }

        var radius = GetDouble(sphere, "radius")
            ?? SphereLimitState.DefaultRadius;
        return new SphereLimitSnapshot(
            GetBoolean(sphere, "active") ?? false,
            GetString(sphere, "centerSystemName"),
            ReadGalacticCoordinate(sphere, "centerStarPos"),
            radius);
    }

    private static BoxelSearchSnapshot ReadBoxelSearch(JsonObject root)
    {
        if (root["boxelSearch"] is not JsonObject boxelSearch)
        {
            return BoxelSearchSnapshot.Empty;
        }

        _ = BoxelAddress.TryParse(
            GetString(boxelSearch, "boxel"),
            out var topBoxel);
        _ = BoxelAddress.TryParse(
            GetString(boxelSearch, "current"),
            out var current);
        var lowMassCodeText = GetString(boxelSearch, "lowMassCode");
        var lowMassCode = string.IsNullOrWhiteSpace(lowMassCodeText)
            ? 'c'
            : char.ToLowerInvariant(lowMassCodeText[0]);
        return new BoxelSearchSnapshot(
            GetBoolean(boxelSearch, "active") ?? false,
            topBoxel,
            GetDateTimeOffset(boxelSearch, "startedOn")
                ?? DateTimeOffset.MinValue,
            current,
            GetInt32(boxelSearch, "currentCount") ?? 0,
            lowMassCode,
            ReadStringArray(boxelSearch, "completed"),
            GetBoolean(boxelSearch, "autoCopy") ?? false,
            GetBoolean(boxelSearch, "collapsed") ?? false,
            GetBoolean(boxelSearch, "skipAlreadyVisited") ?? false,
            GetBoolean(boxelSearch, "skipKnownToSpansh") ?? false,
            GetBoolean(boxelSearch, "completeOnFssAllBodies") == true
                ? BoxelCompletionMode.FssAllBodies
                : BoxelCompletionMode.EnterSystem);
    }

    private static RamTahSnapshot ReadRamTah(JsonObject root)
    {
        return new RamTahSnapshot(
            ReadRamTahStatus(root, "decodeTheRuinsMissionActive"),
            ReadRamTahStatus(root, "decodeTheLogsMissionActive"),
            ReadStringArray(root, "decodeTheRuins"),
            ReadStringArray(root, "decodeTheLogs"));
    }

    private static CombatSnapshot ReadCombat(JsonObject root)
    {
        if (root["trackMassacres"] is not JsonArray array)
        {
            return CombatSnapshot.Empty;
        }

        var missions = array
            .OfType<JsonObject>()
            .Select(mission => new MassacreMissionSnapshot(
                GetInt64(mission, "missionId") ?? 0,
                GetString(mission, "missionGiver") ?? string.Empty,
                GetString(mission, "targetFaction") ?? string.Empty,
                GetDateTimeOffset(mission, "expires"),
                Math.Max(0, GetInt32(mission, "killCount") ?? 0),
                Math.Max(0, GetInt32(mission, "remaining") ?? 0)))
            .Where(mission => mission.MissionId > 0
                && !string.IsNullOrWhiteSpace(mission.MissionGiver)
                && !string.IsNullOrWhiteSpace(mission.TargetFaction))
            .DistinctBy(mission => mission.MissionId)
            .ToArray();
        return new CombatSnapshot(missions);
    }

    private static RamTahMissionStatus ReadRamTahStatus(
        JsonObject root,
        string propertyName)
    {
        var text = GetString(root, propertyName);
        if (Enum.TryParse<RamTahMissionStatus>(text, true, out var status))
        {
            return status;
        }

        var number = GetInt32(root, propertyName);
        return number is not null
            && Enum.IsDefined(typeof(RamTahMissionStatus), number.Value)
                ? (RamTahMissionStatus)number.Value
                : RamTahMissionStatus.NotStarted;
    }

    private static GalacticCoordinate? ReadGalacticCoordinate(
        JsonObject root,
        string propertyName)
    {
        if (root[propertyName] is not JsonArray array || array.Count < 3)
        {
            return null;
        }

        var values = array
            .Take(3)
            .Select(node => node is JsonValue value
                && value.TryGetValue<double>(out var number)
                    ? number
                    : double.NaN)
            .ToArray();
        return values.All(double.IsFinite)
            ? new GalacticCoordinate(values[0], values[1], values[2])
            : null;
    }

    private static void WriteSphereLimit(
        JsonObject root,
        SphereLimitSnapshot sphereLimit)
    {
        if (root["sphereLimit"] is not JsonObject node)
        {
            node = [];
            root["sphereLimit"] = node;
        }

        node["active"] = sphereLimit.Active;
        node["centerSystemName"] = sphereLimit.CenterSystemName;
        node["centerStarPos"] = sphereLimit.Center is { } center
            ? new JsonArray(center.X, center.Y, center.Z)
            : null;
        node["radius"] = sphereLimit.Radius;
    }

    private static void WriteBoxelSearch(
        JsonObject root,
        BoxelSearchSnapshot boxelSearch)
    {
        if (boxelSearch.TopBoxel is null)
        {
            root["boxelSearch"] = null;
            return;
        }

        if (root["boxelSearch"] is not JsonObject node)
        {
            node = [];
            root["boxelSearch"] = node;
        }

        node["active"] = boxelSearch.Active;
        node["startedOn"] = boxelSearch.StartedOn;
        node["boxel"] = boxelSearch.TopBoxel.ToStoredString();
        node["current"] = boxelSearch.Current?.ToStoredString();
        node["currentCount"] = boxelSearch.CurrentCount;
        node["lowMassCode"] = boxelSearch.LowMassCode.ToString();
        var completed = new JsonArray();
        foreach (var prefix in boxelSearch.CompletedPrefixes
                     .Distinct(StringComparer.Ordinal)
                     .Order(StringComparer.Ordinal))
        {
            completed.Add(prefix);
        }

        node["completed"] = completed;
        node["autoCopy"] = boxelSearch.AutoCopy;
        node["collapsed"] = boxelSearch.Collapsed;
        node["skipAlreadyVisited"] = boxelSearch.SkipAlreadyVisited;
        node["skipKnownToSpansh"] = boxelSearch.SkipKnownToSpansh;
        node["completeOnFssAllBodies"] =
            boxelSearch.CompletionMode == BoxelCompletionMode.FssAllBodies;
    }

    private static void WriteRamTah(JsonObject root, RamTahSnapshot ramTah)
    {
        root["decodeTheRuinsMissionActive"] =
            ramTah.AncientRuinsMissionStatus.ToString();
        root["decodeTheLogsMissionActive"] =
            ramTah.GuardianLogsMissionStatus.ToString();
        root["decodeTheRuins"] = WriteStringArray(ramTah.AncientRuinsLogs);
        root["decodeTheLogs"] = WriteStringArray(ramTah.GuardianLogs);
    }

    private static void WriteCombat(JsonObject root, CombatSnapshot combat)
    {
        if (combat.MassacreMissions.Count == 0)
        {
            root["trackMassacres"] = null;
            return;
        }

        var missions = new JsonArray();
        foreach (var mission in combat.MassacreMissions)
        {
            missions.Add(new JsonObject
            {
                ["missionId"] = mission.MissionId,
                ["missionGiver"] = mission.MissionGiver,
                ["targetFaction"] = mission.TargetFaction,
                ["expires"] = mission.Expires,
                ["killCount"] = mission.KillCount,
                ["remaining"] = mission.Remaining,
            });
        }

        root["trackMassacres"] = missions;
    }

    private static JsonArray WriteStringArray(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values
                     .Distinct(StringComparer.Ordinal)
                     .Order(StringComparer.Ordinal))
        {
            array.Add(value);
        }

        return array;
    }

    private static BioSampleSnapshot? ReadBioSample(
        JsonObject root,
        string propertyName)
    {
        if (root[propertyName] is not JsonObject sample)
        {
            return null;
        }

        var location = sample["location"] as JsonObject;
        return new BioSampleSnapshot(
            new SurfaceLocation(
                location is null ? 0 : GetDouble(location, "lat") ?? 0,
                location is null ? 0 : GetDouble(location, "long") ?? 0),
            (float)(GetDouble(sample, "radius") ?? 0),
            GetString(sample, "genus") ?? string.Empty,
            GetString(sample, "species") ?? string.Empty,
            GetString(sample, "status") ?? "Active",
            GetInt64(sample, "entryId") ?? 0,
            GetString(sample, "body"));
    }

    private static IReadOnlyList<string> ReadStringArray(
        JsonObject root,
        string propertyName)
    {
        if (root[propertyName] is not JsonArray array)
        {
            return [];
        }

        return array
            .OfType<JsonValue>()
            .Select(value => value.TryGetValue<string>(out var text) ? text : null)
            .Where(text => text is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static void WriteBioSample(
        JsonObject root,
        string propertyName,
        BioSampleSnapshot? sample)
    {
        if (sample is null)
        {
            root[propertyName] = null;
            return;
        }

        if (root[propertyName] is not JsonObject node)
        {
            node = [];
            root[propertyName] = node;
        }

        if (node["location"] is not JsonObject location)
        {
            location = [];
            node["location"] = location;
        }

        location["lat"] = sample.Location.Latitude;
        location["long"] = sample.Location.Longitude;
        node["radius"] = sample.Radius;
        node["genus"] = sample.Genus;
        node["species"] = sample.Species;
        node["status"] = sample.Status;
        node["entryId"] = sample.EntryId;
        node["body"] = sample.Body;
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
            return new JsonObjectReadResult(null, $"Could not read {path}: {exception.Message}");
        }
    }

    private static string? GetString(JsonObject root, string propertyName)
    {
        return root[propertyName] is JsonValue value
            && value.TryGetValue<string>(out var result)
                ? result
                : null;
    }

    private static bool? GetBoolean(JsonObject root, string propertyName)
    {
        return root[propertyName] is JsonValue value
            && value.TryGetValue<bool>(out var result)
                ? result
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

        return value.TryGetValue<double>(out var doubleResult)
            && doubleResult is >= long.MinValue and <= long.MaxValue
                ? Convert.ToInt64(doubleResult)
                : null;
    }

    private static int? GetInt32(JsonObject root, string propertyName)
    {
        var value = GetInt64(root, propertyName);
        return value is >= int.MinValue and <= int.MaxValue
            ? (int)value.Value
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

        return value.TryGetValue<long>(out var longResult) ? longResult : null;
    }

    private static DateTimeOffset? GetDateTimeOffset(
        JsonObject root,
        string propertyName)
    {
        if (root[propertyName] is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<DateTimeOffset>(out var dateTimeOffset))
        {
            return dateTimeOffset;
        }

        return value.TryGetValue<string>(out var text)
            && DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out dateTimeOffset)
                ? dateTimeOffset
                : null;
    }

    private sealed record JsonObjectReadResult(JsonObject? Root, string? Error);
}

public sealed record CommanderProfileData(
    string FrontierId,
    string? CommanderName,
    bool IsOdyssey,
    ExplorationSnapshot Exploration,
    ExobiologySnapshot Exobiology,
    SphereLimitSnapshot SphereLimit,
    BoxelSearchSnapshot BoxelSearch,
    RamTahSnapshot RamTah,
    CombatSnapshot Combat,
    string? RavenColonialApiKey = null,
    string? ActiveJourneyFileName = null,
    string? InaraApiKey = null);

public sealed record CommanderProfileLoadResult(
    string Path,
    bool Exists,
    CommanderProfileData? Data,
    string? Error)
{
    public bool IsSuccess => Data is not null;
}
