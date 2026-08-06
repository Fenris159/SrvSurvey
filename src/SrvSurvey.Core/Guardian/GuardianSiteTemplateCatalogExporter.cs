using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SrvSurvey.Core.Guardian;

public sealed class GuardianSiteTemplateCatalogExporter
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim>
        PathLocks = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly Func<string, Task>? beforeActivation;

    public GuardianSiteTemplateCatalogExporter()
    {
    }

    internal GuardianSiteTemplateCatalogExporter(
        Func<string, Task> beforeActivation)
    {
        this.beforeActivation = beforeActivation
            ?? throw new ArgumentNullException(nameof(beforeActivation));
    }

    public async Task<GuardianSiteTemplateExportResult> ExportAsync(
        GuardianSiteTemplateCatalog catalog,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var targetPath = Path.GetFullPath(path);
        if (string.IsNullOrWhiteSpace(Path.GetFileName(targetPath)))
        {
            throw new ArgumentException(
                "A Guardian template export file is required.",
                nameof(path));
        }

        var pathLock = PathLocks.GetOrAdd(
            targetPath,
            _ => new SemaphoreSlim(1, 1));
        await pathLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ExportLockedAsync(
                catalog,
                targetPath,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            pathLock.Release();
        }
    }

    private async Task<GuardianSiteTemplateExportResult> ExportLockedAsync(
        GuardianSiteTemplateCatalog catalog,
        string targetPath,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException(
                "The Guardian template export has no parent directory.");
        Directory.CreateDirectory(directory);
        RejectReparsePoint(targetPath);
        var original = await CaptureAsync(targetPath, cancellationToken)
            .ConfigureAwait(false);
        var payload = Serialize(catalog);
        var expectedHash = Convert.ToHexString(SHA256.HashData(payload));
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        string? backupPath = null;
        var activated = false;
        try
        {
            await WriteNewFileAsync(
                temporaryPath,
                payload,
                cancellationToken).ConfigureAwait(false);
            await ValidateFileAsync(
                temporaryPath,
                expectedHash,
                catalog,
                cancellationToken).ConfigureAwait(false);
            EnsureUnchanged(
                original,
                await CaptureAsync(targetPath, cancellationToken)
                    .ConfigureAwait(false));
            if (original is not null)
            {
                backupPath = CreateBackupPath(targetPath);
                File.Copy(targetPath, backupPath, overwrite: false);
                EnsureUnchanged(
                    original,
                    await CaptureAsync(backupPath, cancellationToken)
                        .ConfigureAwait(false));
            }

            if (beforeActivation is not null)
            {
                await beforeActivation(targetPath).ConfigureAwait(false);
            }

            EnsureUnchanged(
                original,
                await CaptureAsync(targetPath, cancellationToken)
                    .ConfigureAwait(false));
            File.Move(temporaryPath, targetPath, overwrite: true);
            activated = true;
            await ValidateFileAsync(
                targetPath,
                expectedHash,
                catalog,
                cancellationToken).ConfigureAwait(false);
            return new GuardianSiteTemplateExportResult(
                targetPath,
                backupPath,
                catalog.Count,
                payload.LongLength,
                expectedHash);
        }
        catch
        {
            if (activated)
            {
                await RestoreAsync(
                    targetPath,
                    backupPath,
                    original,
                    cancellationToken).ConfigureAwait(false);
            }

            throw;
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static byte[] Serialize(GuardianSiteTemplateCatalog catalog)
    {
        var root = new JsonObject();
        foreach (var template in catalog.Templates.OrderBy(
                     template => template.SiteType,
                     StringComparer.OrdinalIgnoreCase))
        {
            var row = new JsonObject
            {
                ["name"] = template.Name,
                ["backgroundImage"] = template.BackgroundImage,
                ["imageOffset"] = string.Create(CultureInfo.InvariantCulture,
                    $"{template.ImageOffset.X}, {template.ImageOffset.Y}"),
                ["scaleFactor"] = template.ScaleFactor,
                ["poi"] = WritePoints(template.PointsOfInterest),
                ["obeliskGroupNameLocations"] = WriteGroups(
                    template.ObeliskGroupNameLocations),
                ["destructablePanels"] = WritePoints(
                    template.DestructiblePanels),
            };
            root[template.SiteType] = row;
        }

        return JsonSerializer.SerializeToUtf8Bytes(root, JsonOptions);
    }

    private static JsonArray WritePoints(
        IEnumerable<GuardianPointOfInterest> points)
    {
        var array = new JsonArray();
        foreach (var point in points.OrderBy(
                     point => point.Name,
                     StringComparer.OrdinalIgnoreCase))
        {
            array.Add(new JsonObject
            {
                ["name"] = point.Name,
                ["type"] = WriteType(point.Type),
                ["angle"] = point.Angle,
                ["dist"] = point.Distance,
                ["rot"] = point.Rotation,
            });
        }

        return array;
    }

    private static JsonObject WriteGroups(
        IReadOnlyDictionary<string, GuardianMapPoint> groups)
    {
        var result = new JsonObject();
        foreach (var group in groups.OrderBy(
                     pair => pair.Key,
                     StringComparer.OrdinalIgnoreCase))
        {
            result[group.Key] = new JsonObject
            {
                ["IsEmpty"] = group.Value.X == 0 && group.Value.Y == 0,
                ["X"] = group.Value.X,
                ["Y"] = group.Value.Y,
            };
        }

        return result;
    }

    private static string WriteType(GuardianPoiType type)
    {
        return type switch
        {
            GuardianPoiType.BrokenObelisk => "brokeObelisk",
            GuardianPoiType.DestructiblePanel => "destructablePanel",
            _ => type.ToString().ToLowerInvariant(),
        };
    }

    private static async Task WriteNewFileAsync(
        string path,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(payload, cancellationToken)
            .ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private static async Task ValidateFileAsync(
        string path,
        string expectedHash,
        GuardianSiteTemplateCatalog expected,
        CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken)
            .ConfigureAwait(false);
        var actualHash = Convert.ToHexString(SHA256.HashData(bytes));
        if (!string.Equals(actualHash, expectedHash, StringComparison.Ordinal))
        {
            throw new IOException(
                "The Guardian template export checksum did not match the staged data.");
        }

        await using var stream = new MemoryStream(bytes, writable: false);
        var loaded = GuardianSiteTemplateCatalog.Load(stream);
        if (loaded.Count != expected.Count)
        {
            throw new InvalidDataException(
                "The Guardian template export did not retain every catalog entry.");
        }

        foreach (var template in expected.Templates)
        {
            var roundTrip = loaded.Find(template.SiteType)
                ?? throw new InvalidDataException(
                    $"The Guardian template export lost '{template.SiteType}'.");
            if (roundTrip.PointsOfInterest.Count != template.PointsOfInterest.Count
                || roundTrip.DestructiblePanels.Count
                    != template.DestructiblePanels.Count
                || roundTrip.ObeliskGroupNameLocations.Count
                    != template.ObeliskGroupNameLocations.Count)
            {
                throw new InvalidDataException(
                    $"The Guardian template export changed the geometry for '{template.SiteType}'.");
            }
        }
    }

    private static async Task<FileFingerprint?> CaptureAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        RejectReparsePoint(path);
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken)
            .ConfigureAwait(false);
        return new FileFingerprint(
            bytes.LongLength,
            Convert.ToHexString(SHA256.HashData(bytes)));
    }

    private static void EnsureUnchanged(
        FileFingerprint? expected,
        FileFingerprint? actual)
    {
        if (expected != actual)
        {
            throw new IOException(
                "The Guardian template destination changed during export. The newer file was not overwritten.");
        }
    }

    private static async Task RestoreAsync(
        string targetPath,
        string? backupPath,
        FileFingerprint? original,
        CancellationToken cancellationToken)
    {
        if (original is null)
        {
            TryDelete(targetPath);
            return;
        }

        if (backupPath is null || !File.Exists(backupPath))
        {
            throw new IOException(
                "The Guardian template export failed and its verified backup is unavailable.");
        }

        File.Copy(backupPath, targetPath, overwrite: true);
        EnsureUnchanged(
            original,
            await CaptureAsync(targetPath, cancellationToken)
                .ConfigureAwait(false));
    }

    private static string CreateBackupPath(string targetPath)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString(
            "yyyyMMdd-HHmmssfff",
            CultureInfo.InvariantCulture);
        var candidate = $"{targetPath}.backup-{timestamp}";
        var suffix = 1;
        while (File.Exists(candidate))
        {
            candidate = $"{targetPath}.backup-{timestamp}-{suffix++}";
        }

        return candidate;
    }

    private static void RejectReparsePoint(string path)
    {
        if (File.Exists(path)
            && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new IOException(
                "Guardian template exports cannot replace a symbolic link or reparse point.");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // Cleanup is best effort; a retained temporary file is harmless.
        }
    }

    private sealed record FileFingerprint(long Size, string Sha256);
}

public sealed record GuardianSiteTemplateExportResult(
    string Path,
    string? BackupPath,
    int TemplateCount,
    long Size,
    string Sha256);
