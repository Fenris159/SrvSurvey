using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class BoxelStatsWindow : Window
{
    private CancellationTokenSource? activationCancellation;
    private int activationVersion;
    private bool isClosed;

    public BoxelStatsWindow()
    {
        InitializeComponent();
        Closed += (_, _) =>
        {
            isClosed = true;
            Interlocked.Increment(ref activationVersion);
            CancelActivation();
            if (DataContext is BoxelSurveyStatsViewModel viewModel)
            {
                viewModel.Dispose();
            }
        };
    }

    private void Close_Click(object? sender, RoutedEventArgs eventArgs)
    {
        Close();
    }

    private async void AverageHelp_Click(object? sender, RoutedEventArgs eventArgs)
    {
        var dialog = new BoxelAverageHelpDialog();
        await dialog.ShowDialog(this);
    }

    private async void Export_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (isClosed || DataContext is not BoxelSurveyStatsViewModel viewModel)
        {
            return;
        }

        try
        {
            if (!StorageProvider.CanPickFolder)
            {
                viewModel.ReportStatus(
                    "This platform does not provide a folder picker for exports.");
                return;
            }

            var folders = await StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    Title = "Choose where to export boxel statistics",
                    AllowMultiple = false,
                });
            var directory = folders.Count > 0
                ? folders[0].TryGetLocalPath()
                : null;
            if (!string.IsNullOrWhiteSpace(directory))
            {
                await viewModel.ExportAsync(directory);
            }
            else if (folders.Count > 0)
            {
                viewModel.ReportStatus(
                    "The selected export folder is not available as a local filesystem path.");
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or InvalidOperationException)
        {
            viewModel.ReportStatus(
                "Could not choose an export folder: " + exception.Message);
        }
    }

    private async void BrowserRow_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (sender is Control { DataContext: BoxelSurveyBrowserRowViewModel row })
        {
            await ActivatePrefixAsync(row.Prefix);
        }
    }

    private async void RecentRow_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (sender is Control { DataContext: SrvSurvey.Core.Search.BoxelSurveyIndexEntry entry })
        {
            await ActivatePrefixAsync(entry.Prefix);
        }
    }

    private async void BrowserRow_DoubleTapped(object? sender, RoutedEventArgs eventArgs)
    {
        if (sender is ListBox { SelectedItem: BoxelSurveyBrowserRowViewModel row })
        {
            await ActivatePrefixAsync(row.Prefix);
        }
    }

    private async Task ActivatePrefixAsync(string prefix)
    {
        if (isClosed || DataContext is not BoxelSurveyStatsViewModel viewModel)
        {
            return;
        }

        var version = Interlocked.Increment(ref activationVersion);
        using var cancellation = new CancellationTokenSource();
        await ReplaceActivationAsync(cancellation);
        if (isClosed)
        {
            await cancellation.CancelAsync();
        }

        try
        {
            await viewModel.OpenPrefixAsync(prefix, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // A newer activation replaced this load.
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            if (!isClosed && version == activationVersion)
            {
                viewModel.ReportStatus(
                    "Could not open boxel statistics: " + exception.Message);
            }
        }
        finally
        {
            Interlocked.CompareExchange(
                ref activationCancellation,
                null,
                cancellation);
        }
    }

    private async Task ReplaceActivationAsync(CancellationTokenSource next)
    {
        var previous = Interlocked.Exchange(ref activationCancellation, next);
        if (previous is not null)
        {
            await previous.CancelAsync();
        }
    }

    private void CancelActivation()
    {
        var scheduled = Interlocked.Exchange(ref activationCancellation, null);
        if (scheduled is null)
        {
            return;
        }

        scheduled.Cancel();
    }
}
