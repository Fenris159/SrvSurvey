using Avalonia.Controls;
using Avalonia.Interactivity;
using SrvSurvey.Desktop.ViewModels;
namespace SrvSurvey.Desktop.Views;

public sealed partial class DistanceView : UserControl
{
    public DistanceView() => InitializeComponent();
    private MiningWorkspaceViewModel? Model => (DataContext as MainWindowViewModel)?.MiningWorkspace;
    private async void Distance_Click(object? sender, RoutedEventArgs e) { if (Model is { } vm) await vm.CalculateDistanceAsync(); }
    private void CurrentOrigin_Click(object? sender, RoutedEventArgs e) { if (Model is { } vm) vm.Origin = vm.CurrentSystem; }
    private void Carrier_Click(object? sender, RoutedEventArgs e) { if (Model is { } vm && DataContext is MainWindowViewModel main) vm.Destination = main.FrontierProfile.Carrier?.System ?? ""; }
    private void Home_Click(object? sender, RoutedEventArgs e) { if (Model is { } vm) vm.Destination = vm.Settings.HomeSystem; }
}
