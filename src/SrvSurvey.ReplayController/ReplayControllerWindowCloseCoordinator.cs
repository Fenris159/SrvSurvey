namespace SrvSurvey.ReplayController;

internal sealed class ReplayControllerWindowCloseCoordinator(
    Func<ValueTask> cleanup,
    Action completeClose)
{
    private Task? completion;
    private bool cleanupComplete;

    internal Task Completion => completion ?? Task.CompletedTask;

    internal bool ShouldCancelClose()
    {
        if (cleanupComplete)
        {
            return false;
        }

        completion ??= CompleteAsync();
        return true;
    }

    private async Task CompleteAsync()
    {
        await Task.Yield();
        try
        {
            await cleanup();
        }
        finally
        {
            cleanupComplete = true;
            completeClose();
        }
    }
}
