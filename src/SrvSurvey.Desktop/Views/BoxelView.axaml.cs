using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using SrvSurvey.Core.Network;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Views;

public sealed partial class BoxelView : UserControl
{
    private BoxelSearchLibraryWindow? boxelSearchLibraryWindow;
    private BoxelStatsWindow? boxelStatsWindow;
    private ExpectedSystemsInformationWindow? expectedSystemsInformationWindow;

    public BoxelView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => ConnectClipboard();
        DetachedFromVisualTree += (_, _) => DisconnectClipboard();
        DataContextChanged += (_, _) => ConnectClipboard();
    }

    private void ConnectClipboard()
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.BoxelSearch.SetClipboardWriter(WriteClipboardAsync);
        }
    }

    private void DisconnectClipboard()
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.BoxelSearch.SetClipboardWriter(null);
        }
    }

    private async Task WriteClipboardAsync(string text)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard
            ?? throw new InvalidOperationException(
                "The desktop clipboard is not available.");
        await clipboard.SetTextAsync(text);
        await clipboard.FlushAsync();
    }

    private void TopBoxelTextBox_KeyDown(
        object? sender,
        KeyEventArgs eventArgs)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (eventArgs.Key == Key.Down)
        {
            viewModel.BoxelSearch.MoveSystemSuggestionSelection(1);
            eventArgs.Handled = viewModel.BoxelSearch.HasSystemNameSuggestions;
        }
        else if (eventArgs.Key == Key.Up)
        {
            viewModel.BoxelSearch.MoveSystemSuggestionSelection(-1);
            eventArgs.Handled = viewModel.BoxelSearch.HasSystemNameSuggestions;
        }
        else if (eventArgs.Key == Key.Enter)
        {
            eventArgs.Handled =
                viewModel.BoxelSearch.SelectCurrentSystemSuggestion();
        }
        else if (eventArgs.Key == Key.Escape
            && viewModel.BoxelSearch.HasSystemNameSuggestions)
        {
            viewModel.BoxelSearch.DismissSystemSuggestions();
            eventArgs.Handled = true;
        }
    }

    private void SystemSuggestion_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel
            && sender is Button { DataContext: SystemNameSuggestion suggestion })
        {
            viewModel.BoxelSearch.SelectSystemSuggestion(suggestion);
            TopBoxelTextBox.Focus();
        }
    }

    private async void SaveProgress_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        try
        {
            var result = await viewModel.BoxelSearch.SaveProgressAsync();
            if (result != SaveBoxelProgressResult.RequiresDetails
                || TopLevel.GetTopLevel(this) is not Window owner)
            {
                return;
            }

            var dialog = new SaveBoxelSearchDialog(
                viewModel.BoxelSearch.SuggestedSaveName);
            var details = await dialog.ShowDialog<BoxelSearchSaveDialogResult?>(owner);
            if (details is not null)
            {
                await viewModel.BoxelSearch.SaveProgressAsync(
                    details.Name,
                    details.Notes);
            }
        }
        catch (Exception exception)
        {
            viewModel.BoxelSearch.ReportSaveProgressFailure(exception.Message);
        }
    }

    private void ResumeSearch_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (DataContext is not MainWindowViewModel viewModel
            || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        if (boxelSearchLibraryWindow is not null)
        {
            boxelSearchLibraryWindow.Activate();
            return;
        }

        var library = new BoxelSearchLibraryViewModel(viewModel.BoxelSearch);
        library.StatisticsRequested += async (_, request) =>
        {
            await OpenStatisticsAsync(viewModel, owner, request);
        };
        boxelSearchLibraryWindow = new BoxelSearchLibraryWindow
        {
            DataContext = library
        };
        boxelSearchLibraryWindow.Closed += (_, _) =>
            boxelSearchLibraryWindow = null;
        boxelSearchLibraryWindow.Show(owner);
    }

    internal async void BoxelStats_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (DataContext is not MainWindowViewModel viewModel
            || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        if (boxelStatsWindow is not null)
        {
            boxelStatsWindow.Activate();
            return;
        }

        var stats = new BoxelSurveyStatsViewModel(
            viewModel.BoxelSurveyStats,
            new BoxelSurveyStatsSettingsStore(viewModel.AppDataPaths.UiSettingsPath),
            viewModel.BoxelSearch,
            viewModel.JournalFolderPath,
            () => viewModel.CurrentJournalPath);
        await stats.InitializeAsync();
        boxelStatsWindow = new BoxelStatsWindow
        {
            DataContext = stats,
        };
        boxelStatsWindow.Closed += (_, _) => boxelStatsWindow = null;
        boxelStatsWindow.Show(owner);
    }

    private async Task OpenStatisticsAsync(
        MainWindowViewModel viewModel,
        Window owner,
        BoxelSurveyStatsFocusRequest request)
    {
        if (boxelStatsWindow?.DataContext is BoxelSurveyStatsViewModel existing)
        {
            await existing.FocusPrefixesAsync(request.Prefixes, request.LowMassCode);
            boxelStatsWindow.Activate();
            return;
        }

        var stats = new BoxelSurveyStatsViewModel(
            viewModel.BoxelSurveyStats,
            new BoxelSurveyStatsSettingsStore(viewModel.AppDataPaths.UiSettingsPath),
            viewModel.BoxelSearch,
            viewModel.JournalFolderPath,
            () => viewModel.CurrentJournalPath);
        await stats.FocusPrefixesAsync(request.Prefixes, request.LowMassCode);
        boxelStatsWindow = new BoxelStatsWindow
        {
            DataContext = stats,
        };
        boxelStatsWindow.Closed += (_, _) => boxelStatsWindow = null;
        boxelStatsWindow.Show(owner);
    }

    private async void VoxStellar_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        try
        {
            var launcher = TopLevel.GetTopLevel(this)?.Launcher
                ?? throw new InvalidOperationException(
                    "The desktop link launcher is not available.");
            if (!await launcher.LaunchUriAsync(WellKnownUris.VoxStellarWebsite))
            {
                throw new InvalidOperationException(
                    "The default browser declined the request.");
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or NotSupportedException)
        {
            viewModel.VoxStellar.ReportLinkFailure(exception.Message);
        }
    }

    private void VoxStellarInfo_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (TopLevel.GetTopLevel(this) is Window owner)
        {
            new VoxStellarInformationWindow().Show(owner);
        }
    }

    private void ExpectedSystemsInfo_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        if (expectedSystemsInformationWindow is not null)
        {
            expectedSystemsInformationWindow.Activate();
            return;
        }

        expectedSystemsInformationWindow = new ExpectedSystemsInformationWindow();
        expectedSystemsInformationWindow.Closed += (_, _) =>
            expectedSystemsInformationWindow = null;
        expectedSystemsInformationWindow.Show(owner);
    }
}
