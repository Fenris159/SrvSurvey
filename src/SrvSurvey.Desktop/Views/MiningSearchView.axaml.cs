using Avalonia.Controls;
using Avalonia.Interactivity;
using SrvSurvey.Desktop.Runtime;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Views;

public sealed partial class MiningSearchView : UserControl
{
    private const string EdsmSystemUrl = "https://www.edsm.net/en/search/systems/index/name/";
    private const string SpanshSearchUrl = "https://spansh.co.uk/bodies/search/";
    private const string InaraSystemUrl = "https://inara.cz/elite/starsystem/?search=";
    public MiningSearchView() => InitializeComponent();
    private MiningSearchViewModel? Model => DataContext as MiningSearchViewModel;
    private async void SearchRings_Click(object? sender, RoutedEventArgs e) { if (Model is { } vm) await vm.SearchRingsAsync(); }
    private async void SearchMarkets_Click(object? sender, RoutedEventArgs e) { if (Model is { } vm) await vm.SearchMarketsAsync(); }
    private async void SearchSystems_Click(object? sender, RoutedEventArgs e) { if (Model is { } vm) await vm.SearchSystemsAsync(); }
    private async void SearchTraders_Click(object? sender, RoutedEventArgs e) { if (Model is { } vm) await vm.SearchTradersAsync(); }
    private void Bookmark_Click(object? sender, RoutedEventArgs e) => Model?.Bookmark();
    private void Cache_Click(object? sender, RoutedEventArgs e) => Model?.CacheSelectedRing();
    private void ClearPlan_Click(object? sender, RoutedEventArgs e) => Model?.ClearPlan();
    private void Cancel_Click(object? sender, RoutedEventArgs e) => Model?.Cancel();
    private async void UseSystem_Click(object? sender, RoutedEventArgs e) { if (Model is { } vm) await vm.FindSelectedSystemRingsAsync(); }
    private async void SellingStations_Click(object? sender, RoutedEventArgs e) { if (Model is { } vm) await vm.FindSellingStationsAsync(); }
    private void Current_Click(object? sender, RoutedEventArgs e)
    {
        if (Model is { } vm && TopLevel.GetTopLevel(this)?.DataContext is MainWindowViewModel main) vm.Reference = main.MiningWorkspace.CurrentSystem;
    }
    private async void OpenSystem_Click(object? sender, RoutedEventArgs e)
    {
        if (!DesktopExternalEffectPolicy.IsAllowed || Model is not { } vm || TopLevel.GetTopLevel(this)?.Launcher is not { } launcher) return;
        var tag = (sender as Control)?.Tag as string ?? "";
        var system = tag.Split(':')[0] switch { "ring" => vm.SelectedRing?.System, "market" => vm.SelectedMarket?.System, "trader" => vm.SelectedTrader?.System, _ => vm.SelectedSystem?.System };
        if (string.IsNullOrWhiteSpace(system)) return;
        var uri = InaraSystemUrl + Uri.EscapeDataString(system);
        if (tag.EndsWith(":edsm", StringComparison.Ordinal)) uri = EdsmSystemUrl + Uri.EscapeDataString(system);
        if (tag.EndsWith(":spansh", StringComparison.Ordinal)) uri = SpanshSearchUrl;
        try { await launcher.LaunchUriAsync(new Uri(uri)); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { if (TopLevel.GetTopLevel(this)?.DataContext is MainWindowViewModel main) main.MiningWorkspace.Status = "Could not open browser: " + ex.Message; }
    }
}
