using Avalonia.Controls;
using Avalonia.Interactivity;
using SrvSurvey.Core.Network;
using SrvSurvey.Desktop.Runtime;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Views;

public sealed partial class MiningSearchView : UserControl
{
    private static readonly string EdsmSystemUrl = new UriBuilder(Uri.UriSchemeHttps, "www.edsm.net")
    {
        Path = UriPath.CombineWithTrailingSeparator("en", "search", "systems", "index", "name"),
    }
        .Uri
        .AbsoluteUri;
    private static readonly string SpanshSearchUrl = new UriBuilder(Uri.UriSchemeHttps, "spansh.co.uk")
    {
        Path = UriPath.CombineWithTrailingSeparator("bodies", "search"),
    }
        .Uri
        .AbsoluteUri;
    private static readonly string InaraSystemUrl = new UriBuilder(Uri.UriSchemeHttps, "inara.cz")
    {
        Path = UriPath.CombineWithTrailingSeparator("elite", "starsystem"),
        Query = "search=",
    }
        .Uri
        .AbsoluteUri;

    public MiningSearchView() => InitializeComponent();

    private MiningSearchViewModel? Model => DataContext as MiningSearchViewModel;

    private async void SearchRings_Click(object? sender, RoutedEventArgs e)
    {
        if (Model is { } vm)
        {
            await vm.SearchRingsAsync();
        }
    }

    private async void SearchMarkets_Click(object? sender, RoutedEventArgs e)
    {
        if (Model is { } vm)
        {
            await vm.SearchMarketsAsync();
        }
    }

    private async void SearchTraders_Click(object? sender, RoutedEventArgs e)
    {
        if (Model is { } vm)
        {
            await vm.SearchTradersAsync();
        }
    }

    private void Bookmark_Click(object? sender, RoutedEventArgs e) => Model?.Bookmark();

    private void Cache_Click(object? sender, RoutedEventArgs e) => Model?.CacheSelectedRing();

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Model?.Cancel();

    private async void SellingStations_Click(object? sender, RoutedEventArgs e)
    {
        if (Model is { } vm)
        {
            await vm.FindSellingStationsAsync();
        }
    }

    private void Current_Click(object? sender, RoutedEventArgs e)
    {
        Model?.UseCurrentLocation();
    }

    private async void SearchPlatinum_Click(object? sender, RoutedEventArgs e)
    {
        if (Model is { } vm)
        {
            await vm.SearchPlatinumAsync();
        }
    }

    private async void PlatinumMarkets_Click(object? sender, RoutedEventArgs e)
    {
        if (Model is { } vm)
        {
            await vm.FindSelectedPlatinumMarketsAsync();
        }
    }

    private void BookmarkPlatinum_Click(object? sender, RoutedEventArgs e) => Model?.BookmarkSelectedPlatinumSpot();

    private async void OpenSystem_Click(object? sender, RoutedEventArgs e)
    {
        if (
            !DesktopExternalEffectPolicy.IsAllowed
            || Model is not { } vm
            || TopLevel.GetTopLevel(this)?.Launcher is not { } launcher
        )
        {
            return;
        }

        string tag = (sender as Control)?.Tag as string ?? "";
        string? system = tag.Split(':')[0] switch
        {
            "ring" => vm.SelectedRing?.System,
            "market" => vm.SelectedMarket?.System,
            "trader" => vm.SelectedTrader?.System,
            _ => vm.SelectedSystem?.System,
        };
        if (string.IsNullOrWhiteSpace(system))
        {
            return;
        }

        string uri = InaraSystemUrl + Uri.EscapeDataString(system);
        if (tag.EndsWith(":edsm", StringComparison.Ordinal))
        {
            uri = EdsmSystemUrl + Uri.EscapeDataString(system);
        }

        if (tag.EndsWith(":spansh", StringComparison.Ordinal))
        {
            uri = SpanshSearchUrl;
        }

        try
        {
            await launcher.LaunchUriAsync(new Uri(uri));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            if (TopLevel.GetTopLevel(this)?.DataContext is MainWindowViewModel main)
            {
                main.MiningWorkspace.Status = "Could not open browser: " + ex.Message;
            }
        }
    }
}
