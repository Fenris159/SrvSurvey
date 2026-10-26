namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class ColonizationViewModel
{
    private CancellationTokenSource? plannedSaveCancellation;

    internal Task PlannedSystemsSaveTask { get; private set; } = Task.CompletedTask;

    private void SchedulePlannedSystemsSave()
    {
        CancelPlannedSystemsSave();
        plannedSaveCancellation = new CancellationTokenSource();
        PlannedSystemsSaveTask = SavePlannedSystemsAfterDelayAsync(plannedSaveCancellation.Token);
    }

    private async Task SavePlannedSystemsAfterDelayAsync(CancellationToken token)
    {
        try
        {
            await delayAsync(TimeSpan.FromMilliseconds(500), token);
            token.ThrowIfCancellationRequested();
            SavePlannedSystems();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // A newer edit or an immediate flush owns persistence now.
        }
    }

    private void CancelPlannedSystemsSave()
    {
        CancellationTokenSource? cancellation = plannedSaveCancellation;
        plannedSaveCancellation = null;
        if (cancellation is not null)
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }
    }

    private void FlushPlannedSystemsSave()
    {
        if (plannedSaveCancellation is not null)
        {
            SavePlannedSystems();
        }
    }

    private void SavePlannedSystems()
    {
        CancelPlannedSystemsSave();
        if (CommanderName is null)
        {
            return;
        }
        try
        {
            settingsStore.SavePlannedSystems(CommanderName, plannedSystems.Values);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            plannedReadWarning = "The planned site requirements could not be saved to disk.";
            StatusMessage = plannedReadWarning;
        }
    }
}
