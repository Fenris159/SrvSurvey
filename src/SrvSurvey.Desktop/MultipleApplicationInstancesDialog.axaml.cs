using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SrvSurvey.Desktop;

public sealed partial class MultipleApplicationInstancesDialog : Window
{
    public MultipleApplicationInstancesDialog()
    {
        InitializeComponent();
    }

    public MultipleApplicationInstancesDialog(int otherInstanceCount) : this()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(otherInstanceCount, 1);
        var total = otherInstanceCount + 1;
        InstanceCountText.Text = total == 2
            ? "2 SrvSurvey instances are currently running."
            : $"{total:N0} SrvSurvey instances are currently running.";
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
