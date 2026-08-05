using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using SkiaSharp;
using SrvSurvey.Core.Guardian;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Platform;

public interface IScreenshotProcessingService
{
    Task<ScreenshotProcessingResult> ProcessAsync(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        ScreenshotProcessingPreferences preferences,
        string? commanderName,
        IReadOnlyDictionary<JournalEventEnvelope, ScreenshotGuardianContext>?
            guardianContexts = null,
        ScreenshotNavigationContext? navigationContext = null,
        CancellationToken cancellationToken = default);
}

public sealed class ScreenshotProcessingService : IScreenshotProcessingService
{
    private readonly SemaphoreSlim processingLock = new(1, 1);
    private readonly Func<int?> primaryWorkingAreaWidthProvider;

    public ScreenshotProcessingService(
        Func<int?>? primaryWorkingAreaWidthProvider = null)
    {
        this.primaryWorkingAreaWidthProvider = primaryWorkingAreaWidthProvider
            ?? GetPrimaryWorkingAreaWidth;
    }

    public static string GetSystemFolderPath(
        string targetFolder,
        string systemName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(systemName);
        return Path.Combine(
            Path.GetFullPath(targetFolder),
            SafeFileName(systemName));
    }

    public async Task<ScreenshotProcessingResult> ProcessAsync(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        ScreenshotProcessingPreferences preferences,
        string? commanderName,
        IReadOnlyDictionary<JournalEventEnvelope, ScreenshotGuardianContext>?
            guardianContexts = null,
        ScreenshotNavigationContext? navigationContext = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        ArgumentNullException.ThrowIfNull(preferences);
        if (!preferences.Enabled)
        {
            return ScreenshotProcessingResult.Empty;
        }

        var screenshots = journalEvents
            .Where(entry => entry.EventName == "Screenshot")
            .ToArray();
        if (screenshots.Length == 0)
        {
            return ScreenshotProcessingResult.Empty;
        }

        await processingLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ProcessCoreAsync(
                screenshots,
                preferences,
                commanderName,
                guardianContexts,
                navigationContext,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            processingLock.Release();
        }
    }

    private async Task<ScreenshotProcessingResult> ProcessCoreAsync(
        IReadOnlyList<JournalEventEnvelope> screenshots,
        ScreenshotProcessingPreferences preferences,
        string? commanderName,
        IReadOnlyDictionary<JournalEventEnvelope, ScreenshotGuardianContext>?
            guardianContexts,
        ScreenshotNavigationContext? navigationContext,
        CancellationToken cancellationToken)
    {
        var conversions = new List<ScreenshotConversion>();
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(preferences.SourceFolder)
            || !Path.IsPathFullyQualified(preferences.SourceFolder))
        {
            return new ScreenshotProcessingResult(
                conversions,
                ["The screenshot source folder must be an absolute path."]);
        }

        if (string.IsNullOrWhiteSpace(preferences.TargetFolder)
            || !Path.IsPathFullyQualified(preferences.TargetFolder))
        {
            return new ScreenshotProcessingResult(
                conversions,
                ["The screenshot target folder must be an absolute path."]);
        }

        var sourceDirectory = Path.GetFullPath(preferences.SourceFolder);
        var targetDirectory = Path.GetFullPath(preferences.TargetFolder);
        if (!Directory.Exists(sourceDirectory))
        {
            return new ScreenshotProcessingResult(
                conversions,
                [$"The screenshot source folder does not exist: {sourceDirectory}"]);
        }

        foreach (var entry in screenshots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var guardianContext = guardianContexts?.GetValueOrDefault(entry);
                var conversion = await ConvertAsync(
                    new ScreenshotConversionRequest(
                        entry,
                        preferences,
                        commanderName,
                        guardianContext,
                        navigationContext,
                        primaryWorkingAreaWidthProvider(),
                        sourceDirectory,
                        targetDirectory),
                    cancellationToken).ConfigureAwait(false);
                conversions.Add(conversion);
                if (conversion.Warning is not null)
                {
                    warnings.Add(conversion.Warning);
                }
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or InvalidDataException
                    or ArgumentException)
            {
                warnings.Add(
                    $"Screenshot {entry.Timestamp?.ToString("u", CultureInfo.InvariantCulture) ?? "with unknown time"} "
                    + "was not converted: "
                    + exception.Message);
            }
        }

        return new ScreenshotProcessingResult(conversions, warnings);
    }

    private readonly record struct ScreenshotConversionRequest(
        JournalEventEnvelope Entry,
        ScreenshotProcessingPreferences Preferences,
        string? CommanderName,
        ScreenshotGuardianContext? GuardianContext,
        ScreenshotNavigationContext? NavigationContext,
        int? PrimaryWorkingAreaWidth,
        string SourceDirectory,
        string TargetDirectory);

    private static async Task<ScreenshotConversion> ConvertAsync(
        ScreenshotConversionRequest request,
        CancellationToken cancellationToken)
    {
        var entry = request.Entry;
        var preferences = request.Preferences;
        var guardianContext = request.GuardianContext;
        var sourcePath = ResolveSourcePath(entry, request.SourceDirectory);
        await WaitForCompletedFileAsync(sourcePath, cancellationToken)
            .ConfigureAwait(false);

        using var source = SKBitmap.Decode(sourcePath)
            ?? throw new InvalidDataException(
                $"'{sourcePath}' is not a supported bitmap image.");
        using var output = source.Copy()
            ?? throw new InvalidDataException(
                $"'{sourcePath}' could not be copied for conversion.");
        if (preferences.AddBanner)
        {
            DrawBanner(
                output,
                entry,
                preferences,
                request.CommanderName,
                guardianContext,
                request.NavigationContext);
        }

        var systemName = GetString(entry, "System") ?? "unknown";
        var bodyName = GetString(entry, "Body") ?? "unknown";
        var timestamp = entry.Timestamp ?? DateTimeOffset.UtcNow;
        var folder = GetSystemFolderPath(request.TargetDirectory, systemName);
        Directory.CreateDirectory(folder);
        var baseName = SafeFileName(
            $"{bodyName} ({timestamp.UtcDateTime:yyyy-MM-dd HHmmss})"
            + GetGuardianFileSuffix(guardianContext)
            + GetHighResolutionSuffix(entry, request.PrimaryWorkingAreaWidth));
        var outputPath = GetAvailablePath(folder, baseName, ".png");
        WritePngAtomically(output, outputPath);

        string? warning = null;
        string? aerialOutputPath = null;
        if (IsGuardianAerial(preferences, guardianContext))
        {
            try
            {
                using var aerial = CreateAerialBitmap(
                    source,
                    guardianContext!.SiteType,
                    preferences.RotateAlphaAerial);
                DrawBanner(
                    aerial,
                    entry,
                    preferences,
                    request.CommanderName,
                    guardianContext,
                    request.NavigationContext);
                var aerialFolder = Path.Combine(
                    request.TargetDirectory,
                    SafeFileName("Aerial " + guardianContext.SiteType));
                Directory.CreateDirectory(aerialFolder);
                aerialOutputPath = GetAvailablePath(
                    aerialFolder,
                    baseName,
                    ".png");
                WritePngAtomically(aerial, aerialOutputPath);
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or InvalidDataException
                    or ArgumentException)
            {
                warning = $"Saved '{outputPath}', but the Guardian aerial copy failed; "
                    + "the original BMP was retained: "
                    + exception.Message;
            }
        }

        var sourceDeleted = false;
        if (preferences.DeleteOriginal && warning is null)
        {
            try
            {
                File.Delete(sourcePath);
                sourceDeleted = true;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                warning = $"Saved '{outputPath}', but the original BMP could not be removed: "
                    + exception.Message;
            }
        }

        return new ScreenshotConversion(
            sourcePath,
            outputPath,
            sourceDeleted,
            warning,
            aerialOutputPath);
    }

    private static void WritePngAtomically(SKBitmap bitmap, string outputPath)
    {
        var temporaryPath = $"{outputPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var image = SKImage.FromBitmap(bitmap))
            using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                data.SaveTo(stream);
                stream.Flush(true);
            }

            using (var verified = SKBitmap.Decode(temporaryPath))
            {
                if (verified is null
                    || verified.Width != bitmap.Width
                    || verified.Height != bitmap.Height)
                {
                    throw new InvalidDataException(
                        "The converted PNG could not be verified.");
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
        ScreenshotGuardianContext? context)
    {
        return preferences.UseGuardianAerialFolder
            && context is not null
            && !string.IsNullOrWhiteSpace(context.SiteType)
            && context.DistanceFromOrigin is >= 0 and < 50
            && context.Altitude is > 500 and < 2000;
    }

    private static string GetGuardianFileSuffix(
        ScreenshotGuardianContext? context)
    {
        if (context is null || string.IsNullOrWhiteSpace(context.SiteType))
        {
            return string.Empty;
        }

        return context.SiteKind == GuardianSiteKind.Ruins
            ? $", Ruins{context.SiteIndex} {context.SiteType}"
            : $", {context.SiteType}";
    }

    private static string GetHighResolutionSuffix(
        JournalEventEnvelope entry,
        int? primaryWorkingAreaWidth)
    {
        return primaryWorkingAreaWidth is > 0
            && entry.Payload.TryGetProperty("Width", out var width)
            && width.TryGetInt32(out var screenshotWidth)
            && screenshotWidth > primaryWorkingAreaWidth
                ? " (HighRes)"
                : string.Empty;
    }

    private static SKBitmap CreateAerialBitmap(
        SKBitmap source,
        string siteType,
        bool rotateAlpha)
    {
        if (!rotateAlpha
            || !string.Equals(siteType, "Alpha", StringComparison.OrdinalIgnoreCase))
        {
            return source.Copy()
                ?? throw new InvalidDataException(
                    "The Guardian aerial bitmap could not be copied.");
        }

        var cropWidth = Math.Min(
            source.Width,
            Math.Max(1, (int)(source.Height * 1.3f)));
        using var cropped = new SKBitmap(cropWidth, source.Height);
        using (var cropCanvas = new SKCanvas(cropped))
        {
            cropCanvas.Clear(SKColors.Black);
            var sourceX = (source.Width - cropWidth) / 2;
            cropCanvas.DrawBitmap(
                source,
                new SKRect(sourceX, 0, sourceX + cropWidth, source.Height),
                new SKRect(0, 0, cropWidth, source.Height));
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

    private static string ResolveSourcePath(
        JournalEventEnvelope entry,
        string sourceDirectory)
    {
        var journalPath = GetString(entry, "Filename")
            ?? throw new InvalidDataException(
                "The Screenshot event has no Filename.");
        var normalized = journalPath.Replace('\\', '/');
        var fileName = Path.GetFileName(normalized);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidDataException(
                "The Screenshot event filename is invalid.");
        }

        return Path.Combine(sourceDirectory, fileName);
    }

    private static async Task WaitForCompletedFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        long previousLength = -1;
        var stableReads = 0;
        for (var attempt = 0; attempt < 25; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);
                if (stream.Length > 0 && stream.Length == previousLength)
                {
                    stableReads++;
                    if (stableReads >= 2)
                    {
                        return;
                    }
                }
                else
                {
                    stableReads = 0;
                }

                previousLength = stream.Length;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                lastError = exception;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken)
                .ConfigureAwait(false);
        }

        throw new IOException(
            $"The source bitmap did not become readable: {path}",
            lastError);
    }

    private static string ResolveGuardianBannerSiteName(
        ScreenshotGuardianContext guardianContext)
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
        ScreenshotNavigationContext? navigationContext)
    {
        using var canvas = new SKCanvas(bitmap);
        using var typeface = SKTypeface.Default;
        var scale = Math.Clamp(bitmap.Width / 1920f, 0.6f, 3f);
        using var titleFont = new SKFont(typeface, 30f * scale);
        using var detailFont = new SKFont(typeface, 18f * scale);
        using var background = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 220),
            IsAntialias = true,
        };
        using var foreground = new SKPaint
        {
            Color = ParseColor(preferences.BannerColor),
            IsAntialias = true,
        };
        var body = GetString(entry, "Body") ?? "unknown";
        var system = GetString(entry, "System") ?? "unknown";
        var timestamp = entry.Timestamp ?? DateTimeOffset.UtcNow;
        var displayedTime = preferences.BannerLocalTime
            ? timestamp.ToLocalTime().ToString("G", CultureInfo.CurrentCulture)
            : timestamp.ToUniversalTime().ToString("u", CultureInfo.InvariantCulture);
        var details = new List<string>
        {
            $"System: {system}",
            $"Cmdr: {commanderName ?? "unknown"} - {displayedTime}",
        };
        if (guardianContext is not null)
        {
            var siteName = ResolveGuardianBannerSiteName(guardianContext);
            details.Add($"{siteName} - {guardianContext.SiteType}");
        }
        var location = CreateLocationLine(entry, navigationContext);
        if (location is not null)
        {
            details.Add(location);
        }

        var padding = 12f * scale;
        var gap = 6f * scale;
        var lineHeight = detailFont.Size * 1.3f;
        var title = $"Body: {body}";
        var width = Math.Max(
            titleFont.MeasureText(title),
            details.Max(line => detailFont.MeasureText(line)));
        var height = padding * 2
            + titleFont.Size
            + gap
            + (details.Count * lineHeight);
        canvas.DrawRect(
            10f * scale,
            10f * scale,
            width + (padding * 2),
            height,
            background);
        var x = 10f * scale + padding;
        var y = 10f * scale + padding + titleFont.Size;
        canvas.DrawText(title, x, y, SKTextAlign.Left, titleFont, foreground);
        y += gap + lineHeight;
        foreach (var line in details)
        {
            canvas.DrawText(line, x, y, SKTextAlign.Left, detailFont, foreground);
            y += lineHeight;
        }
    }

    internal static string? CreateLocationLine(
        JournalEventEnvelope entry,
        ScreenshotNavigationContext? navigationContext)
    {
        if (entry.Timestamp is not { } timestamp
            || navigationContext is not { HasLatitudeLongitude: true } status)
        {
            return null;
        }

        var age = status.ObservedAt - timestamp;
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
        ICollection<string> values,
        JournalEventEnvelope entry,
        string propertyName,
        string label,
        string suffix,
        int decimals,
        double? fallback = null)
    {
        var number = fallback;
        if (entry.Payload.TryGetProperty(propertyName, out var property)
            && property.ValueKind == System.Text.Json.JsonValueKind.Number
            && property.TryGetDouble(out var eventNumber)
            && double.IsFinite(eventNumber))
        {
            number = eventNumber;
        }
        if (number is not { } value || !double.IsFinite(value))
        {
            return;
        }

        values.Add(
            $"{label}: {value.ToString($"F{decimals}", CultureInfo.InvariantCulture)}{suffix}");
    }

    private static int? GetPrimaryWorkingAreaWidth()
    {
        return Application.Current?.ApplicationLifetime
                is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window }
            ? window.Screens.Primary?.WorkingArea.Width
            : null;
    }

    private static SKColor ParseColor(string value)
    {
        if (SKColor.TryParse(value, out var color))
        {
            return color;
        }

        return SKColors.Yellow;
    }

    private static string? GetString(
        JournalEventEnvelope entry,
        string propertyName)
    {
        return entry.Payload.TryGetProperty(propertyName, out var property)
            && property.ValueKind == System.Text.Json.JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static string GetAvailablePath(
        string directory,
        string baseName,
        string extension)
    {
        var candidate = Path.Combine(directory, baseName + extension);
        var suffix = 2;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(
                directory,
                $"{baseName} ({suffix++}){extension}");
        }

        return candidate;
    }

    private static string SafeFileName(string value)
    {
        var invalid = "<>:\"/\\|?*";
        var result = new StringBuilder(value.Length);
        foreach (var character in value.Trim())
        {
            result.Append(character < ' ' || invalid.Contains(character)
                ? '_'
                : character);
        }

        var safe = result.ToString().TrimEnd(' ', '.');
        return string.IsNullOrWhiteSpace(safe) ? "unknown" : safe;
    }
}

public sealed record ScreenshotProcessingResult(
    IReadOnlyList<ScreenshotConversion> Conversions,
    IReadOnlyList<string> Warnings)
{
    public static ScreenshotProcessingResult Empty { get; } = new([], []);
}

public sealed record ScreenshotConversion(
    string SourcePath,
    string OutputPath,
    bool SourceDeleted,
    string? Warning,
    string? AerialOutputPath = null);

public sealed record ScreenshotGuardianContext(
    string SiteType,
    double? DistanceFromOrigin,
    double? Altitude,
    GuardianSiteKind SiteKind = GuardianSiteKind.Structure,
    int SiteIndex = 1,
    string? SiteName = null);

public sealed record ScreenshotNavigationContext(
    DateTimeOffset ObservedAt,
    double Latitude,
    double Longitude,
    int Heading,
    bool HasLatitudeLongitude);
