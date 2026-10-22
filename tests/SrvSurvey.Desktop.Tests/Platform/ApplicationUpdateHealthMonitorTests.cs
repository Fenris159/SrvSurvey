using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class ApplicationUpdateHealthMonitorTests
{
    [Fact]
    public async Task ARunningReplacementWithAValidConfirmationIsHealthy()
    {
        ApplicationUpdateHealthStatus result = await ApplicationUpdateHealthMonitor.WaitAsync(
            () => false,
            _ => Task.FromResult(true),
            TimeSpan.FromSeconds(5)
        );

        Assert.Equal(ApplicationUpdateHealthStatus.Healthy, result);
    }

    [Fact]
    public async Task AnExitedReplacementCannotBeAcceptedEvenWithAHealthMarker()
    {
        bool readMarker = false;
        ApplicationUpdateHealthStatus result = await ApplicationUpdateHealthMonitor.WaitAsync(
            () => true,
            _ =>
            {
                readMarker = true;
                return Task.FromResult(true);
            },
            TimeSpan.FromSeconds(5)
        );

        Assert.Equal(ApplicationUpdateHealthStatus.Exited, result);
        Assert.False(readMarker);
        Assert.Contains("code 23", ApplicationUpdateHealthMonitor.DescribeFailure(result, 23));
    }

    [Fact]
    public async Task AReplacementExitingDuringConfirmationIsRejected()
    {
        bool exited = false;
        ApplicationUpdateHealthStatus result = await ApplicationUpdateHealthMonitor.WaitAsync(
            () => exited,
            _ =>
            {
                exited = true;
                return Task.FromResult(true);
            },
            TimeSpan.FromSeconds(5)
        );

        Assert.Equal(ApplicationUpdateHealthStatus.Exited, result);
    }

    [Fact]
    public async Task ARunningReplacementWithoutConfirmationTimesOutAndReportsTheLimit()
    {
        ApplicationUpdateHealthStatus result = await ApplicationUpdateHealthMonitor
            .WaitAsync(() => false, _ => Task.FromResult(false), TimeSpan.FromMilliseconds(30))
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(ApplicationUpdateHealthStatus.TimedOut, result);
        string failure = ApplicationUpdateHealthMonitor.DescribeFailure(result, null);
        Assert.Contains("180 seconds", failure);
        Assert.Contains("responsive main window", failure);
        Assert.Contains("Rollback was requested", failure);
    }

    [Fact]
    public async Task CancellationIsNotReportedAsAStartupTimeout()
    {
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ApplicationUpdateHealthMonitor.WaitAsync(
                () => false,
                token =>
                {
                    cancellation.Cancel();
                    return Task.FromCanceled<bool>(token);
                },
                TimeSpan.FromSeconds(5),
                cancellation.Token
            )
        );
    }

    [Fact]
    public async Task CallerCancellationWhileReadingAValidMarkerStillCancelsVerification()
    {
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ApplicationUpdateHealthMonitor.WaitAsync(
                () => false,
                _ =>
                {
                    cancellation.Cancel();
                    return Task.FromResult(true);
                },
                TimeSpan.FromSeconds(5),
                cancellation.Token
            )
        );
    }
}
