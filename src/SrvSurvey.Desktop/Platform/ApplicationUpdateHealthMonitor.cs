namespace SrvSurvey.Desktop.Platform;

internal enum ApplicationUpdateHealthStatus
{
    Healthy,
    Exited,
    TimedOut,
}

internal static class ApplicationUpdateHealthMonitor
{
    internal static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(3);

    internal static async Task<ApplicationUpdateHealthStatus> WaitAsync(
        Func<bool> hasExited,
        Func<CancellationToken, Task<bool>> isConfirmed,
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                if (hasExited())
                {
                    return ApplicationUpdateHealthStatus.Exited;
                }
                if (await isConfirmed(deadline.Token).ConfigureAwait(false))
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    return hasExited() ? ApplicationUpdateHealthStatus.Exited : ApplicationUpdateHealthStatus.Healthy;
                }
                await Task.Delay(100, deadline.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ApplicationUpdateHealthStatus.TimedOut;
        }
    }

    internal static string DescribeFailure(ApplicationUpdateHealthStatus status, int? exitCode) =>
        status == ApplicationUpdateHealthStatus.Exited
            ? $"The replacement process exited with code {exitCode} before its main window confirmed startup. See the application log for startup errors."
            : $"The replacement process did not confirm a visible, responsive main window within {StartupTimeout.TotalSeconds:0} seconds. Rollback was requested. See the application log for the last startup stage.";
}
