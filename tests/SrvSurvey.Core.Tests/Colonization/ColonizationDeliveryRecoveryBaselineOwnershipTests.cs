using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed partial class ColonizationDeliveryRecoveryTests
{
    /// <summary>Equivalent empty and trimmed credentials preserve the owner of in-flight work.</summary>
    [Theory]
    [InlineData(null, null, "", "")]
    [InlineData(null, null, "  ", "  ")]
    [InlineData("F123", "key", " F123 ", " key ")]
    public void EquivalentNormalizedProfileDoesNotInvalidateWork(
        string? id,
        string? key,
        string? equivalentId,
        string? equivalentKey
    )
    {
        ColonizationDeliveryRecovery recovery = Create(CarrierClient());
        recovery.SetProfile(id, true, key);
        int version = recovery.ProfileVersion;

        recovery.SetProfile(equivalentId, true, equivalentKey);

        Assert.Equal(version, recovery.ProfileVersion);
    }

    /// <summary>A superseded baseline cannot update the replacement profile or release its queued transactions.</summary>
    [Theory]
    [InlineData("market lookup", false)]
    [InlineData("market replace", false)]
    [InlineData("dock", false)]
    [InlineData("capi", false)]
    [InlineData("publish", false)]
    [InlineData("market lookup", true)]
    public async Task SupersededCargoBaselineDoesNotReleaseReplacementProfile(string operation, bool changeCommander)
    {
        var oldGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var enteredLookup = new ManualResetEventSlim(false);
        RecordingRavenClient client = CarrierClient(75);
        if (operation == "dock")
        {
            client.Workspace = client.Workspace with { FleetCarriers = [Carrier(0) with { Cargo = [] }] };
        }
        ColonizationDeliveryRecovery recovery = Create(client, "old-key", carrierSync: true);
        recovery.UpdateStatus(new EliteStatus { Flags = StatusFlags.InMainShip });
        Task oldBaseline;
        if (operation is "market lookup" or "dock")
        {
            client.GateGetFleetCarrier = oldGate;
            client.EnteredGetFleetCarrier = enteredLookup;
            if (operation == "dock")
            {
                oldBaseline = ApplyAndSynchronizeAsync(recovery, CarrierDock());
            }
            else
            {
                recovery.ApplyJournalEvents([CarrierDock()], null);
                oldBaseline = UpdateMarketAsync(recovery, LinkedCarrierMarket(80));
            }
            Assert.True(enteredLookup.Wait(TimeSpan.FromSeconds(5)));
        }
        else if (operation == "publish")
        {
            client.PublishCarrierTask = WaitForRegistrationAsync();
            recovery.ApplyJournalEvents([CarrierDock()], null);
            recovery.UpdateMarket(LinkedCarrierMarket(80));
            oldBaseline = recovery.PublishCurrentFleetCarrierAsync();
        }
        else
        {
            client.ReplaceCargo = async cargo =>
            {
                oldEntered.SetResult(true);
                await oldGate.Task;
                return cargo;
            };
            if (operation == "capi")
            {
                oldBaseline = recovery.SeedLinkedCarrierCargoFromCapiAsync(LinkedCarrierCapiSnapshot(false, 80));
            }
            else
            {
                recovery.ApplyJournalEvents([CarrierDock()], null);
                oldBaseline = UpdateMarketAsync(recovery, LinkedCarrierMarket(80));
            }
            await oldEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        recovery.SetProfile("F456", true, "new-key");
        if (changeCommander)
        {
            recovery.SetCommander("Replacement Cmdr");
        }
        client.Workspace = client.Workspace with { FleetCarriers = [Carrier(75)] };
        LoadWorkspace(recovery, client);
        recovery.ApplyJournalEvents([CarrierDock()], null);
        client.ReplaceCargo = null;
        var newGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.GateGetFleetCarrier = newGate;
        client.EnteredGetFleetCarrier = enteredLookup;
        enteredLookup.Reset();
        Task newBaseline = UpdateMarketAsync(recovery, LinkedCarrierMarket(90));
        Assert.True(enteredLookup.Wait(TimeSpan.FromSeconds(5)));
        await recovery.SynchronizeLiveEventsAsync(
            [SteelBuy with { Timestamp = LinkedCarrierMarket(90).Timestamp.AddSeconds(1) }],
            true
        );
        int notifications = observer.FleetCarrierStatuses.Count;
        int busyNotifications = observer.BusyChanges.Count;
        int pendingNotifications = observer.PendingCargo.Count;
        oldGate.SetResult(true);
        await oldBaseline;

        Assert.Equal(75, Assert.Single(recovery.FleetCarriers).Cargo["steel"]);
        Assert.Empty(client.FleetCarrierAdjustments);
        Assert.Equal(notifications, observer.FleetCarrierStatuses.Count);
        Assert.Equal(busyNotifications, observer.BusyChanges.Count);
        Assert.Equal(pendingNotifications, observer.PendingCargo.Count);

        newGate.SetResult(true);
        await newBaseline;
        Assert.Equal(-5, Assert.Single(client.FleetCarrierAdjustments).Changes["steel"]);
        Assert.Equal(90, client.LastReplacement?["steel"]);

        async Task<ColonizationFleetCarrier> WaitForRegistrationAsync()
        {
            await oldGate.Task;
            return Carrier(80);
        }
    }

    /// <summary>Only covered transactions at or before a successful market snapshot retire; newer and undated deltas survive.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task QueuedCargoReconcilesPerCommodityAndEventTime(bool failReplacement, bool atSnapshotTime)
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var entered = new ManualResetEventSlim(false);
        RecordingRavenClient client = CarrierClient(75);
        client.GateGetFleetCarrier = gate;
        client.EnteredGetFleetCarrier = entered;
        if (failReplacement)
        {
            client.ReplaceCargo = _ => throw new HttpRequestException("baseline unavailable");
        }
        ColonizationDeliveryRecovery recovery = Create(client, "key", carrierSync: true);
        recovery.ApplyJournalEvents([CarrierDock()], null);
        recovery.UpdateStatus(new EliteStatus { Flags = StatusFlags.InMainShip });
        MarketSnapshot market = LinkedCarrierMarket(80);
        Task baseline = UpdateMarketAsync(recovery, market);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        JournalEventEnvelope oldTransfer = Event(
            "CargoTransfer",
            "\"Transfers\":[{\"Type\":\"Steel\",\"Count\":4,\"Direction\":\"tocarrier\"},{\"Type\":\"Water\",\"Count\":3,\"Direction\":\"tocarrier\"}]"
        ) with
        {
            Timestamp = atSnapshotTime ? market.Timestamp : market.Timestamp.AddSeconds(-1),
        };
        JournalEventEnvelope newBuy = SteelBuy with { Timestamp = market.Timestamp.AddSeconds(1) };
        JournalEventEnvelope undatedSell = Event("MarketSell", "\"MarketID\":42,\"Type\":\"Steel\",\"Count\":2") with
        {
            Timestamp = null,
        };
        await recovery.SynchronizeLiveEventsAsync([oldTransfer, newBuy, undatedSell], true);
        Assert.Empty(client.FleetCarrierAdjustments);

        gate.SetResult(true);
        await baseline;

        Assert.Equal(
            failReplacement ? 1 : -3,
            client.FleetCarrierAdjustments.Sum(call => call.Changes.GetValueOrDefault("steel"))
        );
        Assert.Equal(3, client.FleetCarrierAdjustments.Sum(call => call.Changes.GetValueOrDefault("water")));
        Assert.Empty(store.LoadPendingCargoAdjustments());
    }

    /// <summary>Switching away from a pending operation resets its UI state without waiting for a new baseline.</summary>
    [Fact]
    public async Task ProfileSwitchClearsBusyStateBeforeSupersededBaselineReturns()
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var entered = new ManualResetEventSlim(false);
        RecordingRavenClient client = CarrierClient(75);
        client.GateGetFleetCarrier = gate;
        client.EnteredGetFleetCarrier = entered;
        ColonizationDeliveryRecovery recovery = Create(client, "key", carrierSync: true);
        recovery.ApplyJournalEvents([CarrierDock()], null);
        Task old = UpdateMarketAsync(recovery, LinkedCarrierMarket(80));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(observer.BusyChanges[^1]);

        recovery.SetProfile("F456", true, "replacement-key");
        Assert.False(observer.BusyChanges[^1]);
        Assert.Null(observer.PendingCargo[^1]);
        int busyChanges = observer.BusyChanges.Count;
        gate.SetResult(true);
        await old;
        Assert.Equal(busyChanges, observer.BusyChanges.Count);
    }

    /// <summary>All unsent queued transactions stay durable for their original owner across a switch during relative replay.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProfileSwitchDuringQueuedReplayRetainsLaterWrites(bool failFirstReplay)
    {
        var baselineGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var replayGate = new TaskCompletionSource<IReadOnlyDictionary<string, int>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        using var entered = new ManualResetEventSlim(false);
        using var replayEntered = new ManualResetEventSlim(false);
        RecordingRavenClient client = CarrierClient(75);
        client.GateGetFleetCarrier = baselineGate;
        client.EnteredGetFleetCarrier = entered;
        client.AdjustmentResponseTask = replayGate.Task;
        client.EnteredAdjustment = replayEntered;
        ColonizationDeliveryRecovery recovery = Create(client, "old-key", carrierSync: true);
        recovery.ApplyJournalEvents([CarrierDock()], null);
        recovery.UpdateStatus(new EliteStatus { Flags = StatusFlags.InMainShip });
        MarketSnapshot market = LinkedCarrierMarket(80);
        Task oldBaseline = UpdateMarketAsync(recovery, market);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        JournalEventEnvelope later = Event("MarketSell", "\"MarketID\":42,\"Type\":\"Steel\",\"Count\":2") with
        {
            Timestamp = market.Timestamp.AddSeconds(2),
        };
        await recovery.SynchronizeLiveEventsAsync(
            [SteelBuy with { Timestamp = market.Timestamp.AddSeconds(1) }, later],
            true
        );
        baselineGate.SetResult(true);
        Assert.True(replayEntered.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(2, store.LoadPendingCargoAdjustments().Count);
        recovery.SetProfile("F456", true, "new-key");
        LoadWorkspace(recovery, client);
        var newGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.GateGetFleetCarrier = newGate;
        entered.Reset();
        Task newBaseline = UpdateMarketAsync(recovery, LinkedCarrierMarket(90));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        int notices = observer.FleetCarrierStatuses.Count;
        int pendingChanges = observer.PendingCargo.Count;
        if (failFirstReplay)
        {
            replayGate.SetException(new HttpRequestException("old replay unavailable"));
        }
        else
        {
            replayGate.SetResult(new Dictionary<string, int> { ["steel"] = 75 });
        }
        await oldBaseline;
        Assert.Equal(notices, observer.FleetCarrierStatuses.Count);
        Assert.Equal(pendingChanges, observer.PendingCargo.Count);
        ColonizationPendingCargoAdjustment retained = Assert.Single(
            store.LoadPendingCargoAdjustments(),
            item => item.RecordedAt == later.Timestamp
        );
        Assert.Equal("Test Cmdr|F123|True", retained.Owner);
        Assert.False(retained.Attempted);
        Assert.Equal(2, retained.Delta["steel"]);
        Assert.Single(client.FleetCarrierAdjustments);
        client.AdjustmentResponseTask = null;
        newGate.SetResult(true);
        await newBaseline;
    }
}
