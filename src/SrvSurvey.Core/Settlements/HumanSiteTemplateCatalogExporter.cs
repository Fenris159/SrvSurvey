using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SrvSurvey.Core.Settlements;

public sealed class HumanSiteTemplateCatalogExporter
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim>
        PathLocks = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly Func<string, Task>? beforeActivation;

    public HumanSiteTemplateCatalogExporter()
    {
    }

    internal HumanSiteTemplateCatalogExporter(
        Func<string, Task> beforeActivation)
    {
        this.beforeActivation = beforeActivation
            ?? throw new ArgumentNullException(nameof(beforeActivation));
    }

    public async Task<HumanSiteTemplateExportResult> ExportAsync(
        HumanSiteTemplateCatalog catalog,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var targetPath = Path.GetFullPath(path);
        if (string.IsNullOrWhiteSpace(Path.GetFileName(targetPath)))
        {
            throw new ArgumentException(
                "A settlement template export file is required.",
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

    private async Task<HumanSiteTemplateExportResult> ExportLockedAsync(
        HumanSiteTemplateCatalog catalog,
        string targetPath,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException(
                "The settlement template export has no parent directory.");
        Directory.CreateDirectory(directory);
        RejectReparsePoint(targetPath);
        var original = await CaptureAsync(
            targetPath,
            cancellationToken).ConfigureAwait(false);
        var payload = Serialize(catalog.Templates);
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
                catalog.Count,
                cancellationToken).ConfigureAwait(false);
            EnsureUnchanged(
                original,
                await CaptureAsync(
                    targetPath,
                    cancellationToken).ConfigureAwait(false));
            if (original is not null)
            {
                backupPath = CreateBackupPath(targetPath);
                File.Copy(targetPath, backupPath, overwrite: false);
                var backup = await CaptureAsync(
                    backupPath,
                    cancellationToken).ConfigureAwait(false);
                EnsureUnchanged(original, backup);
            }

            if (beforeActivation is not null)
            {
                await beforeActivation(targetPath).ConfigureAwait(false);
            }

            EnsureUnchanged(
                original,
                await CaptureAsync(
                    targetPath,
                    cancellationToken).ConfigureAwait(false));
            File.Move(temporaryPath, targetPath, overwrite: true);
            activated = true;
            await ValidateFileAsync(
                targetPath,
                expectedHash,
                catalog.Count,
                cancellationToken).ConfigureAwait(false);
            return new HumanSiteTemplateExportResult(
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

    private static byte[] Serialize(
        IReadOnlyList<HumanSiteTemplate> templates)
    {
        _ = new HumanSiteTemplateCatalog(templates);
        var rows = templates.Select(template => new TemplateRow(
            template.Economy.ToString(),
            template.SubType,
            template.Name,
            template.LandingPads.Select(point => ToRow(
                point,
                size: point.Size.ToString())).ToArray(),
            template.SecureDoors.Select(point => ToRow(point)).ToArray(),
            template.NamedPoints.Select(point => ToRow(
                point,
                name: point.Name)).ToArray(),
            template.DataTerminals.Select(point => ToRow(point)).ToArray(),
            template.ConflictZonePoints.Select(point => ToRow(point)).ToArray(),
            template.Buildings.Select(building => new BuildingRow(
                building.Name,
                building.Paths.Select(path => new PathRow(
                    path.Points.Select(ToRow).ToArray(),
                    path.PointTypes.ToArray(),
                    path.FillMode)).ToArray())).ToArray())).ToArray();
        return JsonSerializer.SerializeToUtf8Bytes(rows, JsonOptions);
    }

    private static PoiRow ToRow(
        HumanSitePointOfInterest point,
        string? name = null,
        string? size = null)
    {
        return new PoiRow(
            ToRow(point.Offset),
            point.Rotation,
            point.SecurityLevel,
            point.Floor,
            name,
            size);
    }

    private static PointRow ToRow(HumanSiteMapPoint point)
    {
        return new PointRow(point.X, point.Y);
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
        int expectedCount,
        CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken)
            .ConfigureAwait(false);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        if (!string.Equals(hash, expectedHash, StringComparison.Ordinal))
        {
            throw new IOException(
                "The settlement template export checksum did not match the staged data.");
        }

        await using var stream = new MemoryStream(bytes, writable: false);
        var loaded = HumanSiteTemplateCatalog.Load(stream);
        if (loaded.Count != expectedCount)
        {
            throw new InvalidDataException(
                "The settlement template export did not retain every catalog entry.");
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
                "The settlement template destination changed during export. The newer file was not overwritten.");
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
                "The settlement template export failed and its verified backup is unavailable.");
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
            System.Globalization.CultureInfo.InvariantCulture);
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
                "Settlement template exports cannot replace a symbolic link or reparse point.");
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

    private sealed record TemplateRow(
        [property: JsonPropertyName("economy")] string Economy,
        [property: JsonPropertyName("subType")] int SubType,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("landingPads")] PoiRow[] LandingPads,
        [property: JsonPropertyName("secureDoors")] PoiRow[] SecureDoors,
        [property: JsonPropertyName("namedPoi")] PoiRow[] NamedPoi,
        [property: JsonPropertyName("dataTerminals")] PoiRow[] DataTerminals,
        [property: JsonPropertyName("czPoints")] PoiRow[] ConflictZonePoints,
        [property: JsonPropertyName("buildings")] BuildingRow[] Buildings);

    private sealed record PoiRow(
        [property: JsonPropertyName("offset")] PointRow Offset,
        [property: JsonPropertyName("rot")] double Rotation,
        [property: JsonPropertyName("level")] int SecurityLevel,
        [property: JsonPropertyName("floor")] int Floor,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("size")] string? Size);

    private sealed record PointRow(
        [property: JsonPropertyName("X")] double X,
        [property: JsonPropertyName("Y")] double Y);

    private sealed record BuildingRow(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("paths")] PathRow[] Paths);

    private sealed record PathRow(
        [property: JsonPropertyName("PathPoints")] PointRow[] PathPoints,
        [property: JsonPropertyName("PathTypes")] byte[] PathTypes,
        [property: JsonPropertyName("FillMode")] int FillMode);

    private sealed record FileFingerprint(long Size, string Sha256);
}

public sealed record HumanSiteTemplateExportResult(
    string Path,
    string? BackupPath,
    int TemplateCount,
    long Size,
    string Sha256);
