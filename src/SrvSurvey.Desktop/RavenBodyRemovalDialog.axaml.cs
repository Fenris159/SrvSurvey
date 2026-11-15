using Avalonia.Controls;
using Avalonia.Interactivity;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class RavenBodyRemovalDialog : Window
{
    public RavenBodyRemovalDialog() => InitializeComponent();

    public RavenBodyRemovalDialog(RavenBodyRemovalWarning warning)
        : this()
    {
        SystemText.Text = $"{warning.SystemName} ({warning.SystemAddress})";
        RemovedBodies.ItemsSource = warning.BodyNames;
        Opened += (_, _) => NoButton.Focus();
    }

    private void No_Click(object? sender, RoutedEventArgs args) => Close(false);

    private void Yes_Click(object? sender, RoutedEventArgs args) => Close(true);
}
