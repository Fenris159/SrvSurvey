using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Journeys;

public sealed partial class JourneyJournalProcessor
{
    private readonly System.Threading.Lock stateLock = new();
    private JourneyProgressBaseline baseline = null!;
    private ImmutableList<JourneyObservedRecord> observations = ImmutableList<JourneyObservedRecord>.Empty;
    private readonly Dictionary<string, int> observationIndices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (long Offset, string Identity)> observedSourcePositions = new(
        StringComparer.Ordinal
    );
    private readonly string localWriter = Guid.NewGuid().ToString("N");
    private long localSequence;
    private long observationRevision;
    private bool observationBatch;
    private bool requiresReplay;
    private JournalEventEnvelope? lastObservation;

    private static readonly HashSet<string> ProjectionFields = new(StringComparer.Ordinal)
    {
        "timestamp",
        "event",
        "Odyssey",
        "StarSystem",
        "SystemAddress",
        "StarPos",
        "JumpType",
        "BodyCount",
        "Count",
        "BodyID",
        "PlanetClass",
        "StarType",
        "TerraformState",
        "MassEM",
        "StellarMass",
        "WasDiscovered",
        "WasMapped",
        "ProbesUsed",
        "EfficiencyTarget",
        "Body",
        "Signals",
        "SignalType",
        "EntryID",
        "IsNewEntry",
        "Name_Localised",
        "Name",
        "SubCategory_Localised",
        "ScanType",
        "Species",
    };

    private void InitializeObservationHistory(JourneyDocument source)
    {
        bodies.Clear();
        observationIndices.Clear();
        observedSourcePositions.Clear();
        requiresReplay = false;
        observationBatch = false;
        if (JourneyProgressSignature.IsTrusted(source))
        {
            baseline = source.ObservationHistory!.Baseline;
            observations = source.ObservationHistory.Records.ToImmutableList();
        }
        else
        {
            IReadOnlyList<JourneyObservedRecord> context = source.ObservationHistory is { } old
                ? old
                    .Baseline.Context.Concat(old.Records)
                    .Where(record => record.Timestamp <= source.Watermark)
                    .DistinctBy(record => record.Identity)
                    .ToArray()
                : [];
            baseline = new(
                source.Watermark,
                source.VisitedSystems,
                State.RecordedJournalRecords!,
                context,
                source.VisitedSystems.Count == 0 && source.Watermark == source.StartTime,
                isOdyssey
            );
            observations = ImmutableList<JourneyObservedRecord>.Empty;
        }
        IReadOnlyDictionary<string, int> legacyCounts = JourneyObservationOrder.Multiplicities(
            baseline.Context.Concat(observations).ToArray()
        );
        observations = observations.Select(record => WithLegacyOrder(record, legacyCounts)).ToImmutableList();
        baseline = baseline with
        {
            Context = baseline.Context.Select(record => WithLegacyOrder(record, legacyCounts)).ToArray(),
        };
        for (int index = 0; index < observations.Count; index++)
        {
            observationIndices[observations[index].Identity] = index;
        }
        // This cache is seeded from the saved point, never an end-of-pending-batch cache.
        foreach (JourneyObservedRecord record in OrderedContextAndObservations())
        {
            if (record.Timestamp <= State.Watermark)
            {
                PrimeWithoutCapturing(Decode(record));
            }
        }
        snapshotDirty = true;
    }

    private JourneyDocument Snapshot()
    {
        lock (stateLock)
        {
            if (requiresReplay && !observationBatch)
            {
                ReplayObservations();
            }
            if (snapshotDirty)
            {
                state = state with
                {
                    ObservationHistory = new(
                        baseline,
                        observations,
                        state.Watermark,
                        JourneyProgressSignature.Compute(state)
                    ),
                };
                snapshotDirty = false;
            }
            return state;
        }
    }

    private IReadOnlyList<JourneyObservedRecord> OrderedContextAndObservations() =>
        JourneyObservationOrder.Sort(
            baseline.Context.Concat(observations).DistinctBy(record => record.Identity).ToArray(),
            TimelineAliases()
        );

    private static JournalEventEnvelope Decode(JourneyObservedRecord record)
    {
        if (
            !JournalEventEnvelope.TryParse(record.RawJson, out JournalEventEnvelope? entry, out string? error)
            || entry is null
        )
        {
            throw new InvalidDataException("The saved Journey observation cannot be decoded: " + error);
        }
        return entry with
        {
            SourceContentIdentity = record.Identity,
            SourceRecordIdentity = record.SourceRecordIdentity,
            SourcePreviousContentIdentities = record.PreviousIdentities,
            SourceOrderEvidence = record.OrderEvidence,
            SourceOccurrenceCount = record.SourceOccurrenceCount,
        };
    }

    private JournalEventEnvelope PrepareObservation(JournalEventEnvelope entry)
    {
        string identity =
            RecordReceipt(entry)
            ?? "$journey-local$" + localWriter + ":" + (++localSequence).ToString(CultureInfo.InvariantCulture);
        var predecessors = new HashSet<string>(
            entry.SourcePreviousContentIdentities ?? ImmutableHashSet<string>.Empty,
            StringComparer.Ordinal
        );
        if (entry.SourcePreviousContentIdentity is { } prior)
        {
            predecessors.Add(prior);
        }
        (string file, long offset) = JourneyObservationOrder.Position(entry.SourceRecordIdentity);
        if (
            entry.SourceOrderEvidence is null
            && entry.SourcePreviousContentIdentity is null
            && entry.SourcePreviousContentIdentities is null
            && observedSourcePositions.TryGetValue(file, out (long Offset, string Identity) previous)
            && offset > previous.Offset
        )
        {
            predecessors.Add(previous.Identity);
        }
        observedSourcePositions[file] = (offset, identity);
        return entry with { SourceContentIdentity = identity, SourcePreviousContentIdentities = predecessors };
    }

    private static JourneyObservedRecord Compact(JournalEventEnvelope entry)
    {
        var root = new JsonObject();
        foreach (
            JsonProperty property in entry
                .Payload.EnumerateObject()
                .Where(property => ProjectionFields.Contains(property.Name))
        )
        {
            root[property.Name] = JsonNode.Parse(property.Value.GetRawText());
        }
        return new(
            entry.SourceContentIdentity!,
            entry.Timestamp!.Value,
            root.ToJsonString(),
            entry.SourceRecordIdentity,
            (entry.SourcePreviousContentIdentities ?? ImmutableHashSet<string>.Empty).ToImmutableHashSet(
                StringComparer.Ordinal
            )
        )
        {
            OrderEvidence = entry.SourceOrderEvidence,
            SourceOccurrenceCount = entry.SourceOccurrenceCount,
        };
    }

    private void CaptureObservation(JournalEventEnvelope prepared)
    {
        string identity = prepared.SourceContentIdentity!;
        if (!observationIndices.TryGetValue(identity, out int index))
        {
            string? physical = PhysicalRecordReceipt(prepared);
            if (physical is null || !observationIndices.TryGetValue(physical, out index))
            {
                string? alias = TimelineAliases().GetValueOrDefault(identity);
                if (alias is null || !observationIndices.TryGetValue(alias, out index))
                {
                    return;
                }
            }
        }
        JourneyObservedRecord existing = observations[index];
        ImmutableHashSet<string> previous = existing
            .PreviousIdentities.ToImmutableHashSet(StringComparer.Ordinal)
            .Union(prepared.SourcePreviousContentIdentities ?? ImmutableHashSet<string>.Empty);
        JournalOrderEvidence[] evidence = (existing.OrderEvidence ?? LegacyOrder(existing))
            .Concat(
                prepared.SourceOrderEvidence ?? previous.Select(identity => new JournalOrderEvidence(identity, 1, 1))
            )
            .Distinct()
            .ToArray();
        int count = Math.Max(existing.SourceOccurrenceCount ?? 1, prepared.SourceOccurrenceCount ?? 1);
        if (
            !previous.SetEquals(existing.PreviousIdentities)
            || count != (existing.SourceOccurrenceCount ?? 1)
            || !evidence.ToHashSet().SetEquals(existing.OrderEvidence ?? LegacyOrder(existing))
        )
        {
            observations = observations.SetItem(
                index,
                existing with
                {
                    PreviousIdentities = previous,
                    OrderEvidence = evidence,
                    SourceOccurrenceCount = count,
                }
            );
            MarkObservationChange();
            requiresReplay = true;
        }
        lastObservation = prepared with { SourceContentIdentity = existing.Identity };
    }

    private JournalOrderEvidence[] LegacyOrder(JourneyObservedRecord record)
    {
        IReadOnlyDictionary<string, int> counts = JourneyObservationOrder.Multiplicities(
            baseline.Context.Concat(observations).ToArray()
        );
        return WithLegacyOrder(record, counts).OrderEvidence!.ToArray();
    }

    private static JourneyObservedRecord WithLegacyOrder(
        JourneyObservedRecord record,
        IReadOnlyDictionary<string, int> counts
    ) =>
        record.OrderEvidence is not null
            ? record
            : record with
            {
                OrderEvidence = record
                    .PreviousIdentities.Select(previous => new JournalOrderEvidence(
                        previous,
                        counts.GetValueOrDefault(JourneyObservationOrder.ContentKey(record.Identity), 1),
                        counts.GetValueOrDefault(JourneyObservationOrder.ContentKey(previous), 1)
                    ))
                    .ToArray(),
            };

    internal void MergeRetainedOrdering(JourneyDocument saved)
    {
        if (saved.ObservationHistory is not { } history)
        {
            return;
        }
        IReadOnlyDictionary<string, int> counts = JourneyObservationOrder.Multiplicities(history.Records);
        foreach (JourneyObservedRecord record in history.Records)
        {
            JourneyObservedRecord known = WithLegacyOrder(record, counts);
            JournalEventEnvelope entry = Decode(known);
            if (IsRecorded(entry))
            {
                CaptureObservation(entry);
            }
        }
        if (requiresReplay)
        {
            ReplayObservations();
        }
    }

    private void AppendObservation(JournalEventEnvelope prepared)
    {
        JourneyObservedRecord record = Compact(prepared);
        if (observations.Count > 0 && record.Timestamp <= observations[^1].Timestamp)
        {
            requiresReplay = true;
        }
        if (record.Timestamp < baseline.Watermark)
        {
            requiresReplay = true;
        }
        observationIndices[record.Identity] = observations.Count;
        observations = observations.Add(record);
        lastObservation = prepared;
        MarkObservationChange();
    }

    private void MarkObservationChange()
    {
        observationRevision++;
        HasRecordMetadataChanges = true;
        snapshotDirty = true;
    }

    private void CaptureBaselineContext(JournalEventEnvelope entry)
    {
        if (
            entry.Timestamp is not { } time
            || time > baseline.Watermark
            || entry.EventName is not ("Scan" or "Fileheader")
        )
        {
            return;
        }
        string identity =
            RecordReceipt(entry)
            ?? "context:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(entry.RawJson)));
        if (observationIndices.ContainsKey(identity) || baseline.Context.Any(record => record.Identity == identity))
        {
            return;
        }
        JourneyObservedRecord record = Compact(entry with { SourceContentIdentity = identity });
        baseline = baseline with { Context = [.. baseline.Context, record] };
        MarkObservationChange();
        requiresReplay = true;
    }

    private void RememberBaselineIdentity(string? identity)
    {
        if (identity is null || baseline.KnownRecords.Contains(identity))
        {
            return;
        }
        baseline = baseline with
        {
            KnownRecords = baseline.KnownRecords.ToImmutableHashSet(StringComparer.Ordinal).Add(identity),
        };
        MarkObservationChange();
    }

    private bool CannotReplayBeforeBaseline(JournalEventEnvelope entry) =>
        !baseline.IsOriginKnown
        && entry.Timestamp <= baseline.Watermark
        && entry.EventName is "Location" or "FSDJump" or "CarrierJump" or "StartJump" or "Scan" or "SAAScanComplete";

    private void ReplayObservations()
    {
        JourneyDocument current = State;
        State = current with
        {
            Watermark = baseline.Watermark,
            VisitedSystems = baseline.VisitedSystems,
            RecordedJournalRecords = baseline.KnownRecords.ToImmutableHashSet(StringComparer.Ordinal),
        };
        bodies.Clear();
        isOdyssey = baseline.IsOdyssey;
        var contextIds = baseline.Context.Select(record => record.Identity).ToHashSet(StringComparer.Ordinal);
        foreach (JourneyObservedRecord record in OrderedContextAndObservations())
        {
            JournalEventEnvelope entry = Decode(record);
            if (contextIds.Contains(record.Identity))
            {
                PrimeWithoutCapturing(entry);
                continue;
            }
            ApplyProjectedEvent(entry);
            RememberRecord(record.Identity);
            UpdateBoundary(
                record.Timestamp,
                record.Identity.StartsWith("$journey-local$", StringComparison.Ordinal) ? null : record.Identity
            );
        }
        IReadOnlyList<JourneySystemVisit> visits = RestoreNotes(current.VisitedSystems, State.VisitedSystems);
        // Non-statistical records can have advanced the saved watermark past the last contributing observation.
        if (State.Watermark < current.Watermark)
        {
            State = State with
            {
                Watermark = current.Watermark,
                WatermarkRecords = current.WatermarkRecords,
                WatermarkRecordsComplete = current.WatermarkRecordsComplete,
            };
        }
        State = State with { VisitedSystems = visits };
        requiresReplay = false;
    }

    private void ApplyProjectedEvent(JournalEventEnvelope entry)
    {
        applyingVisitIndex = entry.Timestamp <= baseline.Watermark ? FindVisitAt(entry.Timestamp.Value) : null;
        try
        {
            ApplyEvent(entry);
        }
        finally
        {
            applyingVisitIndex = null;
        }
    }

    internal static IReadOnlyList<JourneySystemVisit> RestoreNotes(
        IReadOnlyList<JourneySystemVisit> saved,
        IReadOnlyList<JourneySystemVisit> rebuilt
    )
    {
        JourneySystemVisit[] visits = rebuilt
            .Select(visit => visit with { Counts = visit.Counts with { Notes = 0 } })
            .ToArray();
        foreach (JourneySystemVisit original in saved.Where(visit => visit.Counts.Notes > 0))
        {
            int index = Array.FindIndex(
                visits,
                visit =>
                    visit.StarSystem.SystemAddress == original.StarSystem.SystemAddress
                    && visit.Arrived == original.Arrived
            );
            if (index < 0)
            {
                index = Array.FindLastIndex(
                    visits,
                    visit =>
                        visit.StarSystem.SystemAddress == original.StarSystem.SystemAddress
                        && visit.Arrived <= original.Arrived
                        && (visit.Departed is null || original.Arrived <= visit.Departed)
                );
            }
            if (index < 0)
            {
                throw new IOException(
                    "Saved Journey note counts could not be attributed; the original observations were retained."
                );
            }
            JourneySystemVisit visit = visits[index];
            visits[index] = visit with
            {
                Counts = visit.Counts with { Notes = checked(visit.Counts.Notes + original.Counts.Notes) },
            };
        }
        return visits;
    }

    private void PrimeWithoutCapturing(JournalEventEnvelope entry, bool replaceEqual = true)
    {
        applyingVisitIndex = entry.Timestamp is { } timestamp ? FindVisitAt(timestamp) : -1;
        try
        {
            PrimeEvent(entry, replaceEqual);
        }
        finally
        {
            applyingVisitIndex = null;
        }
    }

    private Dictionary<string, string> TimelineAliases()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        IEnumerable<(string Identity, PhysicalReceipt? Parsed)> physical = State
            .RecordedJournalRecords!.Concat(observations.Select(record => record.Identity))
            .Distinct(StringComparer.Ordinal)
            .Select(identity => (Identity: identity, Parsed: TryReadPhysicalReceipt(identity)))
            .Where(pair => pair.Parsed is not null);
        foreach (
            IGrouping<
                (string FileName, string Hash),
                (string Identity, PhysicalReceipt? Parsed)
            > group in physical.GroupBy(pair => (pair.Parsed!.FileName, pair.Parsed.Hash))
        )
        {
            int occurrence = 0;
            foreach (
                (string Identity, PhysicalReceipt? Parsed) pair in group.OrderBy(pair =>
                    JourneyObservationOrder.Position(pair.Identity[..pair.Identity.LastIndexOf(':')]).Offset
                )
            )
            {
                result[
                    group.Key.FileName + ":" + group.Key.Hash + ":" + occurrence.ToString(CultureInfo.InvariantCulture)
                ] = pair.Identity;
                occurrence++;
            }
        }
        return result;
    }

    internal JourneyObservedBatch ApplyLiveBatch(IEnumerable<JournalEventEnvelope> entries)
    {
        lock (stateLock)
        {
            var changed = new List<JournalEventEnvelope>();
            int processed = 0;
            observationBatch = true;
            try
            {
                foreach (JournalEventEnvelope entry in entries)
                {
                    long revision = observationRevision;
                    if (ApplyRecord(entry, catchUp: false))
                    {
                        processed++;
                    }
                    if (observationRevision != revision && lastObservation is { } observation)
                    {
                        changed.Add(observation);
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
            return new(processed, changed);
        }
    }

    internal void MergePendingRecords(IEnumerable<JournalEventEnvelope> entries)
    {
        lock (stateLock)
        {
            observationBatch = true;
            try
            {
                foreach (JournalEventEnvelope entry in entries)
                {
                    ApplyPendingRecord(entry);
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
        }
    }
}

internal sealed record JourneyObservedBatch(int Processed, IReadOnlyList<JournalEventEnvelope> Observations);
