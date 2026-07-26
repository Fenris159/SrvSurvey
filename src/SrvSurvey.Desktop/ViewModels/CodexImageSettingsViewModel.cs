using System.ComponentModel;
using System.Runtime.CompilerServices;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class CodexImageSettingsViewModel : INotifyPropertyChanged
{
    private readonly CodexImageSettingsStore settingsStore;
    private readonly string defaultCacheDirectory;
    private CodexImagePreferences preferences;
    private string statusMessage;
    private bool isPreDownloading;

    public CodexImageSettingsViewModel(
        CodexImageSettingsStore settingsStore,
        ExobiologyReferenceCatalog catalog,
        string defaultCacheDirectory)
    {
        this.settingsStore = settingsStore
            ?? throw new ArgumentNullException(nameof(settingsStore));
        ArgumentNullException.ThrowIfNull(catalog);
        this.defaultCacheDirectory = Path.GetFullPath(
            string.IsNullOrWhiteSpace(defaultCacheDirectory)
                ? throw new ArgumentException(
                    "A default Codex image cache directory is required.",
                    nameof(defaultCacheDirectory))
                : defaultCacheDirectory);
        BiologyEntries = catalog.BiologyEntries;
        preferences = settingsStore.Load();
        statusMessage = CreateReadyStatus(preferences);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<ExobiologyReference> BiologyEntries { get; }

    public string CacheDirectory
    {
        get => preferences.CacheDirectory;
        set => Update(preferences with
        {
            CacheDirectory = value?.Trim() ?? string.Empty,
        });
    }

    public string LocalFloraDirectory
    {
        get => preferences.LocalFloraDirectory ?? string.Empty;
        set => Update(preferences with
        {
            LocalFloraDirectory = string.IsNullOrWhiteSpace(value)
                ? null
                : value.Trim(),
        });
    }

    public bool PreDownload
    {
        get => preferences.PreDownload;
        set => Update(preferences with { PreDownload = value });
    }

    public bool IsPreDownloading
    {
        get => isPreDownloading;
        private set => SetField(ref isPreDownloading, value);
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public string EffectiveCacheDirectory =>
        TryGetAbsolutePath(CacheDirectory) ?? defaultCacheDirectory;

    public string? EffectiveLocalFloraDirectory =>
        TryGetAbsolutePath(LocalFloraDirectory);

    internal void SetPreDownloadStatus(
        bool active,
        string message)
    {
        IsPreDownloading = active;
        StatusMessage = message;
    }

    internal void SetReadyStatus()
    {
        IsPreDownloading = false;
        StatusMessage = CreateReadyStatus(preferences);
    }

    private void Update(CodexImagePreferences updated)
    {
        if (preferences == updated)
        {
            return;
        }

        preferences = updated;
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
                "The Codex image preference changed for this session but could not be saved: "
                + exception.Message;
        }

        OnPropertyChanged(string.Empty);
    }

    private string CreateReadyStatus(CodexImagePreferences value)
    {
        var cache = TryGetAbsolutePath(value.CacheDirectory);
        if (cache is null)
        {
            return "The configured Codex cache path is not absolute; downloads will use "
                + defaultCacheDirectory
                + ".";
        }

        if (!string.IsNullOrWhiteSpace(value.LocalFloraDirectory)
            && TryGetAbsolutePath(value.LocalFloraDirectory) is null)
        {
            return "The local flora path is not absolute. Cached and remote Codex images remain available.";
        }

        return value.PreDownload
            ? "Codex biology images will be downloaded in the background to "
                + cache
                + "."
            : "Codex images are downloaded on demand to " + cache + ".";
    }

    private static string? TryGetAbsolutePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !Path.IsPathFullyQualified(value))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(value);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            return null;
        }
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
