namespace SrvSurvey.Desktop.Tests;

public sealed class JournalMonitorSessionTests
{
    [Fact]
    public async Task StopCancelsAndWaitsForTheRunningSession()
    {
        var session = new JournalMonitorSession();
        var cancellationObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var allowCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var running = session.Start(async cancellationToken =>
        {
            using var registration = cancellationToken.Register(
                cancellationObserved.SetResult);
            await allowCompletion.Task;
        });

        var stopping = session.StopAsync();

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
        var running = session.Start(async cancellationToken =>
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
            }
        });

        var first = session.StopAsync();
        var second = session.StopAsync();

        Assert.Same(first, second);
        await first;
        await running;
    }
}
