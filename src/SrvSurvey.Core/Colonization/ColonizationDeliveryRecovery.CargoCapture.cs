using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Colonization;

public sealed partial class ColonizationDeliveryRecovery
{
    private ColonizationDockingSnapshot? cargoDockBeforeSessionEnd;

    private void CaptureCargoSessionEndDock(JournalEventEnvelope journalEvent)
    {
        if (
            journalEvent.EventName == "Shutdown"
            || (journalEvent.EventName == "Music" && GetJournalString(journalEvent.Payload, "MusicTrack") == "MainMenu")
        )
        {
            cargoDockBeforeSessionEnd ??= constructionState.CurrentDock;
        }
        else if (
            journalEvent.EventName
            is "Fileheader"
                or "LoadGame"
                or "Commander"
                or "Docked"
                or "Location"
                or "Undocked"
                or "StartJump"
                or "Died"
                or "Resurrect"
        )
        {
            cargoDockBeforeSessionEnd = null;
        }
    }

    /// <summary>
    /// Durably captures an attributable live batch before any network work. The returned entries are handed
    /// to live synchronization so journal transfers and squadron inventory differences are consumed once.
    /// </summary>
    public IReadOnlyList<ColonizationPendingCargoAdjustment> CaptureFleetCarrierCargo(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        CargoInventoryState? cargoInventory = null,
        bool cargoActivity = false,
        bool preferShipCargoDiffForSquadron = true
    )
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        if (!isEnabled || !FleetCarrierCargoSyncEnabled || commanderName is null || apiKey is null)
        {
            ClearSquadronCargoSyncState(cargoInventory);
            return [];
        }
        ColonizationDockingSnapshot? diffDock = constructionState.CurrentDock ?? cargoDockBeforeSessionEnd;
        bool preferDiff =
            cargoInventory is not null
            && preferShipCargoDiffForSquadron
            && journalEvents
                .Where(IsCarrierCargoEvent)
                .All(item => GetJournalDock(item)?.MarketId == diffDock?.MarketId);
        var captured = new List<ColonizationPendingCargoAdjustment>();
        foreach (JournalEventEnvelope journalEvent in journalEvents.Where(IsCarrierCargoEvent))
        {
            if (GetJournalDock(journalEvent) is { } dock && IsLinkedFleetCarrier(dock.MarketId))
            {
                CaptureCargoDelta(
                    captured,
                    dock.MarketId,
                    ColonizationFleetCarrierCargoSynchronizer.CreateJournalAdjustment(
                        journalEvent,
                        dock,
                        inMainShip,
                        preferDiff
                    ),
                    journalEvent.Timestamp,
                    journalEvent.EventName
                );
            }
        }
        if (cargoInventory is not null)
        {
            if (
                preferDiff
                && diffDock is not null
                && IsLinkedSquadronFleetCarrier(diffDock)
                && (cargoActivity || cargoInventory.HasPreservedSnapshot)
            )
            {
                CaptureCargoDelta(
                    captured,
                    diffDock.MarketId,
                    ColonizationFleetCarrierCargoSynchronizer.CreateSquadronCargoDiffAdjustment(
                        cargoInventory.GetDiff(excludeMarketChanges: true)
                    ),
                    null,
                    "squadron cargo diff"
                );
            }
            else
            {
                cargoInventory.GetDiff();
            }
        }
        if (captured.Count > 0)
        {
            SavePendingCargoAdjustments();
        }
        return captured;
    }

    private static bool IsCarrierCargoEvent(JournalEventEnvelope journalEvent) =>
        journalEvent.EventName is "MarketBuy" or "MarketSell" or "CargoTransfer";

    private void CaptureCargoDelta(
        List<ColonizationPendingCargoAdjustment> captured,
        long marketId,
        IReadOnlyDictionary<string, int> delta,
        DateTimeOffset? recordedAt,
        string sourceEventName
    )
    {
        if (delta.Count == 0)
        {
            return;
        }
        var pending = new ColonizationPendingCargoAdjustment(
            RecoveryOwner,
            marketId,
            new(delta, StringComparer.OrdinalIgnoreCase),
            recordedAt,
            null,
            Attempted: false,
            OutcomeUnknown: false,
            ObservedAt: utcNow(),
            SourceEventName: sourceEventName
        );
        failedCargoAdjustments.Add(pending);
        captured.Add(pending);
        if (IsCargoBaselinePending(marketId))
        {
            if (!pendingCargoDeltas.TryGetValue(marketId, out List<ColonizationPendingCargoAdjustment>? queued))
            {
                queued = [];
                pendingCargoDeltas[marketId] = queued;
            }
            queued.Add(pending);
        }
    }

    private async Task<IReadOnlyList<ColonizationDeliveryNotice>> SendCapturedCargoAsync(
        IReadOnlyList<ColonizationPendingCargoAdjustment> captured,
        CancellationToken cancellationToken
    )
    {
        int version = profileVersion;
        var notices = new List<ColonizationDeliveryNotice>();
        foreach (ColonizationPendingCargoAdjustment pending in captured)
        {
            if (version != profileVersion || cancellationToken.IsCancellationRequested)
            {
                break;
            }
            if (pending.Owner != RecoveryOwner || !failedCargoAdjustments.Contains(pending))
            {
                continue;
            }
            if (IsCargoBaselinePending(pending.MarketId))
            {
                AddNotice(
                    notices,
                    new(
                        pending.SourceEventName == "squadron cargo diff"
                            ? ColonizationDeliveryNoticeKind.SquadronCargoQueued
                            : ColonizationDeliveryNoticeKind.CarrierCargoQueued,
                        Count: pending.Delta.Count
                    )
                );
                continue;
            }
            try
            {
                AddNotice(
                    notices,
                    await ApplyPendingFleetCarrierCargoAdjustmentAsync(
                        pending,
                        pending.SourceEventName ?? "cargo journal",
                        cancellationToken
                    )
                );
            }
            catch (Exception exception)
                when (exception is HttpRequestException or OperationCanceledException or InvalidDataException)
            {
                AddNotice(
                    notices,
                    new(ColonizationDeliveryNoticeKind.QueuedCarrierCargoRetained, Detail: exception.Message)
                );
            }
        }
        return notices;
    }

    /// <summary>Attempts retained cargo at shutdown without replaying journal commands or contribution credits.</summary>
    public async Task<IReadOnlyList<ColonizationDeliveryNotice>> FlushFleetCarrierCargoAsync(
        CancellationToken cancellationToken = default
    )
    {
        if (!isEnabled || !FleetCarrierCargoSyncEnabled || commanderName is null || retryingWrites)
        {
            return [];
        }
        retryingWrites = true;
        try
        {
            return await RetryCargoAdjustmentsAsync(RecoveryOwner, profileVersion, [], cancellationToken);
        }
        finally
        {
            retryingWrites = false;
        }
    }
}
