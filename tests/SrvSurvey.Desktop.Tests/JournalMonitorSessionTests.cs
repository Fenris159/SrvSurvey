namespace SrvSurvey.Desktop.Tests;

public sealed class JournalMonitorSessionTests
{
    [Fact]
    public async Task StopCancelsAndWaitsForTheRunningSession()
    {
        var session = new JournalMonitorSession();
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task running = session.Start(
            async cancellationToken =>
            {
                using CancellationTokenRegistration registration = cancellationToken.Register(
                    cancellationObserved.SetResult
                );
                await allowCompletion.Task;
            },
            exception => Assert.Fail(exception.ToString())
        );

        Task stopping = session.StopAsync();

        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(stopping.IsCompleted);

        allowCompletion.SetResult();
        await stopping;
        await running;
    }

    [Fact]
    public async Task ConcurrentStopsShareTheSameCompletionTask()
    {
        var session = new JournalMonitorSession();
        Task running = session.Start(
            async cancellationToken =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            },
            exception => Assert.Fail(exception.ToString())
        );

        Task first = session.StopAsync();
        Task second = session.StopAsync();

        Assert.Same(first, second);
        await first;
        await running;
    }

    [Fact]
    public async Task UnexpectedFailureIsReportedWithoutEscapingStop()
    {
        var session = new JournalMonitorSession();
        var reported = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var expected = new InvalidOperationException("Monitor failed.");
        Task running = session.Start(_ => Task.FromException(expected), reported.SetResult);

        await running;
        Assert.Same(expected, await reported.Task);
        await session.StopAsync();
    }
}
