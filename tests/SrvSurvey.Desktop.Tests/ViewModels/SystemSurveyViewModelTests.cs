using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class SystemSurveyViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-SystemSurveyViewModel-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void FssOverlayUsesLegacyModesAndForcedToggleSemantics()
    {
        var viewModel = CreateViewModel();
        viewModel.ApplyUpdate(
            [
                Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42}"""),
            ],
            new EliteStatus { GuiFocus = GuiFocus.Fss });

        Assert.True(viewModel.ShouldShowFssInfo);
        Assert.True(viewModel.ToggleFssInfoVisibility());
        Assert.False(viewModel.ShouldShowFssInfo);

        viewModel.ApplyUpdate([], new EliteStatus { GuiFocus = GuiFocus.NoFocus });
        Assert.False(viewModel.ShouldShowFssInfo);
        Assert.True(viewModel.ToggleFssInfoVisibility());
        Assert.True(viewModel.ShouldShowFssInfo);
        Assert.True(viewModel.IsFssInfoForced);

        viewModel.ApplyUpdate(
            [Parse("""{"event":"StartJump","JumpType":"Hyperspace"}""")],
            null);
        Assert.False(viewModel.ShouldShowFssInfo);
    }

    [Fact]
    public void SystemStatusRequiresHonkAndSupportedFlightMode()
    {
        var viewModel = CreateViewModel();
        var supercruise = new EliteStatus
        {
            Flags = StatusFlags.Supercruise | StatusFlags.InMainShip,
        };
        viewModel.ApplyUpdate(
            [Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42}""")],
            supercruise);
        Assert.False(viewModel.ShouldShowSystemStatus);

        viewModel.ApplyUpdate(
            [Parse("""{"event":"FSSDiscoveryScan","SystemAddress":42,"BodyCount":3}""")],
            null);
        Assert.True(viewModel.ShouldShowSystemStatus);
        Assert.Equal("FSS 0% complete", viewModel.SystemStatusText);

        viewModel.ApplyUpdate([], new EliteStatus { Flags2 = StatusFlags2.InTaxi });
        Assert.False(viewModel.ShouldShowSystemStatus);
    }

    [Fact]
    public void DisplayFiltersBodiesAndBuildsDssAndSignalProgress()
    {
        var viewModel = CreateViewModel();
        viewModel.ApplyUpdate(
            [
                Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42}"""),
                Parse("""{"event":"FSSDiscoveryScan","SystemAddress":42,"BodyCount":3,"NonBodyCount":1}"""),
                Parse(TerraformableScan),
                Parse("""{"event":"Scan","ScanType":"Detailed","SystemAddress":42,"BodyName":"Test 2","BodyID":2,"PlanetClass":"Rocky body","MassEM":0.01,"DistanceFromArrivalLS":50,"WasDiscovered":true,"WasMapped":true}"""),
                Parse("""{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 2","BodyID":2,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":2},{"Type":"$SAA_SignalType_Geological;","Count":1}]}"""),
                Parse("""{"event":"ScanOrganic","ScanType":"Analyse","SystemAddress":42,"Body":2,"Genus":"$Genus_A;"}"""),
                Parse("""{"event":"Scan","ScanType":"Detailed","SystemAddress":42,"BodyName":"Test 3","BodyID":3,"PlanetClass":"Sudarsky class II gas giant","MassEM":10,"WasDiscovered":true,"WasMapped":true}"""),
            ],
            new EliteStatus
            {
                GuiFocus = GuiFocus.Fss,
                Destination = new StatusDestination
                {
                    System = 42,
                    Body = 1,
                    Name = "Test 1",
                },
            });

        Assert.Equal(3, viewModel.FssBodies.Count);
        var terraformable = Assert.Single(
            viewModel.FssBodies,
            body => body.Name.Contains('1'));
        Assert.Contains("TERRAFORMABLE", terraformable.Markers);
        Assert.True(terraformable.IsDssCandidate);

        var signalBody = Assert.Single(
            viewModel.FssBodies,
            body => body.Name.Contains('2'));
        Assert.Equal(2, signalBody.BiologicalSignalCount);
        Assert.Equal(1, signalBody.AnalyzedBiologicalSignalCount);
        Assert.Equal(1, signalBody.GeologicalSignalCount);

        var dssBody = Assert.Single(viewModel.DssBodies);
        Assert.Equal("1", dssBody.Name);
        Assert.True(dssBody.IsDestination);
        Assert.Equal("1 biological signal remaining", viewModel.BiologicalHeading);
        Assert.Equal("2", Assert.Single(viewModel.BiologicalBodies).Name);
    }

    [Fact]
    public void SettingsImmediatelyRecalculateFilteredRows()
    {
        var viewModel = CreateViewModel();
        viewModel.ApplyUpdate(
            [
                Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42}"""),
                Parse("""{"event":"Scan","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"PlanetClass":"Rocky body","MassEM":0.01,"WasDiscovered":true,"WasMapped":true}"""),
            ],
            new EliteStatus { GuiFocus = GuiFocus.Fss });
        Assert.Empty(viewModel.FssBodies);

        viewModel.FssBodyValueFloor = 0;

        Assert.Single(viewModel.FssBodies);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private SystemSurveyViewModel CreateViewModel()
    {
        return new SystemSurveyViewModel(new SystemSurveySettingsStore(
            Path.Combine(temporaryDirectory, "ui-settings.json")));
    }

    private static JournalEventEnvelope Parse(string json)
    {
        var success = JournalEventEnvelope.TryParse(
            json,
            out var journalEvent,
            out var error);
        Assert.True(success, error);
        return Assert.IsType<JournalEventEnvelope>(journalEvent);
    }

    private const string TerraformableScan = """
        {
          "event":"Scan",
          "ScanType":"Detailed",
          "SystemAddress":42,
          "BodyName":"Test 1",
          "BodyID":1,
          "TerraformState":"Terraformable",
          "PlanetClass":"High metal content body",
          "MassEM":1.2,
          "DistanceFromArrivalLS":100,
          "Landable":true,
          "WasDiscovered":false,
          "WasMapped":false
        }
        """;
}
