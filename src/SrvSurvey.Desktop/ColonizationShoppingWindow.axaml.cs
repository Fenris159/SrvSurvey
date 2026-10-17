using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

/// <summary>A nonmodal market search whose in-flight requests end when the window closes.</summary>
public sealed partial class ColonizationShoppingWindow : Window
{
    private readonly ColonizationShoppingViewModel viewModel;

    public ColonizationShoppingWindow()
        : this(new ColonizationShoppingViewModel([], () => null)) { }

    public ColonizationShoppingWindow(ColonizationShoppingViewModel viewModel)
    {
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = viewModel;
        Closed += OnClosed;
    }

    private async void Search_Click(object? sender, RoutedEventArgs args) => await viewModel.SearchAsync();

    private void CurrentLocation_Click(object? sender, RoutedEventArgs args) => viewModel.UseCurrentLocation();

    private void Criteria_Click(object? sender, RoutedEventArgs args) => viewModel.ShowSearchCriteria = true;

    private void ExpandAll_Click(object? sender, RoutedEventArgs args) => viewModel.ToggleAllExpanded();

    private void SelectAllNeeds_Click(object? sender, RoutedEventArgs args) =>
        viewModel.SetAllCommoditiesSelected(true);

    private void SelectNoNeeds_Click(object? sender, RoutedEventArgs args) =>
        viewModel.SetAllCommoditiesSelected(false);

    private void Sort_Click(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: string column })
        {
            viewModel.SortBy(column);
        }
    }

    private void ClearSearch_Click(object? sender, RoutedEventArgs args) => viewModel.ClearSearch();

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

    private void OnClosed(object? sender, EventArgs args)
    {
        Closed -= OnClosed;
        viewModel.Dispose();
    }
}
