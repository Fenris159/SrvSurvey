using SrvSurvey.Core.Frontier;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Colonization;

public sealed partial class ColonizationDeliveryRecovery
{
    /// <summary>
    /// Market IDs that already received a once-per-session CAPI full-manifest seed.
    /// Further Frontier refreshes are ignored unless retained writes require full-manifest recovery.
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

    private readonly Dictionary<long, List<ColonizationPendingCargoAdjustment>> pendingCargoDeltas = [];

    private bool fleetCarrierSyncBusy;
    private int capiCargoSeedGeneration;
    private (long MarketId, DateTimeOffset Timestamp)? lastSyncedMarket;

    /// <summary>
    /// Whether the current Market.json can replace linked personal-carrier cargo, ignoring busy state.
    /// Squadron bank totals require a full manifest rather than market-only stock.
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
                && currentMarket.Timestamp > dock.Timestamp
                && !ColonizationFleetCarrierCargoSynchronizer.IsSquadronFleetCarrier(dock);
        }
    }

    /// <summary>
    /// Freeze ship cargo before CargoTransfer mutates the live projection. Desktop cargo projection
    /// precedes docking context, so eligibility is checked later by live synchronization.
    /// </summary>
    public void PrepareSquadronCargoTransferSnapshot(CargoInventoryState cargo)
    {
        ArgumentNullException.ThrowIfNull(cargo);
        if (!FleetCarrierCargoSyncEnabled || apiKey is null)
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
                        or OperationCanceledException
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
    /// linked + workspace totals current between CAPI updates. Retained writes can request another
    /// accepted full baseline before live updates resume. The independent local comparison
    /// accepts newer CAPI snapshots without requiring Raven publishing to be enabled.
    /// </summary>
    public async Task SeedLinkedCarrierCargoFromCapiAsync(FrontierAccountSnapshot? snapshot)
    {
        if (
            snapshot is not null
            && string.Equals(snapshot.CommanderName, commanderName, StringComparison.OrdinalIgnoreCase)
        )
        {
            LocalCarrierCargo.Observe(
                snapshot.Carrier,
                snapshot.CarrierFetchedAt ?? snapshot.FetchedAt,
                IsDockedAtObservedCarrier(snapshot, snapshot.Carrier)
            );
            LocalCarrierCargo.Observe(
                snapshot.SquadronCarrier,
                snapshot.SquadronCarrierFetchedAt ?? snapshot.FetchedAt,
                IsDockedAtObservedCarrier(snapshot, snapshot.SquadronCarrier)
            );
        }
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

    private bool IsDockedAtObservedCarrier(FrontierAccountSnapshot snapshot, FrontierCarrierSnapshot? carrier)
    {
        if (carrier is null)
        {
            return false;
        }
        ColonizationDockingSnapshot? dock = constructionState.CurrentDock;
        long? dockMarketId = dock?.MarketId ?? (snapshot.IsDocked ? snapshot.LastStationDetails?.Id : null);
        string? stationName = dock?.StationName ?? (snapshot.IsDocked ? snapshot.LastStation : null);
        return carrier.Market?.Id is > 0 && dockMarketId is > 0
            ? carrier.Market.Id == dockMarketId
            : stationName?.Equals(carrier.Callsign, StringComparison.OrdinalIgnoreCase) == true;
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
                        or OperationCanceledException
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

    private static void ClearSquadronCargoSyncState(CargoInventoryState? cargoInventory)
    {
        // Consume the baseline as well as the held flag so disabled publishing cannot leak old movement.
        cargoInventory?.GetDiff();
    }

    private async Task<ColonizationDeliveryNotice?> ApplyPendingFleetCarrierCargoAdjustmentAsync(
        ColonizationPendingCargoAdjustment pending,
        string sourceEventName,
        CancellationToken cancellationToken
    )
    {
        if (
            !isEnabled
            || !FleetCarrierCargoSyncEnabled
            || pending.Owner != RecoveryOwner
            || apiKey is not { } adjustApiKey
            || pending.Delta.Count == 0
        )
        {
            return null;
        }
        cancellationToken.ThrowIfCancellationRequested();
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
            OutcomeUnknown = !blocked,
            Before = fleetCarriers.FirstOrDefault(carrier => carrier.MarketId == marketId) is { } carrier
                ? CopyCargo(carrier.Cargo)
                : null,
        };
        ColonizationPendingCargoAdjustment unattempted = failedCargoAdjustments[pendingIndex];
        failedCargoAdjustments[pendingIndex] = pending;
        if (!SavePendingCargoAdjustments())
        {
            failedCargoAdjustments[pendingIndex] = unattempted;
            return new(ColonizationDeliveryNoticeKind.CarrierCargoRecoveryNotSaved);
        }
        if (blocked)
        {
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
            ApplyAcknowledgedCargoAdjustment(marketId, updatedCargo);
            return new(ColonizationDeliveryNoticeKind.CarrierCargoAdjusted, sourceEventName, Count: adjustments.Count);
        }
        catch (Exception exception)
            when (exception is HttpRequestException or OperationCanceledException or InvalidDataException)
        {
            RetainFailedCargoAdjustment(pending, exception);
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

    /// <summary>Installs the acknowledged carrier manifest after the captured movement has been consumed.</summary>
    private void ApplyAcknowledgedCargoAdjustment(long marketId, IReadOnlyDictionary<string, int> updatedCargo)
    {
        ColonizationFleetCarrier? localCarrier = fleetCarriers.FirstOrDefault(carrier => carrier.MarketId == marketId);
        if (localCarrier is not null)
        {
            ReplaceLocalFleetCarrier(localCarrier with { Cargo = CopyCargo(updatedCargo) });
        }
    }

    /// <summary>Records failure only while the adjustment remains pending, preserving the original transport exception.</summary>
    private void RetainFailedCargoAdjustment(ColonizationPendingCargoAdjustment pending, Exception exception)
    {
        int index = failedCargoAdjustments.IndexOf(pending);
        if (index >= 0)
        {
            failedCargoAdjustments[index] = pending with { OutcomeUnknown = !IsDefiniteRejection(exception) };
            SavePendingCargoAdjustments();
        }
        nextWriteRetry = utcNow().Add(WriteRetryDelay);
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
        if (ColonizationFleetCarrierCargoSynchronizer.IsSquadronFleetCarrier(dock))
        {
            observer.FleetCarrierStatusChanged(
                new(ColonizationDeliveryNoticeKind.SquadronCarrierPublished, GetCarrierName(registered))
            );
            return;
        }
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
        (bool accepted, string reason, ColonizationPendingCargoAdjustment[] covered) = GetCapiManifestPolicy(
            linkedMarketId,
            fetchedAt,
            isDocked,
            localCarrier?.Cargo
        );
        if (!accepted)
        {
            return;
        }

        Dictionary<string, int> totals = CreateCapiReconciliationTotals(carrier, covered);
        if (
            localCarrier is not null
            && covered.Length == 0
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
            // A newer acknowledged full manifest covers the retained writes selected by the freshness guard.
            // Writes queued after this baseline began remain owned by CompleteCargoBaselineAsync.
            RetireCapiCargoWrites(covered);

            observer.FleetCarrierStatusChanged(
                new(ColonizationDeliveryNoticeKind.CapiCargoSeeded, Detail: reason, Count: linkedMarketId)
            );
        }
        catch (Exception exception)
            when (exception
                    is HttpRequestException
                        or InvalidDataException
                        or OperationCanceledException
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

    /// <summary>Allows another accepted full baseline only when retained writes need reconciliation.</summary>
    private (bool Accepted, string Reason, ColonizationPendingCargoAdjustment[] Covered) GetCapiManifestPolicy(
        long marketId,
        DateTimeOffset? fetchedAt,
        bool isDocked,
        IReadOnlyDictionary<string, int>? localCargo
    )
    {
        ColonizationPendingCargoAdjustment[] covered = failedCargoAdjustments
            .Where(item => item.Owner == RecoveryOwner && item.MarketId == marketId)
            .ToArray();
        if (!CanReconcileCapiManifest(marketId, fetchedAt, covered))
        {
            return (false, string.Empty, covered);
        }
        (bool accepted, string reason) = ColonizationFleetCarrierCapiCargoSeeder.ShouldAcceptSnapshot(
            marketId,
            isDocked || constructionState.CurrentDock is not null,
            covered.Length > 0 ? [] : capiCargoSeededMarketIds,
            fetchedAt,
            localCargo
        );
        return (accepted, reason, covered);
    }

    /// <summary>Clears only the retained records superseded by an acknowledged full-manifest replacement.</summary>
    private void RetireCapiCargoWrites(ColonizationPendingCargoAdjustment[] covered)
    {
        foreach (ColonizationPendingCargoAdjustment pending in covered)
        {
            failedCargoAdjustments.Remove(pending);
        }
        if (covered.Length > 0)
        {
            SavePendingCargoAdjustments();
        }
    }

    /// <summary>Retains newer movement and avoids replacing a baseline while a relative write can still commit.</summary>
    private bool CanReconcileCapiManifest(
        long marketId,
        DateTimeOffset? fetchedAt,
        ColonizationPendingCargoAdjustment[] pending
    ) =>
        fetchedAt is { } capturedAt
        && !cargoWritesInFlight.Contains((RecoveryOwner, marketId))
        && (pending.Length == 0 || !IsCargoBaselinePending(marketId))
        // Older retained files have no observation time; require a manifest fetched after loading them.
        && pending.All(item =>
            (item.ObservedAt ?? item.RecordedAt ?? retainedCargoLoadedAt) < capturedAt
            && (item.RecordedAt ?? DateTimeOffset.MinValue) < capturedAt
        );

    /// <summary>A complete manifest also supplies zero for retained commodities absent from its cargo rows.</summary>
    private static Dictionary<string, int> CreateCapiReconciliationTotals(
        FrontierCarrierSnapshot carrier,
        ColonizationPendingCargoAdjustment[] pending
    )
    {
        Dictionary<string, int> totals = CopyCargo(ColonizationFleetCarrierCapiCargoSeeder.CreateCargoTotals(carrier));
        foreach (string commodity in pending.SelectMany(item => item.Delta.Keys))
        {
            totals.TryAdd(commodity, 0);
        }
        return totals;
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
                        or OperationCanceledException
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
        if (
            !pendingCargoDeltas.Remove(marketId, out List<ColonizationPendingCargoAdjustment>? pending)
            || apiKey is null
        )
        {
            return;
        }
        int version = profileVersion;
        ColonizationPendingCargoAdjustment[] retained = pending.ToArray();
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
            await ApplyPendingFleetCarrierCargoAdjustmentAsync(pending, "queued dock baseline", cancellationToken);
        }
        catch (Exception exception)
            when (exception is HttpRequestException or OperationCanceledException or InvalidDataException)
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
