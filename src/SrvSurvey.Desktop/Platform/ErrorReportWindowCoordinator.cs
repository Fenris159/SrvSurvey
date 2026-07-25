using Avalonia.Controls;
using Avalonia.Threading;
using SrvSurvey.Core.Diagnostics;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform;

public sealed class ErrorReportWindowCoordinator : IDisposable
{
    private readonly Window owner;
    private readonly ApplicationLogService applicationLog;
    private readonly Func<string?> getJournalPath;
    private readonly Action showLogs;
    private ErrorReportWindow? window;
    private bool disposed;

    public ErrorReportWindowCoordinator(
        Window owner,
        ApplicationLogService applicationLog,
        Func<string?> getJournalPath,
        Action showLogs)
    {
        this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
        this.applicationLog = applicationLog
            ?? throw new ArgumentNullException(nameof(applicationLog));
        this.getJournalPath = getJournalPath
            ?? throw new ArgumentNullException(nameof(getJournalPath));
        this.showLogs = showLogs
            ?? throw new ArgumentNullException(nameof(showLogs));
    }

    public bool IsVisible => window is not null;

    public void Show(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        applicationLog.Append("Error report opened: " + exception);
        if (disposed)
        {
            return;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => TryShowWindow(exception));
            return;
        }

        TryShowWindow(exception);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        var errorWindow = window;
        window = null;
        if (errorWindow is not null)
        {
            errorWindow.Closed -= OnWindowClosed;
            errorWindow.Close();
        }
    }

    private void TryShowWindow(Exception exception)
    {
        try
        {
            ShowWindow(exception);
        }
        catch (Exception reportException)
        {
            applicationLog.Append(
                "The error-report window could not be opened: "
                + reportException);
        }
    }

    private void ShowWindow(Exception exception)
    {
        if (disposed)
        {
            return;
        }

        if (window is not null)
        {
            window.Activate();
            return;
        }

        var version = typeof(ErrorReportWindowCoordinator).Assembly
            .GetName()
            .Version?
            .ToString() ?? "unknown";
        var viewModel = new ErrorReportViewModel(
            exception,
            version,
            applicationLog,
            getJournalPath());
        var errorWindow = new ErrorReportWindow(viewModel, showLogs);
        errorWindow.Closed += OnWindowClosed;
        window = errorWindow;
        errorWindow.Show(owner);
    }

    private void OnWindowClosed(object? sender, EventArgs eventArgs)
    {
        if (sender is ErrorReportWindow errorWindow)
        {
            errorWindow.Closed -= OnWindowClosed;
            if (ReferenceEquals(window, errorWindow))
            {
                window = null;
            }
        }
    }
}
