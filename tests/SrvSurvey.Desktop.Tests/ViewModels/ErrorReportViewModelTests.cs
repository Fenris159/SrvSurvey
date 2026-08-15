using System.Net;
using SrvSurvey.Core.Diagnostics;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class ErrorReportViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-error-report-{Guid.NewGuid():N}");

    [Fact]
    public void CapturesErrorRecentLogsAndExistingJournalAtCreationTime()
    {
        var log = new ApplicationLogService(temporaryDirectory);
        for (var index = 0; index < 25; index++)
        {
            log.Append($"Entry {index:00}");
        }

        var journalPath = Path.Combine(temporaryDirectory, "Journal.test.log");
        Directory.CreateDirectory(temporaryDirectory);
        File.WriteAllText(journalPath, "journal");
        var exception = CaptureException();

        var viewModel = new ErrorReportViewModel(
            exception,
            "2.0.0",
            log,
            journalPath);

        Assert.Equal("InvalidOperationException: Test failure", viewModel.ErrorTitle);
        Assert.Contains(nameof(CaptureException), viewModel.ErrorDetails);
        Assert.Equal(20, viewModel.RecentLogs.Split(Environment.NewLine).Length);
        Assert.DoesNotContain("Entry 04", viewModel.RecentLogs);
        Assert.Contains("Entry 05", viewModel.RecentLogs);
        Assert.Contains("Entry 24", viewModel.RecentLogs);
        Assert.Equal(Path.GetFullPath(journalPath), viewModel.JournalPath);
        Assert.True(viewModel.HasJournal);
    }

    [Fact]
    public void BuildsLegacyCrashReportTemplateUrlWithCurrentSteps()
    {
        var exception = CaptureException();
        var viewModel = new ErrorReportViewModel(exception, "2.0.0")
        {
            Steps = "Jumped to Sol & opened the map",
        };

        var uri = viewModel.BuildIssueUri(
            DateTimeOffset.Parse("2026-07-25T13:14:15-05:00"));
        var decodedQuery = WebUtility.UrlDecode(uri.Query);

        Assert.Equal("github.com", uri.Host);
        Assert.Equal("/Fenris159/SrvSurvey/issues/new", uri.AbsolutePath);
        Assert.Equal(
            "https://github.com/Fenris159/SrvSurvey/issues",
            ErrorReportViewModel.IssuesUri.AbsoluteUri.TrimEnd('/'));
        Assert.Contains("template=crash-report.yml", decodedQuery);
        Assert.Contains("what-happened=Jumped to Sol & opened the map", decodedQuery);
        Assert.Contains("version=2.0.0", decodedQuery);
        Assert.Contains("exception-message=Test failure", decodedQuery);
        Assert.Contains(nameof(CaptureException), decodedQuery);
    }

    [Fact]
    public async Task ClipboardAndLaunchActionsReportTheirOutcome()
    {
        var journalPath = Path.Combine(temporaryDirectory, "Journal.test.log");
        Directory.CreateDirectory(temporaryDirectory);
        File.WriteAllText(journalPath, "journal");
        var viewModel = new ErrorReportViewModel(
            CaptureException(),
            "2.0.0",
            journalPath: journalPath);
        string? copied = null;
        Uri? launchedUri = null;
        FileInfo? launchedFile = null;

        await viewModel.CopyErrorAsync(text =>
        {
            copied = text;
            return Task.CompletedTask;
        });
        var issueOpened = await viewModel.OpenIssueAsync(uri =>
        {
            launchedUri = uri;
            return Task.FromResult(true);
        });
        var journalOpened = await viewModel.OpenJournalAsync(file =>
        {
            launchedFile = file;
            return Task.FromResult(true);
        });

        Assert.Contains("Test failure", copied);
        Assert.True(issueOpened);
        Assert.Contains("crash-report.yml", launchedUri?.Query);
        Assert.True(journalOpened);
        Assert.Equal(Path.GetFullPath(journalPath), launchedFile?.FullName);
        Assert.Equal("Opened the current journal file.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task MissingJournalIsNotExposedOrLaunched()
    {
        var viewModel = new ErrorReportViewModel(
            CaptureException(),
            "2.0.0",
            journalPath: Path.Combine(temporaryDirectory, "missing.log"));
        var launcherCalled = false;

        var launched = await viewModel.OpenJournalAsync(_ =>
        {
            launcherCalled = true;
            return Task.FromResult(true);
        });

        Assert.False(viewModel.HasJournal);
        Assert.False(launched);
        Assert.False(launcherCalled);
        Assert.Equal(
            "No current journal file is available.",
            viewModel.StatusMessage);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private static Exception CaptureException()
    {
        try
        {
            throw new InvalidOperationException("Test failure");
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
