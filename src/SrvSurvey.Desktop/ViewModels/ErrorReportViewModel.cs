using System.ComponentModel;
using System.Net;
using System.Runtime.CompilerServices;
using SrvSurvey.Core.Diagnostics;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class ErrorReportViewModel : INotifyPropertyChanged
{
    public static readonly Uri IssuesUri = new(
        "https://github.com/njthomson/SrvSurvey/issues");
    public static readonly Uri DiscordUri = new(
        "https://discord.gg/QZsMu2SkSA");
    private const string NewIssueAddress =
        "https://github.com/njthomson/SrvSurvey/issues/new";
    private readonly Exception exception;
    private readonly string version;
    private string steps = string.Empty;
    private string statusMessage = string.Empty;

    public ErrorReportViewModel(
        Exception exception,
        string version,
        ApplicationLogService? applicationLog = null,
        string? journalPath = null)
    {
        this.exception = exception
            ?? throw new ArgumentNullException(nameof(exception));
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        this.version = version;
        ErrorTitle = $"{exception.GetType().Name}: {exception.Message}";
        ErrorDetails = exception.ToString();
        RecentLogs = string.Join(
            Environment.NewLine,
            applicationLog?.Entries.TakeLast(20) ?? []);
        JournalPath = !string.IsNullOrWhiteSpace(journalPath)
            && File.Exists(journalPath)
                ? Path.GetFullPath(journalPath)
                : null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ErrorTitle { get; }

    public string ErrorDetails { get; }

    public string RecentLogs { get; }

    public string? JournalPath { get; }

    public bool HasJournal => JournalPath is not null;

    public bool HasRecentLogs => !string.IsNullOrWhiteSpace(RecentLogs);

    public string JournalDescription => JournalPath is null
        ? "No current journal file is available."
        : "Current journal: " + Path.GetFileName(JournalPath);

    public string Steps
    {
        get => steps;
        set => SetField(ref steps, value ?? string.Empty);
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set
        {
            if (SetField(ref statusMessage, value))
            {
                OnPropertyChanged(nameof(HasStatusMessage));
            }
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public Uri BuildIssueUri(DateTimeOffset? timestamp = null)
    {
        var form = new Dictionary<string, string>
        {
            ["title"] = $"{exception.GetType().Name} \"{exception.Message}\" at "
                + (timestamp ?? DateTimeOffset.Now),
            ["what-happened"] = Steps,
            ["version"] = version,
            ["exception-message"] = exception.Message,
            ["exception-stack"] = exception.StackTrace ?? ErrorDetails,
        };
        var query = "template=crash-report.yml&" + string.Join(
            "&",
            form.Select(part =>
                $"{part.Key}={WebUtility.UrlEncode(part.Value)}"));
        return new UriBuilder(NewIssueAddress)
        {
            Scheme = Uri.UriSchemeHttps,
            Query = query,
        }.Uri;
    }

    public Task CopyErrorAsync(Func<string, Task> clipboardWriter)
    {
        return CopyAsync(
            clipboardWriter,
            ErrorDetails,
            "Error details copied to the clipboard.");
    }

    public Task CopyRecentLogsAsync(Func<string, Task> clipboardWriter)
    {
        return CopyAsync(
            clipboardWriter,
            RecentLogs,
            "The last 20 application log entries were copied to the clipboard.");
    }

    public Task CopyJournalPathAsync(Func<string, Task> clipboardWriter)
    {
        return CopyAsync(
            clipboardWriter,
            JournalPath ?? string.Empty,
            "The journal path was copied to the clipboard.");
    }

    public Task<bool> OpenIssueAsync(Func<Uri, Task<bool>> uriLauncher)
    {
        return LaunchUriAsync(
            uriLauncher,
            BuildIssueUri(),
            "Opened the prepared GitHub crash report.",
            "The GitHub crash report could not be opened.");
    }

    public Task<bool> OpenIssuesAsync(Func<Uri, Task<bool>> uriLauncher)
    {
        return LaunchUriAsync(
            uriLauncher,
            IssuesUri,
            "Opened the SrvSurvey issue tracker.",
            "The SrvSurvey issue tracker could not be opened.");
    }

    public Task<bool> OpenDiscordAsync(Func<Uri, Task<bool>> uriLauncher)
    {
        return LaunchUriAsync(
            uriLauncher,
            DiscordUri,
            "Opened the SrvSurvey Discord invite.",
            "The Discord invite could not be opened.");
    }

    public async Task<bool> OpenJournalAsync(
        Func<FileInfo, Task<bool>> fileLauncher)
    {
        ArgumentNullException.ThrowIfNull(fileLauncher);
        if (JournalPath is null)
        {
            StatusMessage = "No current journal file is available.";
            return false;
        }

        try
        {
            var launched = await fileLauncher(new FileInfo(JournalPath));
            StatusMessage = launched
                ? "Opened the current journal file."
                : "The current journal file could not be opened.";
            return launched;
        }
        catch (Exception launchException) when (
            IsExpectedPlatformException(launchException))
        {
            StatusMessage = "The current journal file could not be opened: "
                + launchException.Message;
            return false;
        }
    }

    private async Task CopyAsync(
        Func<string, Task> clipboardWriter,
        string text,
        string successMessage)
    {
        ArgumentNullException.ThrowIfNull(clipboardWriter);
        try
        {
            await clipboardWriter(text);
            StatusMessage = successMessage;
        }
        catch (Exception copyException) when (
            IsExpectedPlatformException(copyException))
        {
            StatusMessage = "The text could not be copied: "
                + copyException.Message;
        }
    }

    private async Task<bool> LaunchUriAsync(
        Func<Uri, Task<bool>> uriLauncher,
        Uri uri,
        string successMessage,
        string failureMessage)
    {
        ArgumentNullException.ThrowIfNull(uriLauncher);
        try
        {
            var launched = await uriLauncher(uri);
            StatusMessage = launched ? successMessage : failureMessage;
            return launched;
        }
        catch (Exception launchException) when (
            IsExpectedPlatformException(launchException))
        {
            StatusMessage = failureMessage + " " + launchException.Message;
            return false;
        }
    }

    private static bool IsExpectedPlatformException(Exception exception)
    {
        return exception is InvalidOperationException
            or IOException
            or NotSupportedException
            or System.Runtime.InteropServices.ExternalException
            or UnauthorizedAccessException;
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
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
