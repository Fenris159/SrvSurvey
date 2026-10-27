using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed partial class ColonizationDeliveryRecoveryTests
{
    [Fact]
    public async Task ConfirmedCargoWriteIsNotReplayedWhenRavenChangesTheCommodityAlias()
    {
        RecordingRavenClient client = CarrierClient(80, "SQD-001");
        client.FleetCarrierResponse = client.FleetCarrierResponse! with { Cargo = new() { ["liquidoxygen"] = 75 } };
        store.SavePendingCargoAdjustments([
            new(
                "Test Cmdr|F123|True",
                42,
                new() { ["liquidoxygen"] = -5 },
                now.AddSeconds(-10),
                new() { ["liquid oxygen"] = 80 },
                Attempted: true,
                OutcomeUnknown: true
            ),
        ]);
        ColonizationDeliveryRecovery recovery = SquadronRecovery(client);
        recovery.ApplyJournalEvents([Event("Undocked", "")], null);
        await recovery.RetryPendingWritesAsync();
        Assert.Empty(client.FleetCarrierAdjustments);
        Assert.Empty(store.LoadPendingCargoAdjustments());
        Assert.Equal(75, Assert.Single(recovery.FleetCarriers).Cargo["liquidoxygen"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CachedFullManifestMustPostdateBothJournalAndLocalObservation(bool journalTimeNewer)
    {
        RecordingRavenClient client = CarrierClient(75, "SQD-001");
        global::SrvSurvey.Core.Frontier.FrontierAccountSnapshot snapshot = LinkedCarrierCapiSnapshot(false, 75);
        now = snapshot.FetchedAt.AddSeconds(journalTimeNewer ? -1 : 1);
        client.AdjustmentFailures.Enqueue(new HttpRequestException("response lost"));
        ColonizationDeliveryRecovery recovery = SquadronRecovery(client);
        CargoInventoryState cargo = ShipCargo(10);
        JournalEventEnvelope market = SquadronMarketEvent(false) with
        {
            Timestamp = snapshot.FetchedAt.AddSeconds(journalTimeNewer ? 1 : -1),
        };
        cargo.Apply(market);
        await recovery.SynchronizeLiveEventsAsync([market], true, cargo, cargoActivity: true);
        recovery.ApplyJournalEvents([Event("Undocked", "")], null);

        await recovery.SeedLinkedCarrierCargoFromCapiAsync(snapshot);

        Assert.Equal(0, client.ReplaceCargoCount);
        Assert.Single(store.LoadPendingCargoAdjustments());
        global::SrvSurvey.Core.Frontier.FrontierAccountSnapshot fresh = LinkedCarrierCapiSnapshot(false, 70) with
        {
            FetchedAt = snapshot.FetchedAt.AddSeconds(2),
            CarrierFetchedAt = snapshot.FetchedAt.AddSeconds(2),
        };
        await recovery.SeedLinkedCarrierCargoFromCapiAsync(fresh);
        Assert.Empty(store.LoadPendingCargoAdjustments());
        Assert.Equal(70, Assert.Single(recovery.FleetCarriers).Cargo["steel"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CachedFullManifestCannotRetireAnUndatedBankMovement(bool legacyRecord)
    {
        RecordingRavenClient client = CarrierClient(75, "SQD-001");
        global::SrvSurvey.Core.Frontier.FrontierAccountSnapshot snapshot = LinkedCarrierCapiSnapshot(false, 75);
        now = snapshot.FetchedAt.AddSeconds(1);
        client.AdjustmentFailures.Enqueue(new HttpRequestException("response lost"));
        ColonizationDeliveryRecovery recovery = SquadronRecovery(client);
        CargoInventoryState cargo = ShipCargo(10);
        JournalEventEnvelope bank = SquadronBankEvent(5, toCarrier: true);
        recovery.PrepareSquadronCargoTransferSnapshot(cargo);
        cargo.Apply(bank);
        await recovery.SynchronizeLiveEventsAsync([bank], true, cargo, cargoActivity: true);
        ColonizationPendingCargoAdjustment pending = Assert.Single(store.LoadPendingCargoAdjustments());
        Assert.Null(pending.RecordedAt);
        if (legacyRecord)
        {
            store.SavePendingCargoAdjustments([pending with { ObservedAt = null }]);
            recovery = SquadronRecovery(client);
        }
        recovery.ApplyJournalEvents([Event("Undocked", "")], null);
        snapshot = snapshot with { Carrier = null, SquadronCarrier = snapshot.Carrier };

        await recovery.SeedLinkedCarrierCargoFromCapiAsync(snapshot);

        Assert.Equal(0, client.ReplaceCargoCount);
        Assert.Single(store.LoadPendingCargoAdjustments());
        global::SrvSurvey.Core.Frontier.FrontierAccountSnapshot fresh = LinkedCarrierCapiSnapshot(false, 80);
        fresh = fresh with
        {
            Carrier = null,
            SquadronCarrier = fresh.Carrier,
            FetchedAt = now.AddSeconds(1),
            SquadronCarrierFetchedAt = now.AddSeconds(1),
        };
        await recovery.SeedLinkedCarrierCargoFromCapiAsync(fresh);
        Assert.Empty(store.LoadPendingCargoAdjustments());
        Assert.Equal(80, Assert.Single(recovery.FleetCarriers).Cargo["steel"]);
    }

    [Fact]
    public async Task FullManifestCannotOvertakeARetainedCargoRetryInFlight()
    {
        RecordingRavenClient client = CarrierClient(75, "SQD-001");
        global::SrvSurvey.Core.Frontier.FrontierAccountSnapshot snapshot = LinkedCarrierCapiSnapshot(false, 85);
        store.SavePendingCargoAdjustments([
            new(
                "Test Cmdr|F123|True",
                42,
                new() { ["steel"] = 10 },
                snapshot.FetchedAt.AddSeconds(-1),
                null,
                Attempted: false,
                OutcomeUnknown: false
            ),
        ]);
        ColonizationDeliveryRecovery recovery = SquadronRecovery(client);
        recovery.ApplyJournalEvents([Event("Undocked", "")], null);
        var response = new TaskCompletionSource<IReadOnlyDictionary<string, int>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        client.AdjustmentResponseTask = response.Task;
        Task retry = recovery.RetryPendingWritesAsync();
        try
        {
            Assert.Single(client.FleetCarrierAdjustments);

            await recovery.SeedLinkedCarrierCargoFromCapiAsync(snapshot);

            Assert.Equal(0, client.ReplaceCargoCount);
        }
        finally
        {
            response.TrySetResult(new Dictionary<string, int> { ["steel"] = 85 });
            await retry.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Empty(store.LoadPendingCargoAdjustments());
        Assert.Equal(85, Assert.Single(recovery.FleetCarriers).Cargo["steel"]);
    }

    [Fact]
    public async Task FailedFullManifestReplacementRetainsUncertainCargoForTheNextRefresh()
    {
        RecordingRavenClient client = CarrierClient(75, "SQD-001");
        client.AdjustmentFailures.Enqueue(new HttpRequestException("response lost"));
        ColonizationDeliveryRecovery recovery = SquadronRecovery(client);
        global::SrvSurvey.Core.Frontier.FrontierAccountSnapshot snapshot = LinkedCarrierCapiSnapshot(false, 70);
        now = snapshot.FetchedAt.AddSeconds(-2);
        CargoInventoryState cargo = ShipCargo(10);
        JournalEventEnvelope market = SquadronMarketEvent(false) with { Timestamp = snapshot.FetchedAt.AddSeconds(-1) };
        cargo.Apply(market);
        await recovery.SynchronizeLiveEventsAsync([market], true, cargo, cargoActivity: true);
        recovery.ApplyJournalEvents([Event("Undocked", "")], null);
        client.ReplaceCargo = _ =>
            Task.FromException<IReadOnlyDictionary<string, int>>(new HttpRequestException("offline"));

        await recovery.SeedLinkedCarrierCargoFromCapiAsync(snapshot);

        Assert.True(Assert.Single(store.LoadPendingCargoAdjustments()).OutcomeUnknown);
        client.ReplaceCargo = null;
        await recovery.SeedLinkedCarrierCargoFromCapiAsync(snapshot);
        Assert.Empty(store.LoadPendingCargoAdjustments());
        Assert.Equal(70, Assert.Single(recovery.FleetCarriers).Cargo["steel"]);
    }

    [Fact]
    public async Task TransactionsQueuedDuringFullManifestRecoveryReplayAfterItsAcknowledgement()
    {
        RecordingRavenClient client = CarrierClient(75, "SQD-001");
        client.AdjustmentFailures.Enqueue(new HttpRequestException("response lost"));
        ColonizationDeliveryRecovery recovery = SquadronRecovery(client);
        global::SrvSurvey.Core.Frontier.FrontierAccountSnapshot snapshot = LinkedCarrierCapiSnapshot(false, 80);
        now = snapshot.FetchedAt.AddSeconds(-2);
        CargoInventoryState cargo = ShipCargo(10);
        JournalEventEnvelope market = SquadronMarketEvent(false) with { Timestamp = snapshot.FetchedAt.AddSeconds(-1) };
        cargo.Apply(market);
        await recovery.SynchronizeLiveEventsAsync([market], true, cargo, cargoActivity: true);
        recovery.ApplyJournalEvents([Event("Undocked", "")], null);
        client.ReplaceCargo = async replacement =>
        {
            recovery.ApplyJournalEvents([CarrierDock(squadron: true, stationName: "SQD-001")], null);
            JournalEventEnvelope next = SquadronMarketEvent(false) with
            {
                Timestamp = snapshot.FetchedAt.AddSeconds(1),
            };
            cargo.Apply(next);
            await recovery.SynchronizeLiveEventsAsync([next], true, cargo, cargoActivity: true);
            client.FleetCarrierResponse = client.FleetCarrierResponse! with
            {
                Cargo = new Dictionary<string, int>(replacement),
            };
            return replacement;
        };

        await recovery.SeedLinkedCarrierCargoFromCapiAsync(snapshot);

        Assert.Equal(75, Assert.Single(recovery.FleetCarriers).Cargo["steel"]);
        Assert.Equal(2, client.FleetCarrierAdjustments.Count);
        Assert.Empty(store.LoadPendingCargoAdjustments());
    }

    [Fact]
    public async Task FullManifestCannotOvertakeARelativeCargoWriteInFlight()
    {
        RecordingRavenClient client = CarrierClient(75, "SQD-001");
        var response = new TaskCompletionSource<IReadOnlyDictionary<string, int>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        client.AdjustmentResponseTask = response.Task;
        now = LinkedCarrierCapiSnapshot(false, 80).FetchedAt.AddSeconds(-2);
        ColonizationDeliveryRecovery recovery = SquadronRecovery(client);
        CargoInventoryState cargo = ShipCargo(10);
        JournalEventEnvelope market = SquadronMarketEvent(false);
        cargo.Apply(market);
        Task writing = recovery.SynchronizeLiveEventsAsync([market], true, cargo, cargoActivity: true);
        try
        {
            Assert.Single(client.FleetCarrierAdjustments);
            recovery.ApplyJournalEvents([Event("Undocked", "")], null);

            await recovery.SeedLinkedCarrierCargoFromCapiAsync(LinkedCarrierCapiSnapshot(false, 80));

            Assert.Equal(0, client.ReplaceCargoCount);
        }
        finally
        {
            response.TrySetResult(new Dictionary<string, int> { ["steel"] = 70 });
            await writing.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Empty(store.LoadPendingCargoAdjustments());
    }

    [Theory]
    [InlineData(false, 75)]
    [InlineData(true, 75)]
    [InlineData(false, 0)]
    public async Task AcceptedFullSquadronManifestReconcilesUncertainAndDependentWrites(bool alreadySeeded, int total)
    {
        RecordingRavenClient client = CarrierClient(75, "SQD-001");
        client.ReplaceCargo = replacement =>
        {
            client.FleetCarrierResponse = client.FleetCarrierResponse! with { Cargo = new(replacement) };
            return Task.FromResult(replacement);
        };
        ColonizationDeliveryRecovery recovery = SquadronRecovery(client);
        global::SrvSurvey.Core.Frontier.FrontierAccountSnapshot snapshot = LinkedCarrierCapiSnapshot(false, total);
        now = snapshot.FetchedAt.AddSeconds(1);
        if (alreadySeeded)
        {
            recovery.ApplyJournalEvents([Event("Undocked", "")], null);
            await recovery.SeedLinkedCarrierCargoFromCapiAsync(LinkedCarrierCapiSnapshot(false, 75));
            recovery.ApplyJournalEvents([CarrierDock(squadron: true, stationName: "SQD-001")], null);
        }
        client.AdjustmentFailures.Enqueue(new HttpRequestException("response lost"));
        CargoInventoryState cargo = ShipCargo(10);
        JournalEventEnvelope market = SquadronMarketEvent(sell: false) with
        {
            Timestamp = snapshot.FetchedAt.AddSeconds(1),
        };
        cargo.Apply(market);
        await recovery.SynchronizeLiveEventsAsync([market], true, cargo, cargoActivity: true);
        JournalEventEnvelope bank = SquadronBankEvent(5, toCarrier: true);
        recovery.PrepareSquadronCargoTransferSnapshot(cargo);
        cargo.Apply(bank);
        await recovery.SynchronizeLiveEventsAsync([bank], true, cargo, cargoActivity: true);
        Assert.Equal(2, store.LoadPendingCargoAdjustments().Count);
        recovery.ApplyJournalEvents([Event("Undocked", "")], null);
        snapshot = snapshot with
        {
            Carrier = null,
            SquadronCarrier = snapshot.Carrier,
            FetchedAt = snapshot.FetchedAt.AddSeconds(2),
            SquadronCarrierFetchedAt = snapshot.FetchedAt.AddSeconds(2),
        };

        await recovery.SeedLinkedCarrierCargoFromCapiAsync(snapshot);

        Assert.Empty(store.LoadPendingCargoAdjustments());
        Assert.Equal(total, Assert.Single(recovery.FleetCarriers).Cargo.GetValueOrDefault("steel"));
        Assert.Equal(1, client.ReplaceCargoCount);
        recovery.ApplyJournalEvents([CarrierDock(squadron: true, stationName: "SQD-001")], null);
        JournalEventEnvelope next = SquadronMarketEvent(sell: true) with
        {
            Timestamp = snapshot.FetchedAt.AddSeconds(1),
        };
        cargo.Apply(next);
        await recovery.SynchronizeLiveEventsAsync([next], true, cargo, cargoActivity: true);
        Assert.Equal(2, client.FleetCarrierAdjustments.Count);
        Assert.Empty(store.LoadPendingCargoAdjustments());
        Assert.Equal(total + 5, Assert.Single(recovery.FleetCarriers).Cargo["steel"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SquadronManifestCannotRetireAnUncoveredWrite(bool stillDocked)
    {
        RecordingRavenClient client = CarrierClient(75, "SQD-001");
        client.AdjustmentFailures.Enqueue(new HttpRequestException("response lost"));
        ColonizationDeliveryRecovery recovery = SquadronRecovery(client);
        global::SrvSurvey.Core.Frontier.FrontierAccountSnapshot snapshot = LinkedCarrierCapiSnapshot(false, 75);
        CargoInventoryState cargo = ShipCargo(10);
        JournalEventEnvelope market = SquadronMarketEvent(false) with
        {
            Timestamp = snapshot.FetchedAt.AddSeconds(stillDocked ? -1 : 1),
        };
        cargo.Apply(market);
        await recovery.SynchronizeLiveEventsAsync([market], true, cargo, cargoActivity: true);
        if (!stillDocked)
        {
            recovery.ApplyJournalEvents([Event("Undocked", "")], null);
        }
        snapshot = snapshot with { SquadronCarrier = snapshot.Carrier, Carrier = null };

        await recovery.SeedLinkedCarrierCargoFromCapiAsync(snapshot);

        Assert.True(Assert.Single(store.LoadPendingCargoAdjustments()).OutcomeUnknown);
        Assert.Equal(0, client.ReplaceCargoCount);
    }

    /// <summary>Count-only journal cargo must not replay a purchase or sale already retained for Raven.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SquadronMarketMovementIsConsumedBeforeTheNextCountOnlyCargo(bool sell)
    {
        RecordingRavenClient client = CarrierClient(75, "SQD-001");
        ColonizationDeliveryRecovery recovery = SquadronRecovery(client);
        CargoInventoryState cargo = ShipCargo(10);
        JournalEventEnvelope market = SquadronMarketEvent(sell);
        cargo.Apply(market);
        await recovery.SynchronizeLiveEventsAsync([market], true, cargo, cargoActivity: true);

        JournalEventEnvelope countOnly = Event("Cargo", $"\"Vessel\":\"Ship\",\"Count\":{(sell ? 5 : 15)}");
        Assert.False(cargo.Apply(countOnly));
        await recovery.SynchronizeLiveEventsAsync([countOnly], true, cargo, cargoActivity: true);

        Assert.Equal(sell ? 5 : -5, Assert.Single(client.FleetCarrierAdjustments).Changes["steel"]);
    }

    /// <summary>Transfers and market transactions count once in either order, with or without Cargo.json.</summary>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task SquadronBankAndMarketMovementDoNotDependOnPollOrder(
        bool bankFirst,
        bool sell,
        bool companionSnapshot
    )
    {
        RecordingRavenClient client = CarrierClient(75, "SQD-001");
        ColonizationDeliveryRecovery recovery = SquadronRecovery(client);
        CargoInventoryState cargo = ShipCargo(50);
        JournalEventEnvelope bank = SquadronBankEvent(5, toCarrier: false);
        JournalEventEnvelope market = SquadronMarketEvent(sell);
        JournalEventEnvelope[] batch = bankFirst ? [bank, market] : [market, bank];
        foreach (JournalEventEnvelope journalEvent in batch)
        {
            if (journalEvent.EventName == "CargoTransfer")
            {
                recovery.PrepareSquadronCargoTransferSnapshot(cargo);
            }
            cargo.Apply(journalEvent);
        }
        if (companionSnapshot)
        {
            cargo.Reset(cargo.CreateSnapshot());
        }

        await recovery.SynchronizeLiveEventsAsync(batch, true, cargo, cargoActivity: true);
        await recovery.SynchronizeLiveEventsAsync([], true, cargo, cargoActivity: true);

        Assert.Equal(2, client.FleetCarrierAdjustments.Count);
        Assert.Equal(-5, client.FleetCarrierAdjustments[1].Changes["steel"]);
        Assert.Equal(sell ? 75 : 65, Assert.Single(recovery.FleetCarriers).Cargo["steel"]);
    }

    /// <summary>Several bank and market actions preserve one bank baseline and exclude every market delta.</summary>
    [Fact]
    public async Task SquadronInterleavedBankAndMarketMovementsAreReconciledOnce()
    {
        RecordingRavenClient client = CarrierClient(75, "SQD-001");
        ColonizationDeliveryRecovery recovery = SquadronRecovery(client);
        CargoInventoryState cargo = ShipCargo(50);
        JournalEventEnvelope[] batch =
        [
            SquadronBankEvent(3, toCarrier: true),
            SquadronMarketEvent(sell: false),
            SquadronBankEvent(4, toCarrier: false),
            SquadronMarketEvent(sell: true),
        ];
        foreach (JournalEventEnvelope journalEvent in batch)
        {
            if (journalEvent.EventName == "CargoTransfer")
            {
                recovery.PrepareSquadronCargoTransferSnapshot(cargo);
            }
            cargo.Apply(journalEvent);
        }

        await recovery.SynchronizeLiveEventsAsync(batch, true, cargo, cargoActivity: true);

        Assert.Equal(3, client.FleetCarrierAdjustments.Count);
        Assert.Equal(-1, client.FleetCarrierAdjustments[^1].Changes["steel"]);
        Assert.Equal(74, Assert.Single(recovery.FleetCarriers).Cargo["steel"]);
    }

    /// <summary>A lost market acknowledgement must not cause a second adjustment from a ship snapshot.</summary>
    [Fact]
    public async Task SquadronUnconfirmedMarketMovementIsNotRetainedTwice()
    {
        RecordingRavenClient client = CarrierClient(75, "SQD-001");
        client.AdjustmentFailures.Enqueue(new HttpRequestException("response lost"));
        ColonizationDeliveryRecovery recovery = SquadronRecovery(client);
        CargoInventoryState cargo = ShipCargo(10);
        JournalEventEnvelope market = SquadronMarketEvent(sell: false);
        cargo.Apply(market);

        await recovery.SynchronizeLiveEventsAsync([market], true, cargo, cargoActivity: true);
        await recovery.SynchronizeLiveEventsAsync([Event("Cargo", "\"Count\":15")], true, cargo, cargoActivity: true);

        Assert.Single(client.FleetCarrierAdjustments);
        Assert.Equal(-5, Assert.Single(store.LoadPendingCargoAdjustments()).Delta["steel"]);
    }

    /// <summary>Ship snapshots still recover bank movement when the journal omits the transfer event.</summary>
    [Fact]
    public async Task SquadronSnapshotOnlyBankMovementStillAdjustsRaven()
    {
        RecordingRavenClient client = CarrierClient(75, "SQD-001");
        ColonizationDeliveryRecovery recovery = SquadronRecovery(client);
        CargoInventoryState cargo = ShipCargo(50);
        CargoSnapshot snapshot = ShipCargo(40).CreateSnapshot()!;
        cargo.Reset(snapshot);

        await recovery.SynchronizeLiveEventsAsync([], true, cargo, cargoActivity: true);
        await recovery.SynchronizeLiveEventsAsync([], true, cargo, cargoActivity: true);

        Assert.Equal(10, Assert.Single(client.FleetCarrierAdjustments).Changes["steel"]);
    }

    /// <summary>A market snapshot cannot replace a squadron manifest that also includes bank storage.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SquadronMarketStockCannotReplaceCombinedCargo(bool force)
    {
        RecordingRavenClient client = CarrierClient(176, "SQD-001");
        ColonizationDeliveryRecovery recovery = SquadronRecovery(client);
        recovery.UpdateMarket(LinkedCarrierMarket(0, "SQD-001"));

        await recovery.SyncFleetCarrierCargoAsync(force);

        Assert.False(recovery.CanSyncFleetCarrierCargo);
        Assert.Equal(0, client.ReplaceCargoCount);
        Assert.Equal(176, Assert.Single(recovery.FleetCarriers).Cargo["steel"]);
    }

    /// <summary>Linking a squadron carrier preserves its cargo even if a fresh commodity market is open.</summary>
    [Fact]
    public async Task PublishingSquadronCarrierDoesNotReplaceCargoFromMarket()
    {
        RecordingRavenClient client = CarrierClient(176, "SQD-001");
        ColonizationDeliveryRecovery recovery = SquadronRecovery(client);
        recovery.UpdateMarket(LinkedCarrierMarket(0, "SQD-001"));

        await recovery.PublishCurrentFleetCarrierAsync();

        Assert.Equal(0, client.ReplaceCargoCount);
        Assert.Contains(
            observer.FleetCarrierStatuses,
            notice => notice.Kind == ColonizationDeliveryNoticeKind.SquadronCarrierPublished
        );
    }

    /// <summary>Desktop projects cargo before docking context, so the first dock-and-transfer poll must also freeze.</summary>
    [Fact]
    public async Task SquadronTransferCaptureSurvivesDockingContextArrivingLaterInThePoll()
    {
        RecordingRavenClient client = CarrierClient(75, "SQD-001");
        ColonizationDeliveryRecovery recovery = Create(client, apiKey: "secret-key", carrierSync: true);
        recovery.UpdateStatus(new EliteStatus { Flags = StatusFlags.InMainShip });
        CargoInventoryState cargo = ShipCargo(50);
        JournalEventEnvelope bank = SquadronBankEvent(5, toCarrier: false);
        JournalEventEnvelope market = SquadronMarketEvent(sell: true);
        recovery.PrepareSquadronCargoTransferSnapshot(cargo);
        cargo.Apply(bank);
        cargo.Apply(market);
        cargo.Reset(cargo.CreateSnapshot());
        JournalEventEnvelope[] batch = [CarrierDock(squadron: true, stationName: "SQD-001"), bank, market];
        recovery.ApplyJournalEvents(batch, null);

        await recovery.SynchronizeLiveEventsAsync(batch, true, cargo, cargoActivity: true);

        Assert.Equal(75, Assert.Single(recovery.FleetCarriers).Cargo["steel"]);
    }

    /// <summary>Creates a linked squadron dock with a verified main-ship context.</summary>
    private ColonizationDeliveryRecovery SquadronRecovery(RecordingRavenClient client)
    {
        ColonizationDeliveryRecovery recovery = Create(client, apiKey: "secret-key", carrierSync: true);
        recovery.ApplyJournalEvents([CarrierDock(squadron: true, stationName: "SQD-001")], null);
        recovery.UpdateStatus(new EliteStatus { Flags = StatusFlags.InMainShip });
        return recovery;
    }

    private static JournalEventEnvelope SquadronMarketEvent(bool sell) =>
        Event(sell ? "MarketSell" : "MarketBuy", "\"MarketID\":42,\"Type\":\"Steel\",\"Count\":5");

    private static JournalEventEnvelope SquadronBankEvent(int count, bool toCarrier) =>
        Event(
            "CargoTransfer",
            $$"""
            "Transfers":[{"Type":"Steel","Count":{{count}},"Direction":"{{(toCarrier ? "tocarrier" : "toship")}}"}]
            """
        );
}
