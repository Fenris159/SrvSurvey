using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SrvSurvey.Core.Exobiology;

public sealed class CommanderCodexStore(string dataDirectory)
{
    private readonly string dataDirectory = Path.GetFullPath(
        string.IsNullOrWhiteSpace(dataDirectory)
            ? throw new ArgumentException(
                "A Commander Codex data directory is required.",
                nameof(dataDirectory))
            : dataDirectory);

    public async Task<CommanderCodexLoadResult> LoadAsync(
        string frontierId,
        string? commanderName,
        int regionId = 0,
        CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(frontierId, regionId);
        if (!File.Exists(path))
        {
            return new CommanderCodexLoadResult(
                path,
                false,
                new CommanderCodexData(
                    frontierId,
                    commanderName,
                    regionId,
                    null,
                    new Dictionary<long, CommanderCodexFirst>()),
                []);
        }

        try
        {
            var root = await ReadRootAsync(path, cancellationToken)
                .ConfigureAwait(false);
            if (root is null)
            {
                return CommanderCodexLoadResult.Failed(
                    path,
                    "The Commander Codex file does not contain a JSON object.");
            }

            var warnings = new List<string>();
            var entries = new Dictionary<long, CommanderCodexFirst>();
            if (root["codexFirsts"] is JsonObject firsts)
            {
                foreach (var property in firsts)
                {
                    if (!long.TryParse(
                            property.Key,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out var entryId)
                        || !TryParseFirst(property.Value, out var first))
                    {
                        warnings.Add(
                            $"Ignored malformed Commander Codex entry {property.Key}.");
                        continue;
                    }

                    entries[entryId] = first;
                }
            }

            return new CommanderCodexLoadResult(
                path,
                true,
                new CommanderCodexData(
                    GetString(root, "fid") ?? frontierId,
                    GetString(root, "commander") ?? commanderName,
                    regionId,
                    GetString(root, "region"),
                    entries),
                warnings);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException)
        {
            return CommanderCodexLoadResult.Failed(path, exception.Message);
        }
    }

    public async Task<CommanderCodexTrackResult> TrackAsync(
        string frontierId,
        string? commanderName,
        long entryId,
        DateTimeOffset timestamp,
        long systemAddress,
        int? bodyId,
        int regionId = 0,
        string? regionName = null,
        CancellationToken cancellationToken = default)
    {
        if (entryId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(entryId),
                "A positive Codex entry ID is required.");
        }

        var path = ResolvePath(frontierId, regionId);
        JsonObject root;
        try
        {
            root = File.Exists(path)
                ? await ReadRootAsync(path, cancellationToken)
                    .ConfigureAwait(false) ?? []
                : [];
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException)
        {
            return new CommanderCodexTrackResult(
                path,
                false,
                false,
                exception.Message);
        }

        var firsts = root["codexFirsts"] as JsonObject;
        if (firsts is null)
        {
            firsts = [];
            root["codexFirsts"] = firsts;
        }

        var key = entryId.ToString(CultureInfo.InvariantCulture);
        if (TryParseFirst(firsts[key], out var existing)
            && existing.SystemAddress != -1
            && timestamp.DateTime >= existing.Timestamp.DateTime)
        {
            return new CommanderCodexTrackResult(path, false, true, null);
        }

        root["fid"] = frontierId;
        if (!string.IsNullOrWhiteSpace(commanderName))
        {
            root["commander"] = commanderName;
        }

        if (regionId > 0 && !string.IsNullOrWhiteSpace(regionName))
        {
            root["region"] = regionName;
        }

        var first = new CommanderCodexFirst(
            timestamp,
            systemAddress,
            bodyId ?? -1);
        firsts[key] = FormatFirst(first);
        try
        {
            await WriteAtomicAsync(path, root, cancellationToken)
                .ConfigureAwait(false);
            return new CommanderCodexTrackResult(path, true, true, null);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException)
        {
            return new CommanderCodexTrackResult(
                path,
                false,
                false,
                exception.Message);
        }
    }

    public string ResolvePath(string frontierId, int regionId = 0)
    {
        if (string.IsNullOrWhiteSpace(frontierId)
            || !string.Equals(
                Path.GetFileName(frontierId),
                frontierId,
                StringComparison.Ordinal)
            || frontierId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException(
                "The Frontier ID is not valid for a Commander Codex file.",
                nameof(frontierId));
        }

        if (regionId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(regionId));
        }

        var fileName = regionId == 0
            ? $"{frontierId}-codex.json"
            : $"{frontierId}-codex-{regionId}.json";
        return Path.Combine(dataDirectory, fileName);
    }

    private static async Task<JsonObject?> ReadRootAsync(
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
        return await JsonNode.ParseAsync(
                stream,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false) as JsonObject;
    }

    private static async Task WriteAtomicAsync(
        string path,
        JsonObject root,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidDataException(
                "The Commander Codex path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
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
                        new JsonSerializerOptions { WriteIndented = true },
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

    private static bool TryParseFirst(
        JsonNode? value,
        out CommanderCodexFirst first)
    {
        first = null!;
        if (value is JsonValue scalar
            && scalar.TryGetValue<string>(out var text))
        {
            return TryParseLegacyFirst(text, out first);
        }

        if (value is not JsonObject item
            || GetDateTimeOffset(item, "time") is not { } timestamp
            || GetInt64(item, "address") is not { } address
            || GetInt32(item, "bodyId") is not { } bodyId)
        {
            return false;
        }

        first = new CommanderCodexFirst(timestamp, address, bodyId);
        return true;
    }

    private static bool TryParseLegacyFirst(
        string? value,
        out CommanderCodexFirst first)
    {
        first = null!;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split(
            '_',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3
            || !DateTimeOffset.TryParse(
                parts[0],
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out var timestamp)
            || !long.TryParse(
                parts[1],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var address)
            || !int.TryParse(
                parts[2],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var bodyId))
        {
            return false;
        }

        first = new CommanderCodexFirst(timestamp, address, bodyId);
        return true;
    }

    private static string FormatFirst(CommanderCodexFirst first)
    {
        return first.Timestamp.ToString("s", CultureInfo.InvariantCulture)
            + $"_{first.SystemAddress}_{first.BodyId}";
    }

    private static string? GetString(JsonObject root, string propertyName)
    {
        return root[propertyName] is JsonValue value
            && value.TryGetValue<string>(out var result)
                ? result
                : null;
    }

    private static long? GetInt64(JsonObject root, string propertyName)
    {
        return root[propertyName] is JsonValue value
            && value.TryGetValue<long>(out var result)
                ? result
                : null;
    }

    private static int? GetInt32(JsonObject root, string propertyName)
    {
        return root[propertyName] is JsonValue value
            && value.TryGetValue<int>(out var result)
                ? result
                : null;
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
                DateTimeStyles.AssumeLocal,
                out result)
                    ? result
                    : null;
    }
}

public sealed record CommanderCodexData(
    string FrontierId,
    string? CommanderName,
    int RegionId,
    string? RegionName,
    IReadOnlyDictionary<long, CommanderCodexFirst> Firsts)
{
    public bool IsDiscovered(long entryId) => Firsts.ContainsKey(entryId);

    public bool IsPersonalFirst(
        long entryId,
        long systemAddress,
        int bodyId)
    {
        return !Firsts.TryGetValue(entryId, out var first)
            || first.SystemAddress == systemAddress && first.BodyId == bodyId;
    }
}

public sealed record CommanderCodexFirst(
    DateTimeOffset Timestamp,
    long SystemAddress,
    int BodyId);

public sealed record CommanderCodexLoadResult(
    string Path,
    bool Exists,
    CommanderCodexData? Data,
    IReadOnlyList<string> Warnings)
{
    public bool IsSuccess => Data is not null;

    public string? Error => IsSuccess ? null : Warnings.FirstOrDefault();

    public static CommanderCodexLoadResult Failed(string path, string error)
    {
        return new CommanderCodexLoadResult(path, true, null, [error]);
    }
}

public sealed record CommanderCodexTrackResult(
    string Path,
    bool Changed,
    bool IsSuccess,
    string? Error);
