using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed partial class ColonizationDeliveryRecoveryTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task JournalTransfersReconcileLegacyAliasesWithoutADuplicateInventoryOrSeparateCleanupWrite(
        bool mixedKeys,
        bool toCarrier
    )
    {
        RecordingRavenClient client = AliasedCarrierClient(mixedKeys);
        ColonizationDeliveryRecovery recovery = Create(client, apiKey: "key", carrierSync: true);
        recovery.ApplyJournalEvents([CarrierDock()], null);
        recovery.UpdateStatus(new EliteStatus { Flags = StatusFlags.InMainShip });

        await recovery.SynchronizeLiveEventsAsync([OxygenTransfer(toCarrier)], allowPublishing: true);

        FleetCarrierAdjustmentCall write = Assert.Single(client.FleetCarrierAdjustments);
        Assert.Equal(toCarrier ? 10 : -10, write.Changes["liquid oxygen"]);
        Assert.Equal(mixedKeys, write.Changes.ContainsKey("liquidoxygen"));
        if (mixedKeys)
        {
            Assert.Equal(toCarrier ? 10 : -10, write.Changes["liquidoxygen"]);
            Assert.Equal(toCarrier ? 10 : -10, write.Changes["LIQUIDOXYGEN"]);
        }
        ColonizationFleetCarrier updated = Assert.Single(recovery.FleetCarriers);
        Assert.Equal(toCarrier ? 110 : 90, ColonizationCarrierInventory.NormalizeStock(updated.Cargo)["liquidoxygen"]);
        Assert.Equal(20, updated.Cargo["steel"]);
        Assert.Equal(0, client.ReplaceCargoCount);
        Assert.Empty(store.LoadPendingCargoAdjustments());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetainedUnattemptedTransfersUseTheFreshRavenSourceKeysOnReplay(bool mixedKeys)
    {
        RecordingRavenClient client = AliasedCarrierClient(mixedKeys);
        store.SavePendingCargoAdjustments([
            new(
                "Test Cmdr|F123|True",
                42,
                new() { ["liquidoxygen"] = -10 },
                now,
                null,
                Attempted: false,
                OutcomeUnknown: false
            ),
        ]);
        ColonizationDeliveryRecovery recovery = Create(client, apiKey: "key", carrierSync: true);

        await recovery.FlushFleetCarrierCargoAsync();
        await recovery.FlushFleetCarrierCargoAsync();

        Assert.Single(client.FleetCarrierAdjustments);
        Assert.Equal(
            90,
            ColonizationCarrierInventory.NormalizeStock(Assert.Single(recovery.FleetCarriers).Cargo)["liquidoxygen"]
        );
        Assert.Empty(store.LoadPendingCargoAdjustments());
    }

    [Fact]
    public async Task LostAliasAdjustmentResponseRetainsTheRawBaselineAndDoesNotApplyTheTransferTwice()
    {
        RecordingRavenClient client = AliasedCarrierClient(mixedKeys: true);
        client.AdjustmentFailures.Enqueue(new HttpRequestException("response lost"));
        ColonizationDeliveryRecovery recovery = Create(client, apiKey: "key", carrierSync: true);
        recovery.ApplyJournalEvents([CarrierDock()], null);
        recovery.UpdateStatus(new EliteStatus { Flags = StatusFlags.InMainShip });
        await recovery.SynchronizeLiveEventsAsync([OxygenTransfer(toCarrier: false)], allowPublishing: true);
        ColonizationPendingCargoAdjustment pending = Assert.Single(store.LoadPendingCargoAdjustments());
        Assert.True(pending.OutcomeUnknown);
        Assert.Equal(-10, Assert.Single(pending.Delta).Value);
        Assert.Equal(100, pending.Before!["liquid oxygen"]);
        Assert.Equal(80, pending.Before["LIQUIDOXYGEN"]);
        Assert.Equal(60, pending.Before["liquidoxygen"]);
        client.FleetCarrierResponse = client.FleetCarrierResponse! with
        {
            Cargo = new()
            {
                ["liquid oxygen"] = 90,
                ["liquidoxygen"] = 50,
                ["LIQUIDOXYGEN"] = 70,
                ["steel"] = 20,
            },
        };

        ColonizationDeliveryRecovery restarted = Create(client, apiKey: "key", carrierSync: true);
        await restarted.FlushFleetCarrierCargoAsync();

        Assert.Single(client.FleetCarrierAdjustments);
        Assert.Empty(store.LoadPendingCargoAdjustments());
        Assert.Equal(
            90,
            ColonizationCarrierInventory.NormalizeStock(Assert.Single(restarted.FleetCarriers).Cargo)["liquidoxygen"]
        );
    }

    [Fact]
    public async Task RelativeAliasWritesPreserveCargoAddedByAnotherClientAfterTheCachedBaseline()
    {
        RecordingRavenClient client = AliasedCarrierClient(mixedKeys: true);
        ColonizationDeliveryRecovery recovery = Create(client, apiKey: "key", carrierSync: true);
        recovery.ApplyJournalEvents([CarrierDock()], null);
        recovery.UpdateStatus(new EliteStatus { Flags = StatusFlags.InMainShip });
        client.FleetCarrierResponse = client.FleetCarrierResponse! with
        {
            Cargo = new()
            {
                ["liquid oxygen"] = 110,
                ["liquidoxygen"] = 60,
                ["LIQUIDOXYGEN"] = 80,
                ["steel"] = 20,
            },
        };

        await recovery.SynchronizeLiveEventsAsync([OxygenTransfer(toCarrier: false)], allowPublishing: true);

        Assert.Single(client.FleetCarrierAdjustments);
        Assert.Equal(
            100,
            ColonizationCarrierInventory.NormalizeStock(Assert.Single(recovery.FleetCarriers).Cargo)["liquidoxygen"]
        );
        Assert.Empty(store.LoadPendingCargoAdjustments());
    }

    private static RecordingRavenClient AliasedCarrierClient(bool mixedKeys)
    {
        var carrier = new ColonizationFleetCarrier
        {
            MarketId = 42,
            Name = "ABC-123",
            Cargo = new() { ["liquid oxygen"] = 100, ["steel"] = 20 },
        };
        if (mixedKeys)
        {
            carrier.Cargo["liquidoxygen"] = 60;
            carrier.Cargo["LIQUIDOXYGEN"] = 80;
        }
        return new RecordingRavenClient { Workspace = new([], [], null, [carrier]), FleetCarrierResponse = carrier };
    }

    private static JournalEventEnvelope OxygenTransfer(bool toCarrier) =>
        Event(
            "CargoTransfer",
            $$"""
            "Transfers":[{"Type":"$LiquidOxygen_Name;","Count":10,"Direction":"{{(
                toCarrier ? "tocarrier" : "toship"
            )}}"}]
            """
        );
}
