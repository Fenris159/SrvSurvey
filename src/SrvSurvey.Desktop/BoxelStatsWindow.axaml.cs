using Avalonia.Controls;
using Avalonia.Interactivity;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class BoxelStatsWindow : Window
{
    private CancellationTokenSource? activationCancellation;
    private int activationVersion;

    public BoxelStatsWindow()
    {
        InitializeComponent();
        Closed += (_, _) =>
        {
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
        if (DataContext is not BoxelSurveyStatsViewModel viewModel)
        {
            return;
        }

        var version = Interlocked.Increment(ref activationVersion);
        var cancellation = ReplaceActivation();
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
            if (version == activationVersion)
            {
                viewModel.ReportStatus(
                    "Could not open boxel statistics: " + exception.Message);
            }
        }
    }

    private CancellationTokenSource ReplaceActivation()
    {
        var next = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref activationCancellation, next);
        if (previous is not null)
        {
            previous.Cancel();
            previous.Dispose();
        }

        return next;
    }

    private void CancelActivation()
    {
        var scheduled = Interlocked.Exchange(ref activationCancellation, null);
        if (scheduled is null)
        {
            return;
        }

        scheduled.Cancel();
        scheduled.Dispose();
    }
}
