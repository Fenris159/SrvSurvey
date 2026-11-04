using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SrvSurvey.Desktop.Platform;

public sealed partial class ScreenshotProcessingService
{
    private readonly string? pendingQueueDirectory;
    private readonly List<string> recoveryWarnings = [];
    private int recoveryWarningCount;

    private sealed record PublishedImage(string Path, string Hash);

    private sealed record StoredCapture(
        int Version,
        string Key,
        ScreenshotConversionRequest Request,
        SourceFingerprint? Source,
        SourceFingerprint? Undecodable,
        ScreenshotConversion? CompletedConversion,
        IReadOnlyList<PublishedImage> Images
    );

    private void RecoverPendingCaptures()
    {
        if (pendingQueueDirectory is null || !Directory.Exists(pendingQueueDirectory))
        {
            return;
        }
        try
        {
            string completedDirectory = Path.Combine(pendingQueueDirectory, "steam-completed");
            RecoverCompletionArchive(completedDirectory);
            foreach (string path in enumerateRecoveryFiles(pendingQueueDirectory))
            {
                RecoverCapture(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            recoveryWarnings.Add("Pending screenshots could not be recovered: " + exception.Message);
        }
        pendingCount = pending.Count;
        recoveryWarningCount = recoveryWarnings.Count;
    }

    private void RecoverCompletionArchive(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                foreach (string path in enumerateRecoveryFiles(directory))
                {
                    RecoverCapture(path, isCompleted: true);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            recoveryWarnings.Add("Completed screenshot history could not be recovered: " + exception.Message);
        }
    }

    private void RecoverCapture(string path, bool isCompleted = false)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            StoredCapture saved =
                JsonSerializer.Deserialize<StoredCapture>(stream)
                ?? throw new InvalidDataException("The pending screenshot record is empty.");
            ValidateStoredCapture(saved);
            if (isCompleted)
            {
                if (!IsSteamCapture(saved.Request.Entry) || saved.CompletedConversion is null)
                {
                    throw new InvalidDataException("The completed Steam screenshot record is invalid.");
                }
                completed.Add(saved.Key);
                return;
            }
            if (completed.Contains(saved.Key))
            {
                return;
            }
            var capture = new PendingScreenshot(saved.Request)
            {
                Source = saved.Source,
                Undecodable = saved.Undecodable,
                CompletedConversion = saved.CompletedConversion,
            };
            capture.PublishedImages.AddRange(saved.Images);
            pending.TryAdd(saved.Key, capture);
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or JsonException
                        or ArgumentException
                        or InvalidDataException
            )
        {
            recoveryWarnings.Add(
                $"A pending screenshot record could not be recovered from '{path}': {exception.Message}"
            );
        }
    }

    private static void ValidateStoredCapture(StoredCapture saved)
    {
        if (
            saved.Version != 1
            || string.IsNullOrWhiteSpace(saved.Key)
            || saved.Request.Entry is not { EventName: "Screenshot" }
            || saved.Request.Entry.Payload.ValueKind != JsonValueKind.Object
            || saved.Request.Preferences is null
            || saved.Request.SourcePaths is not { Count: > 0 }
            || saved.Request.SourcePaths.Any(path => !Path.IsPathFullyQualified(path))
            || !Path.IsPathFullyQualified(saved.Request.TargetDirectory)
            || saved.Images is null
        )
        {
            throw new InvalidDataException("The pending screenshot record is invalid.");
        }
        if (
            saved.CompletedConversion is { } conversion
            && (
                saved.Images.Count == 0
                || !saved.Images.Any(image => image is not null && image.Path == conversion.OutputPath)
                || (
                    conversion.AerialOutputPath is { } aerial
                    && !saved.Images.Any(image => image is not null && image.Path == aerial)
                )
                || saved.Images.Any(image =>
                    image is null || !Path.IsPathFullyQualified(image.Path) || image.Hash?.Length != 64
                )
            )
        )
        {
            throw new InvalidDataException("The pending screenshot output verification is invalid.");
        }
    }

    private string GetCapturePath(string key) =>
        Path.Combine(
            pendingQueueDirectory!,
            System.Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".json"
        );

    private void SavePendingCapture(string key, PendingScreenshot capture)
    {
        if (pendingQueueDirectory is null)
        {
            return;
        }
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(pendingQueueDirectory);
        }
        else
        {
            Directory.CreateDirectory(
                pendingQueueDirectory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
        }
        string path = GetCapturePath(key);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
            };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }
            using (var stream = new FileStream(temporary, options))
            {
                JsonSerializer.Serialize(
                    stream,
                    new StoredCapture(
                        1,
                        key,
                        capture.Request,
                        capture.Source,
                        capture.Undecodable,
                        capture.CompletedConversion,
                        capture.PublishedImages
                    )
                );
                stream.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private void RemovePendingCapture(string key, PendingScreenshot capture, List<string> warnings)
    {
        if (pendingQueueDirectory is null)
        {
            return;
        }
        try
        {
            string path = GetCapturePath(key);
            if (IsSteamCapture(capture.Request.Entry) && capture.CompletedConversion is not null)
            {
                string directory = Path.Combine(pendingQueueDirectory, "steam-completed");
                Directory.CreateDirectory(directory);
                File.Move(path, Path.Combine(directory, Path.GetFileName(path)), overwrite: true);
            }
            else
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            warnings.Add("The converted screenshot's recovery record could not be removed: " + exception.Message);
        }
    }

    private ScreenshotConversion FinishConversion(string key, PendingScreenshot capture)
    {
        ScreenshotConversion conversion = capture.CompletedConversion!;
        // Publish a verified completion before deleting the source. A restart can
        // finish cleanup without requiring the original file or duplicating a PNG.
        SavePendingCapture(key, capture);
        if (capture.Request.Preferences.DeleteOriginal && conversion.Warning is null)
        {
            conversion = RemoveOriginal(capture, conversion);
        }
        if (capture.Source is { } source)
        {
            completedSources[conversion.SourcePath] = new CompletedSource(source, capture.Request.Entry.Timestamp);
        }
        return conversion;
    }

    private static ScreenshotConversion RemoveOriginal(PendingScreenshot capture, ScreenshotConversion conversion)
    {
        try
        {
            if (capture.Source is null)
            {
                return conversion with
                {
                    Warning =
                        "The converted screenshot was saved, but the original image could not be verified and was retained.",
                };
            }
            SourceFingerprint? current = ReadFingerprint(conversion.SourcePath);
            if (current is not null && !SameFingerprint(current, capture.Source))
            {
                return conversion with
                {
                    Warning = "The converted screenshot was saved, but the changed original image was retained.",
                };
            }
            File.Delete(conversion.SourcePath);
            return conversion with { SourceDeleted = true };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return conversion with
            {
                Warning =
                    $"Saved '{conversion.OutputPath}', but the original image could not be removed: {exception.Message}",
            };
        }
    }

    private static bool SameFingerprint(SourceFingerprint? first, SourceFingerprint? second) =>
        first is not null
        && second is not null
        && first.Length == second.Length
        && first.LastWriteTimeUtc == second.LastWriteTimeUtc
        && string.Equals(
            first.Path,
            second.Path,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal
        );

    private static string HashFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return System.Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool ImageStillValid(PublishedImage image)
    {
        try
        {
            return File.Exists(image.Path) && HashFile(image.Path) == image.Hash;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
