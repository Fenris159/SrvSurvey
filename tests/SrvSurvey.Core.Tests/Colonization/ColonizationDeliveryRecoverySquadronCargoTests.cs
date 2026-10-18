using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed partial class ColonizationDeliveryRecoveryTests
{
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
