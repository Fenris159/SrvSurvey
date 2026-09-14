using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SrvSurvey.Desktop;

public sealed partial class WaylandScreenSharePromptDialog : Window
{
    public WaylandScreenSharePromptDialog()
    {
        InitializeComponent();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs eventArgs)
    {
        Close(false);
    }

    private void Continue_Click(object? sender, RoutedEventArgs eventArgs)
    {
        Close(true);
    }
}
