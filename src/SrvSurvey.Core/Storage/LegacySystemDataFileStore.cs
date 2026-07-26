using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Storage;

internal sealed class LegacySystemDataFileStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };
    private static readonly ConcurrentDictionary<string, SemaphoreSlim>
        UpdateLocks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim>
        ProfileWriteLocks = new(StringComparer.OrdinalIgnoreCase);

    private readonly string dataDirectory;

    public LegacySystemDataFileStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        this.dataDirectory = Path.GetFullPath(dataDirectory);
    }

    public async Task<LegacySystemDataFileLoadResult> LoadAsync(
        LegacySystemDataFileContext context,
        CancellationToken cancellationToken = default)
    {
        ValidateContext(context);
        var path = FindSystemPath(context)
            ?? GetNewSystemPath(context);
        if (!File.Exists(path))
        {
            return new LegacySystemDataFileLoadResult(
                path,
                false,
                null,
                null);
        }

        var readResult = await ReadObjectAsync(path, cancellationToken)
            .ConfigureAwait(false);
        return new LegacySystemDataFileLoadResult(
            path,
            true,
            readResult.Root,
            readResult.Error);
    }

    public async Task<string> UpdateAsync(
        LegacySystemDataFileContext context,
        Action<JsonObject> update,
        CancellationToken cancellationToken = default)
    {
        ValidateContext(context);
        ArgumentNullException.ThrowIfNull(update);
        return await ExecuteProfileWriteAsync(
                context.FrontierId,
                async token =>
                {
                    var path = FindSystemPath(context)
                        ?? GetNewSystemPath(context);
                    var updateLock = UpdateLocks.GetOrAdd(
                        Path.GetFullPath(path),
                        static _ => new SemaphoreSlim(1, 1));
                    await updateLock.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        JsonObject root;
                        if (File.Exists(path))
                        {
                            var readResult = await ReadObjectAsync(path, token)
                                .ConfigureAwait(false);
                            root = readResult.Root
                                ?? throw new InvalidDataException(
                                    "The system data file is malformed and was not overwritten: "
                                        + readResult.Error);
                        }
                        else
                        {
                            root = CreateSystemData(context);
                        }

                        update(root);
                        await WriteObjectAsync(path, root, token)
                            .ConfigureAwait(false);
                        return path;
                    }
                    finally
                    {
                        updateLock.Release();
                    }
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<LegacySystemDataFileMutationResult<T>>
        UpdateWithResultAsync<T>(
            LegacySystemDataFileContext context,
            Func<JsonObject, T> update,
            CancellationToken cancellationToken = default)
    {
        ValidateContext(context);
        ArgumentNullException.ThrowIfNull(update);
        return await ExecuteProfileWriteAsync(
                context.FrontierId,
                async token =>
                {
                    var path = FindSystemPath(context)
                        ?? GetNewSystemPath(context);
                    var updateLock = UpdateLocks.GetOrAdd(
                        Path.GetFullPath(path),
                        static _ => new SemaphoreSlim(1, 1));
                    await updateLock.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        JsonObject root;
                        if (File.Exists(path))
                        {
                            var readResult = await ReadObjectAsync(path, token)
                                .ConfigureAwait(false);
                            root = readResult.Root
                                ?? throw new InvalidDataException(
                                    "The system data file is malformed and was not overwritten: "
                                        + readResult.Error);
                        }
                        else
                        {
                            root = CreateSystemData(context);
                        }

                        var result = update(root);
                        await WriteObjectAsync(path, root, token)
                            .ConfigureAwait(false);
                        return new LegacySystemDataFileMutationResult<T>(
                            path,
                            result);
                    }
                    finally
                    {
                        updateLock.Release();
                    }
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<LegacySystemDataFileUpdateResult> UpdateExistingAsync(
        LegacySystemDataFileContext context,
        Func<JsonObject, bool> update,
        CancellationToken cancellationToken = default)
    {
        ValidateContext(context);
        ArgumentNullException.ThrowIfNull(update);
        return await ExecuteProfileWriteAsync(
                context.FrontierId,
                async token =>
                {
                    var path = FindSystemPath(context)
                        ?? GetNewSystemPath(context);
                    var updateLock = UpdateLocks.GetOrAdd(
                        Path.GetFullPath(path),
                        static _ => new SemaphoreSlim(1, 1));
                    await updateLock.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        if (!File.Exists(path))
                        {
                            return new LegacySystemDataFileUpdateResult(
                                path,
                                false,
                                false,
                                null);
                        }

                        var readResult = await ReadObjectAsync(path, token)
                            .ConfigureAwait(false);
                        if (readResult.Root is null)
                        {
                            return new LegacySystemDataFileUpdateResult(
                                path,
                                true,
                                false,
                                readResult.Error);
                        }

                        var changed = update(readResult.Root);
                        if (changed)
                        {
                            await WriteObjectAsync(
                                    path,
                                    readResult.Root,
                                    token)
                                .ConfigureAwait(false);
                        }

                        return new LegacySystemDataFileUpdateResult(
                            path,
                            true,
                            changed,
                            null);
                    }
                    finally
                    {
                        updateLock.Release();
                    }
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal async Task<T> ExecuteProfileWriteAsync<T>(
        string frontierId,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ValidateFrontierId(frontierId);
        ArgumentNullException.ThrowIfNull(operation);
        var key = Path.GetFullPath(Path.Combine(
            dataDirectory,
            "systems",
            frontierId));
        var profileLock = ProfileWriteLocks.GetOrAdd(
            key,
            static _ => new SemaphoreSlim(1, 1));
        await profileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await operation(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            profileLock.Release();
        }
    }

    public static string MakeSafeFileName(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value
            .Replace('\\', '-')
            .Replace('/', '-')
            .Replace(':', '-')
            .Replace('*', '-')
            .Replace('?', '-')
            .Replace('"', '-')
            .Replace('<', '-')
            .Replace('>', '-')
            .Replace('|', '-');
    }

    private static JsonObject CreateSystemData(
        LegacySystemDataFileContext context)
    {
        var root = new JsonObject
        {
            ["name"] = context.SystemName,
            ["address"] = context.SystemAddress,
            ["bodies"] = new JsonArray(),
        };
        if (!string.IsNullOrWhiteSpace(context.CommanderName))
        {
            root["commander"] = context.CommanderName;
        }

        if (context.StarPosition is { } position)
        {
            root["starPos"] = new JsonArray(position.X, position.Y, position.Z);
        }

        return root;
    }

    private string? FindSystemPath(LegacySystemDataFileContext context)
    {
        var directory = GetSystemDirectory(context.FrontierId);
        if (!Directory.Exists(directory))
        {
            return null;
        }

        var paths = Directory.EnumerateFiles(
                directory,
                "*.json",
                SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var addressSuffix = $"_{context.SystemAddress}.json";
        var addressMatch = paths.FirstOrDefault(path =>
            Path.GetFileName(path).EndsWith(
                addressSuffix,
                StringComparison.OrdinalIgnoreCase));
        if (addressMatch is not null)
        {
            return addressMatch;
        }

        var namePrefix = $"{MakeSafeFileName(context.SystemName)}_";
        return paths.FirstOrDefault(path =>
            Path.GetFileName(path).StartsWith(
                namePrefix,
                StringComparison.OrdinalIgnoreCase));
    }

    private string GetNewSystemPath(LegacySystemDataFileContext context)
    {
        return Path.Combine(
            GetSystemDirectory(context.FrontierId),
            MakeSafeFileName($"{context.SystemName}_{context.SystemAddress}.json"));
    }

    private string GetSystemDirectory(string frontierId)
    {
        return Path.Combine(dataDirectory, "systems", frontierId);
    }

    private static void ValidateContext(LegacySystemDataFileContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.SystemName);
        ValidateFrontierId(context.FrontierId);
    }

    private static void ValidateFrontierId(string frontierId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(frontierId);
        if (frontierId is "." or ".."
            || !string.Equals(
                Path.GetFileName(frontierId),
                frontierId,
                StringComparison.Ordinal)
            || frontierId.IndexOfAny(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
        {
            throw new ArgumentException(
                "The Frontier ID must be a folder name, not a path.",
                nameof(frontierId));
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
                $"The system data path has no parent directory: {path}");
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

    private sealed record JsonObjectReadResult(JsonObject? Root, string? Error);
}

internal sealed record LegacySystemDataFileContext(
    string FrontierId,
    string? CommanderName,
    string SystemName,
    long SystemAddress,
    GalacticCoordinate? StarPosition);

internal sealed record LegacySystemDataFileLoadResult(
    string Path,
    bool Exists,
    JsonObject? Root,
    string? Error);

internal sealed record LegacySystemDataFileUpdateResult(
    string Path,
    bool Exists,
    bool Changed,
    string? Error);

internal sealed record LegacySystemDataFileMutationResult<T>(
    string Path,
    T Value);
