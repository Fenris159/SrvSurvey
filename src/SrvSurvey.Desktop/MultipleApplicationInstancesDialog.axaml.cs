using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SrvSurvey.Desktop;

public sealed partial class MultipleApplicationInstancesDialog : Window
{
    public MultipleApplicationInstancesDialog()
    {
        InitializeComponent();
    }

    public MultipleApplicationInstancesDialog(
        int otherInstanceCount,
        int unverifiedInstanceCount = 0) : this()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(otherInstanceCount, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(unverifiedInstanceCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            unverifiedInstanceCount,
            otherInstanceCount);
        var total = otherInstanceCount + 1;
        InstanceCountText.Text = total == 2
            ? "2 SrvSurvey instances are currently running."
            : $"{total:N0} SrvSurvey instances are currently running.";
        if (unverifiedInstanceCount > 0)
        {
            VerificationWarningText.IsVisible = true;
            VerificationWarningText.Text =
                $"The operating system prevented verification of "
                + $"{unverifiedInstanceCount:N0} matching process(es). "
                + "SrvSurvey will not force-close an unverified process, and the update "
                + "will stop safely if it remains open.";
        }
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
