using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Views;

/// <summary>Embeds the commander's market-search session without owning its lifetime.</summary>
public sealed partial class ColonizationShoppingView : UserControl
{
    private ColonizationShoppingViewModel ViewModel => (ColonizationShoppingViewModel)DataContext!;

    public static Thickness ResultsScrollbarMargin { get; } = new(0, 0, 24, 0);

    public static FuncValueConverter<double, double> ExpandedCommodityWidth { get; } =
        new(width => Math.Max(0, width - ResultsScrollbarMargin.Right));

    public ColonizationShoppingView() => InitializeComponent();

    private async void Search_Click(object? sender, RoutedEventArgs args) => await ViewModel.SearchAsync();

    private void CurrentLocation_Click(object? sender, RoutedEventArgs args) => ViewModel.UseCurrentLocation();

    private void Criteria_Click(object? sender, RoutedEventArgs args) => ViewModel.ShowSearchCriteria = true;

    private void ExpandAll_Click(object? sender, RoutedEventArgs args) => ViewModel.ToggleAllExpanded();

    private void SelectAllNeeds_Click(object? sender, RoutedEventArgs args) =>
        ViewModel.SetAllCommoditiesSelected(true);

    private void SelectNoNeeds_Click(object? sender, RoutedEventArgs args) =>
        ViewModel.SetAllCommoditiesSelected(false);

    private async void ExcludeLocalCommodities_Click(object? sender, RoutedEventArgs args) =>
        await ViewModel.ToggleLocalCommoditiesAsync();

    private void Sort_Click(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: string column })
        {
            ViewModel.SortBy(column);
        }
    }

    private void ClearSearch_Click(object? sender, RoutedEventArgs args) => ViewModel.ClearSearch();

    private static void MarketRow_PointerReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (
            args.InitialPressMouseButton != MouseButton.Left
            || sender is not Border { DataContext: ShoppingMarketRowViewModel row } border
        )
        {
            return;
        }
        if (
            args.Source is Avalonia.Visual source
            && source.GetSelfAndVisualAncestors().TakeWhile(visual => visual != border).Any(visual => visual is Button)
        )
        {
            return;
        }
        row.IsExpanded = !row.IsExpanded;
        args.Handled = true;
    }
}
