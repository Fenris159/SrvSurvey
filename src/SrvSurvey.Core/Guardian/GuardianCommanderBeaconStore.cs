using System.Buffers;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SrvSurvey.Core.Guardian;

public sealed class GuardianCommanderBeaconStore(string dataDirectory) : IDisposable
{
    private static readonly char[] CrossPlatformInvalidFileNameCharacters =
        ['<', '>', ':', '"', '/', '\\', '|', '?', '*', '\0'];
    private static readonly SearchValues<char> PathSeparators =
        SearchValues.Create(
        [
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar,
        ]);
    private static readonly SearchValues<char> InvalidFileNameCharacters =
        SearchValues.Create(CrossPlatformInvalidFileNameCharacters);
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };
    private readonly SemaphoreSlim saveLock = new(1, 1);
    private readonly string dataDirectory = Path.GetFullPath(dataDirectory);
    private bool disposed;

    public string GetBeaconPath(
        string frontierId,
        bool isOdyssey,
        string systemName)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ValidateFileName(frontierId, "Frontier ID");
        ValidateFileName(systemName, "system name");
        var folder = Path.Combine(dataDirectory, "guardian", frontierId);
        if (!isOdyssey)
        {
            folder = Path.Combine(folder, "legacy");
        }

        return Path.Combine(folder, $"{systemName}-beacon.json");
    }

    public async Task<string> SaveAsync(
        string frontierId,
        bool isOdyssey,
        GuardianCommanderBeaconVisit beacon,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(beacon);
        var path = GetBeaconPath(frontierId, isOdyssey, beacon.SystemName);
        await saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            JsonObject root;
            if (File.Exists(path))
            {
                try
                {
                    root = await ReadExistingAsync(path, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (InvalidDataException)
                {
                    TryArchiveCorruptFile(path);
                    root = new JsonObject();
                }
            }
            else
            {
                root = new JsonObject();
            }

            root["firstVisited"] = beacon.FirstVisited;
            root["lastVisited"] = beacon.LastVisited;
            root["systemName"] = beacon.SystemName;
            root["systemAddress"] = beacon.SystemAddress;
            root["bodyName"] = beacon.BodyName;
            root["bodyId"] = beacon.BodyId;
            root["notes"] = beacon.Notes;
            root["legacy"] = !isOdyssey;
            root["scannedLocations"] = WriteLocations(beacon.ScannedLocations);
            await WriteAtomicAsync(path, root, cancellationToken)
                .ConfigureAwait(false);
            return path;
        }
        finally
        {
            saveLock.Release();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        saveLock.Dispose();
    }

    private static JsonObject WriteLocations(
        IReadOnlyDictionary<DateTimeOffset, GuardianSurfaceLocation> locations)
    {
        var root = new JsonObject();
        foreach (var pair in locations.OrderBy(pair => pair.Key))
        {
            root[pair.Key.ToString("O", CultureInfo.InvariantCulture)] =
                new JsonObject
                {
                    ["lat"] = pair.Value.Latitude,
                    ["long"] = pair.Value.Longitude,
                };
        }

        return root;
    }

    private static async Task<JsonObject> ReadExistingAsync(
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
            return node as JsonObject
                ?? throw new InvalidDataException(
                    $"The Guardian beacon is not a JSON object and was not overwritten: {path}");
        }
        catch (Exception exception) when (
            exception is JsonException
                or IOException
                or UnauthorizedAccessException)
        {
            throw new InvalidDataException(
                $"The Guardian beacon is malformed and was not overwritten: {path}",
                exception);
        }
    }

    private static async Task WriteAtomicAsync(
        string path,
        JsonObject root,
        CancellationToken cancellationToken)
    {
        var folder = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException(
                "The Guardian beacon path has no parent folder.");
        Directory.CreateDirectory(folder);
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

    private static void TryArchiveCorruptFile(
        string path)
    {
        var folder = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException(
                "The Guardian beacon path has no parent folder.");
        var corruptPath = Path.Combine(
            folder,
            $"{Path.GetFileName(path)}.{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.corrupt.json");
        try
        {
            if (File.Exists(path))
            {
                File.Move(path, corruptPath, true);
            }
        }
        catch (Exception)
        {
            // If archival fails, continue with a fresh object and attempt to overwrite.
            // The malformed payload will be replaced with a fresh object during write.
        }
    }

    private static void ValidateFileName(string value, string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value is "." or ".."
            || !string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal)
            || value.AsSpan().ContainsAny(PathSeparators)
            || value.AsSpan().ContainsAny(InvalidFileNameCharacters))
        {
            throw new ArgumentException(
                $"The {label} must be a valid folder or file name.",
                nameof(value));
        }
    }
}
