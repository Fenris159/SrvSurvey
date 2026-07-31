namespace SrvSurvey.Desktop;

internal sealed class JournalMonitorSession
{
    private readonly object sync = new();
    private CancellationTokenSource? cancellation;
    private Task? runningTask;
    private Task? stopTask;

    public Task Start(Func<CancellationToken, Task> runAsync)
    {
        ArgumentNullException.ThrowIfNull(runAsync);

        lock (sync)
        {
            if (cancellation is not null
                || runningTask is not null
                || stopTask is not null)
            {
                throw new InvalidOperationException(
                    "The journal monitor session has already been started.");
            }

            var source = new CancellationTokenSource();
            try
            {
                runningTask = runAsync(source.Token);
                cancellation = source;
                return runningTask;
            }
            catch
            {
                source.Dispose();
                throw;
            }
        }
    }

    public Task StopAsync()
    {
        lock (sync)
        {
            if (stopTask is not null)
            {
                return stopTask;
            }

            var source = cancellation;
            var task = runningTask;
            cancellation = null;
            runningTask = null;
            stopTask = source is null
                ? Task.CompletedTask
                : CancelWaitAndDisposeAsync(source, task!);
            return stopTask;
        }
    }

    private static async Task CancelWaitAndDisposeAsync(
        CancellationTokenSource cancellation,
        Task runningTask)
    {
        try
        {
            await cancellation.CancelAsync();
            await runningTask;
        }
        finally
        {
            cancellation.Dispose();
        }
    }
}
