using Avalonia.Controls;
using Avalonia.Interactivity;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Views;

public sealed partial class MineMapView : UserControl
{
    public MineMapView() => InitializeComponent();

    private MineMapViewModel? Model => DataContext as MineMapViewModel;

    private void OpenMap_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (Model is { } model && (sender as Control)?.Tag is MineMapSurveyRowViewModel row)
        {
            model.SelectSurvey(row);
        }
    }

    private void RequestDelete_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (Model is { } model && (sender as Control)?.Tag is MineMapSurveyRowViewModel row)
        {
            model.RequestDelete(row);
        }
    }
}
