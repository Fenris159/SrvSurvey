using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class ErrorReportWindow : Window
{
    private readonly ErrorReportViewModel viewModel;
    private readonly Action showLogs;

    public ErrorReportWindow()
        : this(
            new ErrorReportViewModel(
                new InvalidOperationException("Design-time error"),
                "0.0.0"),
            () => { })
    {
    }

    public ErrorReportWindow(
        ErrorReportViewModel viewModel,
        Action showLogs)
    {
        this.viewModel = viewModel
            ?? throw new ArgumentNullException(nameof(viewModel));
        this.showLogs = showLogs
            ?? throw new ArgumentNullException(nameof(showLogs));
        InitializeComponent();
        DataContext = viewModel;
        KeyDown += OnWindowKeyDown;
    }

    private async void CopyError_Click(object? sender, RoutedEventArgs eventArgs)
    {
        await viewModel.CopyErrorAsync(WriteClipboardAsync);
    }

    private async void CopyRecentLogs_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        await viewModel.CopyRecentLogsAsync(WriteClipboardAsync);
    }

    private async void CopyJournalPath_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        await viewModel.CopyJournalPathAsync(WriteClipboardAsync);
    }

    private async void OpenJournal_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        await viewModel.OpenJournalAsync(Launcher.LaunchFileInfoAsync);
    }

    private async void CreateIssue_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        await viewModel.OpenIssueAsync(Launcher.LaunchUriAsync);
    }

    private async void OpenDiscord_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        await viewModel.OpenDiscordAsync(Launcher.LaunchUriAsync);
    }

    private void ViewLogs_Click(object? sender, RoutedEventArgs eventArgs)
    {
        Close();
        showLogs();
    }

    private void Close_Click(object? sender, RoutedEventArgs eventArgs)
    {
        Close();
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Escape)
        {
            Close();
            eventArgs.Handled = true;
        }
    }

    private async Task WriteClipboardAsync(string text)
    {
        var clipboard = Clipboard
            ?? throw new InvalidOperationException(
                "The desktop clipboard is not available.");
        await clipboard.SetTextAsync(text);
        await clipboard.FlushAsync();
    }
}
