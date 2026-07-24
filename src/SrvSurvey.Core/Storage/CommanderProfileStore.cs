using System.Text.Json;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Exploration;

namespace SrvSurvey.Core.Storage;

public sealed class CommanderProfileStore(string profileDirectory)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

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
                    ExplorationSnapshot.Empty),
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
                GetInt32(root, "countLanded") ?? 0));
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
        root["explRewards"] = exploration.EstimatedRewards;
        root["distanceTravelled"] = exploration.DistanceTravelled;
        root["countJumps"] = exploration.JumpCount;
        root["countScans"] = exploration.ScanCount;
        root["countDSS"] = exploration.DetailedSurfaceScanCount;
        root["countLanded"] = exploration.LandedBodyCount;

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
    ExplorationSnapshot Exploration);

public sealed record CommanderProfileLoadResult(
    string Path,
    bool Exists,
    CommanderProfileData? Data,
    string? Error)
{
    public bool IsSuccess => Data is not null;
}
