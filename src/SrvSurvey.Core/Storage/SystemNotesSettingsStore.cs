using System.Text.Json;
using System.Text.Json.Nodes;

namespace SrvSurvey.Core.Storage;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The store is application-scoped and its semaphore may still have in-flight waiters.")]
public sealed class SystemNotesSettingsStore(string dataDirectory)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly SemaphoreSlim saveLock = new(1, 1);

    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetFullPath(dataDirectory),
        "settings.json");

    public SystemNotesSettingsLoadResult Load()
    {
        if (!File.Exists(Path))
        {
            return new SystemNotesSettingsLoadResult(
                Path,
                false,
                SystemNotesSettingsSnapshot.Default,
                null);
        }

        try
        {
            using var stream = new FileStream(
                Path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var root = JsonNode.Parse(stream) as JsonObject;
            if (root is null)
            {
                return new SystemNotesSettingsLoadResult(
                    Path,
                    true,
                    null,
                    $"{Path} does not contain a JSON object.");
            }

            return new SystemNotesSettingsLoadResult(
                Path,
                true,
                new SystemNotesSettingsSnapshot(
                    GetBoolean(root, "systemNotesTopMost") ?? false,
                    GetString(root, "screenshotTargetFolder"),
                    GetBoolean(root, "viewJourneyTopMost") ?? false,
                    GetBoolean(root, "viewJourneyGalacticTime") ?? false),
                null);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException)
        {
            return new SystemNotesSettingsLoadResult(
                Path,
                true,
                null,
                $"Could not read {Path}: {exception.Message}");
        }
    }

    public async Task SaveAlwaysOnTopAsync(
        bool alwaysOnTop,
        CancellationToken cancellationToken = default)
    {
        await SaveSettingsAsync(
            root => root["systemNotesTopMost"] = alwaysOnTop,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveJourneyPreferencesAsync(
        bool alwaysOnTop,
        bool useGalacticTime,
        CancellationToken cancellationToken = default)
    {
        await SaveSettingsAsync(
            root =>
            {
                root["viewJourneyTopMost"] = alwaysOnTop;
                root["viewJourneyGalacticTime"] = useGalacticTime;
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task SaveSettingsAsync(
        Action<JsonObject> update,
        CancellationToken cancellationToken)
    {
        await saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            JsonObject root;
            if (File.Exists(Path))
            {
                try
                {
                    await using var stream = new FileStream(
                        Path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete,
                        16 * 1024,
                        FileOptions.Asynchronous | FileOptions.SequentialScan);
                    root = await JsonNode.ParseAsync(
                            stream,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false) as JsonObject
                        ?? throw new InvalidDataException(
                            "The settings file is malformed and was not overwritten: "
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

            update(root);
            await WriteObjectAsync(root, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            saveLock.Release();
        }
    }

    public string? GetImagesDirectory(string systemName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemName);
        var result = Load();
        if (!result.IsSuccess
            || string.IsNullOrWhiteSpace(result.Snapshot!.ScreenshotTargetFolder))
        {
            return null;
        }

        return System.IO.Path.Combine(
            result.Snapshot.ScreenshotTargetFolder,
            SystemNoteStore.MakeSafeFileName(systemName));
    }

    private async Task WriteObjectAsync(
        JsonObject root,
        CancellationToken cancellationToken)
    {
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

    private static bool? GetBoolean(JsonObject root, string propertyName)
    {
        return root[propertyName] is JsonValue value
            && value.TryGetValue<bool>(out var result)
                ? result
                : null;
    }

    private static string? GetString(JsonObject root, string propertyName)
    {
        return root[propertyName] is JsonValue value
            && value.TryGetValue<string>(out var result)
                ? result
                : null;
    }
}

public sealed record SystemNotesSettingsSnapshot(
    bool AlwaysOnTop,
    string? ScreenshotTargetFolder,
    bool JourneyAlwaysOnTop = false,
    bool JourneyUseGalacticTime = false)
{
    public static SystemNotesSettingsSnapshot Default { get; } = new(
        false,
        null,
        false,
        false);
}

public sealed record SystemNotesSettingsLoadResult(
    string Path,
    bool Exists,
    SystemNotesSettingsSnapshot? Snapshot,
    string? Error)
{
    public bool IsSuccess => Snapshot is not null;
}
