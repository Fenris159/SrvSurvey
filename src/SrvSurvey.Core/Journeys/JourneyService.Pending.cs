using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Journeys;

public sealed partial class JourneyService
{
    private readonly Dictionary<(string FrontierId, string FileName), PendingJourney> retainedProgress = [];

    private sealed record PendingJourney(
        JourneyJournalProcessor Processor,
        bool IsOdyssey,
        IReadOnlyList<JournalEventEnvelope> Events,
        JourneyJournalReadResult? History
    );

    private void ParkPendingProgress()
    {
        if (pendingLiveProgress is not { } pending || activeProcessor is null)
        {
            return;
        }
        retainedProgress[(pending.FrontierId, pending.FileName)] = new(
            activeProcessor,
            activeIsOdyssey,
            pendingJournalEvents.ToArray(),
            pendingHistoryReplay
        );
    }

    private void RestorePendingProgress(JourneyDocument journey)
    {
        if (!retainedProgress.Remove((journey.FrontierId, journey.FileName), out PendingJourney? pending))
        {
            return;
        }
        activeProcessor = pending.Processor;
        activeIsOdyssey = pending.IsOdyssey;
        pendingLiveProgress = activeProcessor.Journey;
        pendingJournalEvents.Clear();
        pendingJournalEvents.AddRange(pending.Events);
        pendingHistoryReplay = pending.History;
    }

    private async Task DrainRetainedProgressAsync(string frontierId, CancellationToken cancellationToken)
    {
        foreach (
            ((string FrontierId, string FileName) key, PendingJourney pending) in retainedProgress
                .Where(pair => pair.Key.FrontierId == frontierId)
                .ToArray()
        )
        {
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attempt.CancelAfter(TimeSpan.FromMilliseconds(250));
            try
            {
                await journeyStore
                    .UpdateAsync(
                        key.FrontierId,
                        key.FileName,
                        stored => MergePendingProgress(stored, pending),
                        cancellationToken,
                        leaseCancellationToken: attempt.Token
                    )
                    .ConfigureAwait(false);
                retainedProgress.Remove(key);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                log?.Invoke($"Journey observations remain pending for {key.FileName}; its storage is busy.");
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                log?.Invoke($"Journey observations remain pending for {key.FileName}: {exception.Message}");
            }
        }
    }
}
