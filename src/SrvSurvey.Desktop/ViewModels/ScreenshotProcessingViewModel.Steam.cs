using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class ScreenshotProcessingViewModel
{
    private readonly SteamScreenshotSource steamSource;
    private readonly TimeProvider steamClock;
    private string? detectedSteamFolder;
    private DateTimeOffset? lastSteamScan;
    private string? lastSteamWarning;

    public bool ProcessSteamScreenshots
    {
        get => preferences.ProcessSteamScreenshots;
        set
        {
            if (value == preferences.ProcessSteamScreenshots)
            {
                return;
            }
            Update(preferences with { ProcessSteamScreenshots = value });
            lastSteamScan = null;
            RefreshSteamSource();
        }
    }

    public string SteamSourceFolder => preferences.SteamSourceFolder ?? detectedSteamFolder ?? string.Empty;

    public string SteamSourceDescription
    {
        get
        {
            if (SteamSourceFolder.Length > 0)
            {
                return "Steam JPEGs are matched by capture time to journal locations. Captures without a matching session are left untouched.";
            }
            return ProcessSteamScreenshots
                ? "No unambiguous Steam screenshot folder was detected. Choose the Elite screenshot folder for your Steam account."
                : "Enable Steam processing to detect the screenshot folder for your Steam account.";
        }
    }

    public void SelectSteamSourceFolder(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        if (!Path.IsPathFullyQualified(folder))
        {
            throw new ArgumentException("The Steam screenshot folder must be an absolute path.", nameof(folder));
        }
        Update(preferences with { SteamSourceFolder = Path.GetFullPath(folder) }, nameof(SteamSourceFolder));
        lastSteamScan = null;
        RefreshSteamSource();
    }

    public void DetectSteamSourceFolder()
    {
        Update(preferences with { SteamSourceFolder = null }, nameof(SteamSourceFolder));
        lastSteamScan = null;
        RefreshSteamSource(forceDetection: true);
    }

    private void RefreshSteamSource(bool forceDetection = false)
    {
        detectedSteamFolder =
            (preferences.ProcessSteamScreenshots || forceDetection) && preferences.SteamSourceFolder is null
                ? steamSource.FindFolder(journalDirectory)
                : null;
        OnPropertyChanged(nameof(SteamSourceFolder));
        OnPropertyChanged(nameof(SteamSourceDescription));
    }

    private async Task<ScreenshotProcessingResult> ProcessSteamScreenshotsAsync(
        string? frontierId,
        CancellationToken cancellationToken
    )
    {
        if (
            !preferences.Enabled
            || !preferences.ProcessSteamScreenshots
            || string.IsNullOrWhiteSpace(frontierId)
            || string.IsNullOrWhiteSpace(journalDirectory)
        )
        {
            return ScreenshotProcessingResult.Empty;
        }
        DateTimeOffset now = steamClock.GetUtcNow();
        if (lastSteamScan is { } previous && now - previous < TimeSpan.FromSeconds(5))
        {
            return ScreenshotProcessingResult.Empty;
        }
        lastSteamScan = now;
        RefreshSteamSource();
        if (SteamSourceFolder.Length == 0)
        {
            return ScreenshotProcessingResult.Empty;
        }
        string source = SteamSourceFolder;
        ScreenshotProcessingPreferences capturedPreferences = preferences with { SourceFolder = source };
        var conversions = new List<ScreenshotConversion>();
        var warnings = new List<string>();
        try
        {
            SteamScreenshotBatch batch = await steamSource.ReadAsync(
                source,
                journalDirectory,
                frontierId,
                cancellationToken
            );
            warnings.AddRange(batch.Warnings);
            foreach (SteamScreenshotCapture capture in batch.Captures)
            {
                ScreenshotProcessingResult converted = await processingService.ProcessLiveAsync(
                    [capture.Entry],
                    capturedPreferences,
                    capture.CommanderName,
                    cancellationToken: cancellationToken
                );
                conversions.AddRange(converted.Conversions);
                warnings.AddRange(converted.Warnings);
                if (converted.Conversions.Count > 0)
                {
                    // Bound completed-image work per poll so a historical library cannot hold up journal projection.
                    break;
                }
            }
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            warnings.Add("Steam screenshots could not be processed; originals were retained: " + exception.Message);
        }
        string warning = string.Join(" ", warnings.Distinct(StringComparer.Ordinal));
        string[] newWarnings = warning.Length > 0 && warning != lastSteamWarning ? [warning] : [];
        lastSteamWarning = warning;
        return new(conversions, newWarnings);
    }
}
