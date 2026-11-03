using System.ComponentModel;
using System.Runtime.CompilerServices;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class ScreenshotProcessingViewModel : INotifyPropertyChanged
{
    private readonly ScreenshotProcessingSettingsStore settingsStore;
    private readonly IScreenshotProcessingService processingService;
    private ScreenshotProcessingPreferences preferences;
    private string statusMessage;
    private string? journalDirectory;
    private bool automaticSource;
    private bool outputUninitialized;

    public ScreenshotProcessingViewModel(
        ScreenshotProcessingSettingsStore settingsStore,
        IScreenshotProcessingService? processingService = null,
        string? journalDirectory = null,
        string? pendingQueueDirectory = null,
        bool deferOutputInitialization = false
    )
    {
        this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        this.processingService =
            processingService ?? new ScreenshotProcessingService(pendingQueueDirectory: pendingQueueDirectory);
        this.journalDirectory = journalDirectory;
        bool automaticOutput;
        (preferences, automaticSource, automaticOutput) = settingsStore.LoadWithFolderModes(journalDirectory);
        statusMessage = CreateReadyStatus(preferences);
        outputUninitialized = automaticOutput || !Path.IsPathFullyQualified(preferences.TargetFolder);
        if (outputUninitialized)
        {
            preferences = preferences with
            {
                TargetFolder = ScreenshotProcessingPreferences.CreateDefaults(journalDirectory).TargetFolder,
            };
            if (!deferOutputInitialization)
            {
                InitializeOutputFolder();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void InitializeOutputFolder()
    {
        if (!outputUninitialized)
        {
            return;
        }
        outputUninitialized = false;
        preferences = preferences with
        {
            TargetFolder = ScreenshotProcessingPreferences.CreateDefaults(journalDirectory).TargetFolder,
        };
        try
        {
            preferences = preferences with { TargetFolder = settingsStore.PinOutputFolder(preferences.TargetFolder) };
            StatusMessage = CreateReadyStatus(preferences);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            StatusMessage =
                "The screenshot output folder is fixed for this session but could not be saved: " + exception.Message;
        }
        OnPropertyChanged(nameof(TargetFolder));
    }

    public void UpdateJournalDirectory(string directory)
    {
        if (!string.Equals(journalDirectory, directory, PathComparison))
        {
            journalDirectory = directory;
            var defaults = ScreenshotProcessingPreferences.CreateDefaults(directory);
            ScreenshotProcessingPreferences updated = preferences with
            {
                SourceFolder = automaticSource ? defaults.SourceFolder : preferences.SourceFolder,
            };
            if (preferences != updated)
            {
                preferences = updated;
                OnPropertyChanged(nameof(SourceFolder));
                StatusMessage = CreateReadyStatus(preferences);
            }
        }
        InitializeOutputFolder();
    }

    public void SelectSourceFolder(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        automaticSource = false;
        string next = folder.Trim();
        if (next == preferences.SourceFolder)
        {
            SavePreferences();
        }
        else
        {
            SourceFolder = next;
        }
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public bool Enabled
    {
        get => preferences.Enabled;
        set => Update(preferences with { Enabled = value });
    }

    public bool AddBanner
    {
        get => preferences.AddBanner;
        set => Update(preferences with { AddBanner = value });
    }

    public bool DeleteOriginal
    {
        get => preferences.DeleteOriginal;
        set => Update(preferences with { DeleteOriginal = value });
    }

    public bool UseGuardianAerialFolder
    {
        get => preferences.UseGuardianAerialFolder;
        set => Update(preferences with { UseGuardianAerialFolder = value });
    }

    public bool RotateAlphaAerial
    {
        get => preferences.RotateAlphaAerial;
        set => Update(preferences with { RotateAlphaAerial = value });
    }

    public string SourceFolder
    {
        get => preferences.SourceFolder;
        set
        {
            string next = value?.Trim() ?? string.Empty;
            if (next == preferences.SourceFolder)
            {
                return;
            }
            automaticSource = false;
            Update(preferences with { SourceFolder = next });
        }
    }

    public string TargetFolder
    {
        get => preferences.TargetFolder;
        set
        {
            string next = value?.Trim() ?? string.Empty;
            if (next == preferences.TargetFolder)
            {
                return;
            }
            Update(preferences with { TargetFolder = next });
        }
    }

    public string BannerColor
    {
        get => preferences.BannerColor;
        set => Update(preferences with { BannerColor = value?.Trim() ?? string.Empty });
    }

    public bool BannerLocalTime
    {
        get => preferences.BannerLocalTime;
        set => Update(preferences with { BannerLocalTime = value });
    }

    public double AerialAltitudeAlpha
    {
        get => preferences.AerialAltitudeAlpha;
        set => Update(preferences with { AerialAltitudeAlpha = NormalizeAerialAltitude(value) });
    }

    public double AerialAltitudeBeta
    {
        get => preferences.AerialAltitudeBeta;
        set => Update(preferences with { AerialAltitudeBeta = NormalizeAerialAltitude(value) });
    }

    public double AerialAltitudeGamma
    {
        get => preferences.AerialAltitudeGamma;
        set => Update(preferences with { AerialAltitudeGamma = NormalizeAerialAltitude(value) });
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public async Task OpenOutputFolderAsync(Func<DirectoryInfo, Task<bool>> directoryLauncher)
    {
        ArgumentNullException.ThrowIfNull(directoryLauncher);
        try
        {
            Runtime.DesktopExternalEffectPolicy.ThrowIfDisabled();
            if (!Path.IsPathFullyQualified(TargetFolder))
            {
                throw new InvalidOperationException("The converted screenshot folder must be an absolute path.");
            }
            DirectoryInfo directory = Directory.CreateDirectory(TargetFolder);
            bool launched = await directoryLauncher(directory);
            StatusMessage = launched
                ? "Opened the converted screenshot folder."
                : "The converted screenshot folder could not be opened.";
        }
        catch (Exception exception)
            when (exception
                    is ArgumentException
                        or InvalidOperationException
                        or IOException
                        or NotSupportedException
                        or UnauthorizedAccessException
                        or System.Runtime.InteropServices.ExternalException
            )
        {
            StatusMessage = "The converted screenshot folder could not be opened: " + exception.Message;
        }
    }

    public bool ToggleBanner()
    {
        AddBanner = !AddBanner;
        StatusMessage = AddBanner ? "Screenshot data banners are enabled." : "Screenshot data banners are disabled.";
        return true;
    }

    public async Task<ScreenshotProcessingResult> ProcessJournalEventsAsync(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        string? commanderName,
        IReadOnlyDictionary<JournalEventEnvelope, ScreenshotGuardianContext>? guardianContexts = null,
        ScreenshotNavigationContext? navigationContext = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!processingService.HasPendingWork && !journalEvents.Any(entry => entry.EventName == "Screenshot"))
        {
            return ScreenshotProcessingResult.Empty;
        }
        ScreenshotProcessingResult result = await processingService.ProcessLiveAsync(
            journalEvents,
            preferences,
            commanderName,
            guardianContexts,
            navigationContext,
            cancellationToken
        );
        if (result.Conversions.Count == 0 && result.Warnings.Count == 0)
        {
            return result;
        }

        string converted = result.Conversions.Count switch
        {
            0 => "No screenshots were converted.",
            1 => $"Saved screenshot: {result.Conversions[0].OutputPath}",
            _ => $"Saved {result.Conversions.Count:N0} screenshots.",
        };
        StatusMessage = result.Warnings.Count == 0 ? converted : converted + " " + string.Join(" ", result.Warnings);
        return result;
    }

    private void Update(ScreenshotProcessingPreferences updated, [CallerMemberName] string? propertyName = null)
    {
        if (preferences == updated)
        {
            return;
        }

        preferences = updated;
        OnPropertyChanged(propertyName);
        SavePreferences();
    }

    private void SavePreferences()
    {
        try
        {
            settingsStore.Save(
                preferences,
                journalDirectory,
                useAutomaticSource: automaticSource,
                useAutomaticTarget: false
            );
            StatusMessage = CreateReadyStatus(preferences);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            StatusMessage =
                "The screenshot preference changed for this session but could not be saved: " + exception.Message;
        }
    }

    private static string CreateReadyStatus(ScreenshotProcessingPreferences preferences)
    {
        if (!preferences.Enabled)
        {
            return "Screenshot conversion is off.";
        }

        if (!Path.IsPathFullyQualified(preferences.SourceFolder) || !Directory.Exists(preferences.SourceFolder))
        {
            return "Choose an existing absolute screenshot source folder.";
        }

        if (!Path.IsPathFullyQualified(preferences.TargetFolder))
        {
            return "The converted screenshot folder must be an absolute path.";
        }

        return "New Elite BMP screenshots will be converted to verified PNG files.";
    }

    private static double NormalizeAerialAltitude(double value)
    {
        return double.IsFinite(value) ? Math.Clamp(value, 0, 5_000) : 0;
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
