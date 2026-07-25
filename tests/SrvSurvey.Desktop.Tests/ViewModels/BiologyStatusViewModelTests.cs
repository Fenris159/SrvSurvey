using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class BiologyStatusViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-BiologyStatus-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ActiveSampleShowsLegacyProgressDistanceRewardAndSignals()
    {
        var viewModel = CreateViewModel();
        var scanOne = new BioSampleSnapshot(
            new SurfaceLocation(0, 0),
            150,
            "$Codex_Ent_Aleoids_Genus_Name;",
            "$Codex_Ent_Aleoids_01_Name;",
            "Active",
            2310101,
            "Test 1");
        var scanTwo = scanOne with
        {
            Location = new SurfaceLocation(0, 0.001),
        };
        viewModel.ApplyUpdate(
        [
            Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42,"Population":0}"""),
            Parse(BodyScan),
            Parse("""{"event":"SAASignalsFound","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":2},{"Type":"$SAA_SignalType_Geological;","Count":2}],"Genuses":[{"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Genus_Localised":"Aleoida"},{"Genus":"$Codex_Ent_Bacterial_Genus_Name;","Genus_Localised":"Bacterium"}]}"""),
            Parse("""{"event":"CodexEntry","SystemAddress":42,"BodyID":1,"EntryID":100,"Name_Localised":"Silicate Vapour Fumarole","SubCategory":"$Codex_SubCategory_Geology_and_Anomalies;"}"""),
            Parse("""{"event":"CodexEntry","SystemAddress":42,"BodyID":1,"EntryID":2310101,"Name_Localised":"Aleoida Arcus - Green","SubCategory":"$Codex_SubCategory_Organic_Structures;"}"""),
            Parse("""{"event":"ScanOrganic","ScanType":"Log","SystemAddress":42,"Body":1,"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Genus_Localised":"Aleoida","Species":"$Codex_Ent_Aleoids_01_Name;","Species_Localised":"Aleoida Arcus","Variant":"$Codex_Ent_Aleoids_01_B_Name;","Variant_Localised":"Aleoida Arcus - Green"}"""),
            Parse("""{"event":"Disembark","SystemAddress":42,"Body":"Test 1","BodyID":1,"OnPlanet":true,"OnStation":false}"""),
        ],
        new EliteStatus
        {
            GuiFocus = GuiFocus.NoFocus,
            Flags = StatusFlags.InSrv | StatusFlags.HasLatLong,
            BodyName = "Test 1",
            Latitude = 0,
            Longitude = 0.002,
            PlanetRadius = 6_000_000,
        },
        new ExobiologySnapshot(null, scanOne, scanTwo, 0, [], 0));

        Assert.True(viewModel.ShouldShowBioStatus);
        var status = Assert.IsType<BiologyStatusViewModel>(
            viewModel.BiologyStatus);
        Assert.Equal("1", status.BodyName);
        Assert.Equal("0 of 2 analyzed", status.ProgressText);
        Assert.Equal(0, status.CompletionPercent);
        Assert.Equal(50, status.TrackedCompletionPercent);
        Assert.False(status.RequiresDss);
        Assert.Equal(4, status.Signals.Count);
        Assert.True(Assert.Single(
            status.Signals,
            signal => signal.Name == "Aleoida").IsActive);
        Assert.Equal("150 m", Assert.Single(
            status.Signals,
            signal => signal.Name == "Aleoida").Detail);
        Assert.True(Assert.Single(
            status.Signals,
            signal => signal.Name == "Silicate Vapour Fumarole").IsAnalyzed);

        var active = Assert.IsType<BiologyActiveSampleViewModel>(
            status.ActiveSample);
        Assert.Equal("Aleoida Arcus - Green", active.DisplayName);
        Assert.Equal(2, active.Stage);
        Assert.True(active.IsFirstSampleComplete);
        Assert.True(active.IsSecondSampleComplete);
        Assert.Equal(104.72, active.NearestDistanceMeters!.Value, 2);
        Assert.Equal(45.28, active.RemainingDistanceMeters!.Value, 2);
        Assert.Equal("36.26 M CR · FF bonus", active.RewardText);
        Assert.False(active.IsSeparationReady);
    }

    [Fact]
    public void StaleSampleWarnsWithoutReplacingCurrentBodySummary()
    {
        var viewModel = CreateViewModel();
        var staleScan = new BioSampleSnapshot(
            new SurfaceLocation(1, 2),
            500,
            "$Codex_Ent_Bacterial_Genus_Name;",
            "$Codex_Ent_Bacterial_01_Name;",
            "Active",
            2320101,
            "Test 1");
        viewModel.ApplyUpdate(
        [
            Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42}"""),
            Parse("""{"event":"Scan","SystemAddress":42,"BodyName":"Test 2","BodyID":2,"PlanetClass":"Rocky body","MassEM":0.1,"Radius":6000000,"Landable":true}"""),
            Parse("""{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 2","BodyID":2,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":1}],"Genuses":[{"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Genus_Localised":"Aleoida"}]}"""),
        ],
        new EliteStatus
        {
            Flags = StatusFlags.InMainShip,
            BodyName = "Test 2",
        },
        new ExobiologySnapshot(null, staleScan, null, 0, [], 0));

        var status = Assert.IsType<BiologyStatusViewModel>(
            viewModel.BiologyStatus);
        Assert.False(status.HasActiveSample);
        Assert.True(status.HasWarning);
        Assert.Contains("Bacterial", status.Warning);
        Assert.Contains("Test 1", status.Warning);
        Assert.Equal("2", status.BodyName);
    }

    [Fact]
    public void CompositionScannerCodexCueShowsRewardAndClearsOnSampling()
    {
        var viewModel = CreateViewModel();
        var status = new EliteStatus
        {
            Flags = StatusFlags.InSrv | StatusFlags.HasLatLong,
            BodyName = "Test 1",
            PlanetRadius = 6_000_000,
        };
        viewModel.ApplyUpdate(
        [
            Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42,"Population":0}"""),
            Parse(BodyScan),
            Parse("""{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":1}],"Genuses":[{"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Genus_Localised":"Aleoida"}]}"""),
            Parse("""{"event":"Disembark","SystemAddress":42,"Body":"Test 1","BodyID":1,"OnPlanet":true,"OnStation":false}"""),
            Parse("""{"event":"CodexEntry","SystemAddress":42,"BodyID":1,"EntryID":2310101,"Name_Localised":"Aleoida Arcus - Green","SubCategory":"$Codex_SubCategory_Organic_Structures;"}"""),
        ],
        status);

        var biologyStatus = Assert.IsType<BiologyStatusViewModel>(
            viewModel.BiologyStatus);
        var notification = Assert.IsType<BiologyCodexNotificationViewModel>(
            biologyStatus.CodexNotification);
        Assert.True(biologyStatus.HasCodexNotification);
        Assert.Equal(2310101, viewModel.LatestBiologyEntryId);
        Assert.Equal(2310101, notification.EntryId);
        Assert.True(notification.IsFirstFootfall);
        Assert.True(notification.HasImage);
        Assert.Equal(36_262_500, notification.Reward);
        Assert.Contains("Aleoida Arcus - Green", biologyStatus.Footer);
        Assert.Contains("36.26 M CR", biologyStatus.Footer);
        Assert.Contains(".show", notification.ActionText);

        viewModel.ApplyUpdate(
        [
            Parse("""{"event":"ScanOrganic","ScanType":"Log","SystemAddress":42,"Body":1,"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Species":"$Codex_Ent_Aleoids_01_Name;","Variant":"$Codex_Ent_Aleoids_01_B_Name;"}"""),
        ],
        null);

        biologyStatus = Assert.IsType<BiologyStatusViewModel>(
            viewModel.BiologyStatus);
        Assert.False(biologyStatus.HasCodexNotification);
        Assert.Equal(2310101, viewModel.LatestBiologyEntryId);
    }

    [Fact]
    public void VisibilityAndDssGuidanceFollowLegacyModesAndPreference()
    {
        var viewModel = CreateViewModel();
        viewModel.ApplyUpdate(
        [
            Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42}"""),
            Parse(BodyScan),
            Parse("""{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":1}]}"""),
        ],
        new EliteStatus
        {
            Flags = StatusFlags.InMainShip,
            BodyName = "Test 1",
        });

        var status = Assert.IsType<BiologyStatusViewModel>(
            viewModel.BiologyStatus);
        Assert.True(status.RequiresDss);
        Assert.False(status.HasFooter);
        Assert.True(viewModel.ShouldShowBioStatus);

        viewModel.ApplyUpdate([], new EliteStatus
        {
            GuiFocus = GuiFocus.SystemMap,
            Flags = StatusFlags.InMainShip,
            BodyName = "Test 1",
        });
        Assert.False(viewModel.ShouldShowBioStatus);

        viewModel.ApplyUpdate([], new EliteStatus
        {
            Flags = StatusFlags.Docked | StatusFlags.InMainShip,
            BodyName = "Test 1",
        });
        Assert.False(viewModel.ShouldShowBioStatus);

        viewModel.ApplyUpdate([], new EliteStatus
        {
            Flags = StatusFlags.InMainShip,
            BodyName = "Test 1",
        });
        viewModel.AutoShowBioStatus = false;
        Assert.False(viewModel.ShouldShowBioStatus);
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

    private const string BodyScan = """
        {
          "event":"Scan",
          "ScanType":"Detailed",
          "SystemAddress":42,
          "BodyName":"Test 1",
          "BodyID":1,
          "PlanetClass":"Rocky body",
          "MassEM":0.1,
          "Radius":6000000,
          "Landable":true,
          "WasFootfalled":false
        }
        """;
}
