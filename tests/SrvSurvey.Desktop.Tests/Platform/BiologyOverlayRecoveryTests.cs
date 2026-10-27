using Avalonia;
using Avalonia.Headless.XUnit;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Platform;

/// <summary>Exercises live biology windows across interrupted journal sessions with enabled panel settings.</summary>
[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class BiologyOverlayRecoveryTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-biology-recovery-{Guid.NewGuid():N}"
    );

    /// <summary>Arrival and resumed-session journals must release the jump suppression without changing preferences.</summary>
    [AvaloniaTheory]
    [InlineData("FSDJump", false)]
    [InlineData("FSDJump", true)]
    [InlineData("Location", false)]
    [InlineData("Location", true)]
    [InlineData("LoadGame", false)]
    [InlineData("LoadGame", true)]
    public async Task BiologyWindowsRecoverAlongsideTheGroundTargetAfterArrival(string arrival, bool onFoot)
    {
        var survey = new SystemSurveyViewModel(
            new SystemSurveySettingsStore(Path.Combine(temporaryDirectory, "ui-settings.json"))
        );
        var store = new SystemSurfaceStore(temporaryDirectory);
        var surface = new SurfaceSurveyViewModel(
            survey,
            store,
            new SurfaceSurveyJournalTracker(store, ExobiologyReferenceCatalog.LoadEmbedded())
        );
        var targetStore = new GroundTargetSettingsStore(Path.Combine(temporaryDirectory, "target"));
        await targetStore.SaveAsync(new GroundTargetSnapshot(true, new SurfaceCoordinate(0, 1)));
        var target = new GroundTargetViewModel(targetStore);
        var landed = new EliteStatus
        {
            Flags = StatusFlags.InMainShip | StatusFlags.HasLatLong | StatusFlags.Landed,
            BodyName = "Test 1",
            Latitude = 0,
            Longitude = 0,
            PlanetRadius = 6_000_000,
        };
        survey.ApplyUpdate(CreateExplorationEvents("Location"), landed);
        target.UpdateStatus(landed);
        using var overlays = new HostedOverlayTestHarness(new OverlayWindowRegistry());
        using var coordinator = new SystemSurveyOverlayCoordinator(
            survey,
            surface,
            overlays.Session,
            new SystemSurveyOverlayCoordinatorOptions { GameScreenCapture = new UnavailableScreenCapture() }
        );
        using var targetCoordinator = new GroundTargetOverlayCoordinator(target, overlays.Session);

        Assert.True(survey.AutoShowBioSystem);
        Assert.True(survey.AutoShowBioStatus);
        Assert.False(survey.SuppressForActiveBuildProjects);
        Assert.True(coordinator.IsBiologyVisible);
        Assert.True(coordinator.IsBiologyStatusVisible);
        Assert.True(targetCoordinator.IsVisible);

        var jumping = new EliteStatus { Flags = StatusFlags.InMainShip | StatusFlags.FsdJump };
        survey.ApplyUpdate([Parse("""{"event":"StartJump","JumpType":"Hyperspace"}""")], jumping);
        target.UpdateStatus(jumping);
        Assert.False(coordinator.IsBiologyVisible);
        Assert.False(coordinator.IsBiologyStatusVisible);

        EliteStatus resumed = landed with
        {
            Flags = onFoot
                ? StatusFlags.HasLatLong
                : StatusFlags.InMainShip | StatusFlags.Supercruise | StatusFlags.HasLatLong,
            Flags2 = onFoot ? StatusFlags2.OnFoot | StatusFlags2.OnFootOnPlanet : StatusFlags2.None,
        };
        survey.ApplyUpdate(CreateExplorationEvents(arrival), resumed);
        target.UpdateStatus(resumed);

        Assert.True(targetCoordinator.IsVisible);
        Assert.NotNull(survey.BiologySurvey);
        Assert.NotNull(survey.BiologyStatus);
        Assert.False(survey.IsFsdJumping);
        Assert.True(coordinator.IsBiologyVisible);
        Assert.Equal(onFoot, coordinator.IsBiologyStatusVisible);
    }

    /// <summary>A new journal session clears a departed session's latch while live jump flags still govern presentation.</summary>
    [Theory]
    [InlineData("Fileheader", GuiFocus.NoFocus)]
    [InlineData("Fileheader", GuiFocus.SystemMap)]
    [InlineData("Fileheader", GuiFocus.CommsPanel)]
    [InlineData("LoadGame", GuiFocus.NoFocus)]
    [InlineData("LoadGame", GuiFocus.SystemMap)]
    [InlineData("LoadGame", GuiFocus.CommsPanel)]
    [InlineData("Location", GuiFocus.NoFocus)]
    [InlineData("Location", GuiFocus.SystemMap)]
    [InlineData("Location", GuiFocus.CommsPanel)]
    public void NewJournalSessionReleasesJumpSuppressionAfterTheLiveStatusRecovers(string eventName, GuiFocus focus)
    {
        var survey = new SystemSurveyViewModel(
            new SystemSurveySettingsStore(Path.Combine(temporaryDirectory, "ui-settings.json"))
        );
        var surface = new EliteStatus
        {
            Flags = StatusFlags.HasLatLong,
            Flags2 = StatusFlags2.OnFoot | StatusFlags2.OnFootOnPlanet,
            BodyName = "Test 1",
            PlanetRadius = 6_000_000,
        };
        survey.ApplyUpdate(CreateExplorationEvents("Location"), surface);
        Assert.True(survey.ShouldShowBioSystem);
        Assert.True(survey.ShouldShowBioStatus);
        EliteStatus jumping = surface with
        {
            Flags = StatusFlags.InMainShip | StatusFlags.FsdJump | StatusFlags.HasLatLong,
            Flags2 = StatusFlags2.None,
            GuiFocus = focus,
        };
        survey.ApplyUpdate([Parse("""{"event":"StartJump","JumpType":"Hyperspace"}""")], jumping);

        survey.ApplyUpdate([Parse($$"""{"event":"{{eventName}}","StarSystem":"Test","SystemAddress":42}""")], jumping);

        Assert.False(survey.IsFsdJumping);
        Assert.NotNull(survey.BiologySurvey);
        Assert.NotNull(survey.BiologyStatus);
        Assert.False(survey.ShouldShowBioSystem);
        Assert.False(survey.ShouldShowBioStatus);

        survey.ApplyUpdate([], surface);

        Assert.True(survey.ShouldShowBioSystem);
        Assert.True(survey.ShouldShowBioStatus);
    }

    /// <summary>Removes this test's isolated settings and survey files after its windows close.</summary>
    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    /// <summary>Creates a known biological body after an arrival or recovered game session.</summary>
    private static JournalEventEnvelope[] CreateExplorationEvents(string arrival)
    {
        List<JournalEventEnvelope> events = [];
        if (arrival == "LoadGame")
        {
            events.Add(Parse("""{"event":"LoadGame","Commander":"Test","FID":"F1","Ship":"python"}"""));
            arrival = "Location";
        }

        events.Add(Parse($$"""{"event":"{{arrival}}","StarSystem":"Test","SystemAddress":42}"""));
        events.Add(
            Parse(
                """{"event":"Scan","BodyName":"Test 1","BodyID":1,"SystemAddress":42,"PlanetClass":"Rocky body","Landable":true,"MassEM":0.1,"Radius":6000000,"SurfaceTemperature":165}"""
            )
        );
        events.Add(
            Parse(
                """{"event":"SAASignalsFound","BodyName":"Test 1","BodyID":1,"SystemAddress":42,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":1}],"Genuses":[{"Genus":"$Codex_Ent_Bacterial_Genus_Name;","Genus_Localised":"Bacterium"}]}"""
            )
        );
        return events.ToArray();
    }

    /// <summary>Parses fixture events through the normal journal envelope parser.</summary>
    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? entry, out string? error), error);
        return Assert.IsType<JournalEventEnvelope>(entry);
    }

    /// <summary>Keeps journal-driven biology tests independent of desktop screen capture.</summary>
    private sealed class UnavailableScreenCapture : IGameScreenCapture
    {
        public bool IsAvailable => false;

        public string? UnavailableReason => "Unavailable in tests";

        /// <summary>Rejects pixel capture so the tests only depend on journal and status context.</summary>
        public CapturedPixelBuffer Capture(PixelRect bounds) => throw new NotSupportedException();

        /// <summary>The unavailable capture stub owns no native resources.</summary>
        public void Dispose() { }
    }
}
