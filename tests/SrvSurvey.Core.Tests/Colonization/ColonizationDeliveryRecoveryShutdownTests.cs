using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed partial class ColonizationDeliveryRecoveryTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    public async Task FailedBaselineRetirementCannotReplayCoveredUnsentCargo(
        bool restart,
        bool preExisting,
        bool partial
    )
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var entered = new ManualResetEventSlim(false);
        RecordingRavenClient client = CarrierClient(75);
        client.GateGetFleetCarrier = gate;
        client.EnteredGetFleetCarrier = entered;
        client.ReplaceCargo = cargo =>
        {
            store.SaveFailure = new IOException("retirement storage unavailable");
            return Task.FromResult(cargo);
        };
        ColonizationDeliveryRecovery recovery = Create(client, "key", carrierSync: true);
        recovery.ApplyJournalEvents([CarrierDock()], null);
        recovery.UpdateStatus(new EliteStatus { Flags = StatusFlags.InMainShip });
        MarketSnapshot market = LinkedCarrierMarket(80);
        string water = partial ? ",{\"Type\":\"Water\",\"Count\":3,\"Direction\":\"tocarrier\"}" : string.Empty;
        JournalEventEnvelope transfer = Event(
            "CargoTransfer",
            "\"Transfers\":[{\"Type\":\"Steel\",\"Count\":4,\"Direction\":\"tocarrier\"}" + water + "]"
        ) with
        {
            Timestamp = market.Timestamp.AddSeconds(-1),
        };
        if (preExisting)
        {
            recovery.CaptureFleetCarrierCargo([transfer]);
        }
        Task baseline = UpdateMarketAsync(recovery, market);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        if (!preExisting)
        {
            await recovery.SynchronizeLiveEventsAsync([transfer], true);
        }
        Assert.Single(store.LoadPendingCargoAdjustments());
        gate.SetResult(true);
        await baseline;
        Assert.Equal(80, client.LastReplacement?["steel"]);
        store.SaveFailure = null;
        RecordingRavenClient recoveredClient = restart ? CarrierClient(80) : client;
        ColonizationDeliveryRecovery recovered = restart ? Create(recoveredClient, "key", carrierSync: true) : recovery;

        await recovered.FlushFleetCarrierCargoAsync();

        if (restart)
        {
            Assert.Empty(recoveredClient.FleetCarrierAdjustments);
            await ApplyAndSynchronizeAsync(recovered, CarrierDock());
            await recovered.FlushFleetCarrierCargoAsync();
            Assert.Empty(recoveredClient.FleetCarrierAdjustments);
            await UpdateMarketAsync(recovered, market);
            await recovered.FlushFleetCarrierCargoAsync();
        }
        Assert.Equal(0, recoveredClient.FleetCarrierAdjustments.Sum(call => call.Changes.GetValueOrDefault("steel")));
        Assert.Equal(
            partial ? 3 : 0,
            recoveredClient.FleetCarrierAdjustments.Sum(call => call.Changes.GetValueOrDefault("water"))
        );
        Assert.Empty(store.LoadPendingCargoAdjustments());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadOnlyDockBaselineDoesNotRequireAMarketToRecoverUnsentCargo(bool squadron)
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var entered = new ManualResetEventSlim(false);
        RecordingRavenClient client = CarrierClient(75);
        client.Workspace = client.Workspace with { FleetCarriers = [Carrier(0) with { Cargo = [] }] };
        client.GateGetFleetCarrier = gate;
        client.EnteredGetFleetCarrier = entered;
        ColonizationDeliveryRecovery recovery = Create(client, "key", carrierSync: true);
        Task baseline = ApplyAndSynchronizeAsync(recovery, CarrierDock(squadron));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            recovery.CaptureFleetCarrierCargo([SteelBuy]);
            Assert.False(Assert.Single(store.LoadPendingCargoAdjustments()).AwaitingBaseline);
            recovery.SetProfile("F456", true, "other-key");
            RecordingRavenClient restartedClient = CarrierClient(75);
            ColonizationDeliveryRecovery restarted = Create(restartedClient, "key", carrierSync: true);

            await restarted.FlushFleetCarrierCargoAsync();

            Assert.Equal(-5, Assert.Single(restartedClient.FleetCarrierAdjustments).Changes["steel"]);
            Assert.Empty(store.LoadPendingCargoAdjustments());
        }
        finally
        {
            recovery.SetProfile("F456", true, "other-key");
            gate.TrySetResult(true);
            await baseline;
        }
    }

    [Fact]
    public async Task BaselineWaitsUntilItsDependencyCanBeSaved()
    {
        RecordingRavenClient client = CarrierClient(75);
        ColonizationDeliveryRecovery recovery = Create(client, "key", carrierSync: true);
        recovery.ApplyJournalEvents([CarrierDock()], null);
        recovery.CaptureFleetCarrierCargo([SteelBuy]);
        store.SaveFailure = new IOException("baseline dependency storage unavailable");

        await UpdateMarketAsync(recovery, LinkedCarrierMarket(80));

        Assert.Equal(0, client.ReplaceCargoCount);
        Assert.False(Assert.Single(store.LoadPendingCargoAdjustments()).AwaitingBaseline);
        store.SaveFailure = null;
        await recovery.FlushFleetCarrierCargoAsync();
        Assert.Equal(-5, Assert.Single(client.FleetCarrierAdjustments).Changes["steel"]);
    }

    [Fact]
    public async Task CargoWaitingForABaselineIsDurableBeforeThatBaselineFinishes()
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var entered = new ManualResetEventSlim(false);
        RecordingRavenClient client = CarrierClient(75);
        client.GateGetFleetCarrier = gate;
        client.EnteredGetFleetCarrier = entered;
        ColonizationDeliveryRecovery recovery = Create(client, apiKey: "test-key", carrierSync: true);
        recovery.ApplyJournalEvents([CarrierDock()], null);
        recovery.UpdateStatus(new EliteStatus { Flags = StatusFlags.InMainShip });
        Task baseline = UpdateMarketAsync(recovery, LinkedCarrierMarket(80));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            await recovery.SynchronizeLiveEventsAsync(
                [SteelBuy with { Timestamp = LinkedCarrierMarket(80).Timestamp.AddSeconds(1) }],
                allowPublishing: true
            );

            ColonizationPendingCargoAdjustment pending = Assert.Single(store.LoadPendingCargoAdjustments());
            Assert.Equal(-5, pending.Delta["steel"]);
            Assert.False(pending.Attempted);
            Assert.False(pending.OutcomeUnknown);
            Assert.Empty(client.FleetCarrierAdjustments);
        }
        finally
        {
            gate.TrySetResult(true);
            await baseline;
        }
    }

    [Fact]
    public async Task EntireCargoBatchIsDurableBeforeTheFirstHttpRequestCompletes()
    {
        RecordingRavenClient client = CarrierClient();
        var response = new TaskCompletionSource<IReadOnlyDictionary<string, int>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        client.AdjustmentResponseTask = response.Task;
        ColonizationDeliveryRecovery recovery = Create(client, apiKey: "test-key", carrierSync: true);
        recovery.ApplyJournalEvents([CarrierDock()], null);
        JournalEventEnvelope sell = Event("MarketSell", "\"MarketID\":42,\"Type\":\"Steel\",\"Count\":3");
        Task sending = recovery.SynchronizeLiveEventsAsync([SteelBuy, sell], true);
        try
        {
            ColonizationPendingCargoAdjustment[] pending = store.LoadPendingCargoAdjustments().ToArray();
            Assert.Equal(2, pending.Length);
            Assert.True(pending[0].Attempted);
            Assert.True(pending[0].OutcomeUnknown);
            Assert.Equal(100, pending[0].Before?["steel"]);
            Assert.False(pending[1].Attempted);
            Assert.Equal(3, pending[1].Delta["steel"]);
            Assert.Single(client.FleetCarrierAdjustments);
        }
        finally
        {
            response.TrySetResult(new Dictionary<string, int> { ["steel"] = 95 });
            await sending;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CapturedCargoSurvivesACanceledProjectionAndRestartsWithoutADock(bool squadron)
    {
        RecordingRavenClient client = CarrierClient();
        ColonizationDeliveryRecovery recovery = Create(client, apiKey: "test-key", carrierSync: true);
        recovery.ApplyJournalEvents([CarrierDock(squadron)], null);
        recovery.UpdateStatus(new EliteStatus { Flags = StatusFlags.InMainShip });
        JournalEventEnvelope transfer = SquadronBankEvent(5, toCarrier: true);
        CargoInventoryState? cargo = squadron ? ShipCargo(50) : null;
        if (cargo is not null)
        {
            recovery.PrepareSquadronCargoTransferSnapshot(cargo);
            cargo.Apply(transfer);
        }
        IReadOnlyList<ColonizationPendingCargoAdjustment> captured = recovery.CaptureFleetCarrierCargo(
            [transfer],
            cargo,
            cargoActivity: true
        );
        Assert.Single(captured);
        Assert.Empty(client.FleetCarrierAdjustments);
        await recovery.SynchronizeLiveEventsAsync([transfer], false, cargo, capturedCargo: captured);
        Assert.Single(store.LoadPendingCargoAdjustments());

        ColonizationDeliveryRecovery restarted = Create(client, apiKey: "test-key", carrierSync: true);
        Assert.Null(restarted.CurrentDock);
        await restarted.FlushFleetCarrierCargoAsync();
        await restarted.FlushFleetCarrierCargoAsync();

        Assert.Equal(5, Assert.Single(client.FleetCarrierAdjustments).Changes["steel"]);
        Assert.Empty(store.LoadPendingCargoAdjustments());
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task GameShutdownInSameBatchUsesSquadronInventoryDifferenceRatherThanBankCount(
        bool mainMenuFirst,
        bool shutdown
    )
    {
        RecordingRavenClient client = CarrierClient(75, "SQD-001");
        ColonizationDeliveryRecovery recovery = SquadronRecovery(client);
        CargoInventoryState cargo = ShipCargo(50);
        JournalEventEnvelope transfer = SquadronBankEvent(10, toCarrier: true);
        recovery.PrepareSquadronCargoTransferSnapshot(cargo);
        cargo.Apply(transfer);
        cargo.Reset(ShipCargo(35).CreateSnapshot());
        var events = new List<JournalEventEnvelope> { transfer };
        if (mainMenuFirst)
        {
            events.Add(Event("Music", "\"MusicTrack\":\"MainMenu\""));
        }
        if (shutdown)
        {
            events.Add(Event("Shutdown", ""));
        }
        recovery.ApplyJournalEvents(events, null);

        await recovery.SynchronizeLiveEventsAsync(events, true, cargo, cargoActivity: true);
        await recovery.SynchronizeLiveEventsAsync([], true, cargo, cargoActivity: true);

        Assert.Null(recovery.CurrentDock);
        Assert.Equal(15, Assert.Single(client.FleetCarrierAdjustments).Changes["steel"]);
        Assert.Empty(store.LoadPendingCargoAdjustments());
    }

    [Fact]
    public async Task CargoIsNotSentWithoutADurableRecordAndCanRecoverWhenStorageReturns()
    {
        RecordingRavenClient client = CarrierClient();
        ColonizationDeliveryRecovery recovery = Create(client, apiKey: "test-key", carrierSync: true);
        recovery.ApplyJournalEvents([CarrierDock()], null);
        store.SaveFailure = new IOException("disk unavailable");

        await recovery.SynchronizeLiveEventsAsync([SteelBuy], true);

        Assert.Empty(client.FleetCarrierAdjustments);
        Assert.Empty(store.LoadPendingCargoAdjustments());
        store.SaveFailure = null;
        await recovery.FlushFleetCarrierCargoAsync();
        Assert.Equal(-5, Assert.Single(client.FleetCarrierAdjustments).Changes["steel"]);
        Assert.Empty(store.LoadPendingCargoAdjustments());
    }

    [Fact]
    public async Task ShutdownFlushDoesNotGuessAnUncertainHttpOutcome()
    {
        RecordingRavenClient client = CarrierClient();
        client.AdjustmentFailures.Enqueue(new HttpRequestException("response lost"));
        ColonizationDeliveryRecovery recovery = Create(client, apiKey: "test-key", carrierSync: true);
        recovery.ApplyJournalEvents([CarrierDock()], null);
        await recovery.SynchronizeLiveEventsAsync([SteelBuy], true);

        IReadOnlyList<ColonizationDeliveryNotice> notices = await recovery.FlushFleetCarrierCargoAsync();

        Assert.Contains(notices, item => item.Kind == ColonizationDeliveryNoticeKind.CarrierCargoPendingUncertain);
        Assert.Single(client.FleetCarrierAdjustments);
        Assert.Single(store.LoadPendingCargoAdjustments());
    }

    [Fact]
    public async Task ShutdownFlushDoesNotSendDisabledOrAnotherCommandersCargo()
    {
        RecordingRavenClient client = CarrierClient();
        ColonizationDeliveryRecovery recovery = Create(client, apiKey: "test-key", carrierSync: true);
        recovery.ApplyJournalEvents([CarrierDock()], null);
        recovery.CaptureFleetCarrierCargo([SteelBuy]);
        recovery.FleetCarrierCargoSyncEnabled = false;
        Assert.Empty(await recovery.FlushFleetCarrierCargoAsync());
        recovery.FleetCarrierCargoSyncEnabled = true;
        recovery.SetCommander("Someone Else");
        Assert.Empty(await recovery.FlushFleetCarrierCargoAsync());
        recovery.IsEnabled = false;
        Assert.Empty(await recovery.FlushFleetCarrierCargoAsync());
        Assert.Empty(client.FleetCarrierAdjustments);
        Assert.Single(store.LoadPendingCargoAdjustments());
    }

    [Fact]
    public async Task ShutdownFlushWaitsForDurableAttemptStorageBeforeSendingAnyDependentCargo()
    {
        RecordingRavenClient client = CarrierClient();
        ColonizationDeliveryRecovery recovery = Create(client, apiKey: "test-key", carrierSync: true);
        recovery.ApplyJournalEvents([CarrierDock()], null);
        recovery.CaptureFleetCarrierCargo([SteelBuy, SteelBuy]);
        store.SaveFailure = new IOException("disk unavailable");

        IReadOnlyList<ColonizationDeliveryNotice> notices = await recovery.FlushFleetCarrierCargoAsync();

        Assert.Contains(notices, item => item.Kind == ColonizationDeliveryNoticeKind.CarrierCargoRecoveryNotSaved);
        Assert.Empty(client.FleetCarrierAdjustments);
        Assert.All(store.LoadPendingCargoAdjustments(), pending => Assert.False(pending.Attempted));
        store.SaveFailure = null;
        await recovery.FlushFleetCarrierCargoAsync();
        Assert.Equal(2, client.FleetCarrierAdjustments.Count);
        Assert.Empty(store.LoadPendingCargoAdjustments());
    }

    [Fact]
    public async Task CancelingTheShutdownDrainKeepsUnsentCargoRecoverable()
    {
        RecordingRavenClient client = CarrierClient();
        ColonizationDeliveryRecovery recovery = Create(client, apiKey: "test-key", carrierSync: true);
        recovery.ApplyJournalEvents([CarrierDock()], null);
        recovery.CaptureFleetCarrierCargo([SteelBuy]);
        using var entered = new ManualResetEventSlim(false);
        client.EnteredGetFleetCarrier = entered;
        client.GateGetFleetCarrier = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        Task drain = recovery.FlushFleetCarrierCargoAsync(cancellation.Token);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));

        await cancellation.CancelAsync();
        await drain.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(client.FleetCarrierAdjustments);
        Assert.False(Assert.Single(store.LoadPendingCargoAdjustments()).Attempted);
        client.GateGetFleetCarrier = null;
        ColonizationDeliveryRecovery restarted = Create(client, apiKey: "test-key", carrierSync: true);
        await restarted.FlushFleetCarrierCargoAsync();
        Assert.Equal(-5, Assert.Single(client.FleetCarrierAdjustments).Changes["steel"]);
        Assert.Empty(store.LoadPendingCargoAdjustments());
    }
}
