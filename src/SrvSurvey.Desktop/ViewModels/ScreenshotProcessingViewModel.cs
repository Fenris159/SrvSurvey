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

    public ScreenshotProcessingViewModel(
        ScreenshotProcessingSettingsStore settingsStore,
        IScreenshotProcessingService? processingService = null)
    {
        this.settingsStore = settingsStore
            ?? throw new ArgumentNullException(nameof(settingsStore));
        this.processingService = processingService
            ?? new ScreenshotProcessingService();
        preferences = settingsStore.Load();
        statusMessage = CreateReadyStatus(preferences);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

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
        set => Update(preferences with
        {
            SourceFolder = value?.Trim() ?? string.Empty,
        });
    }

    public string TargetFolder
    {
        get => preferences.TargetFolder;
        set => Update(preferences with
        {
            TargetFolder = value?.Trim() ?? string.Empty,
        });
    }

    public string BannerColor
    {
        get => preferences.BannerColor;
        set => Update(preferences with
        {
            BannerColor = value?.Trim() ?? string.Empty,
        });
    }

    public bool BannerLocalTime
    {
        get => preferences.BannerLocalTime;
        set => Update(preferences with { BannerLocalTime = value });
    }

    public double AerialAltitudeAlpha
    {
        get => preferences.AerialAltitudeAlpha;
        set => Update(preferences with
        {
            AerialAltitudeAlpha = NormalizeAerialAltitude(value),
        });
    }

    public double AerialAltitudeBeta
    {
        get => preferences.AerialAltitudeBeta;
        set => Update(preferences with
        {
            AerialAltitudeBeta = NormalizeAerialAltitude(value),
        });
    }

    public double AerialAltitudeGamma
    {
        get => preferences.AerialAltitudeGamma;
        set => Update(preferences with
        {
            AerialAltitudeGamma = NormalizeAerialAltitude(value),
        });
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public bool ToggleBanner()
    {
        AddBanner = !AddBanner;
        StatusMessage = AddBanner
            ? "Screenshot data banners are enabled."
            : "Screenshot data banners are disabled.";
        return true;
    }

    public async Task<ScreenshotProcessingResult> ProcessJournalEventsAsync(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        string? commanderName,
        IReadOnlyDictionary<JournalEventEnvelope, ScreenshotGuardianContext>?
            guardianContexts = null,
        ScreenshotNavigationContext? navigationContext = null,
        CancellationToken cancellationToken = default)
    {
        var result = await processingService.ProcessAsync(
            journalEvents,
            preferences,
            commanderName,
            guardianContexts,
            navigationContext,
            cancellationToken);
        if (result.Conversions.Count == 0 && result.Warnings.Count == 0)
        {
            return result;
        }

        var converted = result.Conversions.Count switch
        {
            0 => "No screenshots were converted.",
            1 => $"Saved screenshot: {result.Conversions[0].OutputPath}",
            _ => $"Saved {result.Conversions.Count:N0} screenshots to "
                + preferences.TargetFolder
                + ".",
        };
        StatusMessage = result.Warnings.Count == 0
            ? converted
            : converted + " " + string.Join(" ", result.Warnings);
        return result;
    }

    private void Update(
        ScreenshotProcessingPreferences updated,
        [CallerMemberName] string? propertyName = null)
    {
        if (preferences == updated)
        {
            return;
        }

        preferences = updated;
        OnPropertyChanged(propertyName);
        try
        {
            settingsStore.Save(preferences);
            StatusMessage = CreateReadyStatus(preferences);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            StatusMessage =
                "The screenshot preference changed for this session but could not be saved: "
                + exception.Message;
        }
    }

    private static string CreateReadyStatus(
        ScreenshotProcessingPreferences preferences)
    {
        if (!preferences.Enabled)
        {
            return "Screenshot conversion is off.";
        }

        if (!Path.IsPathFullyQualified(preferences.SourceFolder)
            || !Directory.Exists(preferences.SourceFolder))
        {
            return "Choose an existing absolute screenshot source folder.";
        }

        if (!Path.IsPathFullyQualified(preferences.TargetFolder))
        {
            return "Choose an absolute screenshot target folder.";
        }

        return "New Elite BMP screenshots will be converted to verified PNG files.";
    }

    private static double NormalizeAerialAltitude(double value)
    {
        return double.IsFinite(value) ? Math.Clamp(value, 0, 5_000) : 0;
    }

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
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
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}
