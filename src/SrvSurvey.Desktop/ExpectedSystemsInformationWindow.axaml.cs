using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SrvSurvey.Desktop;

public sealed partial class ExpectedSystemsInformationWindow : Window
{
    public ExpectedSystemsInformationWindow()
    {
        InitializeComponent();
    }

    private void Close_Click(object? sender, RoutedEventArgs eventArgs)
    {
        Close();
    }
}
