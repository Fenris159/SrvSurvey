using Avalonia.Controls;
using Avalonia.Interactivity;
using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop;

public sealed partial class MultipleApplicationInstancesDialog : Window
{
    private readonly TaskCompletionSource<bool>? startupCompletion;
    private readonly Func<Task>? closeOtherInstances;
    private bool isClosingOtherInstances;

    public MultipleApplicationInstancesDialog()
    {
        InitializeComponent();
    }

    public MultipleApplicationInstancesDialog(int otherInstanceCount, int unverifiedInstanceCount = 0)
        : this()
    {
        ConfigureInstanceCount(otherInstanceCount, unverifiedInstanceCount, isStartupReplacement: false);
    }

    internal MultipleApplicationInstancesDialog(ApplicationInstanceScan scan, Func<Task> closeOtherInstances)
        : this()
    {
        ArgumentNullException.ThrowIfNull(scan);
        this.closeOtherInstances = closeOtherInstances ?? throw new ArgumentNullException(nameof(closeOtherInstances));
        startupCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Closed += (_, _) => startupCompletion.TrySetResult(false);
        Closing += (_, eventArgs) =>
            eventArgs.Cancel = isClosingOtherInstances && eventArgs.CloseReason != WindowCloseReason.OSShutdown;
        Title = "SrvSurvey is already running";
        EyebrowText.Text = "APPLICATION STARTUP";
        HeadlineText.Text =
            scan.TotalCount == 1
                ? "Another SrvSurvey instance is already open"
                : "Other SrvSurvey instances are already open";
        ConfirmationText.Text =
            scan.TotalCount == 1
                ? "Do you want to close the existing instance and continue opening this one?"
                : "Do you want to close the existing instances and continue opening this one?";
        DetailText.Text =
            "Choose No to leave the existing instance running. Multiple instances should be started from the Multiple Commanders panel.";
        CancelButton.Content = "No";
        ContinueButton.Content = "Yes, close it and continue";
        ConfigureInstanceCount(scan.TotalCount, scan.UnverifiedCount, isStartupReplacement: true);
    }

    internal Task<bool> ShowForStartupAsync()
    {
        if (startupCompletion is null)
        {
            throw new InvalidOperationException("This dialog was not configured for application startup.");
        }

        Show();
        return startupCompletion.Task;
    }

    private void ConfigureInstanceCount(int otherInstanceCount, int unverifiedInstanceCount, bool isStartupReplacement)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(otherInstanceCount, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(unverifiedInstanceCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(unverifiedInstanceCount, otherInstanceCount);
        int total = otherInstanceCount + 1;
        InstanceCountText.Text =
            total == 2
                ? "2 SrvSurvey instances are currently running."
                : $"{total:N0} SrvSurvey instances are currently running.";
        if (unverifiedInstanceCount > 0)
        {
            VerificationWarningText.IsVisible = true;
            VerificationWarningText.Text =
                $"The operating system prevented verification of "
                + $"{unverifiedInstanceCount:N0} matching process(es). "
                + (
                    isStartupReplacement
                        ? "SrvSurvey will not force-close an unverified process. Choose No and close it manually if replacement cannot continue."
                        : "SrvSurvey will not force-close an unverified process, and the update will stop safely if it remains open."
                );
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (startupCompletion is null)
        {
            Close(false);
            return;
        }

        startupCompletion.TrySetResult(false);
        Close();
    }

    private async void Continue_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (startupCompletion is null || closeOtherInstances is null)
        {
            Close(true);
            return;
        }

        CancelButton.IsEnabled = false;
        ContinueButton.IsEnabled = false;
        isClosingOtherInstances = true;
        ActionStatusText.IsVisible = true;
        ActionStatusText.Text = "Closing the existing SrvSurvey instance...";
        try
        {
            await closeOtherInstances();
            startupCompletion.TrySetResult(true);
            isClosingOtherInstances = false;
            Close();
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ActionStatusText.Text = "The existing instance could not be closed: " + exception.Message;
            isClosingOtherInstances = false;
            CancelButton.IsEnabled = true;
            ContinueButton.IsEnabled = true;
        }
    }
}
