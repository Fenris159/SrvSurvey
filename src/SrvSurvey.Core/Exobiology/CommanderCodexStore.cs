using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SrvSurvey.Core.Exobiology;

public sealed class CommanderCodexStore(string dataDirectory)
{
    private static readonly JsonSerializerOptions IndentedJson = new()
    {
        WriteIndented = true,
    };

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

    public async Task<CommanderCodexCommanderCatalogResult>
        DiscoverCommandersAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(dataDirectory))
        {
            return new CommanderCodexCommanderCatalogResult([], []);
        }

        FileInfo[] files;
        try
        {
            files = new DirectoryInfo(dataDirectory)
                .EnumerateFiles("*-codex.json", SearchOption.TopDirectoryOnly)
                .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return new CommanderCodexCommanderCatalogResult(
                [],
                [exception.Message]);
        }

        var commanders = new List<CommanderCodexData>();
        var warnings = new List<string>();
        const string suffix = "-codex.json";
        foreach (var fileName in files.Select(file => file.Name))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frontierId = fileName[..^suffix.Length];
            var loaded = await LoadAsync(
                    frontierId,
                    null,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (loaded.Data is not null)
            {
                commanders.Add(loaded.Data);
            }

            warnings.AddRange(loaded.Warnings.Select(warning =>
                $"{fileName}: {warning}"));
        }

        return new CommanderCodexCommanderCatalogResult(
            commanders
                .GroupBy(
                    commander => commander.FrontierId,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(
                    commander => commander.CommanderName
                        ?? commander.FrontierId,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            warnings);
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
        var result = await TrackBatchAsync(
                frontierId,
                commanderName,
                [new CommanderCodexDiscovery(
                    entryId,
                    timestamp,
                    systemAddress,
                    bodyId ?? -1)],
                regionId,
                regionName,
                cancellationToken)
            .ConfigureAwait(false);
        return new CommanderCodexTrackResult(
            result.Path,
            result.ChangedEntryCount > 0,
            result.IsSuccess,
            result.Error);
    }

    public async Task<CommanderCodexManualUpdateResult> SetManualDiscoveryAsync(
        string frontierId,
        string? commanderName,
        long entryId,
        bool isDiscovered,
        DateTimeOffset? timestamp = null,
        CancellationToken cancellationToken = default)
    {
        if (entryId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(entryId),
                "A positive Codex entry ID is required.");
        }

        var path = ResolvePath(frontierId);
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
            return CommanderCodexManualUpdateResult.Failed(
                path,
                exception.Message);
        }

        var firsts = root["codexFirsts"] as JsonObject;
        if (firsts is null)
        {
            firsts = [];
            root["codexFirsts"] = firsts;
        }

        var key = entryId.ToString(CultureInfo.InvariantCulture);
        var hasExisting = TryParseFirst(firsts[key], out var existing);
        if (isDiscovered == hasExisting
            || !isDiscovered && existing.SystemAddress != -1)
        {
            return new CommanderCodexManualUpdateResult(
                path,
                false,
                hasExisting,
                true,
                null);
        }

        if (isDiscovered)
        {
            firsts[key] = FormatFirst(new CommanderCodexFirst(
                timestamp ?? DateTimeOffset.Now,
                -1,
                -1));
        }
        else
        {
            firsts.Remove(key);
        }

        root["fid"] = frontierId;
        if (!string.IsNullOrWhiteSpace(commanderName))
        {
            root["commander"] = commanderName;
        }

        try
        {
            await WriteAtomicAsync(path, root, cancellationToken)
                .ConfigureAwait(false);
            return new CommanderCodexManualUpdateResult(
                path,
                true,
                isDiscovered,
                true,
                null);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException)
        {
            return CommanderCodexManualUpdateResult.Failed(
                path,
                exception.Message);
        }
    }

    public async Task<CommanderCodexBatchTrackResult> TrackBatchAsync(
        string frontierId,
        string? commanderName,
        IReadOnlyList<CommanderCodexDiscovery> discoveries,
        int regionId = 0,
        string? regionName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(discoveries);
        if (discoveries.Any(discovery => discovery.EntryId <= 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(discoveries),
                "Every Codex discovery requires a positive entry ID.");
        }

        var path = ResolvePath(frontierId, regionId);
        if (discoveries.Count == 0)
        {
            return new CommanderCodexBatchTrackResult(path, 0, true, null);
        }

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
            return new CommanderCodexBatchTrackResult(
                path,
                0,
                false,
                exception.Message);
        }

        var firsts = root["codexFirsts"] as JsonObject;
        if (firsts is null)
        {
            firsts = [];
            root["codexFirsts"] = firsts;
        }

        var changedEntryIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var discovery in discoveries)
        {
            var key = discovery.EntryId.ToString(CultureInfo.InvariantCulture);
            if (TryParseFirst(firsts[key], out var existing)
                && ShouldKeepExistingFirst(existing, discovery))
            {
                continue;
            }

            firsts[key] = FormatFirst(new CommanderCodexFirst(
                discovery.Timestamp,
                discovery.SystemAddress,
                discovery.BodyId));
            changedEntryIds.Add(key);
        }

        var changedEntryCount = changedEntryIds.Count;
        if (changedEntryCount == 0)
        {
            return new CommanderCodexBatchTrackResult(path, 0, true, null);
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

        try
        {
            await WriteAtomicAsync(path, root, cancellationToken)
                .ConfigureAwait(false);
            return new CommanderCodexBatchTrackResult(
                path,
                changedEntryCount,
                true,
                null);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException)
        {
            return new CommanderCodexBatchTrackResult(
                path,
                0,
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

        ArgumentOutOfRangeException.ThrowIfNegative(regionId);

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
                        IndentedJson,
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

    private static bool ShouldKeepExistingFirst(
        CommanderCodexFirst existing,
        CommanderCodexDiscovery discovery)
    {
        if (discovery.SystemAddress == -1)
        {
            return true;
        }

        return existing.SystemAddress != -1
            && discovery.Timestamp.DateTime >= existing.Timestamp.DateTime;
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

    public string? Error => IsSuccess || Warnings.Count == 0
        ? null
        : Warnings[0];

    public static CommanderCodexLoadResult Failed(string path, string error)
    {
        return new CommanderCodexLoadResult(path, true, null, [error]);
    }
}

public sealed record CommanderCodexCommanderCatalogResult(
    IReadOnlyList<CommanderCodexData> Commanders,
    IReadOnlyList<string> Warnings)
{
    public bool IsSuccess => Warnings.Count == 0;
}

public sealed record CommanderCodexTrackResult(
    string Path,
    bool Changed,
    bool IsSuccess,
    string? Error);

public sealed record CommanderCodexManualUpdateResult(
    string Path,
    bool Changed,
    bool IsDiscovered,
    bool IsSuccess,
    string? Error)
{
    public static CommanderCodexManualUpdateResult Failed(
        string path,
        string error)
    {
        return new CommanderCodexManualUpdateResult(
            path,
            false,
            false,
            false,
            error);
    }
}

public sealed record CommanderCodexDiscovery(
    long EntryId,
    DateTimeOffset Timestamp,
    long SystemAddress,
    int BodyId);

public sealed record CommanderCodexBatchTrackResult(
    string Path,
    int ChangedEntryCount,
    bool IsSuccess,
    string? Error);
