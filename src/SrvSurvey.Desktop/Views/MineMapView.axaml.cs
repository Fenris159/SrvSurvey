using Avalonia.Controls;
using Avalonia.Input;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Views;

public sealed partial class MineMapView : UserControl
{
    public MineMapView() => InitializeComponent();

    private void OnSurveyRowTapped(object? sender, TappedEventArgs eventArgs)
    {
        if (
            sender is Control { DataContext: MineMapSurveyRowViewModel row }
            && DataContext is MainWindowViewModel mainWindow
        )
        {
            mainWindow.MineMap.SelectSurvey(row);
            eventArgs.Handled = true;
        }
    }
}
