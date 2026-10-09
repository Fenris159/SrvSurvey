using SrvSurvey.Core.Frontier;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Colonization;

public sealed partial class ColonizationDeliveryRecovery
{
    /// <summary>
    /// Market IDs that already received a once-per-session CAPI full-manifest seed.
    /// Further Frontier refreshes are ignored for Raven cargo; journals keep totals live.
    /// </summary>
    private readonly HashSet<long> capiCargoSeededMarketIds = [];

    /// <summary>
    /// EDMC-style dock/Market.json baseline: journal cargo deltas queue here until the
    /// async baseline finishes so MarketBuy/Sell/Transfer cannot race a replace.
    /// Nested CAPI/market/dock baselines share a depth count so an inner complete
    /// cannot replay deltas while an outer baseline is still in flight.
    /// </summary>
    private readonly Dictionary<long, int> cargoBaselinePendingDepth = [];

    private readonly HashSet<long> cargoBaselineReady = [];

    private readonly Dictionary<long, List<PendingCarrierCargoDelta>> pendingCargoDeltas = [];

    private sealed record PendingCarrierCargoDelta(Dictionary<string, int> Delta, DateTimeOffset? RecordedAt);

    private bool fleetCarrierSyncBusy;
    private int capiCargoSeedGeneration;
    private (long MarketId, DateTimeOffset Timestamp)? lastSyncedMarket;

    /// <summary>
    /// When true, the next squadron cargo GetDiff is skipped because MarketBuy/Sell
    /// already sent AdjustFleetCarrierCargo for this linked squadron FC (legacy parity).
    /// </summary>
    private bool skipNextCargoEvent;

    /// <summary>
    /// Whether the current Market.json belongs to a linked carrier opened since docking, ignoring busy state.
    /// </summary>
    public bool CanSyncFleetCarrierCargo
    {
        get
        {
            if (
                !isEnabled
                || !FleetCarrierCargoSyncEnabled
                || apiKey is null
                || currentMarket is null
                || !string.Equals(
                    currentMarket.StationType,
                    FleetCarrierStationType,
                    StringComparison.OrdinalIgnoreCase
                )
                || !IsLinkedFleetCarrier(currentMarket.MarketId)
            )
            {
                return false;
            }

            ColonizationDockingSnapshot? dock = constructionState.CurrentDock;
            return dock?.Timestamp is not null
                && dock.MarketId == currentMarket.MarketId
                && currentMarket.Timestamp > dock.Timestamp;
        }
    }

    /// <summary>
    /// Freeze ship cargo before CargoTransfer mutates the live projection when docked on a
    /// linked squadron fleet carrier. Squadron carriers do not use journal transfer deltas;
    /// they rely on the aggregate ship cargo difference.
    /// </summary>
    public void PrepareSquadronCargoTransferSnapshot(CargoInventoryState cargo)
    {
        ArgumentNullException.ThrowIfNull(cargo);
        if (
            !FleetCarrierCargoSyncEnabled
            || apiKey is null
            || constructionState.CurrentDock is not { } dock
            || !IsLinkedSquadronFleetCarrier(dock)
        )
        {
            return;
        }

        // Preserve the first before-state across multiple CargoTransfer events in one poll.
        if (!cargo.HasPreservedSnapshot)
        {
            cargo.CaptureBeforeSnapshot();
        }
    }

    /// <summary>Registers the docked carrier and serializes its absolute cargo baseline before replaying queued transactions.</summary>
    public async Task PublishCurrentFleetCarrierAsync()
    {
        if (constructionState.CurrentDock is not { } dock || apiKey is not { } publishApiKey)
        {
            return;
        }

        int version = profileVersion;
        BeginCargoBaselinePending(dock.MarketId);
        bool published = false;
        SetFleetCarrierSyncBusy(true);
        observer.FleetCarrierStatusChanged(new(ColonizationDeliveryNoticeKind.CarrierPublishing, dock.StationName));
        try
        {
            ColonizationFleetCarrier registered = await client.PublishFleetCarrierAsync(
                new ColonizationFleetCarrierRegistration
                {
                    MarketId = dock.MarketId,
                    Name = dock.StationName,
                    DisplayName = fleetCarrierIdentityTracker.ResolveDisplayName(dock.StationName),
                },
                publishApiKey,
                CancellationToken.None
            );
            if (version != profileVersion)
            {
                return;
            }

            published = true;
            registered = registered with
            {
                Cargo = registered.Cargo ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            };
            ReplaceLocalFleetCarrier(registered);
            await ReplacePublishedCarrierCargoAsync(dock, registered, publishApiKey, version);
        }
        catch (Exception exception)
            when (exception
                    is HttpRequestException
                        or InvalidDataException
                        or TaskCanceledException
                        or ArgumentException
            )
        {
            if (version == profileVersion)
            {
                observer.FleetCarrierStatusChanged(
                    new(
                        published
                            ? ColonizationDeliveryNoticeKind.CarrierLinkedCargoNotUpdated
                            : ColonizationDeliveryNoticeKind.CarrierNotPublished,
                        Detail: exception.Message
                    )
                );
            }
        }
        finally
        {
            if (version == profileVersion)
            {
                observer.PendingFleetCarrierCargoChanged(null);
                await CompleteCargoBaselineAsync(dock.MarketId, CancellationToken.None);
                if (version == profileVersion)
                {
                    SetFleetCarrierSyncBusy(false);
                }
            }
        }
    }

    /// <summary>
    /// Seeds RavenColonial with a full CAPI cargo manifest once per linked carrier per
    /// session (EDMC parity). Later Frontier refreshes are ignored; journal deltas keep
    /// linked + workspace totals current between CAPI updates.
    /// </summary>
    public async Task SeedLinkedCarrierCargoFromCapiAsync(FrontierAccountSnapshot? snapshot)
    {
        if (snapshot is null || !FleetCarrierCargoSyncEnabled || apiKey is null || fleetCarriers.Count == 0)
        {
            return;
        }

        int generation = capiCargoSeedGeneration;
        await TrySeedCarrierFromCapiAsync(
            snapshot.Carrier,
            snapshot.CarrierFetchedAt ?? snapshot.FetchedAt,
            snapshot.IsDocked,
            generation
        );
        if (generation != capiCargoSeedGeneration)
        {
            return;
        }

        await TrySeedCarrierFromCapiAsync(
            snapshot.SquadronCarrier,
            snapshot.SquadronCarrierFetchedAt ?? snapshot.FetchedAt,
            snapshot.IsDocked,
            generation
        );
    }

    /// <summary>
    /// Installs the current linked carrier market as Raven's cargo baseline. Unforced calls skip a market snapshot
    /// that was already synchronized; callers report blocked synchronization themselves.
    /// </summary>
    public async Task SyncFleetCarrierCargoAsync(bool force)
    {
        if (!CanSyncFleetCarrierCargo || currentMarket is not { } market || apiKey is not { } syncApiKey)
        {
            return;
        }

        int version = profileVersion;
        (long MarketId, DateTimeOffset Timestamp) identity = (market.MarketId, market.Timestamp);
        if (!force && lastSyncedMarket == identity)
        {
            return;
        }

        ColonizationFleetCarrier localCarrier = fleetCarriers.First(candidate => candidate.MarketId == market.MarketId);
        SetFleetCarrierSyncBusy(true);
        observer.FleetCarrierStatusChanged(
            new(ColonizationDeliveryNoticeKind.CarrierCargoChecking, GetCarrierName(localCarrier))
        );
        try
        {
            await ApplyFleetCarrierMarketCargoSyncAsync(market, syncApiKey, identity, version);
        }
        catch (Exception exception)
            when (exception
                    is HttpRequestException
                        or InvalidDataException
                        or TaskCanceledException
                        or ArgumentException
            )
        {
            if (version == profileVersion)
            {
                observer.FleetCarrierStatusChanged(
                    new(ColonizationDeliveryNoticeKind.CarrierCargoNotUpdated, Detail: exception.Message)
                );
            }
        }
        finally
        {
            if (version == profileVersion)
            {
                observer.PendingFleetCarrierCargoChanged(null);
                SetFleetCarrierSyncBusy(false);
            }
        }
    }

    private void ClearSquadronCargoSyncState(CargoInventoryState? cargoInventory)
    {
        // Drop held squadron state when publishing is disabled so lastInventory /
        // skipNext cannot survive across later cargo updates.
        skipNextCargoEvent = false;
        if (cargoInventory?.HasPreservedSnapshot == true)
        {
            cargoInventory.ClearPreservedSnapshot();
        }
    }

    /// <summary>Converts expected squadron cargo synchronization failures into recoverable notices.</summary>
    private async Task<ColonizationDeliveryNotice?> TrySynchronizeSquadronCargoDiffAsync(
        CargoInventoryState cargoInventory,
        bool cargoActivity,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await SynchronizeSquadronFleetCarrierCargoDiffAsync(
                cargoInventory,
                cargoActivity,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception)
            when (exception
                    is HttpRequestException
                        or InvalidDataException
                        or TaskCanceledException
                        or ArgumentException
            )
        {
            return new(ColonizationDeliveryNoticeKind.SquadronCargoDiffSkipped, Detail: exception.Message);
        }
    }

    /// <summary>
    /// After ship cargo is updated, compute the squadron FC cargo delta from the frozen
    /// before-snapshot (or the last pre-replace inventory) and send it to Raven Colonial.
    /// </summary>
    private async Task<ColonizationDeliveryNotice?> SynchronizeSquadronFleetCarrierCargoDiffAsync(
        CargoInventoryState cargo,
        bool cargoActivity,
        CancellationToken cancellationToken = default
    )
    {
        if (!FleetCarrierCargoSyncEnabled || apiKey is null || constructionState.CurrentDock is not { } dock)
        {
            skipNextCargoEvent = false;
            if (cargo.HasPreservedSnapshot)
            {
                cargo.ClearPreservedSnapshot();
            }

            return null;
        }

        if (!ShouldSendSquadronCargoDiff(cargo, cargoActivity, dock))
        {
            return null;
        }

        // Compute diff while inventory is stable; network I/O stays outside GetDiff's lock.
        Dictionary<string, int> shipDiff = cargo.GetDiff();
        if (shipDiff.Count == 0)
        {
            return null;
        }

        IReadOnlyDictionary<string, int> adjustments =
            ColonizationFleetCarrierCargoSynchronizer.CreateSquadronCargoDiffAdjustment(shipDiff);

        // This aggregate difference has no single originating event; do not date it using the local clock.
        if (TryQueuePendingCargoDelta(dock.MarketId, adjustments, null))
        {
            return new(ColonizationDeliveryNoticeKind.SquadronCargoQueued, Count: adjustments.Count);
        }

        return await ApplyFleetCarrierCargoAdjustmentAsync(
            dock.MarketId,
            adjustments,
            "squadron cargo diff",
            true,
            cargo,
            null,
            cancellationToken: cancellationToken
        );
    }

    /// <summary>Applies legacy squadron diff gating: market-adjusted polls, unlinked docks, and inactive cargo are skipped.</summary>
    private bool ShouldSendSquadronCargoDiff(
        CargoInventoryState cargo,
        bool cargoActivity,
        ColonizationDockingSnapshot dock
    )
    {
        // MarketBuy/Sell already adjusted the FC. Suppress GetDiff only when there is
        // no preserved transfer snapshot — transfer capture baselines after market
        // events, so its GetDiff must still be sent (same-poll Market+Transfer).
        if (skipNextCargoEvent)
        {
            skipNextCargoEvent = false;
            if (!cargo.HasPreservedSnapshot)
            {
                return false;
            }
        }

        if (!IsLinkedSquadronFleetCarrier(dock))
        {
            if (cargo.HasPreservedSnapshot)
            {
                cargo.ClearPreservedSnapshot();
            }

            return false;
        }

        // Match legacy: only run after Cargo activity / preserved transfer snapshot.
        return cargoActivity || cargo.HasPreservedSnapshot;
    }

    /// <summary>Attributes a carrier transaction to its captured dock and queues it behind a pending baseline.</summary>
    private async Task<ColonizationDeliveryNotice?> SynchronizeFleetCarrierCargoAdjustmentAsync(
        JournalEventEnvelope journalEvent,
        bool preferShipCargoDiffForSquadron,
        CargoInventoryState? cargoInventory = null,
        CancellationToken cancellationToken = default
    )
    {
        if (
            !FleetCarrierCargoSyncEnabled
            || apiKey is null
            || GetJournalDock(journalEvent) is not { } dock
            || !IsLinkedFleetCarrier(dock.MarketId)
        )
        {
            return null;
        }

        IReadOnlyDictionary<string, int> adjustments =
            ColonizationFleetCarrierCargoSynchronizer.CreateJournalAdjustment(
                journalEvent,
                dock,
                inMainShip,
                preferShipCargoDiffForSquadron
            );
        if (adjustments.Count == 0)
        {
            return null;
        }

        if (TryQueuePendingCargoDelta(dock.MarketId, adjustments, journalEvent.Timestamp))
        {
            SuppressSquadronCargoDiffAfterMarketAdjustment(
                journalEvent.EventName,
                dock,
                preferShipCargoDiffForSquadron
            );
            return new(ColonizationDeliveryNoticeKind.CarrierCargoQueued, Count: adjustments.Count);
        }

        return await ApplyFleetCarrierCargoAdjustmentAsync(
            dock.MarketId,
            adjustments,
            journalEvent.EventName,
            preferShipCargoDiffForSquadron,
            cargoInventory,
            journalEvent.Timestamp,
            cancellationToken: cancellationToken
        );
    }

    /// <summary>Retains relative cargo updates until acknowledged and prevents later updates overtaking uncertain writes.</summary>
    private async Task<ColonizationDeliveryNotice?> ApplyFleetCarrierCargoAdjustmentAsync(
        long marketId,
        IReadOnlyDictionary<string, int> adjustments,
        string sourceEventName,
        bool preferShipCargoDiffForSquadron,
        CargoInventoryState? cargoInventory,
        DateTimeOffset? recordedAt,
        CancellationToken cancellationToken = default
    )
    {
        if (apiKey is null || adjustments.Count == 0)
        {
            return null;
        }
        var pending = new ColonizationPendingCargoAdjustment(
            RecoveryOwner,
            marketId,
            new Dictionary<string, int>(adjustments, StringComparer.OrdinalIgnoreCase),
            recordedAt,
            null
        );
        failedCargoAdjustments.Add(pending);
        return await ApplyPendingFleetCarrierCargoAdjustmentAsync(
            pending,
            sourceEventName,
            preferShipCargoDiffForSquadron,
            cargoInventory,
            cancellationToken
        );
    }

    private async Task<ColonizationDeliveryNotice?> ApplyPendingFleetCarrierCargoAdjustmentAsync(
        ColonizationPendingCargoAdjustment pending,
        string sourceEventName,
        bool preferShipCargoDiffForSquadron,
        CargoInventoryState? cargoInventory,
        CancellationToken cancellationToken
    )
    {
        if (apiKey is not { } adjustApiKey || pending.Delta.Count == 0)
        {
            return null;
        }
        int version = profileVersion;
        long marketId = pending.MarketId;
        IReadOnlyDictionary<string, int> adjustments = pending.Delta;
        int pendingIndex = failedCargoAdjustments.IndexOf(pending);
        if (pendingIndex < 0)
        {
            return null;
        }
        bool blocked = failedCargoAdjustments
            .Take(pendingIndex)
            .Any(item => item.Owner == pending.Owner && item.MarketId == marketId);
        pending = pending with
        {
            Attempted = !blocked,
            Before = fleetCarriers.FirstOrDefault(carrier => carrier.MarketId == marketId) is { } carrier
                ? CopyCargo(carrier.Cargo)
                : null,
        };
        failedCargoAdjustments[pendingIndex] = pending;
        SavePendingCargoAdjustments();
        if (blocked)
        {
            if (GetJournalDockForMarket(marketId) is { } blockedDock)
            {
                SuppressSquadronCargoDiffAfterMarketAdjustment(
                    sourceEventName,
                    blockedDock,
                    preferShipCargoDiffForSquadron
                );
            }
            return new(ColonizationDeliveryNoticeKind.CarrierCargoQueuedBehindUnconfirmed);
        }
        cargoWritesInFlight.Add((pending.Owner, marketId));
        observer.PendingFleetCarrierCargoChanged(adjustments.Keys);
        try
        {
            IReadOnlyDictionary<string, int> updatedCargo = await client.AdjustFleetCarrierCargoAsync(
                marketId,
                adjustments,
                adjustApiKey,
                cancellationToken
            );
            failedCargoAdjustments.Remove(pending);
            SavePendingCargoAdjustments();
            if (version != profileVersion)
            {
                return null;
            }
            ApplyAcknowledgedCargoAdjustment(
                marketId,
                updatedCargo,
                sourceEventName,
                preferShipCargoDiffForSquadron,
                cargoInventory
            );
            return new(ColonizationDeliveryNoticeKind.CarrierCargoAdjusted, sourceEventName, Count: adjustments.Count);
        }
        catch (Exception exception)
            when (exception is HttpRequestException or TaskCanceledException or InvalidDataException)
        {
            RetainFailedCargoAdjustment(pending, exception, sourceEventName, preferShipCargoDiffForSquadron, version);
            throw;
        }
        finally
        {
            cargoWritesInFlight.Remove((pending.Owner, marketId));
            if (version == profileVersion)
            {
                observer.PendingFleetCarrierCargoChanged(null);
            }
        }
    }

    /// <summary>Installs Raven's acknowledged cargo and suppresses a duplicate squadron difference for the same purchase.</summary>
    private void ApplyAcknowledgedCargoAdjustment(
        long marketId,
        IReadOnlyDictionary<string, int> updatedCargo,
        string sourceEventName,
        bool preferShipCargoDiffForSquadron,
        CargoInventoryState? cargoInventory
    )
    {
        if (
            !preferShipCargoDiffForSquadron
            && constructionState.CurrentDock is { } dock
            && dock.MarketId == marketId
            && IsLinkedSquadronFleetCarrier(dock)
        )
        {
            cargoInventory?.ClearPreservedSnapshot();
        }
        ColonizationFleetCarrier? localCarrier = fleetCarriers.FirstOrDefault(carrier => carrier.MarketId == marketId);
        if (localCarrier is not null)
        {
            ReplaceLocalFleetCarrier(localCarrier with { Cargo = CopyCargo(updatedCargo) });
        }

        // Market buy/sell already adjusted the FC; suppress market-only GetDiff so
        // squadron carriers are not double-counted. Transfer snapshots still send.
        if (constructionState.CurrentDock is { } currentDock && currentDock.MarketId == marketId)
        {
            SuppressSquadronCargoDiffAfterMarketAdjustment(
                sourceEventName,
                currentDock,
                preferShipCargoDiffForSquadron
            );
        }
    }

    /// <summary>Records failure only while the adjustment remains pending, preserving the original transport exception.</summary>
    private void RetainFailedCargoAdjustment(
        ColonizationPendingCargoAdjustment pending,
        Exception exception,
        string sourceEventName,
        bool preferShipCargoDiffForSquadron,
        int version
    )
    {
        int index = failedCargoAdjustments.IndexOf(pending);
        if (index >= 0)
        {
            failedCargoAdjustments[index] = pending with { OutcomeUnknown = !IsDefiniteRejection(exception) };
            SavePendingCargoAdjustments();
        }
        nextWriteRetry = utcNow().Add(WriteRetryDelay);
        if (version != profileVersion)
        {
            return;
        }
        if (GetJournalDockForMarket(pending.MarketId) is { } dock)
        {
            SuppressSquadronCargoDiffAfterMarketAdjustment(sourceEventName, dock, preferShipCargoDiffForSquadron);
        }
    }

    private void SuppressSquadronCargoDiffAfterMarketAdjustment(
        string eventName,
        ColonizationDockingSnapshot dock,
        bool preferShipCargoDiffForSquadron
    )
    {
        if (
            preferShipCargoDiffForSquadron
            && eventName is "MarketBuy" or "MarketSell"
            && ColonizationFleetCarrierCargoSynchronizer.IsSquadronFleetCarrier(dock)
        )
        {
            skipNextCargoEvent = true;
        }
    }

    private bool IsLinkedFleetCarrier(long marketId) => fleetCarriers.Any(carrier => carrier.MarketId == marketId);

    private bool IsLinkedSquadronFleetCarrier(ColonizationDockingSnapshot dock)
    {
        return string.Equals(dock.StationType, FleetCarrierStationType, StringComparison.OrdinalIgnoreCase)
            && ColonizationFleetCarrierCargoSynchronizer.IsSquadronFleetCarrier(dock)
            && IsLinkedFleetCarrier(dock.MarketId);
    }

    /// <summary>Replaces a newly published carrier's cargo from a fresh market and reports the publication outcome.</summary>
    private async Task ReplacePublishedCarrierCargoAsync(
        ColonizationDockingSnapshot dock,
        ColonizationFleetCarrier registered,
        string publishApiKey,
        int version
    )
    {
        MarketSnapshot? market = GetFreshFleetCarrierMarket(dock);
        if (market is null)
        {
            observer.FleetCarrierStatusChanged(
                new(ColonizationDeliveryNoticeKind.CarrierPublishedAwaitingMarket, GetCarrierName(registered))
            );
            return;
        }

        IReadOnlyDictionary<string, int> replacements =
            ColonizationFleetCarrierCargoSynchronizer.CreateMarketReplacement(market, registered);
        if (replacements.Count == 0)
        {
            ReconcilePendingCargo(market);
            lastSyncedMarket = (market.MarketId, market.Timestamp);
            observer.FleetCarrierStatusChanged(
                new(ColonizationDeliveryNoticeKind.CarrierPublishedCargoCurrent, GetCarrierName(registered))
            );
            return;
        }

        observer.PendingFleetCarrierCargoChanged(replacements.Keys);
        IReadOnlyDictionary<string, int> updatedCargo = await client.ReplaceFleetCarrierCargoAsync(
            dock.MarketId,
            replacements,
            publishApiKey,
            CancellationToken.None
        );
        if (version != profileVersion)
        {
            return;
        }

        ReplaceLocalFleetCarrier(registered with { Cargo = CopyCargo(updatedCargo) });
        ReconcilePendingCargo(market);
        lastSyncedMarket = (market.MarketId, market.Timestamp);
        observer.FleetCarrierStatusChanged(
            new(
                ColonizationDeliveryNoticeKind.CarrierPublishedCargoUpdated,
                GetCarrierName(registered),
                Count: replacements.Count
            )
        );
    }

    private MarketSnapshot? GetFreshFleetCarrierMarket(ColonizationDockingSnapshot dock)
    {
        return
            currentMarket is not null
            && dock.Timestamp is not null
            && currentMarket.MarketId == dock.MarketId
            && string.Equals(currentMarket.StationType, FleetCarrierStationType, StringComparison.OrdinalIgnoreCase)
            && currentMarket.Timestamp > dock.Timestamp
            ? currentMarket
            : null;
    }

    private void ClearCapiCargoSeedSession()
    {
        capiCargoSeededMarketIds.Clear();
        capiCargoSeedGeneration++;
    }

    private bool IsCurrentCapiCargoSeed(int generation, int version) =>
        generation == capiCargoSeedGeneration && version == profileVersion;

    /// <summary>Installs a reliable Frontier carrier manifest through the same baseline queue as market synchronization.</summary>
    private async Task TrySeedCarrierFromCapiAsync(
        FrontierCarrierSnapshot? carrier,
        DateTimeOffset? fetchedAt,
        bool isDocked,
        int generation
    )
    {
        if (carrier is null || apiKey is not { } seedApiKey || generation != capiCargoSeedGeneration)
        {
            return;
        }

        long? marketId = ColonizationFleetCarrierCapiCargoSeeder.ResolveLinkedMarketId(carrier, fleetCarriers);
        if (marketId is not { } linkedMarketId)
        {
            return;
        }

        ColonizationFleetCarrier? localCarrier = fleetCarriers.FirstOrDefault(c => c.MarketId == linkedMarketId);
        (bool accepted, string reason) = ColonizationFleetCarrierCapiCargoSeeder.ShouldAcceptSnapshot(
            linkedMarketId,
            isDocked || constructionState.CurrentDock is not null,
            capiCargoSeededMarketIds,
            fetchedAt,
            localCarrier?.Cargo
        );
        if (!accepted)
        {
            return;
        }

        IReadOnlyDictionary<string, int> totals = ColonizationFleetCarrierCapiCargoSeeder.CreateCargoTotals(carrier);
        if (
            localCarrier is not null
            && !ColonizationFleetCarrierCapiCargoSeeder.ManifestsDiffer(localCarrier.Cargo, totals)
        )
        {
            capiCargoSeededMarketIds.Add(linkedMarketId);
            return;
        }

        int version = profileVersion;
        BeginCargoBaselinePending(linkedMarketId);
        try
        {
            observer.PendingFleetCarrierCargoChanged(totals.Keys);
            IReadOnlyDictionary<string, int> updatedCargo = await client.ReplaceFleetCarrierCargoAsync(
                linkedMarketId,
                totals,
                seedApiKey,
                CancellationToken.None
            );
            if (!IsCurrentCapiCargoSeed(generation, version))
            {
                return;
            }

            capiCargoSeededMarketIds.Add(linkedMarketId);
            ColonizationFleetCarrier? current = fleetCarriers.FirstOrDefault(c => c.MarketId == linkedMarketId);
            if (current is not null)
            {
                ReplaceLocalFleetCarrier(current with { Cargo = CopyCargo(updatedCargo) });
            }

            observer.FleetCarrierStatusChanged(
                new(ColonizationDeliveryNoticeKind.CapiCargoSeeded, Detail: reason, Count: linkedMarketId)
            );
        }
        catch (Exception exception)
            when (exception
                    is HttpRequestException
                        or InvalidDataException
                        or TaskCanceledException
                        or ArgumentException
            )
        {
            if (IsCurrentCapiCargoSeed(generation, version))
            {
                observer.FleetCarrierStatusChanged(
                    new(ColonizationDeliveryNoticeKind.CapiCargoSeedNotApplied, Detail: exception.Message)
                );
            }
        }
        finally
        {
            if (IsCurrentCapiCargoSeed(generation, version))
            {
                observer.PendingFleetCarrierCargoChanged(null);
                await CompleteCargoBaselineAsync(linkedMarketId, CancellationToken.None);
            }
        }
    }

    /// <summary>Installs an authoritative market baseline before applying transactions recorded while synchronization was pending.</summary>
    private async Task ApplyFleetCarrierMarketCargoSyncAsync(
        MarketSnapshot market,
        string syncApiKey,
        (long MarketId, DateTimeOffset Timestamp) identity,
        int version
    )
    {
        BeginCargoBaselinePending(market.MarketId);
        try
        {
            ColonizationFleetCarrier? serverCarrier = await client.GetFleetCarrierAsync(
                market.MarketId,
                CancellationToken.None
            );
            if (version != profileVersion)
            {
                return;
            }

            if (serverCarrier is null)
            {
                observer.FleetCarrierStatusChanged(new(ColonizationDeliveryNoticeKind.CarrierNotOnRaven));
                return;
            }

            IReadOnlyDictionary<string, int> replacements =
                ColonizationFleetCarrierCargoSynchronizer.CreateMarketReplacement(market, serverCarrier);
            if (replacements.Count == 0)
            {
                // Re-resolve after await: the local list may have changed while waiting.
                if (IsLinkedFleetCarrier(market.MarketId))
                {
                    ReplaceLocalFleetCarrier(serverCarrier);
                }

                ReconcilePendingCargo(market);
                lastSyncedMarket = identity;
                observer.FleetCarrierStatusChanged(
                    new(ColonizationDeliveryNoticeKind.CarrierCargoCurrent, GetCarrierName(serverCarrier))
                );
                return;
            }

            observer.PendingFleetCarrierCargoChanged(replacements.Keys);
            observer.FleetCarrierStatusChanged(
                new(
                    ColonizationDeliveryNoticeKind.CarrierCargoUpdating,
                    GetCarrierName(serverCarrier),
                    Count: replacements.Count
                )
            );
            IReadOnlyDictionary<string, int> updatedCargo = await client.ReplaceFleetCarrierCargoAsync(
                market.MarketId,
                replacements,
                syncApiKey,
                CancellationToken.None
            );
            if (version != profileVersion)
            {
                return;
            }

            if (IsLinkedFleetCarrier(market.MarketId))
            {
                ReplaceLocalFleetCarrier(serverCarrier with { Cargo = CopyCargo(updatedCargo) });
            }

            ReconcilePendingCargo(market);
            lastSyncedMarket = identity;
            observer.FleetCarrierStatusChanged(
                new(
                    ColonizationDeliveryNoticeKind.CarrierCargoUpdated,
                    GetCarrierName(serverCarrier),
                    Count: replacements.Count
                )
            );
        }
        finally
        {
            if (version == profileVersion)
            {
                observer.PendingFleetCarrierCargoChanged(null);
                await CompleteCargoBaselineAsync(market.MarketId, CancellationToken.None);
            }
        }
    }

    /// <summary>Loads the linked carrier baseline for the event-time dock before relative cargo synchronization.</summary>
    private async Task<ColonizationDeliveryNotice?> EnsureLinkedFleetCarrierDockBaselineAsync(
        ColonizationDockingSnapshot? dock,
        CancellationToken cancellationToken = default
    )
    {
        if (
            !FleetCarrierCargoSyncEnabled
            || apiKey is null
            || dock is null
            || !string.Equals(dock.StationType, FleetCarrierStationType, StringComparison.OrdinalIgnoreCase)
            || cargoBaselineReady.Contains(dock.MarketId)
            || IsCargoBaselinePending(dock.MarketId)
            || fleetCarriers.FirstOrDefault(carrier => carrier.MarketId == dock.MarketId) is not { } localCarrier
        )
        {
            return null;
        }

        if (!ColonizationFleetCarrierPendingCargo.NeedsServerBaseline(localCarrier.Cargo))
        {
            cargoBaselineReady.Add(dock.MarketId);
            return null;
        }

        int version = profileVersion;
        BeginCargoBaselinePending(dock.MarketId);
        bool stored = false;
        try
        {
            ColonizationFleetCarrier? serverCarrier = await client.GetFleetCarrierAsync(
                dock.MarketId,
                cancellationToken
            );
            if (version != profileVersion)
            {
                return null;
            }

            if (serverCarrier is null)
            {
                return null;
            }

            ReplaceLocalFleetCarrier(
                serverCarrier with
                {
                    Cargo = CopyCargo(
                        serverCarrier.Cargo ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                    ),
                }
            );
            stored = true;
            return new(ColonizationDeliveryNoticeKind.CarrierBaselineLoaded, GetCarrierName(serverCarrier));
        }
        catch (Exception exception)
            when (exception
                    is HttpRequestException
                        or InvalidDataException
                        or TaskCanceledException
                        or ArgumentException
            )
        {
            return version == profileVersion
                ? new(ColonizationDeliveryNoticeKind.CarrierBaselineNotLoaded, Detail: exception.Message)
                : null;
        }
        finally
        {
            if (version == profileVersion)
            {
                await CompleteCargoBaselineAsync(dock.MarketId, cancellationToken: cancellationToken);
                if (version == profileVersion && !stored)
                {
                    cargoBaselineReady.Remove(dock.MarketId);
                }
            }
        }
    }

    private void BeginCargoBaselinePending(long marketId)
    {
        cargoBaselinePendingDepth[marketId] = cargoBaselinePendingDepth.GetValueOrDefault(marketId) + 1;
        cargoBaselineReady.Remove(marketId);
    }

    private bool IsCargoBaselinePending(long marketId)
    {
        return cargoBaselinePendingDepth.GetValueOrDefault(marketId) > 0;
    }

    /// <summary>Preserves each queued transaction’s journal time so market coverage can retire only included commodities.</summary>
    private bool TryQueuePendingCargoDelta(
        long marketId,
        IReadOnlyDictionary<string, int> delta,
        DateTimeOffset? recordedAt
    )
    {
        if (!IsCargoBaselinePending(marketId) || delta.Count == 0)
        {
            return false;
        }
        if (!pendingCargoDeltas.TryGetValue(marketId, out List<PendingCarrierCargoDelta>? pending))
        {
            pending = [];
            pendingCargoDeltas[marketId] = pending;
        }
        pending.Add(new(new Dictionary<string, int>(delta, StringComparer.OrdinalIgnoreCase), recordedAt));
        return true;
    }

    /// <summary>Completes the outermost baseline and preserves failed queued transactions for later reconciliation.</summary>
    private async Task CompleteCargoBaselineAsync(long marketId, CancellationToken cancellationToken = default)
    {
        int current = cargoBaselinePendingDepth.GetValueOrDefault(marketId);
        if (current <= 0)
        {
            return;
        }
        int remaining = current - 1;
        if (remaining > 0)
        {
            cargoBaselinePendingDepth[marketId] = remaining;
            return;
        }
        cargoBaselinePendingDepth.Remove(marketId);
        cargoBaselineReady.Add(marketId);
        if (!pendingCargoDeltas.Remove(marketId, out List<PendingCarrierCargoDelta>? pending) || apiKey is null)
        {
            return;
        }
        int version = profileVersion;
        string owner = RecoveryOwner;
        ColonizationPendingCargoAdjustment[] retained = pending
            .Select(item => new ColonizationPendingCargoAdjustment(
                owner,
                marketId,
                item.Delta,
                item.RecordedAt,
                null,
                Attempted: false,
                OutcomeUnknown: false
            ))
            .ToArray();
        failedCargoAdjustments.AddRange(retained);
        SavePendingCargoAdjustments();
        foreach (ColonizationPendingCargoAdjustment item in retained)
        {
            if (version != profileVersion)
            {
                return;
            }
            await ReplayQueuedCargoDeltaAsync(item, version, cancellationToken);
        }
    }

    private async Task ReplayQueuedCargoDeltaAsync(
        ColonizationPendingCargoAdjustment pending,
        int version,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await ApplyPendingFleetCarrierCargoAdjustmentAsync(
                pending,
                "queued dock baseline",
                true,
                null,
                cancellationToken
            );
        }
        catch (Exception exception)
            when (exception is HttpRequestException or TaskCanceledException or InvalidDataException)
        {
            if (version == profileVersion)
            {
                observer.FleetCarrierStatusChanged(
                    new(ColonizationDeliveryNoticeKind.QueuedCarrierCargoRetained, Detail: exception.Message)
                );
            }
        }
    }

    /// <summary>Retires covered deltas only when their known event time is no later than the authoritative market snapshot.</summary>
    private void ReconcilePendingCargo(MarketSnapshot market)
    {
        var covered = market
            .Items.Where(item => item.Producer || !item.Consumer)
            .Select(item => item.Commodity)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (pendingCargoDeltas.TryGetValue(market.MarketId, out List<PendingCarrierCargoDelta>? queued))
        {
            foreach (
                PendingCarrierCargoDelta pending in queued.Where(item =>
                    item.RecordedAt is { } recordedAt && recordedAt <= market.Timestamp
                )
            )
            {
                RemoveCoveredCargo(pending.Delta, covered);
            }
            queued.RemoveAll(item => item.Delta.Count == 0);
        }
        string owner = RecoveryOwner;
        foreach (
            ColonizationPendingCargoAdjustment pending in failedCargoAdjustments
                .Where(item =>
                    item.Owner == owner
                    && item.MarketId == market.MarketId
                    && item.RecordedAt is { } recordedAt
                    && recordedAt <= market.Timestamp
                )
                .ToArray()
        )
        {
            RemoveCoveredCargo(pending.Delta, covered);
            if (pending.Delta.Count == 0)
            {
                failedCargoAdjustments.Remove(pending);
            }
        }
        SavePendingCargoAdjustments();
    }

    private static void RemoveCoveredCargo(Dictionary<string, int> delta, HashSet<string> covered)
    {
        foreach (string commodity in delta.Keys.Where(covered.Contains).ToArray())
        {
            delta.Remove(commodity);
        }
    }

    /// <summary>Invalidates dock readiness without discarding transactions owned by an in-flight baseline.</summary>
    private void ClearCargoBaseline(long marketId)
    {
        cargoBaselineReady.Remove(marketId);
    }

    private void ClearAllCargoBaselines()
    {
        if (cargoBaselinePendingDepth.Count > 0 || fleetCarrierSyncBusy || cargoWritesInFlight.Count > 0)
        {
            observer.PendingFleetCarrierCargoChanged(null);
        }
        if (fleetCarrierSyncBusy)
        {
            SetFleetCarrierSyncBusy(false);
        }
        cargoBaselinePendingDepth.Clear();
        cargoBaselineReady.Clear();
        pendingCargoDeltas.Clear();
    }

    private void SetFleetCarrierSyncBusy(bool value)
    {
        fleetCarrierSyncBusy = value;
        observer.FleetCarrierSyncBusyChanged(value);
    }

    private void ReplaceLocalFleetCarrier(ColonizationFleetCarrier updatedCarrier)
    {
        fleetCarriers = fleetCarriers
            .Where(carrier => carrier.MarketId != updatedCarrier.MarketId)
            .Append(updatedCarrier)
            .ToArray();
        observer.FleetCarriersChanged();
    }
}
