using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class ColonizationViewModelTests
{
    /// <summary>A discarded workspace recovers its personal or squadron cargo from the durable outbox.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CapturedCargoReopensFromDiskWithoutHavingSentAnHttpRequest(bool squadron)
    {
        StubRavenColonialClient client = CarrierClient();
        using (ColonizationViewModel vm = await CreateRecoveryAsync(client))
        {
            vm.ApplyJournalEvents([CarrierDock(squadron)]);
            vm.UpdateStatus(new EliteStatus { Flags = StatusFlags.InMainShip });
            JournalEventEnvelope transfer = Event(
                "CargoTransfer",
                "\"Transfers\":[{\"Type\":\"steel\",\"Count\":5,\"Direction\":\"tocarrier\"}]"
            );
            CargoInventoryState? cargo = null;
            if (squadron)
            {
                cargo = new CargoInventoryState();
                cargo.Apply(Event("Cargo", "\"Inventory\":[{\"Name\":\"steel\",\"Count\":50}]"));
                cargo.GetDiff();
                vm.PrepareSquadronCargoTransferSnapshot(cargo);
                cargo.Apply(transfer);
            }
            IReadOnlyList<ColonizationPendingCargoAdjustment>? captured = vm.CaptureLiveCarrierCargo(
                [transfer],
                "Test Cmdr",
                "F123",
                true,
                cargo,
                true,
                true
            );
            Assert.Single(captured!);
            Assert.Empty(client.FleetCarrierAdjustments);
            var disk = new ColonizationSettingsStore(Path.Combine(directory, "recovery.json"));
            Assert.False(Assert.Single(disk.LoadPendingCargoAdjustments()).Attempted);
            // Discard the workspace without a shutdown drain, as when the process stops unexpectedly.
        }

        using ColonizationViewModel restarted = await CreateRecoveryAsync(client);
        await restarted.FlushFleetCarrierCargoAsync(CancellationToken.None);
        await restarted.FlushFleetCarrierCargoAsync(CancellationToken.None);

        Assert.Equal(5, Assert.Single(client.FleetCarrierAdjustments).Changes["steel"]);
        Assert.Empty(
            new ColonizationSettingsStore(Path.Combine(directory, "recovery.json")).LoadPendingCargoAdjustments()
        );
    }

    /// <summary>Early capture rejects a mismatched commander, Frontier identity, or galaxy without persisting cargo.</summary>
    [Theory]
    [InlineData("Someone Else", "F123", true)]
    [InlineData("Test Cmdr", "F456", true)]
    [InlineData("Test Cmdr", "F123", false)]
    public async Task EarlyCargoCaptureCannotAdoptAnotherProfile(string commander, string frontierId, bool odyssey)
    {
        StubRavenColonialClient client = CarrierClient();
        using ColonizationViewModel vm = await CreateRecoveryAsync(client);
        vm.ApplyJournalEvents([CarrierDock()]);

        Assert.Null(
            vm.CaptureLiveCarrierCargo(
                [Event("MarketBuy", "\"MarketID\":42,\"Type\":\"steel\",\"Count\":5")],
                commander,
                frontierId,
                odyssey,
                null,
                true,
                true
            )
        );

        Assert.Empty(client.FleetCarrierAdjustments);
        Assert.Empty(
            new ColonizationSettingsStore(Path.Combine(directory, "recovery.json")).LoadPendingCargoAdjustments()
        );
    }
}
