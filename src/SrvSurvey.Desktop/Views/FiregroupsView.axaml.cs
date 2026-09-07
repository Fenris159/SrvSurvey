using Avalonia.Controls;
using Avalonia.Interactivity;
using SrvSurvey.Desktop.ViewModels;
namespace SrvSurvey.Desktop.Views;

public sealed partial class FiregroupsView : UserControl
{
    public FiregroupsView() => InitializeComponent();
    private MiningWorkspaceViewModel? Model => (DataContext as MainWindowViewModel)?.MiningWorkspace;
    private void RemoveFiregroup_Click(object? sender, RoutedEventArgs e) => Model?.RemoveFiregroup();
    private void SaveFiregroup_Click(object? sender, RoutedEventArgs e) => Model?.SaveFiregroup();
}
