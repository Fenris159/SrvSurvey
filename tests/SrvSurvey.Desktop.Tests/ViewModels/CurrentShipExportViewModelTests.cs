using System.Text.Json;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class CurrentShipExportViewModelTests
{
    /// <summary>
    /// Checks that journal projection populates overview export on startup and disables it after refitting.
    /// </summary>
    [Fact]
    public async Task JournalProjectionPopulatesOverviewExportOnStartupAndDisablesItAfterRefitting()
    {
        string root = Path.Combine(Path.GetTempPath(), $"SrvSurvey-slef-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string journal = Path.Combine(root, "Journal.2026-10-13T120000.01.log");
            await File.WriteAllTextAsync(
                journal,
                """
                {"event":"Fileheader","Odyssey":true}
                {"event":"LoadGame","FID":"F1","Commander":"Export Test","Ship":"LakonMiner","ShipID":12}
                {"event":"Loadout","Ship":"LakonMiner","ShipID":12,"ShipName":"Surveyor","Modules":[{"Slot":"FighterBay01","Item":"Int_MkIILargeBuggyBay_Size4_Class3"}]}

                """
            );
            using MainWindowViewModel viewModel = MainWindowViewModelTestBuilder.Create(root);
            await viewModel.RefreshAsync();
            Assert.True(viewModel.CurrentShipExport.CanExport);
            Assert.Equal("Surveyor · Type-11 Prospector", viewModel.CurrentShipExport.ShipDescription);
            await File.AppendAllTextAsync(
                journal,
                """{"event":"ModuleBuy","Slot":"Slot01_Size6","BuyItem":"Int_CargoRack_Size6_Class1"}"""
                    + Environment.NewLine
            );
            await viewModel.RefreshAsync();
            Assert.False(viewModel.CurrentShipExport.CanExport);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// Checks that copy offers the piloted journal ship and retains the same export between unchanged ticks.
    /// </summary>
    [Fact]
    public void CopyOffersThePilotedJournalShipAndRetainsTheSameExportBetweenUnchangedTicks()
    {
        JournalSessionState session = CreateSession();
        var viewModel = new CurrentShipExportViewModel();
        var status = new EliteStatus { Flags = StatusFlags.InMainShip };
        viewModel.Update(session, status, false);
        Assert.True(viewModel.CanExport);
        Assert.Equal("Surveyor · Type-11 Prospector", viewModel.ShipDescription);
        Assert.Contains("EDSY", viewModel.Status);
        string export = viewModel.SlefJson;
        viewModel.Update(session, status, false);
        Assert.Same(export, viewModel.SlefJson);
        using var document = JsonDocument.Parse(export);
        Assert.Equal(12, document.RootElement[0].GetProperty("data").GetProperty("ShipID").GetInt64());
        session.Apply(Parse("""{"event":"ModuleBuy"}"""));
        viewModel.Update(session, status, false);
        Assert.False(viewModel.CanExport);
        Assert.Empty(viewModel.SlefJson);
        Assert.Contains("Waiting", viewModel.ShipDescription);
    }

    /// <summary>
    /// Checks that export is disabled outside the identified commanders current ship.
    /// </summary>
    [Theory]
    [InlineData("Shutdown", false, StatusFlags.InMainShip)]
    [InlineData("Fileheader", false, StatusFlags.InMainShip)]
    [InlineData("JoinACrew", false, StatusFlags.InMainShip)]
    [InlineData("Scan", true, StatusFlags.InMainShip)]
    [InlineData("Scan", false, StatusFlags.InSrv)]
    [InlineData("Scan", false, StatusFlags.InFighter)]
    public void ExportIsDisabledOutsideTheIdentifiedCommandersCurrentShip(
        string eventName,
        bool awaitingCommander,
        StatusFlags flags
    )
    {
        JournalSessionState session = CreateSession();
        var viewModel = new CurrentShipExportViewModel();
        session.Apply(Parse($$"""{"event":"{{eventName}}"}"""));
        viewModel.Update(session, new EliteStatus { Flags = flags }, awaitingCommander);
        Assert.False(viewModel.CanExport);
        Assert.Empty(viewModel.SlefJson);
        Assert.Contains("Loadout", viewModel.Status);
    }

    /// <summary>
    /// Checks that unknown ships are reported and empty names and missing status are supported.
    /// </summary>
    [Fact]
    public void UnknownShipsAreReportedAndEmptyNamesAndMissingStatusAreSupported()
    {
        JournalSessionState session = CreateSession();
        var viewModel = new CurrentShipExportViewModel();
        session.Apply(
            Parse(
                """{"event":"Loadout","Ship":"future_ship","ShipID":12,"Modules":[{"Slot":"PowerPlant","Item":"Int_PowerPlant_Size3_Class5"}]}"""
            )
        );
        viewModel.Update(session, null, false);
        Assert.Equal("Unnamed ship · future_ship", viewModel.ShipDescription);
        Assert.False(viewModel.CanExport);
        session.Apply(
            Parse(
                """{"event":"Loadout","Ship":"LakonMiner","ShipID":12,"Modules":[{"Slot":"PowerPlant","Item":"Int_PowerPlant_Size3_Class5"}]}"""
            )
        );
        viewModel.Update(session, null, false);
        Assert.True(viewModel.CanExport);
        Assert.Equal("Unnamed ship · Type-11 Prospector", viewModel.ShipDescription);
    }

    /// <summary>
    /// Builds an identified commander and complete ship loadout for export tests.
    /// </summary>
    private static JournalSessionState CreateSession()
    {
        var session = new JournalSessionState();
        session.Apply(Parse("""{"event":"LoadGame","FID":"F1","Ship":"LakonMiner","ShipID":12}"""));
        session.Apply(
            Parse(
                """{"event":"Loadout","Ship":"LakonMiner","ShipID":12,"ShipName":"Surveyor","Modules":[{"Slot":"FighterBay01","Item":"Int_MkIILargeBuggyBay_Size4_Class3"}]}"""
            )
        );
        return session;
    }

    /// <summary>
    /// Parses a journal fixture and includes parser diagnostics in any test failure.
    /// </summary>
    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? entry, out string? error), error);
        return entry!;
    }
}
