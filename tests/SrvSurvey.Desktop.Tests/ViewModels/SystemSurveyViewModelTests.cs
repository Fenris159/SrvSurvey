using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class SystemSurveyViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-SystemSurveyViewModel-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void PriorScanEligibilityUsesLegacySurfaceModesAndPreferences()
    {
        var viewModel = CreateViewModel();
        viewModel.ApplyUpdate(
            [
                Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42}"""),
            ],
            new EliteStatus
            {
                Flags = StatusFlags.HasLatLong | StatusFlags.InSrv,
                BodyName = "Test 1",
                PlanetRadius = 1_000_000,
            });

        Assert.True(viewModel.ShouldLoadPriorScans);

        viewModel.UseExternalData = false;
        Assert.False(viewModel.ShouldLoadPriorScans);
        viewModel.UseExternalData = true;
        viewModel.AutoShowPriorScans = false;
        Assert.False(viewModel.ShouldLoadPriorScans);
        viewModel.AutoShowPriorScans = true;

        viewModel.ApplyUpdate([], new EliteStatus
        {
            Flags = StatusFlags.HasLatLong | StatusFlags.Docked,
            BodyName = "Test 1",
            PlanetRadius = 1_000_000,
        });
        Assert.False(viewModel.ShouldLoadPriorScans);
    }

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
    public void FlightWarningMatchesLegacyGravityBodyAndModeRules()
    {
        var viewModel = CreateViewModel();
        viewModel.ApplyUpdate(
            [
                Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42}"""),
                Parse(BodyInformationScan),
            ],
            new EliteStatus
            {
                Flags = StatusFlags.InMainShip,
                BodyName = "Test 1",
            });

        Assert.True(viewModel.ShouldShowFlightWarning);
        Assert.Equal("WARNING: SURFACE GRAVITY 1.20 g", viewModel.FlightWarningText);

        viewModel.AutoShowFlightWarnings = false;
        Assert.False(viewModel.ShouldShowFlightWarning);
        viewModel.AutoShowFlightWarnings = true;
        viewModel.HighGravityWarningLevel = 2;
        Assert.False(viewModel.ShouldShowFlightWarning);

        viewModel.HighGravityWarningLevel = 1;
        viewModel.ApplyUpdate([], new EliteStatus
        {
            Flags2 = StatusFlags2.OnFoot | StatusFlags2.OnFootOnPlanet,
            BodyName = "Test 1",
        });
        Assert.False(viewModel.ShouldShowFlightWarning);

        viewModel.ApplyUpdate([], new EliteStatus
        {
            Flags = StatusFlags.InSrv,
            BodyName = "Test 1",
            GuiFocus = GuiFocus.RolePanel,
        });
        Assert.False(viewModel.ShouldShowFlightWarning);
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

        Assert.True(viewModel.ShouldShowLastFssBody);
        Assert.Equal("Test 3", viewModel.LastFssBodyName);
        Assert.Equal("Sudarsky class II gas giant", viewModel.LastFssBodyClass);
        Assert.Equal("0 LS", viewModel.LastFssBodyDistance);
        Assert.Equal("18.3 K CR", viewModel.LastFssScanValue);
        Assert.Equal("99.2 K CR", viewModel.LastFssMappedValue);
        Assert.False(viewModel.HasLastFssMarkers);
        Assert.False(viewModel.HasLastFssSignals);

        var dssBody = Assert.Single(viewModel.DssBodies);
        Assert.Equal("1", dssBody.Name);
        Assert.True(dssBody.IsDestination);
        Assert.Equal("1 biological signal remaining", viewModel.BiologicalHeading);
        Assert.Equal("2", Assert.Single(viewModel.BiologicalBodies).Name);
    }

    [Fact]
    public void LastFssBodyVisibilityHonorsModeAndPreference()
    {
        var viewModel = CreateViewModel();
        viewModel.ApplyUpdate(
            [
                Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42}"""),
                Parse(TerraformableScan),
            ],
            new EliteStatus { GuiFocus = GuiFocus.Fss });

        Assert.True(viewModel.HasLastFssBody);
        Assert.Equal("⚑ Test 1", viewModel.LastFssBodyName);
        Assert.Equal("TERRAFORMABLE · LANDABLE", viewModel.LastFssMarkers);
        Assert.True(viewModel.ShouldShowLastFssBody);

        viewModel.ApplyUpdate([], new EliteStatus { GuiFocus = GuiFocus.NoFocus });
        Assert.False(viewModel.ShouldShowLastFssBody);

        viewModel.ApplyUpdate([], new EliteStatus { GuiFocus = GuiFocus.Fss });
        viewModel.AutoShowLastFssBody = false;
        Assert.False(viewModel.ShouldShowLastFssBody);
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

    [Fact]
    public void BodyInformationUsesMapDestinationAndFormatsDetailedScan()
    {
        var viewModel = CreateViewModel();
        viewModel.ApplyUpdate(
            [
                Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42,"StarPos":[500,0,0]}"""),
                Parse(BodyInformationScan),
                Parse("""{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":2},{"Type":"$SAA_SignalType_Geological;","Count":1}]}"""),
            ],
            new EliteStatus
            {
                GuiFocus = GuiFocus.SystemMap,
                Destination = new StatusDestination
                {
                    System = 42,
                    Body = 1,
                    Name = "Test 1",
                },
            });

        Assert.True(viewModel.ShouldShowBodyInfo);
        var body = Assert.IsType<BodyInformationViewModel>(
            viewModel.BodyInformation);
        Assert.Equal("⚑ Test 1", body.Name);
        Assert.Equal("High metal content body", body.BodyClass);
        Assert.Equal("123 LS", body.Distance);
        Assert.Equal("TERRAFORMABLE · UNDISCOVERED", body.Markers);
        Assert.EndsWith(" CR", body.ScanValue);
        Assert.EndsWith(" CR", body.MappedValue);
        Assert.Equal("300 K", body.Temperature);
        Assert.Equal("1.200 g", body.Gravity);
        Assert.True(body.IsHighGravity);
        Assert.True(body.IsHighValue);
        Assert.Equal("0.0100 bar", body.Pressure);
        Assert.Equal("2 biological signals", body.BiologicalSignals);
        Assert.Equal("1 geological signal", body.GeologicalSignals);
        Assert.Equal("Minor silicate vapour geysers", body.Volcanism);
        Assert.Equal("Thin carbon dioxide", body.Atmosphere);
        Assert.Equal(2, body.AtmosphereComposition.Count);
        Assert.Equal("Carbon Dioxide", body.AtmosphereComposition[0].Name);
        Assert.Equal(2, body.Materials.Count);
        Assert.True(Assert.Single(
            body.Materials,
            material => material.Name == "Yttrium").IsRare);
        var ring = Assert.Single(body.Rings);
        Assert.Equal("A", ring.Name);
        Assert.Equal("Rocky", ring.RingClass);
    }

    [Fact]
    public void BodyInformationPreservesLegacyVisibilityAndToggleModes()
    {
        var viewModel = CreateViewModel();
        viewModel.ApplyUpdate(
            [
                Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42,"StarPos":[500,0,0]}"""),
                Parse(BodyInformationScan),
            ],
            new EliteStatus
            {
                BodyName = "Test 1",
                Destination = new StatusDestination
                {
                    System = 42,
                    Body = 1,
                    Name = "Test 1",
                },
            });

        Assert.False(viewModel.ShouldShowBodyInfo);
        Assert.True(viewModel.ToggleBodyInfoVisibility());
        Assert.True(viewModel.IsBodyInfoForced);
        Assert.True(viewModel.ShouldShowBodyInfo);
        Assert.True(viewModel.ToggleBodyInfoVisibility());
        Assert.False(viewModel.ShouldShowBodyInfo);

        viewModel.ApplyUpdate([], new EliteStatus
        {
            GuiFocus = GuiFocus.SystemMap,
            Destination = new StatusDestination
            {
                System = 42,
                Body = 1,
                Name = "Test 1",
            },
        });
        viewModel.ShowFssInfoInSystemMap = true;
        Assert.False(viewModel.ShouldShowBodyInfo);
        viewModel.ShowFssInfoInSystemMap = false;
        Assert.True(viewModel.ShouldShowBodyInfo);

        viewModel.ApplyUpdate([], new EliteStatus
        {
            BodyName = "Test 1",
            Flags = StatusFlags.InMainShip
                | StatusFlags.Supercruise
                | StatusFlags.HasLatLong,
        });
        Assert.True(viewModel.ShouldShowBodyInfo);

        viewModel.ApplyUpdate([], new EliteStatus
        {
            BodyName = "Test 1",
            Flags = StatusFlags.InMainShip
                | StatusFlags.HasLatLong
                | StatusFlags.HudInAnalysisMode,
        });
        Assert.False(viewModel.ShouldShowBodyInfo);
        viewModel.ShowBodyInfoAtSurface = true;
        Assert.True(viewModel.ShouldShowBodyInfo);
    }

    [Fact]
    public void BodyInformationHonorsBubbleAndSupportsUnscannedTargets()
    {
        var viewModel = CreateViewModel();
        viewModel.ApplyUpdate(
            [Parse("""{"event":"Location","StarSystem":"Sol vicinity","SystemAddress":42,"StarPos":[100,0,0]}""")],
            new EliteStatus
            {
                GuiFocus = GuiFocus.SystemMap,
                Destination = new StatusDestination
                {
                    System = 42,
                    Body = 9,
                    Name = "Sol vicinity 9",
                },
            });

        var body = Assert.IsType<BodyInformationViewModel>(
            viewModel.BodyInformation);
        Assert.True(body.IsScanRequired);
        Assert.Equal("Sol vicinity 9", body.Name);
        Assert.True(viewModel.IsWithinBodyInfoBubble);
        Assert.False(viewModel.ShouldShowBodyInfo);

        viewModel.HideBodyInfoInBubble = false;
        Assert.True(viewModel.ShouldShowBodyInfo);
    }

    [Fact]
    public void BiologySurveyUsesSystemOverviewInMapModes()
    {
        var viewModel = CreateViewModel();
        viewModel.ApplyUpdate(
            [
                Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42}"""),
                Parse(BodyInformationScan),
                Parse("""{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":2}],"Genuses":[{"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Genus_Localised":"Aleoida"}]}"""),
                Parse("""{"event":"ScanOrganic","ScanType":"Analyse","SystemAddress":42,"Body":1,"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Genus_Localised":"Aleoida","Species":"$Codex_Ent_Aleoids_01_Name;","Species_Localised":"Aleoida Arcus","Variant":"$Codex_Ent_Aleoids_01_B_Name;","Variant_Localised":"Aleoida Arcus - Green"}"""),
            ],
            new EliteStatus
            {
                GuiFocus = GuiFocus.SystemMap,
                Destination = new StatusDestination
                {
                    System = 42,
                    Body = 1,
                    Name = "Test 1",
                },
            });

        Assert.True(viewModel.ShouldShowBioSystem);
        var biology = Assert.IsType<BiologySurveyViewModel>(
            viewModel.BiologySurvey);
        Assert.True(biology.IsSystemOverview);
        Assert.Equal("1 of 2 biological signals analyzed", biology.ProgressText);
        var body = Assert.Single(biology.Bodies);
        Assert.True(body.IsDestination);
        Assert.Equal(7_252_500, body.KnownReward);
        Assert.True(body.HasUnknownReward);
        Assert.Equal("Known reward: 7.25 M CR", biology.RewardSummary);
    }

    [Fact]
    public void BiologySurveyShowsBodySamplesRewardsFootfallAndGeology()
    {
        var viewModel = CreateViewModel();
        var scan = new BioSampleSnapshot(
            new SurfaceLocation(1, 2),
            150,
            "$Codex_Ent_Aleoids_Genus_Name;",
            "$Codex_Ent_Aleoids_01_Name;",
            "Active",
            2310101,
            "Test 1");
        viewModel.ApplyUpdate(
            [
                Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42,"Population":0}"""),
                Parse(BodyInformationScan),
                Parse("""{"event":"SAASignalsFound","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":1},{"Type":"$SAA_SignalType_Geological;","Count":2}],"Genuses":[{"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Genus_Localised":"Aleoida"}]}"""),
                Parse("""{"event":"CodexEntry","SystemAddress":42,"BodyID":1,"EntryID":100,"Name_Localised":"Silicate Vapour Fumarole","SubCategory":"$Codex_SubCategory_Geology_and_Anomalies;"}"""),
                Parse("""{"event":"CodexEntry","SystemAddress":42,"BodyID":1,"EntryID":2310101,"Name_Localised":"Aleoida Arcus - Green","SubCategory":"$Codex_SubCategory_Organic_Structures;","IsNewEntry":true}"""),
                Parse("""{"event":"ScanOrganic","ScanType":"Log","SystemAddress":42,"Body":1,"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Genus_Localised":"Aleoida","Species":"$Codex_Ent_Aleoids_01_Name;","Species_Localised":"Aleoida Arcus","Variant":"$Codex_Ent_Aleoids_01_B_Name;","Variant_Localised":"Aleoida Arcus - Green"}"""),
                Parse("""{"event":"Disembark","SystemAddress":42,"Body":"Test 1","BodyID":1,"OnPlanet":true,"OnStation":false}"""),
            ],
            new EliteStatus { GuiFocus = GuiFocus.Fss },
            new ExobiologySnapshot(null, scan, null, 0, [], 0));

        Assert.True(viewModel.ShouldShowBioSystem);
        var biology = Assert.IsType<BiologySurveyViewModel>(
            viewModel.BiologySurvey);
        Assert.True(biology.IsBodyDetail);
        Assert.Equal("Test 1 biology", biology.Heading);
        var organism = Assert.Single(biology.Organisms);
        Assert.Equal("Aleoida Arcus - Green", organism.DisplayName);
        Assert.Equal("7.25 M CR", organism.RewardText);
        Assert.True(organism.IsRegionalFirst);
        Assert.False(organism.IsHighlightedFirst);
        Assert.True(organism.IsCurrentSample);
        Assert.False(organism.ShouldDim);
        Assert.Equal(
            "First-footfall value: 36.26 M CR",
            biology.FirstFootfallRewardSummary);
        Assert.Equal(2, biology.GeologicalSignalCount);
        Assert.Equal("Silicate Vapour Fumarole", Assert.Single(
            biology.GeologicalSignals));

        viewModel.HideGeoCountInBioSystem = true;
        Assert.False(viewModel.BiologySurvey!.HasGeologicalSignals);
        viewModel.HighlightRegionalFirsts = true;
        Assert.True(Assert.Single(
            viewModel.BiologySurvey!.Organisms).IsHighlightedFirst);
    }

    [Fact]
    public void BiologySurveyHonorsNearBodySelectionPreference()
    {
        var viewModel = CreateViewModel();
        viewModel.ApplyUpdate(
            [
                Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42}"""),
                Parse(BodyInformationScan),
                Parse("""{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":1}]}"""),
                Parse("""{"event":"Scan","ScanType":"Detailed","SystemAddress":42,"BodyName":"Test 2","BodyID":2,"PlanetClass":"Rocky body","MassEM":0.1,"Landable":true}"""),
                Parse("""{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 2","BodyID":2,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":1}]}"""),
            ],
            new EliteStatus
            {
                Flags = StatusFlags.InMainShip,
                BodyName = "Test 1",
                Destination = new StatusDestination
                {
                    System = 42,
                    Body = 2,
                    Name = "Test 2",
                },
            });

        Assert.True(viewModel.BiologySurvey!.IsSystemOverview);
        Assert.False(viewModel.ShouldShowBioSystem);

        viewModel.DrawBodyBiosOnlyWhenNear = false;

        Assert.True(viewModel.BiologySurvey!.IsBodyDetail);
        Assert.Equal("Test 2 biology", viewModel.BiologySurvey.Heading);
        Assert.True(viewModel.ShouldShowBioSystem);
    }

    [Fact]
    public void BiologySurveyShowsExactCriteriaPredictionsAndHonorsDisableSetting()
    {
        var viewModel = CreateViewModel();
        viewModel.ApplyUpdate(
            [
                Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42,"StarPos":[0,0,0]}"""),
                Parse("""{"event":"Scan","SystemAddress":42,"BodyName":"Test A","BodyID":0,"StarType":"L","StellarMass":1,"Radius":695700000,"SurfaceTemperature":5000}"""),
                Parse(PredictableAleoidaScan),
                Parse("""{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":1}],"Genuses":[{"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Genus_Localised":"Aleoida"}]}"""),
            ],
            new EliteStatus
            {
                GuiFocus = GuiFocus.SystemMap,
                Destination = new StatusDestination
                {
                    System = 42,
                    Body = 1,
                    Name = "Test 1",
                },
            });

        var systemSurvey = Assert.IsType<BiologySurveyViewModel>(
            viewModel.BiologySurvey);
        var bodySummary = Assert.Single(systemSurvey.Bodies);
        Assert.True(bodySummary.HasPredictedReward);
        Assert.StartsWith("Estimated reward:", systemSurvey.RewardSummary);
        var bodyInformation = Assert.IsType<BodyInformationViewModel>(
            viewModel.BodyInformation);
        Assert.StartsWith(
            "Estimated reward:",
            bodyInformation.BiologicalReward);
        Assert.True(bodyInformation.HasBiologicalReward);

        viewModel.ApplyUpdate([], new EliteStatus { GuiFocus = GuiFocus.Fss });

        var bodySurvey = Assert.IsType<BiologySurveyViewModel>(
            viewModel.BiologySurvey);
        var prediction = Assert.Single(bodySurvey.Organisms);
        Assert.Equal("Aleoida Coronamus - Lime", prediction.DisplayName);
        Assert.Equal("Aleoida", prediction.GenusName);
        Assert.True(prediction.IsPrediction);
        Assert.False(prediction.IsGenusIdentified);
        Assert.True(prediction.HasReward);
        Assert.False(bodySurvey.HasPredictionStatus);
        Assert.StartsWith("Estimated reward:", bodySurvey.RewardSummary);

        viewModel.DisableBioPredictions = true;

        var genus = Assert.Single(viewModel.BiologySurvey!.Organisms);
        Assert.Equal("Aleoida", genus.DisplayName);
        Assert.False(genus.IsPrediction);
        Assert.True(genus.IsGenusIdentified);
        Assert.Equal("Reward pending identification", viewModel.BiologySurvey.RewardSummary);
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

    private const string BodyInformationScan = """
        {
          "event":"Scan",
          "ScanType":"Detailed",
          "StarSystem":"Test",
          "SystemAddress":42,
          "BodyName":"Test 1",
          "BodyID":1,
          "DistanceFromArrivalLS":123.4,
          "TerraformState":"Terraformable",
          "PlanetClass":"High metal content body",
          "Atmosphere":"thin carbon dioxide atmosphere",
          "AtmosphereType":"CarbonDioxide",
          "AtmosphereComposition":[
            {"Name":"CarbonDioxide","Percent":99.0},
            {"Name":"SulphurDioxide","Percent":1.0}
          ],
          "Volcanism":"minor silicate vapour geysers volcanism",
          "MassEM":1.2,
          "Radius":6000000,
          "SurfaceGravity":12.0,
          "SurfaceTemperature":300,
          "SurfacePressure":1000,
          "Landable":true,
          "Materials":[
            {"Name":"iron","Percent":20.0},
            {"Name":"yttrium","Percent":1.0}
          ],
          "Rings":[
            {"Name":"Test 1 A Ring","RingClass":"eRingClass_Rocky","InnerRad":1,"OuterRad":2}
          ],
          "WasDiscovered":false,
          "WasMapped":false,
          "WasFootfalled":false
        }
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
          "Materials":[{"Name":"Iron","Percent":20}]
        }
        """;
}
