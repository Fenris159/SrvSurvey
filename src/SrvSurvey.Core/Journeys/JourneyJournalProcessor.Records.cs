using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Journeys;

public sealed partial class JourneyJournalProcessor
{
    private int? applyingVisitIndex;
    private DateTimeOffset catchUpBoundary;
    private bool catchUpBoundaryComplete;
    internal bool HasRecordMetadataChanges { get; private set; }
    private JourneySystemVisit? CurrentVisit
    {
        get
        {
            if (applyingVisitIndex is not { } index)
            {
                return State.CurrentSystem;
            }
            return index < 0 ? null : State.VisitedSystems[index];
        }
    }

    private static JourneyDocument InitializeRecordTracking(JourneyDocument journey)
    {
        var records = (journey.RecordedJournalRecords ?? ImmutableHashSet<string>.Empty).ToImmutableHashSet(
            StringComparer.Ordinal
        );
        records = records.Union(journey.WatermarkRecords ?? ImmutableHashSet<string>.Empty);
        // Earlier RCs used byte offsets. Convert each equal-content occurrence
        // into its copied-file identity while retaining the original aliases.
        IEnumerable<PhysicalReceipt> physical = records.Select(TryReadPhysicalReceipt).OfType<PhysicalReceipt>();
        foreach (
            IGrouping<(string FileName, string Hash), PhysicalReceipt> group in physical.GroupBy(record =>
                (record.FileName, record.Hash)
            )
        )
        {
            for (int occurrence = 0; occurrence < group.Count(); occurrence++)
            {
                records = records.Add(
                    group.Key.FileName + ":" + group.Key.Hash + ":" + occurrence.ToString(CultureInfo.InvariantCulture)
                );
            }
        }
        return journey with
        {
            RecordTrackingStart = journey.RecordTrackingStart ?? journey.Watermark,
            RecordTrackingStartComplete = journey.RecordTrackingStart is not null
                ? journey.RecordTrackingStartComplete
                : journey.WatermarkRecordsComplete ?? journey.WatermarkRecords is not null,
            RecordedJournalRecords = records,
        };
    }

    internal static bool CoversRecordedProgress(JourneyDocument saved, JourneyDocument rebuilt)
    {
        IReadOnlySet<string> previous = InitializeRecordTracking(saved).RecordedJournalRecords!;
        IReadOnlySet<string> observed = InitializeRecordTracking(rebuilt).RecordedJournalRecords!;
        return previous.Where(record => TryReadPhysicalReceipt(record) is null).All(observed.Contains);
    }

    private sealed record PhysicalReceipt(string FileName, string Hash);

    private static PhysicalReceipt? TryReadPhysicalReceipt(string receipt)
    {
        int hashSeparator = receipt.LastIndexOf(':');
        if (hashSeparator <= 0 || receipt.Length - hashSeparator - 1 != 64)
        {
            return null;
        }
        int positionSeparator = receipt.LastIndexOf(':', hashSeparator - 1);
        return
            positionSeparator > 0
            && long.TryParse(
                receipt.AsSpan(positionSeparator + 1, hashSeparator - positionSeparator - 1),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out _
            )
            ? new(receipt[..positionSeparator], receipt[(hashSeparator + 1)..])
            : null;
    }

    private static string? RecordReceipt(JournalEventEnvelope entry) =>
        entry.SourceContentIdentity ?? PhysicalRecordReceipt(entry);

    private static string? PhysicalRecordReceipt(JournalEventEnvelope entry) =>
        string.IsNullOrWhiteSpace(entry.SourceRecordIdentity)
            ? null
            : entry.SourceRecordIdentity
                + ":"
                + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(entry.RawJson)));

    private bool IsRecorded(JournalEventEnvelope entry) =>
        (RecordReceipt(entry) is { } primary && State.RecordedJournalRecords!.Contains(primary))
        || (PhysicalRecordReceipt(entry) is { } physical && State.RecordedJournalRecords!.Contains(physical));

    public bool Apply(JournalEventEnvelope journalEvent)
    {
        lock (stateLock)
        {
            bool applied = ApplyRecord(journalEvent, catchUp: false);
            if (requiresReplay && !observationBatch)
            {
                ReplayObservations();
            }
            return applied;
        }
    }

    internal void ApplyPendingRecord(JournalEventEnvelope entry)
    {
        if (entry.Timestamp < State.StartTime || (State.EndTime is { } end && entry.Timestamp > end))
        {
            return;
        }
        if (
            !Apply(entry)
            && !IsRecorded(entry)
            && entry.EventName is not ("Fileheader" or "Commander" or "LoadGame" or "Music")
        )
        {
            throw new IOException(
                "Pending Journey activity was retained because the saved snapshot lacks matching record coverage. Restore journal history before retrying."
            );
        }
    }

    private bool ApplyRecord(JournalEventEnvelope entry, bool catchUp)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lastObservation = null;
        if (entry.Timestamp is not { } timestamp)
        {
            return false;
        }
        string? originalReceipt = RecordReceipt(entry);
        if (State.EndTime is { } end && timestamp > end)
        {
            return false;
        }
        if (BeforeReplayRange(timestamp, originalReceipt))
        {
            PrimeAtTime(entry);
            lastObservation = entry;
            return false;
        }
        if (catchUp && originalReceipt is null && timestamp == catchUpBoundary && !catchUpBoundaryComplete)
        {
            PrimeAtTime(entry);
            lastObservation = entry;
            return false;
        }
        JournalEventEnvelope prepared = PrepareObservation(entry);
        lastObservation = prepared;
        if (IsRecorded(entry))
        {
            if (!CaptureObservation(prepared))
            {
                PrimeAtTime(entry);
            }
            return false;
        }
        if (catchUp && timestamp == State.RecordTrackingStart && !State.RecordTrackingStartComplete)
        {
            RememberRecord(originalReceipt);
            RememberBaselineIdentity(originalReceipt);
            PrimeAtTime(entry);
            return false;
        }
        if (CannotReplayBeforeBaseline(entry))
        {
            return false;
        }
        AppendObservation(prepared);
        if (!requiresReplay)
        {
            ApplyProjectedEvent(entry);
        }
        RememberRecord(prepared.SourceContentIdentity);
        UpdateBoundary(
            timestamp,
            prepared.SourceContentIdentity!.StartsWith("$journey-local$", StringComparison.Ordinal)
                ? null
                : originalReceipt
        );
        return true;
    }

    private bool BeforeReplayRange(DateTimeOffset timestamp, string? receipt) =>
        timestamp < State.RecordTrackingStart || (receipt is null && timestamp < State.Watermark);

    private void RememberRecord(string? receipt)
    {
        if (receipt is not null)
        {
            var records = (ImmutableHashSet<string>)State.RecordedJournalRecords!;
            ImmutableHashSet<string> updated = records.Add(receipt);
            HasRecordMetadataChanges |= !ReferenceEquals(records, updated);
            State = State with { RecordedJournalRecords = updated };
        }
    }

    private void UpdateBoundary(DateTimeOffset timestamp, string? receipt)
    {
        if (timestamp < State.Watermark)
        {
            return;
        }
        bool advancing = timestamp > State.Watermark;
        HashSet<string> boundary = advancing
            ? []
            : new(State.WatermarkRecords ?? ImmutableHashSet<string>.Empty, StringComparer.Ordinal);
        if (receipt is not null)
        {
            boundary.Add(receipt);
        }
        State = State with
        {
            Watermark = timestamp,
            WatermarkRecords = boundary,
            WatermarkRecordsComplete =
                receipt is not null
                && (advancing || (State.WatermarkRecordsComplete ?? State.WatermarkRecords is not null)),
        };
    }

    public JourneyReplaySummary ApplyCatchUp(IEnumerable<JournalEventEnvelope> journalEvents)
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        lock (stateLock)
        {
            catchUpBoundary = State.Watermark;
            catchUpBoundaryComplete = State.WatermarkRecordsComplete ?? State.WatermarkRecords is not null;
            int processed = 0;
            int ignored = 0;
            observationBatch = true;
            try
            {
                foreach (JournalEventEnvelope entry in journalEvents)
                {
                    if (ApplyRecord(entry, catchUp: true))
                    {
                        processed++;
                    }
                    else
                    {
                        ignored++;
                    }
                }
            }
            finally
            {
                observationBatch = false;
            }
            if (requiresReplay)
            {
                ReplayObservations();
            }
            return new(Journey, processed, ignored);
        }
    }

    internal JourneyJournalProcessor CreateMergeProcessor(JourneyDocument stored)
    {
        var processor = new JourneyJournalProcessor(stored, exobiologyCatalog, isOdyssey);
        return processor;
    }

    private void PrimeAtTime(JournalEventEnvelope entry)
    {
        CaptureBaselineContext(entry);
        PrimeWithoutCapturing(entry);
    }

    private int FindVisitAt(DateTimeOffset timestamp)
    {
        for (int index = State.VisitedSystems.Count - 1; index >= 0; index--)
        {
            JourneySystemVisit visit = State.VisitedSystems[index];
            if (visit.Arrived <= timestamp && (visit.Departed is null || timestamp <= visit.Departed))
            {
                return index;
            }
        }
        return -1;
    }

    private void ApplyArrivalAtTime(JourneySystemReference system, DateTimeOffset timestamp)
    {
        var visits = State.VisitedSystems.ToList();
        int existing = FindVisitAt(timestamp);
        if (existing >= 0 && visits[existing].StarSystem.SystemAddress == system.SystemAddress)
        {
            return;
        }
        int next = visits.FindIndex(visit => visit.Arrived > timestamp);
        int insertion = next < 0 ? visits.Count : next;
        if (insertion > 0)
        {
            JourneySystemVisit previous = visits[insertion - 1];
            if (previous.Departed is null || previous.Departed > timestamp)
            {
                visits[insertion - 1] = previous with { Departed = timestamp };
            }
        }
        visits.Insert(
            insertion,
            new JourneySystemVisit(
                system,
                timestamp,
                next < 0 ? null : visits[next].Arrived,
                JourneyCounts.Empty,
                null,
                null,
                null,
                null,
                null,
                null,
                null
            )
        );
        State = State with { VisitedSystems = visits };
    }
}
