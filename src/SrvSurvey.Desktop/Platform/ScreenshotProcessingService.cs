using System.Globalization;
using System.Text;
using System.Text.Json;
using SkiaSharp;
using SrvSurvey.Core.Guardian;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Platform;

public interface IScreenshotProcessingService
{
    int PendingCount => 0;
    bool IsGameWindowAvailable => false;
    bool HasPendingWork => PendingCount > 0;

    Task<ScreenshotProcessingResult> ProcessLiveAsync(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        ScreenshotProcessingPreferences preferences,
        string? commanderName,
        IReadOnlyDictionary<JournalEventEnvelope, ScreenshotGuardianContext>? guardianContexts = null,
        ScreenshotNavigationContext? navigationContext = null,
        CancellationToken cancellationToken = default
    ) =>
        ProcessAsync(journalEvents, preferences, commanderName, guardianContexts, navigationContext, cancellationToken);

    Task<ScreenshotProcessingResult> ProcessAsync(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        ScreenshotProcessingPreferences preferences,
        string? commanderName,
        IReadOnlyDictionary<JournalEventEnvelope, ScreenshotGuardianContext>? guardianContexts = null,
        ScreenshotNavigationContext? navigationContext = null,
        CancellationToken cancellationToken = default
    );
}

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The service is application-scoped and its gate may have in-flight waiters."
)]
public sealed partial class ScreenshotProcessingService : IScreenshotProcessingService
{
    private const string UnknownLabel = "unknown";
    private const int FileReadinessAttempts = 150;
    private const int LiveConversionAttemptLimit = 2;
    private const int LiveSteamAdmissionLimit = 4;
    private static readonly TimeSpan FileReadinessPollInterval = TimeSpan.FromMilliseconds(200);

    private readonly Dictionary<string, PendingScreenshot> pending = new(StringComparer.Ordinal);
    private readonly HashSet<string> completed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CompletedSource> completedSources = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal
    );
    private int pendingCount;
    private string? nextAttempt;
    private readonly TimeProvider retryClock;
    private long processingPass;
    private readonly Dictionary<string, (SourceFingerprint Stamp, string Hash)> imageHashes = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal
    );

    public int PendingCount => Volatile.Read(ref pendingCount);
    public bool IsGameWindowAvailable => gameClientWidthProvider() is > 0;
    public bool HasPendingWork => PendingCount > 0 || Volatile.Read(ref recoveryWarningCount) > 0;

    private readonly SemaphoreSlim processingLock = new(1, 1);
    private readonly Func<string, string, bool> tryPublishImage;
    private readonly Func<int?> gameClientWidthProvider;
    private readonly Func<TimeSpan, CancellationToken, Task> retryDelay;
    private readonly Func<string, IEnumerable<string>> enumerateRecoveryFiles;
    private readonly Func<string, string> readImageHash;

    public ScreenshotProcessingService(
        Func<int?>? gameClientWidthProvider = null,
        Func<TimeSpan, CancellationToken, Task>? retryDelay = null,
        string? pendingQueueDirectory = null,
        Func<string, string, bool>? tryPublishImage = null,
        Func<string, IEnumerable<string>>? enumerateRecoveryFiles = null,
        TimeProvider? timeProvider = null,
        Func<string, string>? readImageHash = null
    )
    {
        this.tryPublishImage = tryPublishImage ?? TryMoveImage;
        this.gameClientWidthProvider = gameClientWidthProvider ?? GetGameClientWidth;
        this.retryDelay = retryDelay ?? Task.Delay;
        this.enumerateRecoveryFiles = enumerateRecoveryFiles ?? (folder => Directory.EnumerateFiles(folder, "*.json"));
        this.readImageHash = readImageHash ?? HashFile;
        retryClock = timeProvider ?? TimeProvider.System;
        this.pendingQueueDirectory = pendingQueueDirectory is null ? null : Path.GetFullPath(pendingQueueDirectory);
        RecoverPendingCaptures();
    }

    public static string GetSystemFolderPath(string targetFolder, string systemName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(systemName);
        return Path.Combine(Path.GetFullPath(targetFolder), SafeFileName(systemName));
    }

    public Task<ScreenshotProcessingResult> ProcessAsync(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        ScreenshotProcessingPreferences preferences,
        string? commanderName,
        IReadOnlyDictionary<JournalEventEnvelope, ScreenshotGuardianContext>? guardianContexts = null,
        ScreenshotNavigationContext? navigationContext = null,
        CancellationToken cancellationToken = default
    ) =>
        ProcessQueuedAsync(
            journalEvents,
            preferences,
            commanderName,
            guardianContexts,
            navigationContext,
            false,
            cancellationToken
        );

    public Task<ScreenshotProcessingResult> ProcessLiveAsync(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        ScreenshotProcessingPreferences preferences,
        string? commanderName,
        IReadOnlyDictionary<JournalEventEnvelope, ScreenshotGuardianContext>? guardianContexts = null,
        ScreenshotNavigationContext? navigationContext = null,
        CancellationToken cancellationToken = default
    ) =>
        ProcessQueuedAsync(
            journalEvents,
            preferences,
            commanderName,
            guardianContexts,
            navigationContext,
            true,
            cancellationToken
        );

    private async Task<ScreenshotProcessingResult> ProcessQueuedAsync(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        ScreenshotProcessingPreferences preferences,
        string? commanderName,
        IReadOnlyDictionary<JournalEventEnvelope, ScreenshotGuardianContext>? guardianContexts,
        ScreenshotNavigationContext? navigationContext,
        bool live,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        ArgumentNullException.ThrowIfNull(preferences);
        if (!preferences.Enabled)
        {
            return ScreenshotProcessingResult.Empty;
        }

        await processingLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var warnings = new List<string>(recoveryWarnings);
            recoveryWarnings.Clear();
            Volatile.Write(ref recoveryWarningCount, 0);
            processingPass++;
            Enqueue(journalEvents, preferences, commanderName, guardianContexts, navigationContext, warnings, live);
            return await DrainAsync(warnings, live, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref pendingCount, pending.Count);
            processingLock.Release();
        }
    }

    private void Enqueue(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        ScreenshotProcessingPreferences preferences,
        string? commanderName,
        IReadOnlyDictionary<JournalEventEnvelope, ScreenshotGuardianContext>? guardianContexts,
        ScreenshotNavigationContext? navigationContext,
        List<string> warnings,
        bool live
    )
    {
        int admitted = 0;
        foreach (JournalEventEnvelope entry in journalEvents.Where(entry => entry.EventName == "Screenshot"))
        {
            string key = GetCaptureKey(entry, preferences, commanderName);
            if (completed.Contains(key) || pending.ContainsKey(key))
            {
                continue;
            }
            if (
                live
                && pendingQueueDirectory is not null
                && IsSteamCapture(entry)
                && admitted++ >= LiveSteamAdmissionLimit
            )
            {
                // Deferred Steam originals remain discoverable on the next scan.
                continue;
            }
            try
            {
                pending.Add(
                    key,
                    CreatePendingCapture(entry, preferences, commanderName, guardianContexts, navigationContext)
                );
                PersistNewCapture(key, pending[key], warnings);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidDataException)
            {
                warnings.Add(DescribeFailure(entry, exception, retained: false));
                completed.Add(key);
            }
        }
    }

    private static string GetCaptureKey(
        JournalEventEnvelope entry,
        ScreenshotProcessingPreferences preferences,
        string? commanderName
    ) =>
        IsSteamCapture(entry)
            ? JsonSerializer.Serialize(new { SteamFile = GetSteamCaptureIdentity(entry) })
            : JsonSerializer.Serialize(
                new
                {
                    entry.SourceRecordIdentity,
                    Commander = commanderName,
                    entry.RawJson,
                    preferences.SourceFolder,
                }
            );

    private PendingScreenshot CreatePendingCapture(
        JournalEventEnvelope entry,
        ScreenshotProcessingPreferences preferences,
        string? commanderName,
        IReadOnlyDictionary<JournalEventEnvelope, ScreenshotGuardianContext>? guardianContexts,
        ScreenshotNavigationContext? navigationContext
    )
    {
        if (!Path.IsPathFullyQualified(preferences.SourceFolder))
        {
            throw new ArgumentException("The screenshot source folder must be an absolute path.");
        }
        if (!Path.IsPathFullyQualified(preferences.TargetFolder))
        {
            throw new ArgumentException("The screenshot target folder must be an absolute path.");
        }
        return new(
            new ScreenshotConversionRequest(
                entry.Timestamp is null ? entry with { Timestamp = retryClock.GetUtcNow() } : entry,
                preferences,
                GetCaptureCommanderName(entry, commanderName),
                guardianContexts?.GetValueOrDefault(entry),
                navigationContext,
                gameClientWidthProvider(),
                ResolveSourcePaths(entry, preferences.SourceFolder),
                Path.GetFullPath(preferences.TargetFolder)
            )
        );
    }

    private static string? GetCaptureCommanderName(JournalEventEnvelope entry, string? fallback) =>
        IsSteamCapture(entry) ? GetString(entry, "Commander") ?? fallback : fallback;

    private static bool IsSteamCapture(JournalEventEnvelope entry) =>
        entry.Payload.TryGetProperty("SteamScreenshot", out JsonElement steam) && steam.ValueKind == JsonValueKind.True;

    private static string? GetSteamCaptureIdentity(JournalEventEnvelope entry)
    {
        string? path = GetString(entry, "Filename");
        return OperatingSystem.IsWindows() ? path?.ToUpperInvariant() : path;
    }

    private void PersistNewCapture(string key, PendingScreenshot capture, List<string> warnings)
    {
        capture.LastAdmissionAttemptPass = processingPass;
        try
        {
            SavePendingCapture(key, capture);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            warnings.Add("The pending screenshot could not be saved for restart recovery: " + exception.Message);
        }
    }

    private async Task<ScreenshotProcessingResult> DrainAsync(
        List<string> warnings,
        bool live,
        CancellationToken cancellationToken
    )
    {
        var conversions = new List<ScreenshotConversion>();
        foreach ((string key, PendingScreenshot capture) in GetPendingWork(live))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                (ScreenshotConversion? conversion, bool complete) = await TryConvertAsync(
                        key,
                        capture,
                        live,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                if (conversion is not null)
                {
                    conversions.Add(conversion);
                    if (conversion.Warning is not null)
                    {
                        warnings.Add(conversion.Warning);
                    }
                }
                if (complete)
                {
                    RemovePendingCapture(key, capture, warnings);
                    pending.Remove(key);
                    completed.Add(key);
                }
                if (live && conversion is not null)
                {
                    // Report a finished image without waiting for the rest of the persistent queue.
                    break;
                }
            }
            catch (Exception exception)
                when (exception
                        is IOException
                            or UnauthorizedAccessException
                            or InvalidDataException
                            or ArgumentException
                )
            {
                string warning = DescribeFailure(capture.Request.Entry, exception);
                if (warning != capture.LastWarning)
                {
                    warnings.Add(warning);
                    capture.LastWarning = warning;
                }
            }
        }
        return new ScreenshotProcessingResult(conversions, warnings) { PendingCount = pending.Count };
    }

    private IEnumerable<KeyValuePair<string, PendingScreenshot>> GetPendingWork(bool live)
    {
        KeyValuePair<string, PendingScreenshot>[] work = pending.ToArray();
        if (work.Length == 0)
        {
            yield break;
        }
        int start = live ? Math.Max(0, Array.FindIndex(work, item => item.Key == nextAttempt)) : 0;
        int count = live ? Math.Min(work.Length, LiveConversionAttemptLimit) : work.Length;
        for (int i = 0; i < count; i++)
        {
            int index = (start + i) % work.Length;
            // Preserve the successor before a successful item removes itself.
            nextAttempt = work[(index + 1) % work.Length].Key;
            yield return work[index];
        }
    }

    private async Task<(ScreenshotConversion? Conversion, bool Complete)> TryConvertAsync(
        string key,
        PendingScreenshot capture,
        bool live,
        CancellationToken cancellationToken
    )
    {
        if (!capture.AdmissionStored && capture.LastAdmissionAttemptPass != processingPass)
        {
            capture.LastAdmissionAttemptPass = processingPass;
            SavePendingCapture(key, capture);
        }
        if (capture.CompletedConversion is not null && capture.PublishedImages.All(ImageStillValid))
        {
            ScreenshotConversion recovered = FinishConversion(key, capture);
            return (recovered, true);
        }
        capture.CompletedConversion = null;
        capture.PublishedImages.Clear();
        string candidate = SelectSourcePath(capture.Request.SourcePaths);
        SourceFingerprint? fingerprint = ReadFingerprint(candidate);
        if (IsDuplicateSource(capture.Request, fingerprint))
        {
            return (null, true);
        }
        if (
            fingerprint is not null
            && SameFingerprint(fingerprint, capture.Undecodable)
            && capture.RetryAfter is { } retryAfter
            && retryClock.GetUtcNow() < retryAfter
        )
        {
            throw new InvalidDataException($"'{candidate}' is incomplete or not a supported bitmap image.");
        }
        string sourcePath = await WaitForCompletedFileAsync(capture.Request.SourcePaths, live, cancellationToken)
            .ConfigureAwait(false);
        fingerprint = ReadFingerprint(sourcePath);
        using SKBitmap? source = DecodeBitmapFile(sourcePath);
        if (source is null)
        {
            capture.Undecodable = fingerprint;
            capture.RetryAfter = retryClock.GetUtcNow().AddSeconds(30);
            SavePendingCapture(key, capture);
            throw new InvalidDataException($"'{sourcePath}' is incomplete or not a supported bitmap image.");
        }
        capture.Undecodable = null;
        capture.RetryAfter = null;
        capture.Source = fingerprint;
        capture.CompletedConversion = Convert(capture, sourcePath, source);
        ScreenshotConversion conversion = FinishConversion(key, capture);
        return (conversion, true);
    }

    private bool IsDuplicateSource(ScreenshotConversionRequest request, SourceFingerprint? fingerprint)
    {
        foreach (string path in request.SourcePaths)
        {
            if (
                completedSources.TryGetValue(path, out CompletedSource? prior)
                && (
                    SameFingerprint(prior.Fingerprint, fingerprint)
                    || (
                        fingerprint is null
                        && request.Entry.Timestamp is not null
                        && prior.Timestamp == request.Entry.Timestamp
                    )
                )
            )
            {
                return true;
            }
        }
        return false;
    }

    private static SourceFingerprint? ReadFingerprint(string path)
    {
        var info = new FileInfo(path);
        return info.Exists ? new SourceFingerprint(info.FullName, info.Length, info.LastWriteTimeUtc) : null;
    }

    private sealed record SourceFingerprint(string Path, long Length, DateTime LastWriteTimeUtc);

    private sealed record CompletedSource(SourceFingerprint Fingerprint, DateTimeOffset? Timestamp);

    private static string DescribeFailure(JournalEventEnvelope entry, Exception exception, bool retained = true)
    {
        string timestamp = entry.Timestamp?.ToString("u", CultureInfo.InvariantCulture) ?? "with unknown time";
        return retained
            ? $"Screenshot {timestamp} was not converted; it remains pending and the original is retained: {exception.Message}"
            : $"Screenshot {timestamp} was not converted; the original is retained: {exception.Message}";
    }

    private sealed class PendingScreenshot(ScreenshotConversionRequest request)
    {
        public ScreenshotConversionRequest Request { get; } = request;
        public string? LastWarning { get; set; }
        public bool AdmissionStored { get; set; }
        public long LastAdmissionAttemptPass { get; set; } = -1;
        public DateTimeOffset? RetryAfter { get; set; }
        public SourceFingerprint? Undecodable { get; set; }
        public SourceFingerprint? Source { get; set; }
        public ScreenshotConversion? CompletedConversion { get; set; }
        public List<PublishedImage> PublishedImages { get; } = [];
    }

    private readonly record struct ScreenshotConversionRequest(
        JournalEventEnvelope Entry,
        ScreenshotProcessingPreferences Preferences,
        string? CommanderName,
        ScreenshotGuardianContext? GuardianContext,
        ScreenshotNavigationContext? NavigationContext,
        int? GameClientWidth,
        IReadOnlyList<string> SourcePaths,
        string TargetDirectory
    );

    private ScreenshotConversion Convert(PendingScreenshot capture, string sourcePath, SKBitmap source)
    {
        ScreenshotConversionRequest request = capture.Request;
        JournalEventEnvelope entry = request.Entry;
        ScreenshotProcessingPreferences preferences = request.Preferences;
        ScreenshotGuardianContext? guardianContext = request.GuardianContext;
        using SKBitmap output =
            source.Copy() ?? throw new InvalidDataException($"'{sourcePath}' could not be copied for conversion.");
        if (preferences.AddBanner)
        {
            DrawBanner(output, entry, preferences, request.CommanderName, guardianContext, request.NavigationContext);
        }

        string systemName = GetString(entry, "System") ?? UnknownLabel;
        string bodyName = GetString(entry, "Body") ?? UnknownLabel;
        DateTimeOffset timestamp = entry.Timestamp ?? DateTimeOffset.UtcNow;
        string folder = GetSystemFolderPath(request.TargetDirectory, systemName);
        Directory.CreateDirectory(folder);
        string baseName = SafeFileName(
            $"{bodyName} ({timestamp.UtcDateTime:yyyy-MM-dd HHmmss})"
                + GetGuardianFileSuffix(guardianContext)
                + GetHighResolutionSuffix(entry, request.GameClientWidth)
        );
        PublishedImage main = WritePngAtomically(output, Path.Combine(folder, baseName + ".png"));
        capture.PublishedImages.Add(main);
        string outputPath = main.Path;

        string? warning = null;
        string? aerialOutputPath = null;
        if (IsGuardianAerial(preferences, guardianContext))
        {
            try
            {
                using SKBitmap aerial = CreateAerialBitmap(
                    source,
                    guardianContext!.SiteType,
                    preferences.RotateAlphaAerial
                );
                DrawBanner(
                    aerial,
                    entry,
                    preferences,
                    request.CommanderName,
                    guardianContext,
                    request.NavigationContext
                );
                string aerialFolder = Path.Combine(
                    request.TargetDirectory,
                    SafeFileName("Aerial " + guardianContext.SiteType)
                );
                Directory.CreateDirectory(aerialFolder);
                PublishedImage aerialImage = WritePngAtomically(aerial, Path.Combine(aerialFolder, baseName + ".png"));
                capture.PublishedImages.Add(aerialImage);
                aerialOutputPath = aerialImage.Path;
            }
            catch (Exception exception)
                when (exception
                        is IOException
                            or UnauthorizedAccessException
                            or InvalidDataException
                            or ArgumentException
                )
            {
                warning =
                    $"Saved '{outputPath}', but the Guardian aerial copy failed; "
                    + "the original image was retained: "
                    + exception.Message;
            }
        }

        return new ScreenshotConversion(sourcePath, outputPath, false, warning, aerialOutputPath)
        {
            IncludedBanner = preferences.AddBanner,
        };
    }

    private static SKBitmap? DecodeBitmapFile(string path)
    {
        // Managed file streams support long Windows paths without asking the
        // native decoder to reopen a filename through its own file API.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var codec = SKCodec.Create(stream);
        if (codec is null)
        {
            return null;
        }
        SKImageInfo info = codec.Info;
        if (info.AlphaType == SKAlphaType.Unpremul)
        {
            info = info.WithAlphaType(SKAlphaType.Premul);
        }
        SKBitmap? bitmap = new(info);
        try
        {
            // SKBitmap.Decode also accepts IncompleteInput. Only a complete
            // decode is safe to publish before optional source deletion.
            if (codec.GetPixels(info, bitmap.GetPixels()) != SKCodecResult.Success)
            {
                return null;
            }
            SKBitmap complete = bitmap;
            bitmap = null;
            return complete;
        }
        finally
        {
            bitmap?.Dispose();
        }
    }

    private PublishedImage WritePngAtomically(SKBitmap bitmap, string outputPath)
    {
        string temporaryPath = $"{outputPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            // Lower compression keeps PNG pixels lossless while avoiding long high-resolution encodes.
            using (SKPixmap pixels = bitmap.PeekPixels())
            using (
                SKData data =
                    pixels.Encode(new SKPngEncoderOptions(SKPngEncoderFilterFlags.AllFilters, 1))
                    ?? throw new InvalidDataException("The screenshot could not be encoded as PNG.")
            )
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                data.SaveTo(stream);
                stream.Flush(true);
            }

            using (SKBitmap? verified = DecodeBitmapFile(temporaryPath))
            {
                if (verified is null || verified.Width != bitmap.Width || verified.Height != bitmap.Height)
                {
                    throw new InvalidDataException("The converted PNG could not be verified.");
                }
            }

            return PublishImage(temporaryPath, outputPath, readImageHash(temporaryPath));
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private PublishedImage PublishImage(string temporaryPath, string outputPath, string hash)
    {
        string directory = Path.GetDirectoryName(outputPath)!;
        string baseName = Path.GetFileNameWithoutExtension(outputPath);
        outputPath = GetAvailablePath(directory, baseName, ".png", hash);
        while (!tryPublishImage(temporaryPath, outputPath))
        {
            if (ImageStillValid(new PublishedImage(outputPath, hash)))
            {
                return new PublishedImage(outputPath, hash);
            }
            outputPath = GetAvailablePath(directory, baseName, ".png", hash);
        }
        RememberImageHash(outputPath, hash);
        return new PublishedImage(outputPath, hash);
    }

    private void RememberImageHash(string path, string hash)
    {
        if (ReadFingerprint(path) is { } stamp)
        {
            imageHashes[path] = (stamp, hash);
        }
    }

    private bool CandidateMatches(string path, string expectedHash)
    {
        try
        {
            SourceFingerprint? stamp = ReadFingerprint(path);
            if (
                imageHashes.TryGetValue(path, out (SourceFingerprint Stamp, string Hash) cached)
                && SameFingerprint(stamp, cached.Stamp)
                && cached.Hash != expectedHash
            )
            {
                return false;
            }
            // A positive reuse always verifies current bytes before source cleanup.
            string hash = readImageHash(path);
            RememberImageHash(path, hash);
            return hash == expectedHash;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // An unrelated unreadable output must not prevent a distinct suffixed image.
            return false;
        }
    }

    private static bool TryMoveImage(string source, string destination)
    {
        try
        {
            File.Move(source, destination, false);
            return true;
        }
        catch (IOException) when (File.Exists(destination))
        {
            return false;
        }
    }

    private static bool IsGuardianAerial(
        ScreenshotProcessingPreferences preferences,
        ScreenshotGuardianContext? context
    )
    {
        return preferences.UseGuardianAerialFolder
            && context is not null
            && !string.IsNullOrWhiteSpace(context.SiteType)
            && context.DistanceFromOrigin is >= 0 and < 50
            && context.Altitude is > 500 and < 2000;
    }

    private static string GetGuardianFileSuffix(ScreenshotGuardianContext? context)
    {
        if (context is null || string.IsNullOrWhiteSpace(context.SiteType))
        {
            return string.Empty;
        }

        return context.SiteKind == GuardianSiteKind.Ruins
            ? $", Ruins{context.SiteIndex} {context.SiteType}"
            : $", {context.SiteType}";
    }

    private static string GetHighResolutionSuffix(JournalEventEnvelope entry, int? gameClientWidth)
    {
        return
            gameClientWidth is > 0
            && entry.Payload.TryGetProperty("Width", out JsonElement width)
            && width.TryGetInt32(out int screenshotWidth)
            && screenshotWidth > gameClientWidth
            ? " (HighRes)"
            : string.Empty;
    }

    private static SKBitmap CreateAerialBitmap(SKBitmap source, string siteType, bool rotateAlpha)
    {
        if (!rotateAlpha || !string.Equals(siteType, "Alpha", StringComparison.OrdinalIgnoreCase))
        {
            return source.Copy() ?? throw new InvalidDataException("The Guardian aerial bitmap could not be copied.");
        }

        int cropWidth = Math.Min(source.Width, Math.Max(1, (int)(source.Height * 1.3f)));
        using var cropped = new SKBitmap(cropWidth, source.Height);
        using (var cropCanvas = new SKCanvas(cropped))
        {
            cropCanvas.Clear(SKColors.Black);
            int sourceX = (source.Width - cropWidth) / 2;
            cropCanvas.DrawBitmap(
                source,
                new SKRect(sourceX, 0, sourceX + cropWidth, source.Height),
                new SKRect(0, 0, cropWidth, source.Height)
            );
        }

        var rotated = new SKBitmap(cropped.Height, cropped.Width);
        using (var rotateCanvas = new SKCanvas(rotated))
        {
            rotateCanvas.Clear(SKColors.Black);
            rotateCanvas.Translate(rotated.Width, 0);
            rotateCanvas.RotateDegrees(90);
            rotateCanvas.DrawBitmap(cropped, 0, 0);
        }

        return rotated;
    }

    private static IReadOnlyList<string> ResolveSourcePaths(JournalEventEnvelope entry, string sourceDirectory)
    {
        string journalPath =
            GetString(entry, "Filename") ?? throw new InvalidDataException("The Screenshot event has no Filename.");
        string normalized = journalPath.Replace('\\', '/');
        string fileName = Path.GetFileName(normalized);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidDataException("The Screenshot event filename is invalid.");
        }

        string fallback = Path.Combine(Path.GetFullPath(sourceDirectory), fileName);
        // ED_Pictures is a game placeholder, including when its leading slash
        // resembles an absolute Unix path. Guest Windows paths also use the
        // configured (or Proton-derived) host folder on Linux.
        bool placeholder = normalized.StartsWith("/ED_Pictures/", StringComparison.OrdinalIgnoreCase);
        if (!placeholder && Path.IsPathFullyQualified(journalPath))
        {
            string absolute = Path.GetFullPath(journalPath);
            return absolute == fallback ? [absolute] : [absolute, fallback];
        }
        return [fallback];
    }

    private async Task<string> WaitForCompletedFileAsync(
        IReadOnlyList<string> paths,
        bool live,
        CancellationToken cancellationToken
    )
    {
        Exception? lastError = null;
        string path = paths[0];
        long previousLength = -1;
        string? previousPath = null;
        int stableReads = 0;
        // Live polling never waits for a missing or locked BMP. Ready files still
        // need two unchanged length observations before decoding; otherwise the
        // immutable capture is retried on the next journal poll.
        int attempts = live ? 3 : FileReadinessAttempts;
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            path = SelectSourcePath(paths);
            (long length, Exception? error) = ReadSourceLength(path);
            lastError = error;
            stableReads = length > 0 && length == previousLength && path == previousPath ? stableReads + 1 : 0;
            if (stableReads >= 2)
            {
                return path;
            }
            previousPath = path;
            previousLength = length;
            if (live && error is not null)
            {
                break;
            }
            if (attempt + 1 < attempts)
            {
                await retryDelay(FileReadinessPollInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        string reason = lastError switch
        {
            FileNotFoundException or DirectoryNotFoundException => "The source bitmap was not found",
            UnauthorizedAccessException => "Access to the source bitmap was denied",
            not null => "The source bitmap could not be read (it may still be locked or writing)",
            _ => "The source bitmap is empty or still changing",
        };
        throw new IOException($"{reason}: {path}. {lastError?.Message}", lastError);
    }

    private static (long Length, Exception? Error) ReadSourceLength(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return (stream.Length, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (-1, exception);
        }
    }

    private static string SelectSourcePath(IReadOnlyList<string> paths)
    {
        foreach (string path in paths)
        {
            // File.Exists hides permission and sharing errors. Fall back only
            // when a native absolute journal path really is absent.
            try
            {
                File.GetAttributes(path);
                return path;
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                // Try the configured screenshot folder for moved/guest files.
            }
        }
        return paths[^1];
    }

    private static string ResolveGuardianBannerSiteName(ScreenshotGuardianContext guardianContext)
    {
        if (!string.IsNullOrWhiteSpace(guardianContext.SiteName))
        {
            return guardianContext.SiteName;
        }

        return guardianContext.SiteKind == GuardianSiteKind.Ruins
            ? $"Ancient Ruins ({guardianContext.SiteIndex})"
            : $"Guardian Structure ({guardianContext.SiteIndex})";
    }

    private static void DrawBanner(
        SKBitmap bitmap,
        JournalEventEnvelope entry,
        ScreenshotProcessingPreferences preferences,
        string? commanderName,
        ScreenshotGuardianContext? guardianContext,
        ScreenshotNavigationContext? navigationContext
    )
    {
        using var canvas = new SKCanvas(bitmap);
        using SKTypeface typeface = SKTypeface.Default;
        float scale = Math.Clamp(bitmap.Width / 1920f, 0.6f, 3f);
        using var titleFont = new SKFont(typeface, 30f * scale);
        using var detailFont = new SKFont(typeface, 18f * scale);
        using var background = new SKPaint { Color = new SKColor(0, 0, 0, 220), IsAntialias = true };
        using var foreground = new SKPaint { Color = ParseColor(preferences.BannerColor), IsAntialias = true };
        string body = GetString(entry, "Body") ?? UnknownLabel;
        string system = GetString(entry, "System") ?? UnknownLabel;
        DateTimeOffset timestamp = entry.Timestamp ?? DateTimeOffset.UtcNow;
        string displayedTime = preferences.BannerLocalTime
            ? timestamp.ToLocalTime().ToString("G", CultureInfo.CurrentCulture)
            : timestamp.ToUniversalTime().ToString("u", CultureInfo.InvariantCulture);
        var details = new List<string>
        {
            $"System: {system}",
            $"Cmdr: {commanderName ?? UnknownLabel} - {displayedTime}",
        };
        if (guardianContext is not null)
        {
            string siteName = ResolveGuardianBannerSiteName(guardianContext);
            details.Add($"{siteName} - {guardianContext.SiteType}");
        }
        string? location = CreateLocationLine(entry, navigationContext);
        if (location is not null)
        {
            details.Add(location);
        }

        float padding = 12f * scale;
        float gap = 6f * scale;
        float lineHeight = detailFont.Size * 1.3f;
        string title = $"Body: {body}";
        float width = Math.Max(titleFont.MeasureText(title), details.Max(line => detailFont.MeasureText(line)));
        float height = padding * 2 + titleFont.Size + gap + (details.Count * lineHeight);
        canvas.DrawRect(10f * scale, 10f * scale, width + (padding * 2), height, background);
        float x = 10f * scale + padding;
        float y = 10f * scale + padding + titleFont.Size;
        canvas.DrawText(title, x, y, SKTextAlign.Left, titleFont, foreground);
        y += gap + lineHeight;
        foreach (string line in details)
        {
            canvas.DrawText(line, x, y, SKTextAlign.Left, detailFont, foreground);
            y += lineHeight;
        }
    }

    internal static string? CreateLocationLine(
        JournalEventEnvelope entry,
        ScreenshotNavigationContext? navigationContext
    )
    {
        if (entry.Timestamp is not { } timestamp || navigationContext is not { HasLatitudeLongitude: true } status)
        {
            return null;
        }

        TimeSpan age = status.ObservedAt - timestamp;
        if (age < TimeSpan.Zero || age >= TimeSpan.FromSeconds(10))
        {
            return null;
        }

        var values = new List<string>();
        AddNumber(values, entry, "Latitude", "Lat", "°", 6, status.Latitude);
        AddNumber(values, entry, "Longitude", "Long", "°", 6, status.Longitude);
        AddNumber(values, entry, "Heading", "Heading", "°", 0, status.Heading);
        AddNumber(values, entry, "Altitude", "Altitude", "m", 0);
        return values.Count == 0 ? null : string.Join("  ", values);
    }

    private static void AddNumber(
        List<string> values,
        JournalEventEnvelope entry,
        string propertyName,
        string label,
        string suffix,
        int decimals,
        double? fallback = null
    )
    {
        double? number = fallback;
        if (
            entry.Payload.TryGetProperty(propertyName, out JsonElement property)
            && property.ValueKind == System.Text.Json.JsonValueKind.Number
            && property.TryGetDouble(out double eventNumber)
            && double.IsFinite(eventNumber)
        )
        {
            number = eventNumber;
        }
        if (number is not { } value || !double.IsFinite(value))
        {
            return;
        }

        values.Add($"{label}: {value.ToString($"F{decimals}", CultureInfo.InvariantCulture)}{suffix}");
    }

    private static int? GetGameClientWidth()
    {
        using IGameWindowTracker tracker = GameWindowTracker.CreateCurrent();
        GameWindowSnapshot snapshot = tracker.GetSnapshot();
        return snapshot.IsAvailable ? snapshot.ClientBounds.Width : null;
    }

    private static SKColor ParseColor(string value)
    {
        if (SKColor.TryParse(value, out SKColor color))
        {
            return color;
        }

        return SKColors.Yellow;
    }

    private static string? GetString(JournalEventEnvelope entry, string propertyName)
    {
        return
            entry.Payload.TryGetProperty(propertyName, out JsonElement property)
            && property.ValueKind == System.Text.Json.JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private string GetAvailablePath(string directory, string baseName, string extension, string expectedHash)
    {
        string candidate = Path.Combine(directory, baseName + extension);
        int suffix = 2;
        while (File.Exists(candidate))
        {
            if (CandidateMatches(candidate, expectedHash))
            {
                return candidate;
            }
            candidate = Path.Combine(directory, $"{baseName} ({suffix++}){extension}");
        }

        return candidate;
    }

    private static string SafeFileName(string value)
    {
        string invalid = "<>:\"/\\|?*";
        var result = new StringBuilder(value.Length);
        foreach (char character in value.Trim())
        {
            result.Append(character < ' ' || invalid.Contains(character) ? '_' : character);
        }

        string safe = result.ToString().TrimEnd(' ', '.');
        return string.IsNullOrWhiteSpace(safe) ? UnknownLabel : safe;
    }
}

public sealed record ScreenshotProcessingResult(
    IReadOnlyList<ScreenshotConversion> Conversions,
    IReadOnlyList<string> Warnings
)
{
    public int PendingCount { get; init; }

    public static ScreenshotProcessingResult Empty { get; } = new([], []);
}

public sealed record ScreenshotConversion(
    string SourcePath,
    string OutputPath,
    bool SourceDeleted,
    string? Warning,
    string? AerialOutputPath = null
)
{
    public bool? IncludedBanner { get; init; }
}

public sealed record ScreenshotGuardianContext(
    string SiteType,
    double? DistanceFromOrigin,
    double? Altitude,
    GuardianSiteKind SiteKind = GuardianSiteKind.Structure,
    int SiteIndex = 1,
    string? SiteName = null
);

public sealed record ScreenshotNavigationContext(
    DateTimeOffset ObservedAt,
    double Latitude,
    double Longitude,
    int Heading,
    bool HasLatitudeLongitude
);
