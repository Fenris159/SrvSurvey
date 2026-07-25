using System.Globalization;
using System.Text;
using SkiaSharp;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Platform;

public sealed class ScreenshotProcessingService
{
    private readonly SemaphoreSlim processingLock = new(1, 1);

    public async Task<ScreenshotProcessingResult> ProcessAsync(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        ScreenshotProcessingPreferences preferences,
        string? commanderName,
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
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            processingLock.Release();
        }
    }

    private static async Task<ScreenshotProcessingResult> ProcessCoreAsync(
        IReadOnlyList<JournalEventEnvelope> screenshots,
        ScreenshotProcessingPreferences preferences,
        string? commanderName,
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
                var conversion = await ConvertAsync(
                    entry,
                    preferences,
                    commanderName,
                    sourceDirectory,
                    targetDirectory,
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

    private static async Task<ScreenshotConversion> ConvertAsync(
        JournalEventEnvelope entry,
        ScreenshotProcessingPreferences preferences,
        string? commanderName,
        string sourceDirectory,
        string targetDirectory,
        CancellationToken cancellationToken)
    {
        var sourcePath = ResolveSourcePath(entry, sourceDirectory);
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
            DrawBanner(output, entry, preferences, commanderName);
        }

        var systemName = GetString(entry, "System") ?? "unknown";
        var bodyName = GetString(entry, "Body") ?? "unknown";
        var timestamp = entry.Timestamp ?? DateTimeOffset.UtcNow;
        var folder = Path.Combine(targetDirectory, SafeFileName(systemName));
        Directory.CreateDirectory(folder);
        var baseName = SafeFileName(
            $"{bodyName} ({timestamp.UtcDateTime:yyyy-MM-dd HHmmss})");
        var outputPath = GetAvailablePath(folder, baseName, ".png");
        var temporaryPath = $"{outputPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var image = SKImage.FromBitmap(output))
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
                    || verified.Width != output.Width
                    || verified.Height != output.Height)
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

        string? warning = null;
        var sourceDeleted = false;
        if (preferences.DeleteOriginal)
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
            warning);
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

    private static void DrawBanner(
        SKBitmap bitmap,
        JournalEventEnvelope entry,
        ScreenshotProcessingPreferences preferences,
        string? commanderName)
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
        var location = CreateLocationLine(entry);
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

    private static string? CreateLocationLine(JournalEventEnvelope entry)
    {
        var values = new List<string>();
        AddNumber(values, entry, "Latitude", "Lat", "°", 6);
        AddNumber(values, entry, "Longitude", "Long", "°", 6);
        AddNumber(values, entry, "Heading", "Heading", "°", 0);
        AddNumber(values, entry, "Altitude", "Altitude", "m", 0);
        return values.Count == 0 ? null : string.Join("  ", values);
    }

    private static void AddNumber(
        ICollection<string> values,
        JournalEventEnvelope entry,
        string propertyName,
        string label,
        string suffix,
        int decimals)
    {
        if (!entry.Payload.TryGetProperty(propertyName, out var property)
            || property.ValueKind != System.Text.Json.JsonValueKind.Number
            || !property.TryGetDouble(out var number)
            || !double.IsFinite(number))
        {
            return;
        }

        values.Add(
            $"{label}: {number.ToString($"F{decimals}", CultureInfo.InvariantCulture)}{suffix}");
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
    string? Warning);
