using System.Text.Json;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Storage;

public sealed class SystemNoteStore(string dataDirectory)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string dataDirectory = GetFullPath(dataDirectory);
    private readonly SemaphoreSlim saveLock = new(1, 1);

    public async Task<SystemNoteLoadResult> LoadAsync(
        string frontierId,
        string systemName,
        long systemAddress,
        CancellationToken cancellationToken = default)
    {
        ValidateContext(frontierId, systemName);
        var path = FindSystemPath(frontierId, systemName, systemAddress)
            ?? GetNewSystemPath(frontierId, systemName, systemAddress);
        if (!File.Exists(path))
        {
            return new SystemNoteLoadResult(path, false, string.Empty, null);
        }

        var readResult = await ReadObjectAsync(path, cancellationToken)
            .ConfigureAwait(false);
        if (readResult.Root is null)
        {
            return new SystemNoteLoadResult(path, true, null, readResult.Error);
        }

        return new SystemNoteLoadResult(
            path,
            true,
            GetString(readResult.Root, "notes") ?? string.Empty,
            null);
    }

    public async Task<string> SaveAsync(
        SystemNoteContext context,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ValidateContext(context.FrontierId, context.SystemName);
        await saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = FindSystemPath(
                    context.FrontierId,
                    context.SystemName,
                    context.SystemAddress)
                ?? GetNewSystemPath(
                    context.FrontierId,
                    context.SystemName,
                    context.SystemAddress);
            JsonObject root;
            if (File.Exists(path))
            {
                var readResult = await ReadObjectAsync(path, cancellationToken)
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

            root["notes"] = notes ?? string.Empty;
            await WriteObjectAsync(path, root, cancellationToken).ConfigureAwait(false);
            return path;
        }
        finally
        {
            saveLock.Release();
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

    private static JsonObject CreateSystemData(SystemNoteContext context)
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

    private string? FindSystemPath(
        string frontierId,
        string systemName,
        long systemAddress)
    {
        var directory = GetSystemDirectory(frontierId);
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
        var addressSuffix = $"_{systemAddress}.json";
        var addressMatch = paths.FirstOrDefault(path =>
            Path.GetFileName(path).EndsWith(
                addressSuffix,
                StringComparison.OrdinalIgnoreCase));
        if (addressMatch is not null)
        {
            return addressMatch;
        }

        var namePrefix = $"{MakeSafeFileName(systemName)}_";
        return paths.FirstOrDefault(path =>
            Path.GetFileName(path).StartsWith(
                namePrefix,
                StringComparison.OrdinalIgnoreCase));
    }

    private string GetNewSystemPath(
        string frontierId,
        string systemName,
        long systemAddress)
    {
        return Path.Combine(
            GetSystemDirectory(frontierId),
            MakeSafeFileName($"{systemName}_{systemAddress}.json"));
    }

    private string GetSystemDirectory(string frontierId)
    {
        return Path.Combine(dataDirectory, "systems", frontierId);
    }

    private static void ValidateContext(string frontierId, string systemName)
    {
        ValidateFrontierId(frontierId);
        ArgumentException.ThrowIfNullOrWhiteSpace(systemName);
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

public sealed record SystemNoteContext(
    string FrontierId,
    string? CommanderName,
    string SystemName,
    long SystemAddress,
    GalacticCoordinate? StarPosition);

public sealed record SystemNoteLoadResult(
    string Path,
    bool Exists,
    string? Notes,
    string? Error)
{
    public bool IsSuccess => Notes is not null;
}
