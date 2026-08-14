using Avalonia.Controls;
using Avalonia.Interactivity;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class BoxelStatsWindow : Window
{
    public BoxelStatsWindow()
    {
        InitializeComponent();
        Closed += (_, _) =>
        {
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
        if (DataContext is BoxelSurveyStatsViewModel viewModel
            && sender is Control { DataContext: BoxelSurveyBrowserRowViewModel row })
        {
            await viewModel.OpenPrefixAsync(row.Prefix);
        }
    }

    private async void RecentRow_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is BoxelSurveyStatsViewModel viewModel
            && sender is Control { DataContext: SrvSurvey.Core.Search.BoxelSurveyIndexEntry entry })
        {
            await viewModel.OpenPrefixAsync(entry.Prefix);
        }
    }

    private async void BrowserRow_DoubleTapped(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is BoxelSurveyStatsViewModel viewModel
            && sender is ListBox { SelectedItem: BoxelSurveyBrowserRowViewModel row })
        {
            await viewModel.OpenPrefixAsync(row.Prefix);
        }
    }
}
