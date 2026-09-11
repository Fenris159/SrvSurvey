using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SrvSurvey.Desktop;

public sealed partial class DeleteFiregroupDialog : Window
{
    public DeleteFiregroupDialog() => InitializeComponent();

    public DeleteFiregroupDialog(string name, string ship)
        : this()
    {
        ConfigurationText.Text = $"Delete “{name}” for {ship}?";
        Opened += (_, _) => NoButton.Focus();
    }

    private void No_Click(object? sender, RoutedEventArgs e) => Close(false);

    private void Yes_Click(object? sender, RoutedEventArgs e) => Close(true);
}
