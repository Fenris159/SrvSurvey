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
public sealed class ScreenshotProcessingService : IScreenshotProcessingService
{
    private const string UnknownLabel = "unknown";
    private const int FileReadinessAttempts = 150;
    private static readonly TimeSpan FileReadinessPollInterval = TimeSpan.FromMilliseconds(200);

    private readonly Dictionary<string, PendingScreenshot> pending = new(StringComparer.Ordinal);
    private readonly HashSet<string> completed = new(StringComparer.Ordinal);
    private int pendingCount;

    public int PendingCount => Volatile.Read(ref pendingCount);

    private readonly SemaphoreSlim processingLock = new(1, 1);
    private readonly Func<int?> gameClientWidthProvider;
    private readonly Func<TimeSpan, CancellationToken, Task> retryDelay;

    public ScreenshotProcessingService(
        Func<int?>? gameClientWidthProvider = null,
        Func<TimeSpan, CancellationToken, Task>? retryDelay = null
    )
    {
        this.gameClientWidthProvider = gameClientWidthProvider ?? GetGameClientWidth;
        this.retryDelay = retryDelay ?? Task.Delay;
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
            var warnings = new List<string>();
            Enqueue(journalEvents, preferences, commanderName, guardianContexts, navigationContext, warnings);
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
        List<string> warnings
    )
    {
        foreach (JournalEventEnvelope entry in journalEvents.Where(entry => entry.EventName == "Screenshot"))
        {
            string key = entry.SourceRecordIdentity ?? commanderName + "\n" + entry.RawJson;
            if (completed.Contains(key) || pending.ContainsKey(key))
            {
                continue;
            }
            try
            {
                if (!Path.IsPathFullyQualified(preferences.SourceFolder))
                {
                    throw new ArgumentException("The screenshot source folder must be an absolute path.");
                }
                if (!Path.IsPathFullyQualified(preferences.TargetFolder))
                {
                    throw new ArgumentException("The screenshot target folder must be an absolute path.");
                }
                pending.Add(
                    key,
                    new PendingScreenshot(
                        new ScreenshotConversionRequest(
                            entry,
                            preferences,
                            commanderName,
                            guardianContexts?.GetValueOrDefault(entry),
                            navigationContext,
                            gameClientWidthProvider(),
                            ResolveSourcePaths(entry, preferences.SourceFolder),
                            Path.GetFullPath(preferences.TargetFolder)
                        )
                    )
                );
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidDataException)
            {
                warnings.Add(DescribeFailure(entry, exception, retained: false));
                completed.Add(key);
            }
        }
    }

    private async Task<ScreenshotProcessingResult> DrainAsync(
        List<string> warnings,
        bool live,
        CancellationToken cancellationToken
    )
    {
        var conversions = new List<ScreenshotConversion>();
        foreach ((string key, PendingScreenshot capture) in pending.ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                string sourcePath = await WaitForCompletedFileAsync(
                        capture.Request.SourcePaths,
                        live,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                ScreenshotConversion conversion = Convert(capture.Request, sourcePath);
                conversions.Add(conversion);
                if (conversion.Warning is not null)
                {
                    warnings.Add(conversion.Warning);
                }
                pending.Remove(key);
                completed.Add(key);
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

    private static ScreenshotConversion Convert(ScreenshotConversionRequest request, string sourcePath)
    {
        JournalEventEnvelope entry = request.Entry;
        ScreenshotProcessingPreferences preferences = request.Preferences;
        ScreenshotGuardianContext? guardianContext = request.GuardianContext;
        using SKBitmap source =
            DecodeBitmapFile(sourcePath)
            ?? throw new InvalidDataException($"'{sourcePath}' is not a supported bitmap image.");
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
        string outputPath = GetAvailablePath(folder, baseName, ".png");
        WritePngAtomically(output, outputPath);

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
                aerialOutputPath = GetAvailablePath(aerialFolder, baseName, ".png");
                WritePngAtomically(aerial, aerialOutputPath);
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
                    + "the original BMP was retained: "
                    + exception.Message;
            }
        }

        bool sourceDeleted = false;
        if (preferences.DeleteOriginal && warning is null)
        {
            try
            {
                File.Delete(sourcePath);
                sourceDeleted = true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                warning = $"Saved '{outputPath}', but the original BMP could not be removed: " + exception.Message;
            }
        }

        return new ScreenshotConversion(sourcePath, outputPath, sourceDeleted, warning, aerialOutputPath)
        {
            IncludedBanner = preferences.AddBanner,
        };
    }

    private static SKBitmap? DecodeBitmapFile(string path)
    {
        // Managed file streams support long Windows paths without asking the
        // native decoder to reopen a filename through its own file API.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return SKBitmap.Decode(stream);
    }

    private static void WritePngAtomically(SKBitmap bitmap, string outputPath)
    {
        string temporaryPath = $"{outputPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var image = SKImage.FromBitmap(bitmap))
            using (SKData data = image.Encode(SKEncodedImageFormat.Png, 100))
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

            File.Move(temporaryPath, outputPath, false);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
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

    private static string GetAvailablePath(string directory, string baseName, string extension)
    {
        string candidate = Path.Combine(directory, baseName + extension);
        int suffix = 2;
        while (File.Exists(candidate))
        {
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
