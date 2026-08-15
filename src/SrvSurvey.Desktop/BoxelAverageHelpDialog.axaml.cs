using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SrvSurvey.Desktop;

public sealed partial class BoxelAverageHelpDialog : Window
{
    public BoxelAverageHelpDialog()
    {
        InitializeComponent();
    }

    private void Close_Click(object? sender, RoutedEventArgs eventArgs)
    {
        Close();
    }
}
