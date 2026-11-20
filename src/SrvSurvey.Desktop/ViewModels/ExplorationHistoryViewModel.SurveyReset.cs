using SrvSurvey.Core.Exploration.History;
using SrvSurvey.Desktop.Localization;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class ExplorationHistoryViewModel
{
    private ExplorationSurveyResetPoint? pendingSurveyResetPoint;
    private bool refreshRequiredBeforeSurveyReset;

    public async Task<ExplorationSurveyResetPoint> CaptureSurveyResetPointAsync(
        IReadOnlyList<ExplorationSurveySourceCheckpoint> consumed
    )
    {
        long version = generation;
        CancellationToken token = contextCancellation.Token;
        ExplorationHistoryCatalog? current = catalog;
        await PendingSynchronization;
        RequireSurveyResetContext(current, version, token);
        EnsureHistoryReadyForSurveyReset();
        try
        {
            return await TrackIo(
                current!.CaptureSurveyResetPointAsync(
                    consumed,
                    includeEarlierSources: true,
                    cancellationToken: CancellationToken.None
                )
            );
        }
        catch (ExplorationSurveyResetRecoveryException exception)
        {
            throw new InvalidOperationException(SurveyResetRecoveryMessage(exception.JournalName), exception);
        }
    }

    public Task ApplySurveyResetPointAsync(ExplorationSurveyResetPoint point)
    {
        pendingSurveyResetPoint = point;
        NotifyBalance();
        Task reset = ApplySurveyResetCoreAsync(PendingSynchronization, point);
        PendingSynchronization = RetainResetCompletionAsync(reset);
        return reset;
    }

    public Task ResetSurveyValueAsync()
    {
        Task reset = ApplySurveyResetCoreAsync(PendingSynchronization, null);
        PendingSynchronization = RetainResetCompletionAsync(reset);
        return reset;
    }

    private async Task ApplySurveyResetCoreAsync(Task previous, ExplorationSurveyResetPoint? point)
    {
        long version = generation;
        CancellationToken token = contextCancellation.Token;
        ExplorationHistoryCatalog? current = catalog;
        await previous;
        RequireSurveyResetContext(current, version, token);
        if (point is null)
        {
            EnsureHistoryReadyForSurveyReset();
            point = await TrackIo(current!.CaptureSurveyResetPointAsync(cancellationToken: CancellationToken.None));
        }
        if (!Current(version, token))
        {
            return;
        }

        pendingSurveyResetPoint = point;
        ExplorationHistorySummary result = await TrackIo(
            current!.ApplySurveyResetPointAsync(point, CancellationToken.None)
        );
        if (!Current(version, token))
        {
            return;
        }

        if (pendingSurveyResetPoint?.Id == point.Id)
        {
            pendingSurveyResetPoint = null;
        }

        queryGeneration++;
        summary = result;
        NotifyBalance();
        PendingQuery = QueryAsync();
        await PendingQuery;
    }

    private void RequireSurveyResetContext(ExplorationHistoryCatalog? current, long version, CancellationToken token)
    {
        if (current is null || !Current(version, token))
        {
            throw new InvalidOperationException("Select the commander again before resetting exploration value.");
        }
    }

    private void EnsureHistoryReadyForSurveyReset()
    {
        if (refreshRequiredBeforeSurveyReset)
        {
            throw new InvalidOperationException(SurveyResetRecoveryMessage());
        }
    }

    private static string SurveyResetRecoveryMessage(string? journalName = null)
    {
        string guidance = LocalizationCatalog.Translate(
            "Open Exploration history and select Refresh history. If prompted, restore the original journals, select the confirmation checkbox, and choose Resume history recovery. When recovery finishes, go back to Survey totals and retry the reset."
        );
        return journalName is null
            ? guidance
            : LocalizationCatalog.Translate($"Restore the complete journal {journalName} or re-add its folder.")
                + " "
                + guidance;
    }

    private static async Task RetainResetCompletionAsync(Task reset)
    {
        try
        {
            await reset;
        }
        catch (Exception exception) when (IsStorageFailure(exception) || exception is InvalidOperationException)
        {
            // The reset caller reports the failure; the next refresh retries a saved intent.
        }
    }
}
