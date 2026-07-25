using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class BiologyPredictionsViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-BiologyPredictions-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task WorkspaceBuildsExactRowsFocusesCurrentBodyAndOpensLinks()
    {
        var settingsPath = Path.Combine(temporaryDirectory, "ui-settings.json");
        var survey = new SystemSurveyViewModel(
            new SystemSurveySettingsStore(settingsPath));
        using var viewModel = new BiologyPredictionsViewModel(
            survey,
            new BiologyPredictionsSettingsStore(settingsPath));

        survey.ApplyUpdate(
        [
            Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42,"StarPos":[0,0,0]}"""),
            Parse("""{"event":"Scan","SystemAddress":42,"BodyName":"Test A","BodyID":0,"StarType":"L","StellarMass":1,"Radius":695700000,"SurfaceTemperature":5000}"""),
            Parse(PredictableAleoidaScan),
            Parse("""{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":1}],"Genuses":[{"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Genus_Localised":"Aleoida"}]}"""),
            Parse("""{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 2","BodyID":2,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":1}],"Genuses":[{"Genus":"$Codex_Ent_Bacterial_Genus_Name;","Genus_Localised":"Bacterium"}]}"""),
            Parse("""{"event":"Disembark","Body":"Test 1","BodyID":1,"OnPlanet":true,"OnStation":false}"""),
        ],
        new EliteStatus
        {
            GuiFocus = GuiFocus.SystemMap,
            BodyName = "Test 1",
        });

        Assert.True(viewModel.HasSystem);
        Assert.Equal("Test", viewModel.SystemName);
        Assert.Equal(42, viewModel.SystemAddress);
        Assert.Equal(2, viewModel.Bodies.Count);
        Assert.StartsWith("Estimated reward:", viewModel.EstimatedReward);
        Assert.NotEqual("Prediction unavailable", viewModel.FirstFootfallEstimate);

        var currentBody = Assert.Single(viewModel.Bodies, body => body.BodyId == 1);
        var prediction = Assert.Single(currentBody.Organisms);
        Assert.Equal("Aleoida Coronamus - Lime", prediction.DisplayName);
        Assert.Equal("150 m sample separation", prediction.SampleDistanceText);
        Assert.True(prediction.IsPrediction);
        Assert.True(currentBody.IsFirstFootfall);

        survey.UpdateCommanderCodexContext(
            new CommanderCodexData(
                "fid",
                "Cmdr Test",
                0,
                null,
                new Dictionary<long, CommanderCodexFirst>()),
            new CommanderCodexData(
                "fid",
                "Cmdr Test",
                18,
                "Inner Orion Spur",
                new Dictionary<long, CommanderCodexFirst>()));
        currentBody = Assert.Single(
            viewModel.Bodies,
            body => body.BodyId == 1);
        prediction = Assert.Single(currentBody.Organisms);
        Assert.True(prediction.IsCommanderFirst);
        Assert.True(prediction.IsHighlightedFirst);

        viewModel.CurrentBodyOnly = true;

        Assert.True(currentBody.IsExpanded);
        Assert.False(Assert.Single(
            viewModel.Bodies,
            body => body.BodyId == 2).IsExpanded);
        Assert.True(new BiologyPredictionsSettingsStore(settingsPath)
            .Load().CurrentBodyOnly);

        viewModel.SelectedRowSize = viewModel.RowSizeOptions[2];

        Assert.Equal(3, viewModel.RowSize);
        Assert.All(
            viewModel.Bodies.SelectMany(body => body.Organisms),
            organism => Assert.Equal(15, organism.RowFontSize));

        viewModel.CollapseAllCommand.Execute(null);
        Assert.All(viewModel.Bodies, body => Assert.False(body.IsExpanded));
        viewModel.ExpandAllCommand.Execute(null);
        Assert.False(viewModel.CurrentBodyOnly);
        Assert.All(viewModel.Bodies, body => Assert.True(body.IsExpanded));

        Uri? launchedUri = null;
        viewModel.SetUriLauncher(uri =>
        {
            launchedUri = uri;
            return Task.FromResult(true);
        });

        Assert.True(await viewModel.OpenCanonnAsync());
        Assert.Contains("system=Test", launchedUri!.AbsoluteUri);
        Assert.True(await viewModel.OpenSpanshAsync());
        Assert.EndsWith("/42", launchedUri!.AbsoluteUri);
        Assert.True(await viewModel.OpenEdsmAsync());
        Assert.Contains("systemID64=42", launchedUri!.AbsoluteUri);
    }

    [Fact]
    public void WindowCommandTracksSystemAndSingleInstanceOpener()
    {
        var settingsPath = Path.Combine(temporaryDirectory, "ui-settings.json");
        var survey = new SystemSurveyViewModel(
            new SystemSurveySettingsStore(settingsPath));
        using var viewModel = new BiologyPredictionsViewModel(
            survey,
            new BiologyPredictionsSettingsStore(settingsPath));
        var opened = false;
        viewModel.SetWindowOpener(() =>
        {
            opened = true;
            return Task.FromResult(true);
        });

        Assert.False(viewModel.OpenWindowCommand.CanExecute(null));

        survey.ApplyUpdate(
        [
            Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42,"StarPos":[0,0,0]}"""),
            Parse("""{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":1}]}"""),
        ],
        new EliteStatus { GuiFocus = GuiFocus.SystemMap });

        Assert.True(viewModel.OpenWindowCommand.CanExecute(null));
        viewModel.OpenWindowCommand.Execute(null);
        Assert.True(SpinWait.SpinUntil(() => opened, TimeSpan.FromSeconds(1)));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
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

    private const string PredictableAleoidaScan = """
        {
          "event":"Scan",
          "ScanType":"Detailed",
          "StarSystem":"Test",
          "SystemAddress":42,
          "BodyName":"Test 1",
          "BodyID":1,
          "Parents":[{"Star":0}],
          "DistanceFromArrivalLS":500,
          "PlanetClass":"Rocky body",
          "Atmosphere":"thin carbon dioxide atmosphere",
          "AtmosphereType":"CarbonDioxide",
          "AtmosphereComposition":[
            {"Name":"CarbonDioxide","Percent":100}
          ],
          "Volcanism":"",
          "MassEM":0.1,
          "Radius":6000000,
          "SurfaceGravity":2,
          "SurfaceTemperature":185,
          "SurfacePressure":3000,
          "SemiMajorAxis":100000,
          "Landable":true,
          "WasFootfalled":false,
          "Materials":[{"Name":"Iron","Percent":20}]
        }
        """;
}
