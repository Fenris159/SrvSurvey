using Avalonia.Controls;
using Avalonia.Interactivity;
using SrvSurvey.Core.Network;
using SrvSurvey.Desktop.Runtime;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Views;

public sealed partial class MiningPowerplayView : UserControl
{
    private static readonly string InaraSystemUrl = new UriBuilder(Uri.UriSchemeHttps, "inara.cz")
    {
        Path = UriPath.CombineWithTrailingSeparator("elite", "starsystem"),
        Query = "search=",
    }
        .Uri
        .AbsoluteUri;

    public MiningPowerplayView()
    {
        InitializeComponent();
        ResultsScroller.SizeChanged += (_, _) => FitResultsToWorkspace();
    }

    private void FitResultsToWorkspace()
    {
        double width = ResultsScroller.Bounds.Width;
        if (width > 0)
        {
            ResultsTable.Width = Math.Max(1280, width);
        }
    }

    private MiningWorkspaceViewModel? Model => DataContext as MiningWorkspaceViewModel;

    private async void SearchSystems_Click(object? sender, RoutedEventArgs e)
    {
        if (Model?.Search is { } search)
        {
            await search.SearchSystemsAsync();
        }
    }

    private async void FindRings_Click(object? sender, RoutedEventArgs e)
    {
        if (Model?.Search is { } search)
        {
            await search.FindSelectedSystemRingsAsync();
        }
    }

    private async void SellingStations_Click(object? sender, RoutedEventArgs e)
    {
        if (Model?.Search is { } search)
        {
            await search.FindSellingStationsAsync();
        }
    }

    private void Bookmark_Click(object? sender, RoutedEventArgs e) => Model?.Search.Bookmark();

    private void Reset_Click(object? sender, RoutedEventArgs e) => Model?.Search.ResetPowerplay();

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Model?.Search.Cancel();

    private async void OpenSystem_Click(object? sender, RoutedEventArgs e)
    {
        if (!DesktopExternalEffectPolicy.IsAllowed || Model?.Search is not { } search)
        {
            return;
        }

        string? system =
            (sender as Control)?.Tag as string == "market"
                ? search.SelectedMarket?.System
                : search.SelectedSystem?.System;
        if (string.IsNullOrWhiteSpace(system) || TopLevel.GetTopLevel(this)?.Launcher is not { } launcher)
        {
            return;
        }

        try
        {
            await launcher.LaunchUriAsync(new Uri(InaraSystemUrl + Uri.EscapeDataString(system)));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Model.Status = "Could not open the system link: " + ex.Message;
        }
    }
}
