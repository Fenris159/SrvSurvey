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
}
