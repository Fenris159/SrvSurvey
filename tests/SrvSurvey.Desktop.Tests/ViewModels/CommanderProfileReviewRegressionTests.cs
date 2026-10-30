using SrvSurvey.Core.Frontier;
using SrvSurvey.Desktop.Platform.Frontier;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class CommanderProfileViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReviewRegressionUncachedCommanderCannotKeepThePreviousJournalShip(bool linked)
    {
        DateTimeOffset start = DateTimeOffset.UnixEpoch;
        FrontierAccountSnapshot cached = CreateSnapshot(start);
        var account = new StubAccountService(new(true, cached, start));
        account.SetStateForCommander("F123", new(true, cached, start));
        account.SetStateForCommander("F456", new(linked, null, null));
        account.PendingRefresh = Task.FromException<FrontierAccountSnapshot>(new HttpRequestException("Offline"));
        using var model = new CommanderProfileViewModel(account, () => start);
        await model.SetCommanderContextAsync("F123", "Fenris", true);
        model.UpdateJournalState(
            "F123",
            [
                ParseJournalEvent(
                    """
                    {"timestamp":"1970-01-01T00:00:01Z","event":"Loadout","Ship":"python","ShipID":8,"Modules":[{"Slot":"MainEngines","Item":"int_engine_size5_class5"}]}
                    """
                ),
            ]
        );
        Assert.Single(model.CurrentShipModules);
        await model.SelectCommanderAsync(FrontierCommanderSelectionOption.Available("F456", "Second"));
        Assert.Null(model.Snapshot);
        Assert.False(model.IsViewingJournalCommander);
        Assert.Null(model.CurrentShip);
        Assert.Empty(model.CurrentShipModules);
        Assert.Empty(model.CurrentShipModuleGroups);
        Assert.Equal("—", model.CurrentShipDescription);
        await model.SelectCommanderAsync(model.CommanderSelectionOptions.Single(option => option.IsAutomatic));
        Assert.Equal(8, model.CurrentShip!.Id);
        Assert.Single(model.CurrentShipModules);
    }

    [Fact]
    public async Task ReviewRegressionDetectedCommanderChangeInvalidatesAManuallySelectedShipsRows()
    {
        DateTimeOffset start = DateTimeOffset.UnixEpoch;
        FrontierAccountSnapshot cached = CreateSnapshot(start);
        var account = new StubAccountService(new(true, cached, start));
        account.SetStateForCommander("F123", new(true, cached, start));
        using var model = new CommanderProfileViewModel(account, () => start);
        await model.SetCommanderContextAsync("F123", "Fenris", true);
        model.UpdateJournalState(
            "F123",
            [
                ParseJournalEvent(
                    """
                    {"timestamp":"1970-01-01T00:00:01Z","event":"Loadout","Ship":"python","ShipID":8,"Modules":[{"Slot":"MainEngines","Item":"int_engine_size5_class5"}]}
                    """
                ),
            ]
        );
        Assert.Single(model.CurrentShipModules);
        Assert.Equal("Python", Assert.Single(model.Ships, ship => ship.Status == "CURRENT").Name);
        await model.SelectCommanderAsync(FrontierCommanderSelectionOption.Available("F123", "Fenris"));
        await model.SetCommanderContextAsync("F456", "Second", false);
        Assert.False(model.IsViewingJournalCommander);
        Assert.Equal(7, model.CurrentShip!.Id);
        Assert.Empty(model.CurrentShipModules);
        Assert.Empty(model.CurrentShipModuleGroups);
        Assert.Equal("Surveyor", Assert.Single(model.Ships, ship => ship.Status == "CURRENT").Name);
    }

    [Fact]
    public async Task ReviewRegressionSameCommanderRestartCanResumeTheOwnShipAfterCrew()
    {
        DateTimeOffset start = DateTimeOffset.UnixEpoch;
        FrontierAccountSnapshot cached = CreateSnapshot(start);
        using var model = new CommanderProfileViewModel(new StubAccountService(new(true, cached, start)), () => start);
        await model.SetCommanderContextAsync("F123", "Fenris", true);
        model.UpdateJournalState(
            "F123",
            [
                ReconciliationEvents(1)[3],
                ParseJournalEvent("""{"timestamp":"1970-01-01T00:00:02Z","event":"JoinACrew"}"""),
            ]
        );
        model.UpdateJournalState(
            "F123",
            [
                ParseJournalEvent("""{"timestamp":"1970-01-01T00:00:03Z","event":"Fileheader"}"""),
                ParseJournalEvent(
                    """{"timestamp":"1970-01-01T00:00:04Z","event":"Commander","Name":"Fenris","FID":"F123"}"""
                ),
                ParseJournalEvent(
                    """{"timestamp":"1970-01-01T00:00:05Z","event":"LoadGame","Commander":"Fenris","FID":"F123","Ship":"python","ShipID":7}"""
                ),
                ParseJournalEvent(
                    """{"timestamp":"1970-01-01T00:00:06Z","event":"Loadout","Ship":"python","ShipID":7,"ShipName":"Returned ship","Modules":[{"Slot":"MainEngines","Item":"int_engine_size5_class5"}]}"""
                ),
            ],
            "Fenris"
        );
        Assert.Equal(7, model.CurrentShip!.Id);
        Assert.Contains("Returned ship", model.CurrentShipDescription);
        Assert.Single(model.CurrentShipModules);
    }
}
