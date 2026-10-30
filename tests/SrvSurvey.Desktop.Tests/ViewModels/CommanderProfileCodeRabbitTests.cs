using SrvSurvey.Core.Frontier;
using SrvSurvey.Desktop.Platform.Frontier;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class CommanderProfileViewModelTests
{
    [Fact]
    public async Task CodeRabbitProjectedModulesResolveTheirOwnLegacyMetadata()
    {
        DateTimeOffset at = DateTimeOffset.UnixEpoch;
        FrontierAccountSnapshot baseline = CreateSnapshot(at);
        FrontierShipSnapshot old = baseline.CurrentShip! with
        {
            DataPoints = [new("ship.modules.mainengines.module.name", "int_engine_size2_class1")],
        };
        FrontierShipSnapshot stored = old with
        {
            Id = 8,
            Type = "Python",
            Modules = [new("Main Engines", 1, "Legacy thrusters", "", 1, false, 100, true, 1, "", "", null, [])],
            DataPoints = [new("ship.modules.mainengines.module.name", "int_engine_size5_class5")],
        };
        FrontierAccountSnapshot cached = baseline with { CurrentShip = old, Ships = [old, stored] };
        using var model = new CommanderProfileViewModel(new StubAccountService(new(true, cached, at)), () => at);
        await model.SetCommanderContextAsync("F123", "Fenris", true);
        model.UpdateJournalState("F123", [ReconciliationEvents(1)[3]]);
        Assert.Equal("5A", Assert.Single(model.CurrentShipModules).ClassRating);
    }

    [Fact]
    public async Task CodeRabbitTimestamplessCarrierEventsCannotSupersedeObservedJumps()
    {
        DateTimeOffset at = DateTimeOffset.UnixEpoch;
        FrontierAccountSnapshot cached = CreateSnapshot(at) with { CarrierFetchedAt = at };
        using var model = new CommanderProfileViewModel(
            new StubAccountService(new(true, cached, at)),
            () => at.AddDays(1)
        );
        await model.OpenAsync();
        model.UpdateJournalCarrierJump("Fenris", [ReconciliationEvents(1)[2]]);
        model.UpdateJournalCarrierJump(
            "Fenris",
            [ParseJournalEvent("""{"event":"CarrierJumpRequest","CarrierID":111,"SystemName":"Unobserved"}""")]
        );
        Assert.Equal("Achenar", model.CarrierOperations.Single(row => row.Label == "Current jump").Value);
        model.UpdateJournalCarrierJump(
            "Fenris",
            [
                ParseJournalEvent(
                    """{"timestamp":"1970-01-01T00:00:02Z","event":"CarrierJumpCancelled","CarrierID":111}"""
                ),
            ]
        );
        Assert.Equal("None plotted", model.CarrierOperations.Single(row => row.Label == "Current jump").Value);
    }

    [Fact]
    public async Task CodeRabbitUnknownJournalShipIdHasOnePendingFleetRowUntilCapiIdentifiesIt()
    {
        DateTimeOffset at = DateTimeOffset.UnixEpoch;
        FrontierAccountSnapshot cached = CreateSnapshot(at);
        FrontierShipSnapshot known = cached.CurrentShip! with { Id = 8, Type = "Anaconda", Name = "New Anaconda" };
        FrontierAccountSnapshot fresh = cached with
        {
            FetchedAt = at.AddSeconds(2),
            CurrentShip = known,
            Ships = [cached.CurrentShip, known],
        };
        using var model = new CommanderProfileViewModel(new StubAccountService(new(true, cached, at), fresh), () => at);
        await model.SetCommanderContextAsync("F123", "Fenris", true);
        model.UpdateJournalState(
            "F123",
            [ParseJournalEvent("""{"timestamp":"1970-01-01T00:00:01Z","event":"ShipyardBuy","ShipType":"anaconda"}""")]
        );
        Assert.Null(model.CurrentShip!.Id);
        Assert.Equal("Anaconda", Assert.Single(model.Ships, row => row.Status == "CURRENT").Name);
        Assert.Equal("2 ships", model.FleetCount);
        await model.RefreshAsync();
        Assert.Equal(8, model.CurrentShip.Id);
        Assert.Equal("New Anaconda", Assert.Single(model.Ships, row => row.Status == "CURRENT").Name);
        Assert.Equal("2 ships", model.FleetCount);
    }
}
