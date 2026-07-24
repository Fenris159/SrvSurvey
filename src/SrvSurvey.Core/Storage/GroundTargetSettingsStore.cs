using System.Text.Json;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Navigation;

namespace SrvSurvey.Core.Storage;

public sealed class GroundTargetSettingsStore(string dataDirectory)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly SemaphoreSlim saveLock = new(1, 1);

    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetFullPath(dataDirectory),
        "settings.json");

    public GroundTargetSettingsLoadResult Load()
    {
        if (!File.Exists(Path))
        {
            return new GroundTargetSettingsLoadResult(
                Path,
                false,
                GroundTargetSnapshot.Empty,
                null);
        }

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(Path)) as JsonObject;
            if (root is null)
            {
                return new GroundTargetSettingsLoadResult(
                    Path,
                    true,
                    null,
                    $"{Path} does not contain a JSON object.");
            }

            var active = GetBoolean(root, "targetLatLongActive") ?? false;
            var coordinate = root["targetLatLong"] as JsonObject;
            var latitude = coordinate is null ? 0 : GetDouble(coordinate, "lat") ?? 0;
            var longitude = coordinate is null ? 0 : GetDouble(coordinate, "long") ?? 0;
            try
            {
                return new GroundTargetSettingsLoadResult(
                    Path,
                    true,
                    new GroundTargetSnapshot(
                        active,
                        new SurfaceCoordinate(latitude, longitude)),
                    null);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                return new GroundTargetSettingsLoadResult(
                    Path,
                    true,
                    null,
                    $"The saved ground target is invalid: {exception.Message}");
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException)
        {
            return new GroundTargetSettingsLoadResult(
                Path,
                true,
                null,
                $"Could not read {Path}: {exception.Message}");
        }
    }

    public async Task SaveAsync(
        GroundTargetSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        await saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            JsonObject root;
            if (File.Exists(Path))
            {
                try
                {
                    root = JsonNode.Parse(await File.ReadAllTextAsync(
                            Path,
                            cancellationToken).ConfigureAwait(false)) as JsonObject
                        ?? throw new InvalidDataException(
                            $"The settings file is malformed and was not overwritten: "
                                + $"{Path} does not contain a JSON object.");
                }
                catch (JsonException exception)
                {
                    throw new InvalidDataException(
                        "The settings file is malformed and was not overwritten: "
                            + exception.Message,
                        exception);
                }
            }
            else
            {
                root = [];
            }

            if (root["targetLatLong"] is not JsonObject coordinate)
            {
                coordinate = [];
                root["targetLatLong"] = coordinate;
            }

            coordinate["lat"] = snapshot.Target.Latitude;
            coordinate["long"] = snapshot.Target.Longitude;
            root["targetLatLongActive"] = snapshot.IsActive;

            var directory = System.IO.Path.GetDirectoryName(Path)
                ?? throw new InvalidOperationException(
                    $"The settings path has no parent directory: {Path}");
            Directory.CreateDirectory(directory);
            var temporaryPath = $"{Path}.{Guid.NewGuid():N}.tmp";
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

                File.Move(temporaryPath, Path, true);
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

    private static bool? GetBoolean(JsonObject root, string propertyName)
    {
        return root[propertyName] is JsonValue value
            && value.TryGetValue<bool>(out var result)
                ? result
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

        return value.TryGetValue<long>(out var integer) ? integer : null;
    }
}

public sealed record GroundTargetSettingsLoadResult(
    string Path,
    bool Exists,
    GroundTargetSnapshot? Snapshot,
    string? Error)
{
    public bool IsSuccess => Snapshot is not null;
}
