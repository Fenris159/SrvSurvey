using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed partial class ColonizationDeliveryRecoveryTests
{
    private static readonly JournalEventEnvelope SteelBuy = Event(
        "MarketBuy",
        "\"MarketID\":42,\"Type\":\"Steel\",\"Count\":5"
    );

    /// <summary>Market snapshots replace linked carrier cargo only after opt-in, once per market snapshot unless forced.</summary>
    [Fact]
    public async Task MarketSyncReplacesLinkedCarrierCargoOnlyWhenEnabled()
    {
        RecordingRavenClient client = CarrierClient(75);
        client.FleetCarrierResponse = client.FleetCarrierResponse! with { DisplayName = "Supply carrier" };
        ColonizationDeliveryRecovery recovery = Create(client, apiKey: "secret-key");
        recovery.ApplyJournalEvents([CarrierDock(stationName: "Supply carrier ABC-123")], null);
        MarketSnapshot market = LinkedCarrierMarket(80, "Supply carrier ABC-123");

        await UpdateMarketAsync(recovery, market);
        await recovery.SyncFleetCarrierCargoAsync(force: true);
        Assert.False(recovery.CanSyncFleetCarrierCargo);
        Assert.Equal(0, client.ReplaceCargoCount);

        recovery.FleetCarrierCargoSyncEnabled = true;
        await UpdateMarketAsync(recovery, market with { Timestamp = market.Timestamp.AddSeconds(1) });

        Assert.Equal(1, client.ReplaceCargoCount);
        Assert.Equal(80, client.LastReplacement?["steel"]);
        Assert.Equal(
            [
                ColonizationDeliveryNoticeKind.CarrierCargoChecking,
                ColonizationDeliveryNoticeKind.CarrierCargoUpdating,
                ColonizationDeliveryNoticeKind.CarrierCargoUpdated,
            ],
            observer.FleetCarrierStatuses.Select(notice => notice.Kind)
        );
        Assert.Equal(
            new ColonizationDeliveryNotice(
                ColonizationDeliveryNoticeKind.CarrierCargoUpdated,
                "Supply carrier",
                Count: 1
            ),
            observer.FleetCarrierStatuses[^1]
        );
        Assert.Equal([true, false], observer.BusyChanges);
        Assert.Null(observer.PendingCargo[^1]);
        Assert.Equal(80, Assert.Single(recovery.FleetCarriers).Cargo["steel"]);

        await recovery.SyncFleetCarrierCargoAsync(force: false);
        Assert.Equal(1, client.ReplaceCargoCount);
        client.FleetCarrierResponse = client.FleetCarrierResponse with { Cargo = new() { ["steel"] = 80 } };
        await recovery.SyncFleetCarrierCargoAsync(force: true);
        Assert.Equal(ColonizationDeliveryNoticeKind.CarrierCargoCurrent, observer.FleetCarrierStatuses[^1].Kind);
    }

    /// <summary>Reports a carrier Raven does not know and a market synchronization failure.</summary>
    [Fact]
    public async Task MarketSyncReportsMissingCarrierAndFailures()
    {
        RecordingRavenClient client = CarrierClient(75);
        ColonizationDeliveryRecovery recovery = Create(client, apiKey: "secret-key", carrierSync: true);
        recovery.ApplyJournalEvents([CarrierDock()], null);
        client.FleetCarrierResponse = null;

        await UpdateMarketAsync(recovery, LinkedCarrierMarket(80));
        Assert.Equal(ColonizationDeliveryNoticeKind.CarrierNotOnRaven, observer.FleetCarrierStatuses[^1].Kind);

        client.FleetCarrierResponse = Carrier(75);
        client.ReplaceCargo = _ => throw new HttpRequestException("replace unavailable");
        await recovery.SyncFleetCarrierCargoAsync(force: true);

        Assert.Equal(
            new ColonizationDeliveryNotice(
                ColonizationDeliveryNoticeKind.CarrierCargoNotUpdated,
                Detail: "replace unavailable"
            ),
            observer.FleetCarrierStatuses[^1]
        );
        Assert.Equal(75, Assert.Single(recovery.FleetCarriers).Cargo["steel"]);
    }

    /// <summary>Journal market and transfer events adjust linked carriers in order only after opt-in and while live.</summary>
    [Fact]
    public async Task AdjustsLinkedCarrierFromLiveCargoEventsOnlyAfterOptIn()
    {
        var carrier = new ColonizationFleetCarrier
        {
            MarketId = 42,
            Name = "ABC-123",
            Cargo = new Dictionary<string, int> { ["steel"] = 75, ["water"] = 10 },
        };
        var client = new RecordingRavenClient
        {
            Workspace = new([], [], null, [carrier]),
            FleetCarrierResponse = carrier,
        };
        ColonizationDeliveryRecovery recovery = Create(client, apiKey: "secret-key");
        recovery.ApplyJournalEvents([CarrierDock()], null);
        recovery.UpdateStatus(new EliteStatus { Flags = StatusFlags.InMainShip });

        await recovery.SynchronizeLiveEventsAsync([SteelBuy], allowPublishing: true);
        Assert.Empty(client.FleetCarrierAdjustments);

        recovery.FleetCarrierCargoSyncEnabled = true;
        IReadOnlyList<ColonizationDeliveryNotice> notices = await recovery.SynchronizeLiveEventsAsync(
            [
                SteelBuy,
                Event("MarketSell", "\"MarketID\":42,\"Type\":\"Water\",\"Count\":2"),
                Event(
                    "CargoTransfer",
                    """
                    "Transfers":[
                      {"Type":"Steel","Count":4,"Direction":"tocarrier"},
                      {"Type":"Water","Count":3,"Direction":"toship"}]
                    """
                ),
            ],
            allowPublishing: true
        );

        Assert.Collection(
            client.FleetCarrierAdjustments,
            call => Assert.Equal(-5, call.Changes["steel"]),
            call => Assert.Equal(2, call.Changes["water"]),
            call =>
            {
                Assert.Equal(4, call.Changes["steel"]);
                Assert.Equal(-3, call.Changes["water"]);
            }
        );
        Assert.Contains(
            notices,
            notice => notice is { Kind: ColonizationDeliveryNoticeKind.CarrierCargoAdjusted, Name: "CargoTransfer" }
        );
        Assert.Null(observer.PendingCargo[^1]);

        await recovery.SynchronizeLiveEventsAsync([SteelBuy], allowPublishing: false);
        Assert.Equal(3, client.FleetCarrierAdjustments.Count);
    }

    /// <summary>Journal deltas recorded during a market baseline wait and replay after the absolute replacement.</summary>
    [Fact]
    public async Task QueuesJournalCargoDeltasUntilMarketBaselineCompletes()
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var entered = new ManualResetEventSlim(false);
        RecordingRavenClient client = CarrierClient(75);
        client.GateGetFleetCarrier = gate;
        client.EnteredGetFleetCarrier = entered;
        ColonizationDeliveryRecovery recovery = Create(client, apiKey: "secret-key", carrierSync: true);
        recovery.ApplyJournalEvents([CarrierDock()], null);
        recovery.UpdateStatus(new EliteStatus { Flags = StatusFlags.InMainShip });

        Task marketSync = UpdateMarketAsync(recovery, LinkedCarrierMarket(80));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        IReadOnlyList<ColonizationDeliveryNotice> notices = await recovery.SynchronizeLiveEventsAsync(
            [SteelBuy],
            allowPublishing: true
        );
        Assert.Empty(client.FleetCarrierAdjustments);
        Assert.Equal(
            new ColonizationDeliveryNotice(ColonizationDeliveryNoticeKind.CarrierCargoQueued, Count: 1),
            Assert.Single(notices)
        );

        gate.SetResult(true);
        await marketSync;

        Assert.Equal(1, client.ReplaceCargoCount);
        Assert.Equal(-5, Assert.Single(client.FleetCarrierAdjustments).Changes["steel"]);
        Assert.Equal(ColonizationDeliveryNoticeKind.CarrierCargoUpdated, observer.FleetCarrierStatuses[^1].Kind);
    }

    /// <summary>A failed replay of queued baseline deltas is retained and reported.</summary>
    [Fact]
    public async Task QueuedBaselineReplayFailureIsRetained()
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var entered = new ManualResetEventSlim(false);
        RecordingRavenClient client = CarrierClient(75);
        client.GateGetFleetCarrier = gate;
        client.EnteredGetFleetCarrier = entered;
        ColonizationDeliveryRecovery recovery = Create(client, apiKey: "secret-key", carrierSync: true);
        recovery.ApplyJournalEvents([CarrierDock()], null);
        recovery.UpdateStatus(new EliteStatus { Flags = StatusFlags.InMainShip });
        Task marketSync = UpdateMarketAsync(recovery, LinkedCarrierMarket(75));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        await recovery.SynchronizeLiveEventsAsync([SteelBuy], allowPublishing: true);
        client.AdjustmentFailures.Enqueue(new HttpRequestException("replay unavailable"));

        gate.SetResult(true);
        await marketSync;

        Assert.Contains(
            observer.FleetCarrierStatuses,
            notice =>
                notice
                == new ColonizationDeliveryNotice(
                    ColonizationDeliveryNoticeKind.QueuedCarrierCargoRetained,
                    Detail: "replay unavailable"
                )
        );
        Assert.Equal(-5, Assert.Single(store.LoadPendingCargoAdjustments()).Delta["steel"]);
    }

    /// <summary>An empty local carrier cache loads Raven's baseline when docking.</summary>
    [Fact]
    public async Task LoadsServerCargoBaselineWhenDockedLinkedCarrierCacheIsEmpty()
    {
        var local = new ColonizationFleetCarrier
        {
            MarketId = 42,
            Name = "ABC-123",
            Cargo = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
        };
        var client = new RecordingRavenClient
        {
            Workspace = new([], [], null, [local]),
            FleetCarrierResponse = local with { Cargo = new Dictionary<string, int> { ["aluminium"] = 50 } },
        };
        ColonizationDeliveryRecovery recovery = Create(client, apiKey: "secret-key", carrierSync: true);

        IReadOnlyList<ColonizationDeliveryNotice> notices = await ApplyAndSynchronizeAsync(recovery, CarrierDock());

        Assert.Equal(50, Assert.Single(recovery.FleetCarriers).Cargo["aluminium"]);
        Assert.Equal(
            new ColonizationDeliveryNotice(ColonizationDeliveryNoticeKind.CarrierBaselineLoaded, "ABC-123"),
            Assert.Single(notices)
        );

        await recovery.SynchronizeLiveEventsAsync([CarrierDock()], allowPublishing: true);
        Assert.Equal(1, client.GetFleetCarrierCount);
    }

    /// <summary>A missing or failed dock baseline is retried on the next dock.</summary>
    [Fact]
    public async Task FailedDockBaselineAllowsRetryWhenServerCarrierIsMissing()
    {
        var local = new ColonizationFleetCarrier
        {
            MarketId = 42,
            Name = "ABC-123",
            Cargo = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
        };
        var client = new RecordingRavenClient { Workspace = new([], [], null, [local]) };
        ColonizationDeliveryRecovery recovery = Create(client, apiKey: "secret-key", carrierSync: true);
        JournalEventEnvelope docked = CarrierDock();

        await ApplyAndSynchronizeAsync(recovery, docked);
        Assert.Equal(1, client.GetFleetCarrierCount);
        Assert.Empty(Assert.Single(recovery.FleetCarriers).Cargo);

        client.GateGetFleetCarrier = new TaskCompletionSource<bool>();
        client.GateGetFleetCarrier.SetException(new HttpRequestException("baseline unavailable"));
        AssertNotice(
            await recovery.SynchronizeLiveEventsAsync([docked], allowPublishing: true),
            ColonizationDeliveryNoticeKind.CarrierBaselineNotLoaded
        );

        client.GateGetFleetCarrier = null;
        client.FleetCarrierResponse = local with { Cargo = new Dictionary<string, int> { ["aluminium"] = 50 } };
        await recovery.SynchronizeLiveEventsAsync([docked], allowPublishing: true);

        Assert.Equal(3, client.GetFleetCarrierCount);
        Assert.Equal(50, Assert.Single(recovery.FleetCarriers).Cargo["aluminium"]);
    }

    /// <summary>A CAPI seed nested inside a market baseline cannot replay queued deltas early.</summary>
    [Fact]
    public async Task DoesNotReplayQueuedCargoDeltasWhenCapiSeedOverlapsMarketBaseline()
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var entered = new ManualResetEventSlim(false);
        RecordingRavenClient client = CarrierClient(75);
        client.GateGetFleetCarrier = gate;
        client.EnteredGetFleetCarrier = entered;
        ColonizationDeliveryRecovery recovery = Create(client, apiKey: "secret-key", carrierSync: true);
        recovery.ApplyJournalEvents([CarrierDock()], null);
        recovery.UpdateStatus(new EliteStatus { Flags = StatusFlags.InMainShip });

        Task marketSync = UpdateMarketAsync(recovery, LinkedCarrierMarket(80));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        await recovery.SynchronizeLiveEventsAsync([SteelBuy], allowPublishing: true);
        Assert.Empty(client.FleetCarrierAdjustments);

        await recovery.SeedLinkedCarrierCargoFromCapiAsync(LinkedCarrierCapiSnapshot(isDocked: false, steel: 90));
        Assert.Empty(client.FleetCarrierAdjustments);

        gate.SetResult(true);
        await marketSync;
        Assert.Equal(-5, Assert.Single(client.FleetCarrierAdjustments).Changes["steel"]);
    }

    /// <summary>A stale CAPI manifest is not seeded while the journal shows the commander docked at the carrier.</summary>
    [Fact]
    public async Task DoesNotSeedStaleCapiManifestWhileJournalDockedAtLinkedCarrier()
    {
        RecordingRavenClient client = CarrierClient(75);
        ColonizationDeliveryRecovery recovery = Create(client, apiKey: "secret-key", carrierSync: true);
        recovery.ApplyJournalEvents([CarrierDock()], null);

        await recovery.SeedLinkedCarrierCargoFromCapiAsync(LinkedCarrierCapiSnapshot(isDocked: false, steel: 90));

        Assert.Equal(0, client.ReplaceCargoCount);
        Assert.Equal(75, Assert.Single(recovery.FleetCarriers).Cargo["steel"]);
    }

    /// <summary>Seeds each linked carrier from CAPI once per session and ignores later snapshots.</summary>
    [Fact]
    public async Task SeedsCapiManifestOncePerSessionThenIgnoresLaterSnapshots()
    {
        RecordingRavenClient client = CarrierClient(75);
        ColonizationDeliveryRecovery recovery = Create(client, apiKey: "secret-key", carrierSync: true);

        await recovery.SeedLinkedCarrierCargoFromCapiAsync(null);
        await recovery.SeedLinkedCarrierCargoFromCapiAsync(LinkedCarrierCapiSnapshot(isDocked: false, steel: 90));
        Assert.Equal(1, client.ReplaceCargoCount);
        Assert.Equal(90, client.LastReplacement?["steel"]);
        ColonizationDeliveryNotice seeded = observer.FleetCarrierStatuses[^1];
        Assert.Equal(ColonizationDeliveryNoticeKind.CapiCargoSeeded, seeded.Kind);
        Assert.Equal(42, seeded.Count);
        Assert.Equal(90, Assert.Single(recovery.FleetCarriers).Cargo["steel"]);

        await recovery.SeedLinkedCarrierCargoFromCapiAsync(LinkedCarrierCapiSnapshot(isDocked: false, steel: 40));
        Assert.Equal(1, client.ReplaceCargoCount);
    }

    /// <summary>A matching CAPI manifest marks the carrier seeded without a write; a failed seed is reported.</summary>
    [Fact]
    public async Task CapiSeedSkipsMatchingManifestAndReportsFailure()
    {
        RecordingRavenClient client = CarrierClient(90);
        ColonizationDeliveryRecovery recovery = Create(client, apiKey: "secret-key", carrierSync: true);

        await recovery.SeedLinkedCarrierCargoFromCapiAsync(LinkedCarrierCapiSnapshot(isDocked: false, steel: 90));
        Assert.Equal(0, client.ReplaceCargoCount);

        recovery.SetCommander("Other Cmdr");
        recovery.SetCommander("Test Cmdr");
        LoadWorkspace(recovery, CarrierClient(75));
        client.ReplaceCargo = _ => throw new HttpRequestException("seed unavailable");
        await recovery.SeedLinkedCarrierCargoFromCapiAsync(LinkedCarrierCapiSnapshot(isDocked: false, steel: 90));

        Assert.Equal(
            new ColonizationDeliveryNotice(
                ColonizationDeliveryNoticeKind.CapiCargoSeedNotApplied,
                Detail: "seed unavailable"
            ),
            observer.FleetCarrierStatuses[^1]
        );
    }

    /// <summary>A squadron market buy queued behind a baseline is not applied again from the inverted ship difference.</summary>
    [Fact]
    public async Task QueuedSquadronMarketBuyDoesNotAlsoApplyInvertedShipCargoDiff()
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var entered = new ManualResetEventSlim(false);
        RecordingRavenClient client = CarrierClient(75, "SQD-001");
        client.GateGetFleetCarrier = gate;
        client.EnteredGetFleetCarrier = entered;
        ColonizationDeliveryRecovery recovery = Create(client, apiKey: "secret-key", carrierSync: true);
        recovery.ApplyJournalEvents([CarrierDock(squadron: true, stationName: "SQD-001")], null);
        recovery.UpdateStatus(new EliteStatus { Flags = StatusFlags.InMainShip });
        CargoInventoryState cargo = ShipCargo(0);
        cargo.Apply(SteelBuy);

        Task marketSync = UpdateMarketAsync(recovery, LinkedCarrierMarket(80, "SQD-001"));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        await recovery.SynchronizeLiveEventsAsync([SteelBuy], true, cargo, cargoActivity: true);
        Assert.Empty(client.FleetCarrierAdjustments);

        gate.SetResult(true);
        await marketSync;

        Assert.Equal(-5, Assert.Single(client.FleetCarrierAdjustments).Changes["steel"]);
    }

    /// <summary>Squadron carrier detection follows the journal commander through later commander activation.</summary>
    [Fact]
    public void StartupSquadronDetectionSurvivesCommanderActivationAfterJournalReplay()
    {
        ColonizationDeliveryRecovery recovery = Create(CarrierClient(10, "SQD-001"));
        recovery.SetCommander(null);
        recovery.ApplyJournalEvents([CarrierDock(squadron: true, stationName: "SQD-001")], "Test Cmdr");
        recovery.SetCommander("Test Cmdr");
        Assert.Equal(42, recovery.DetectedSquadronCarrierMarketId);

        recovery.ApplyJournalEvents([], "Other Cmdr");
        recovery.SetCommander("Other Cmdr");
        Assert.Null(recovery.DetectedSquadronCarrierMarketId);
    }

    /// <summary>Squadron carriers use the inverted ship cargo difference instead of transfer journal deltas.</summary>
    [Fact]
    public async Task AdjustsLinkedSquadronCarrierFromShipCargoDiffNotTransferJournal()
    {
        var carrier = new ColonizationFleetCarrier
        {
            MarketId = 42,
            Name = "SQD-001",
            Cargo = new Dictionary<string, int> { ["steel"] = 75, ["water"] = 10 },
        };
        var client = new RecordingRavenClient
        {
            Workspace = new([], [], null, [carrier]),
            FleetCarrierResponse = carrier,
        };
        ColonizationDeliveryRecovery recovery = Create(client, apiKey: "secret-key", carrierSync: true);
        recovery.ApplyJournalEvents([CarrierDock(squadron: true, stationName: "SQD-001")], null);
        recovery.UpdateStatus(new EliteStatus { Flags = StatusFlags.InMainShip });
        CargoInventoryState cargo = ShipCargo(50);
        JournalEventEnvelope transfer = Event(
            "CargoTransfer",
            """
            "Transfers":[
              {"Type":"Steel","Count":10,"Direction":"tocarrier"},
              {"Type":"Water","Count":3,"Direction":"toship"}]
            """
        );
        recovery.PrepareSquadronCargoTransferSnapshot(cargo);
        Assert.True(cargo.HasPreservedSnapshot);
        Assert.True(cargo.Apply(transfer));

        IReadOnlyList<ColonizationDeliveryNotice> notices = await recovery.SynchronizeLiveEventsAsync(
            [transfer],
            true,
            cargo,
            cargoActivity: true
        );

        FleetCarrierAdjustmentCall adjustment = Assert.Single(client.FleetCarrierAdjustments);
        Assert.Equal(10, adjustment.Changes["steel"]);
        Assert.Equal(-3, adjustment.Changes["water"]);
        Assert.Contains(
            notices,
            notice =>
                notice is { Kind: ColonizationDeliveryNoticeKind.CarrierCargoAdjusted, Name: "squadron cargo diff" }
        );
        Assert.False(cargo.HasPreservedSnapshot);
        ColonizationFleetCarrier updated = Assert.Single(recovery.FleetCarriers);
        Assert.Equal(85, updated.Cargo["steel"]);
        Assert.Equal(7, updated.Cargo["water"]);
    }

    /// <summary>A squadron market buy suppresses the next ship difference once; a later transfer still adjusts.</summary>
    [Fact]
    public async Task SkipsSquadronCargoDiffAfterMarketBuyAlreadyAdjustedCarrier()
    {
        RecordingRavenClient client = CarrierClient(75, "SQD-001");
        ColonizationDeliveryRecovery recovery = Create(client, apiKey: "secret-key", carrierSync: true);
        recovery.ApplyJournalEvents([CarrierDock(squadron: true, stationName: "SQD-001")], null);
        recovery.UpdateStatus(new EliteStatus { Flags = StatusFlags.InMainShip });
        CargoInventoryState cargo = ShipCargo(0);
        cargo.Apply(SteelBuy);

        await recovery.SynchronizeLiveEventsAsync([SteelBuy], true, cargo, cargoActivity: true);

        Assert.Equal(-5, Assert.Single(client.FleetCarrierAdjustments).Changes["steel"]);

        JournalEventEnvelope transfer = Event(
            "CargoTransfer",
            """ "Transfers":[{"Type":"Steel","Count":4,"Direction":"tocarrier"}] """
        );
        recovery.PrepareSquadronCargoTransferSnapshot(cargo);
        Assert.True(cargo.HasPreservedSnapshot);
        Assert.True(cargo.Apply(transfer));
        await recovery.SynchronizeLiveEventsAsync([transfer], true, cargo, cargoActivity: true);

        Assert.Equal(2, client.FleetCarrierAdjustments.Count);
        Assert.Equal(4, client.FleetCarrierAdjustments[1].Changes["steel"]);
    }

    /// <summary>A transfer captured after a market buy in the same poll is still sent.</summary>
    [Fact]
    public async Task SendsSquadronTransferDiffWhenMarketBuyAndTransferShareAPoll()
    {
        RecordingRavenClient client = CarrierClient(75, "SQD-001");
        ColonizationDeliveryRecovery recovery = Create(client, apiKey: "secret-key", carrierSync: true);
        recovery.ApplyJournalEvents([CarrierDock(squadron: true, stationName: "SQD-001")], null);
        recovery.UpdateStatus(new EliteStatus { Flags = StatusFlags.InMainShip });
        CargoInventoryState cargo = ShipCargo(50);
        JournalEventEnvelope transfer = Event(
            "CargoTransfer",
            """ "Transfers":[{"Type":"Steel","Count":10,"Direction":"tocarrier"}] """
        );
        Assert.True(cargo.Apply(SteelBuy));
        recovery.PrepareSquadronCargoTransferSnapshot(cargo);
        Assert.True(cargo.Apply(transfer));
        Assert.Equal(45, cargo.CreateSnapshot()!.GetCount("steel"));

        await recovery.SynchronizeLiveEventsAsync([SteelBuy, transfer], true, cargo, cargoActivity: true);

        Assert.Equal(2, client.FleetCarrierAdjustments.Count);
        Assert.Equal(-5, client.FleetCarrierAdjustments[0].Changes["steel"]);
        Assert.Equal(10, client.FleetCarrierAdjustments[1].Changes["steel"]);
    }

    /// <summary>Multiple transfers in one poll keep the first captured baseline.</summary>
    [Fact]
    public async Task CapturesFirstSquadronBaselineAcrossMultipleTransfersInOnePoll()
    {
        RecordingRavenClient client = CarrierClient(75, "SQD-001");
        ColonizationDeliveryRecovery recovery = Create(client, apiKey: "secret-key", carrierSync: true);
        recovery.ApplyJournalEvents([CarrierDock(squadron: true, stationName: "SQD-001")], null);
        recovery.UpdateStatus(new EliteStatus { Flags = StatusFlags.InMainShip });
        CargoInventoryState cargo = ShipCargo(50);
        JournalEventEnvelope first = Event(
            "CargoTransfer",
            """ "Transfers":[{"Type":"Steel","Count":10,"Direction":"tocarrier"}] """
        );
        JournalEventEnvelope second = Event(
            "CargoTransfer",
            """ "Transfers":[{"Type":"Steel","Count":5,"Direction":"tocarrier"}] """
        );
        recovery.PrepareSquadronCargoTransferSnapshot(cargo);
        Assert.True(cargo.Apply(first));
        recovery.PrepareSquadronCargoTransferSnapshot(cargo);
        Assert.True(cargo.Apply(second));
        Assert.Equal(35, cargo.CreateSnapshot()!.GetCount("steel"));

        await recovery.SynchronizeLiveEventsAsync([first, second], true, cargo, cargoActivity: true);

        Assert.Equal(15, Assert.Single(client.FleetCarrierAdjustments).Changes["steel"]);
    }

    /// <summary>Without ship cargo, squadron transfers fall back to journal deltas.</summary>
    [Fact]
    public async Task FallsBackToJournalTransfersForSquadronWhenShipCargoDiffUnavailable()
    {
        RecordingRavenClient client = CarrierClient(75, "SQD-001");
        ColonizationDeliveryRecovery recovery = Create(client, apiKey: "secret-key", carrierSync: true);
        recovery.ApplyJournalEvents([CarrierDock(squadron: true, stationName: "SQD-001")], null);
        recovery.UpdateStatus(new EliteStatus { Flags = StatusFlags.InMainShip });

        await recovery.SynchronizeLiveEventsAsync(
            [
                Event(
                    "CargoTransfer",
                    """
                    "Transfers":[
                      {"Type":"Steel","Count":4,"Direction":"tocarrier"},
                      {"Type":"Water","Count":3,"Direction":"toship"}]
                    """
                ),
            ],
            allowPublishing: true,
            cargoInventory: null,
            cargoActivity: false
        );

        FleetCarrierAdjustmentCall adjustment = Assert.Single(client.FleetCarrierAdjustments);
        Assert.Equal(4, adjustment.Changes["steel"]);
        Assert.Equal(-3, adjustment.Changes["water"]);
    }

    /// <summary>A journal fallback consumes the preserved snapshot so the next ship difference does not duplicate it.</summary>
    [Fact]
    public async Task SkipsDuplicateSquadronDiffWhenFallbackRunsBeforeSnapshotReplay()
    {
        RecordingRavenClient client = CarrierClient(75, "SQD-001");
        ColonizationDeliveryRecovery recovery = Create(client, apiKey: "secret-key", carrierSync: true);
        recovery.ApplyJournalEvents([CarrierDock(squadron: true, stationName: "SQD-001")], null);
        recovery.UpdateStatus(new EliteStatus { Flags = StatusFlags.InMainShip });
        CargoInventoryState cargo = ShipCargo(50);
        JournalEventEnvelope transfer = Event(
            "CargoTransfer",
            """ "Transfers":[{"Type":"Steel","Count":10,"Direction":"tocarrier"}] """
        );
        recovery.PrepareSquadronCargoTransferSnapshot(cargo);
        Assert.True(cargo.Apply(transfer));

        await recovery.SynchronizeLiveEventsAsync(
            [transfer],
            allowPublishing: true,
            cargoInventory: cargo,
            cargoActivity: true,
            preferShipCargoDiffForSquadron: false
        );

        Assert.Single(client.FleetCarrierAdjustments);
        Assert.False(cargo.HasPreservedSnapshot);

        cargo.Reset(
            new CargoSnapshot(
                DateTimeOffset.Parse("2026-07-24T12:00:01Z", System.Globalization.CultureInfo.InvariantCulture),
                "Cargo",
                "Ship",
                40,
                [new CargoItem("steel", "Steel", 40, 0)]
            )
        );
        await recovery.SynchronizeLiveEventsAsync([], true, cargo, cargoActivity: false);

        Assert.Single(client.FleetCarrierAdjustments);
    }

    /// <summary>Squadron differences are dropped when synchronization is off, the dock is not a linked squadron carrier, or publishing is paused.</summary>
    [Fact]
    public async Task SquadronDiffIsDiscardedOutsideALinkedSquadronDock()
    {
        RecordingRavenClient client = CarrierClient(75, "SQD-001");
        ColonizationDeliveryRecovery recovery = Create(client, apiKey: "secret-key", carrierSync: true);
        CargoInventoryState cargo = ShipCargo(50);
        cargo.CaptureBeforeSnapshot();

        await recovery.SynchronizeLiveEventsAsync([], true, cargo, cargoActivity: true);
        Assert.False(cargo.HasPreservedSnapshot);

        recovery.ApplyJournalEvents([CarrierDock(stationName: "SQD-001")], null);
        cargo.CaptureBeforeSnapshot();
        await recovery.SynchronizeLiveEventsAsync([], true, cargo, cargoActivity: true);
        Assert.False(cargo.HasPreservedSnapshot);

        cargo.CaptureBeforeSnapshot();
        await recovery.SynchronizeLiveEventsAsync([], allowPublishing: false, cargo);
        Assert.False(cargo.HasPreservedSnapshot);
        Assert.Empty(client.FleetCarrierAdjustments);
    }

    /// <summary>A squadron difference arriving during a baseline is queued without a journal time.</summary>
    [Fact]
    public async Task QueuesSquadronDiffDuringBaseline()
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var entered = new ManualResetEventSlim(false);
        RecordingRavenClient client = CarrierClient(75, "SQD-001");
        client.GateGetFleetCarrier = gate;
        client.EnteredGetFleetCarrier = entered;
        ColonizationDeliveryRecovery recovery = Create(client, apiKey: "secret-key", carrierSync: true);
        recovery.ApplyJournalEvents([CarrierDock(squadron: true, stationName: "SQD-001")], null);
        CargoInventoryState cargo = ShipCargo(50);
        Task marketSync = UpdateMarketAsync(recovery, LinkedCarrierMarket(75, "SQD-001"));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        recovery.PrepareSquadronCargoTransferSnapshot(cargo);
        cargo.Apply(Event("CargoTransfer", """ "Transfers":[{"Type":"Steel","Count":10,"Direction":"tocarrier"}] """));

        IReadOnlyList<ColonizationDeliveryNotice> notices = await recovery.SynchronizeLiveEventsAsync(
            [],
            true,
            cargo,
            cargoActivity: true
        );
        gate.SetResult(true);
        await marketSync;

        Assert.Equal(
            new ColonizationDeliveryNotice(ColonizationDeliveryNoticeKind.SquadronCargoQueued, Count: 1),
            Assert.Single(notices)
        );
        Assert.Equal(10, Assert.Single(client.FleetCarrierAdjustments).Changes["steel"]);
    }

    /// <summary>Publishes the docked carrier, links it, and replaces its cargo from a fresh market.</summary>
    [Fact]
    public async Task PublishesCurrentCarrierAndThenReconcilesFreshMarket()
    {
        RecordingRavenClient client = CarrierClient(75);
        client.FleetCarrierResponse = client.FleetCarrierResponse! with { DisplayName = "Supply carrier" };
        client.Workspace = new([], [], null, []);
        ColonizationDeliveryRecovery recovery = Create(client, apiKey: "secret-key");
        recovery.ApplyJournalEvents(
            [Event("ReceiveText", "\"From\":\"Supply carrier | ABC-123\""), CarrierDock()],
            null
        );
        recovery.UpdateMarket(LinkedCarrierMarket(80));

        await recovery.PublishCurrentFleetCarrierAsync();

        Assert.Equal(1, client.PublishCarrierCount);
        Assert.Equal(42, client.LastCarrierRegistration?.MarketId);
        Assert.Equal("ABC-123", client.LastCarrierRegistration?.Name);
        Assert.Equal("Supply carrier", client.LastCarrierRegistration?.DisplayName);
        Assert.Null(client.LastCarrierRegistration?.Cargo);
        Assert.Equal(1, client.ReplaceCargoCount);
        Assert.Equal(80, client.LastReplacement?["steel"]);
        Assert.Equal(80, Assert.Single(recovery.FleetCarriers).Cargo["steel"]);
        Assert.Equal(
            [
                new ColonizationDeliveryNotice(ColonizationDeliveryNoticeKind.CarrierPublishing, "ABC-123"),
                new ColonizationDeliveryNotice(
                    ColonizationDeliveryNoticeKind.CarrierPublishedCargoUpdated,
                    "Supply carrier",
                    Count: 1
                ),
            ],
            observer.FleetCarrierStatuses
        );
        Assert.Equal([true, false], observer.BusyChanges);
    }

    /// <summary>Publishing reports stale markets, already-current cargo, a failed registration, and a failed cargo replacement.</summary>
    [Fact]
    public async Task PublishReportsEachCarrierOutcome()
    {
        RecordingRavenClient client = CarrierClient(80);
        client.Workspace = new([], [], null, []);
        ColonizationDeliveryRecovery recovery = Create(client, apiKey: "secret-key");
        await recovery.PublishCurrentFleetCarrierAsync();
        Assert.Equal(0, client.PublishCarrierCount);

        recovery.ApplyJournalEvents([CarrierDock()], null);
        await recovery.PublishCurrentFleetCarrierAsync();
        Assert.Equal(
            ColonizationDeliveryNoticeKind.CarrierPublishedAwaitingMarket,
            observer.FleetCarrierStatuses[^1].Kind
        );

        recovery.UpdateMarket(LinkedCarrierMarket(80));
        await recovery.PublishCurrentFleetCarrierAsync();
        Assert.Equal(
            ColonizationDeliveryNoticeKind.CarrierPublishedCargoCurrent,
            observer.FleetCarrierStatuses[^1].Kind
        );

        recovery.UpdateMarket(LinkedCarrierMarket(60));
        client.ReplaceCargo = _ => throw new HttpRequestException("replace unavailable");
        await recovery.PublishCurrentFleetCarrierAsync();
        Assert.Equal(
            new ColonizationDeliveryNotice(
                ColonizationDeliveryNoticeKind.CarrierLinkedCargoNotUpdated,
                Detail: "replace unavailable"
            ),
            observer.FleetCarrierStatuses[^1]
        );

        var failing = new RecordingRavenClient { PublishFailure = new HttpRequestException("publish unavailable") };
        ColonizationDeliveryRecovery failingRecovery = Create(failing, apiKey: "secret-key");
        failingRecovery.ApplyJournalEvents([CarrierDock()], null);
        await failingRecovery.PublishCurrentFleetCarrierAsync();
        Assert.Equal(ColonizationDeliveryNoticeKind.CarrierNotPublished, observer.FleetCarrierStatuses[^1].Kind);
    }
}
