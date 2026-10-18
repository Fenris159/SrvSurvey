using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class ColonizationViewModel
{
    private CancellationTokenSource? systemPlanningCancellation;

    internal Task CurrentSystemPlanningTask { get; private set; } = Task.CompletedTask;

    private void RefreshCurrentPlannedSystem()
    {
        CancelCurrentPlannedSystemRead();
        if (disposed || !IsEnabled || CommanderName is null || string.IsNullOrWhiteSpace(recovery.CurrentSystemName))
        {
            return;
        }
        systemPlanningCancellation = new CancellationTokenSource();
        CurrentSystemPlanningTask = RefreshCurrentPlannedSystemAsync(
            recovery.CurrentSystemName,
            systemPlanningCancellation.Token
        );
    }

    private async Task RefreshCurrentPlannedSystemAsync(string systemName, CancellationToken token)
    {
        int readVersion = ++plannedReadVersion;
        int profileVersion = recovery.ProfileVersion;
        try
        {
            (ColonizationSystemRecord? snapshot, string? warning) = await ReadPlannedSystemAsync(systemName, token);
            if (!IsCurrentPlannedRead(readVersion, profileVersion))
            {
                return;
            }
            token.ThrowIfCancellationRequested();
            plannedReadWarning = warning;
            if (snapshot is not null)
            {
                UpdatePlannedSystem(ResolvePlanningSnapshot(snapshot));
                SavePlannedSystems();
                RebuildProjectRows();
                UpdateProjectSummary();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // A newer system or session owns planning updates now.
        }
    }

    private void CancelCurrentPlannedSystemRead()
    {
        CancellationTokenSource? cancellation = systemPlanningCancellation;
        systemPlanningCancellation = null;
        if (cancellation is not null)
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }
    }
}
