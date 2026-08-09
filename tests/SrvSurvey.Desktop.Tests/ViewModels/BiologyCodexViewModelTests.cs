using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class BiologyCodexViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-BiologyCodex-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task BuildsExactPredictionAndPreservesLegacyDiscoveryStatesAndLinks()
    {
        var survey = CreateSurvey();
        using var viewModel = new BiologyCodexViewModel(
            survey,
            ExobiologyReferenceCatalog.LoadEmbedded(),
            BiologyCriteriaCatalog.LoadEmbedded(),
            () => "Cmdr Test");
        survey.ApplyUpdate(
        [
            Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42,"StarPos":[0,0,0]}"""),
            Parse("""{"event":"Scan","SystemAddress":42,"BodyName":"Test A","BodyID":0,"StarType":"L","StellarMass":1,"Radius":695700000,"SurfaceTemperature":5000}"""),
            Parse(PredictableAleoidaScan),
            Parse("""{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":1}],"Genuses":[{"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Genus_Localised":"Aleoida"}]}"""),
        ],
        new EliteStatus
        {
            BodyName = "Test 1",
            Flags2 = StatusFlags2.OnFoot,
            Temperature = 1_000,
        });

        Assert.True(viewModel.HasSystem);
        Assert.Equal("Test", viewModel.SystemName);
        Assert.Equal("Test 1", viewModel.SelectedBody!.Name);
        var organism = Assert.IsType<BiologyCodexOrganismViewModel>(
            viewModel.SelectedOrganism);
        Assert.Equal(2310206, organism.EntryId);
        Assert.Equal("Aleoida Coronamus - Lime", organism.DisplayName);
        Assert.Equal(BiologyCodexDiscoveryStatus.Predicted, organism.Status);
        Assert.Equal("150 m minimum sample separation", organism.SampleDistanceText);
        Assert.Equal("6,284,600 CR base reward", organism.RewardText);
        Assert.Contains("K", organism.TemperatureRangeText);
        Assert.Contains("too hot", organism.TemperatureWarningText);
        Assert.True(organism.HasImage);
        Assert.Equal("Reference image by CMDR Malleus", organism.ImageCreditText);

        Uri? launchedUri = null;
        viewModel.SetUriLauncher(uri =>
        {
            launchedUri = uri;
            return Task.FromResult(true);
        });

        Assert.True(await viewModel.OpenSubmitImageAsync());
        Assert.Contains("Cmdr%20Test", launchedUri!.AbsoluteUri);
        Assert.Contains("2310206", launchedUri.AbsoluteUri);
        Assert.True(await viewModel.OpenCanonnRegionsAsync());
        Assert.Contains("entryid=2310206", launchedUri.AbsoluteUri);
        Assert.True(await viewModel.OpenBioforgeAsync());
        Assert.Contains("Aleoida%20Coronamus", launchedUri.AbsoluteUri);
        Assert.True(await viewModel.OpenCanonnSignalsAsync());
        Assert.Contains("system=Test", launchedUri.AbsoluteUri);
        Assert.True(await viewModel.OpenSpanshAsync());
        Assert.EndsWith("/42", launchedUri.AbsoluteUri);

        survey.ApplyUpdate(
        [
            Parse("""{"event":"CodexEntry","SystemAddress":42,"BodyID":1,"EntryID":2310206,"Name_Localised":"Aleoida Coronamus - Lime","SubCategory":"$Codex_SubCategory_Organic_Structures;","Latitude":1,"Longitude":2}"""),
        ],
        null);
        Assert.Equal(
            BiologyCodexDiscoveryStatus.Reported,
            viewModel.SelectedOrganism!.Status);

        survey.ApplyUpdate(
        [
            Parse(OrganicLog),
        ],
        null);
        Assert.Equal(
            BiologyCodexDiscoveryStatus.Confirmed,
            viewModel.SelectedOrganism!.Status);

        survey.ApplyUpdate(
        [
            Parse(OrganicAnalyse),
        ],
        null);
        Assert.Equal(
            BiologyCodexDiscoveryStatus.Analyzed,
            viewModel.SelectedOrganism!.Status);
    }

    [Fact]
    public void NavigationWrapsBodiesAndEntriesAndWindowAvailability()
    {
        var survey = CreateSurvey();
        using var viewModel = new BiologyCodexViewModel(
            survey,
            ExobiologyReferenceCatalog.LoadEmbedded(),
            BiologyCriteriaCatalog.LoadEmbedded());
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
            Parse("""{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 2","BodyID":2,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":1}]}"""),
            Parse("""{"event":"CodexEntry","SystemAddress":42,"BodyID":1,"EntryID":2310101,"Name_Localised":"Aleoida Arcus - Yellow","SubCategory":"$Codex_SubCategory_Organic_Structures;","Latitude":1,"Longitude":2}"""),
            Parse("""{"event":"CodexEntry","SystemAddress":42,"BodyID":1,"EntryID":2320101,"Name_Localised":"Bacterium Aurasus - Teal","SubCategory":"$Codex_SubCategory_Organic_Structures;","Latitude":1.1,"Longitude":2.1}"""),
        ],
        null);

        Assert.True(viewModel.OpenWindowCommand.CanExecute(null));
        viewModel.OpenWindowCommand.Execute(null);
        Assert.True(SpinWait.SpinUntil(() => opened, TimeSpan.FromSeconds(1)));
        Assert.Equal(2, viewModel.Bodies.Count);
        Assert.Equal(2, viewModel.SelectedBody!.Organisms.Count);
        var firstEntry = viewModel.SelectedOrganism;
        viewModel.PreviousOrganismCommand.Execute(null);
        Assert.NotEqual(firstEntry, viewModel.SelectedOrganism);
        viewModel.NextOrganismCommand.Execute(null);
        Assert.Equal(firstEntry, viewModel.SelectedOrganism);

        viewModel.PreviousBodyCommand.Execute(null);
        Assert.Equal(2, viewModel.SelectedBody.BodyId);
        Assert.Null(viewModel.SelectedOrganism);
        viewModel.NextBodyCommand.Execute(null);
        Assert.Equal(1, viewModel.SelectedBody.BodyId);
    }

    [Fact]
    public async Task OpenEntrySelectsTheCompositionScannerTargetBeforeOpening()
    {
        var survey = CreateSurvey();
        using var viewModel = new BiologyCodexViewModel(
            survey,
            ExobiologyReferenceCatalog.LoadEmbedded(),
            BiologyCriteriaCatalog.LoadEmbedded());
        survey.ApplyUpdate(
        [
            Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42,"StarPos":[0,0,0]}"""),
            Parse("""{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":2}]}"""),
            Parse("""{"event":"CodexEntry","SystemAddress":42,"BodyID":1,"EntryID":2310101,"Name_Localised":"Aleoida Arcus - Yellow","SubCategory":"$Codex_SubCategory_Organic_Structures;","Latitude":1,"Longitude":2}"""),
            Parse("""{"event":"CodexEntry","SystemAddress":42,"BodyID":1,"EntryID":2320101,"Name_Localised":"Bacterium Aurasus - Teal","SubCategory":"$Codex_SubCategory_Organic_Structures;","Latitude":1.1,"Longitude":2.1}"""),
        ],
        null);
        long? selectedAtOpen = null;
        viewModel.SetWindowOpener(() =>
        {
            selectedAtOpen = viewModel.SelectedOrganism?.EntryId;
            return Task.FromResult(true);
        });

        Assert.True(await viewModel.OpenEntryAsync(2320101));

        Assert.Equal(2320101, selectedAtOpen);
        Assert.Equal(2320101, viewModel.SelectedOrganism!.EntryId);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private SystemSurveyViewModel CreateSurvey()
    {
        return new SystemSurveyViewModel(
            new SystemSurveySettingsStore(
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

    private const string OrganicLog = """
        {"event":"ScanOrganic","ScanType":"Log","SystemAddress":42,"Body":1,"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Genus_Localised":"Aleoida","Species":"$Codex_Ent_Aleoids_02_Name;","Species_Localised":"Aleoida Coronamus","Variant":"$Codex_Ent_Aleoids_02_L_Name;","Variant_Localised":"Aleoida Coronamus - Lime"}
        """;

    private const string OrganicAnalyse = """
        {"event":"ScanOrganic","ScanType":"Analyse","SystemAddress":42,"Body":1,"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Genus_Localised":"Aleoida","Species":"$Codex_Ent_Aleoids_02_Name;","Species_Localised":"Aleoida Coronamus","Variant":"$Codex_Ent_Aleoids_02_L_Name;","Variant_Localised":"Aleoida Coronamus - Lime"}
        """;

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
