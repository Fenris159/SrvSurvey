using System.Text.Json;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Exploration;

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
                    ExobiologySnapshot.Empty),
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
            ReadExobiology(root));
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

    private sealed record JsonObjectReadResult(JsonObject? Root, string? Error);
}

public sealed record CommanderProfileData(
    string FrontierId,
    string? CommanderName,
    bool IsOdyssey,
    ExplorationSnapshot Exploration,
    ExobiologySnapshot Exobiology);

public sealed record CommanderProfileLoadResult(
    string Path,
    bool Exists,
    CommanderProfileData? Data,
    string? Error)
{
    public bool IsSuccess => Data is not null;
}
