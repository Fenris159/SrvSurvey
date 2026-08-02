using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class SurfaceSurveyViewModelTests : IDisposable
{
    private const string Genus = "$Codex_Ent_Aleoids_Genus_Name;";
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-surface-survey-vm-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task LoadsHistoryTrackersActiveSamplesAndShipMarker()
    {
        var (viewModel, survey, store) = CreateViewModel();
        await store.SetLastTouchdownAsync(
            BodyContext(),
            new SurfaceCoordinate(0, 3));
        await store.AddBookmarkAsync(
            BodyContext(),
            Genus,
            new SurfaceCoordinate(0, 2));
        await store.AppendBioScansAsync(
            BodyContext(),
            [new SurfaceBioScan(
                new SurfaceCoordinate(0, 1),
                150,
                Genus,
                "$Codex_Ent_Aleoids_01_Name;",
                "Complete",
                2310101,
                "Test System 1")]);
        ApplySurveyContext(survey, Status(StatusFlags.InSrv));
        var exobiology = ExobiologySnapshot.Empty with
        {
            ScanOne = Sample(new SurfaceLocation(0, 4)),
        };

        await viewModel.ApplyUpdateAsync(
            Session(),
            [],
            survey.CurrentStatus,
            exobiology);

        Assert.True(viewModel.ShouldShow);
        Assert.Equal("Test System 1", viewModel.BodyName);
        Assert.Equal(4, viewModel.RadarMarkers.Count);
        Assert.Contains(viewModel.RadarMarkers, marker => marker.IsHistoricalScan);
        Assert.Contains(viewModel.RadarMarkers, marker => marker.IsBookmark);
        Assert.Contains(viewModel.RadarMarkers, marker => marker.IsActiveSample);
        Assert.Contains(viewModel.RadarMarkers, marker => marker.IsVehicle);
        Assert.Equal(2, viewModel.NavigationMarkers.Count);
        Assert.Contains(
            viewModel.NavigationMarkers,
            marker => marker.Name == "Sample 1");
        Assert.True(viewModel.HasNavigationMarkers);
        var group = Assert.Single(viewModel.TrackerGroups);
        Assert.True(group.IsActive);
        Assert.Equal("Aleoida", group.Name);
        Assert.All(group.Targets, marker => Assert.True(marker.IsActive));

        var markers = viewModel.RadarMarkers;
        var navigationMarkers = viewModel.NavigationMarkers;
        var groups = viewModel.TrackerGroups;
        var quickGroups = viewModel.QuickTrackerGroups;
        var notifications = new List<string?>();
        viewModel.PropertyChanged += (_, eventArgs) =>
            notifications.Add(eventArgs.PropertyName);
        await viewModel.ApplyUpdateAsync(
            Session(),
            [],
            null,
            exobiology with { ScannedBioEntryIds = [] });
        Assert.Same(markers, viewModel.RadarMarkers);
        Assert.Same(navigationMarkers, viewModel.NavigationMarkers);
        Assert.Same(groups, viewModel.TrackerGroups);
        Assert.Same(quickGroups, viewModel.QuickTrackerGroups);
        Assert.Empty(notifications);
    }

    [Fact]
    public async Task LiftoffRendersPersistedTouchdownAsFormerShipLocation()
    {
        var (viewModel, survey, _) = CreateViewModel();
        ApplySurveyContext(survey, Status(StatusFlags.InSrv));

        await viewModel.ApplyUpdateAsync(
            Session(),
            [
                Event(
                    """
                    {"event":"Touchdown","StarSystem":"Test System","SystemAddress":42,"Body":"Test System 1","BodyID":7,"Latitude":0,"Longitude":1}
                    """),
                Event("""{"event":"Liftoff"}"""),
            ],
            survey.CurrentStatus,
            ExobiologySnapshot.Empty);

        var marker = Assert.Single(viewModel.RadarMarkers);
        Assert.Equal(SurfaceRadarMarkerKind.FormerShip, marker.Kind);
        Assert.Equal("Former ship location", marker.Name);
        Assert.Equal("Departed", marker.Status);
        Assert.True(marker.IsVehicle);
        Assert.Contains(marker, viewModel.NavigationMarkers);
    }

    [Fact]
    public async Task EligibilityMatchesLegacyAltitudePanelAndLandingGearRules()
    {
        var (viewModel, survey, store) = CreateViewModel();
        await store.AddBookmarkAsync(
            BodyContext(),
            Genus,
            new SurfaceCoordinate(0, 2));

        ApplySurveyContext(survey, Status(StatusFlags.InMainShip));
        await viewModel.ApplyUpdateAsync(
            Session(),
            [],
            survey.CurrentStatus,
            ExobiologySnapshot.Empty);
        Assert.True(viewModel.ShouldShow);

        survey.AutoHideSurfaceRadarWithoutLandingGear = true;
        Assert.False(viewModel.ShouldShow);

        ApplySurveyContext(
            survey,
            Status(StatusFlags.InMainShip | StatusFlags.LandingGearDown));
        await viewModel.ApplyUpdateAsync(
            Session(),
            [],
            survey.CurrentStatus,
            ExobiologySnapshot.Empty);
        Assert.True(viewModel.ShouldShow);

        ApplySurveyContext(
            survey,
            Status(
                StatusFlags.InMainShip,
                focus: GuiFocus.RolePanel));
        await viewModel.ApplyUpdateAsync(
            Session(),
            [],
            survey.CurrentStatus,
            ExobiologySnapshot.Empty);
        Assert.True(viewModel.ShouldShow);

        ApplySurveyContext(
            survey,
            Status(
                StatusFlags.InMainShip,
                altitude: 10_000));
        await viewModel.ApplyUpdateAsync(
            Session(),
            [],
            survey.CurrentStatus,
            ExobiologySnapshot.Empty);
        Assert.False(viewModel.ShouldShow);

        ApplySurveyContext(
            survey,
            Status(StatusFlags.InMainShip | StatusFlags.Docked));
        await viewModel.ApplyUpdateAsync(
            Session(),
            [],
            survey.CurrentStatus,
            ExobiologySnapshot.Empty);
        Assert.False(viewModel.ShouldShow);
    }

    [Fact]
    public async Task HiddenQuickTrackersDoNotOpenRadarByThemselves()
    {
        var (viewModel, survey, store) = CreateViewModel();
        await store.AddBookmarkAsync(
            BodyContext(),
            "#temporary",
            new SurfaceCoordinate(0, 2));
        ApplySurveyContext(survey, Status(StatusFlags.InSrv));

        await viewModel.ApplyUpdateAsync(
            Session(),
            [],
            survey.CurrentStatus,
            ExobiologySnapshot.Empty);

        Assert.Single(viewModel.TrackerGroups);
        Assert.False(viewModel.ShouldShowRadar);
        Assert.True(viewModel.ShouldShow);
        Assert.True(viewModel.IsTrackerOnly);
    }

    [Fact]
    public async Task MiniTrackUsesQuickTargetsAndLegacyModes()
    {
        var (viewModel, survey, store) = CreateViewModel();
        await store.AddBookmarkAsync(
            BodyContext(),
            "#1",
            new SurfaceCoordinate(0, 2));
        ApplySurveyContext(survey, Status(StatusFlags.InSrv));
        await viewModel.ApplyUpdateAsync(
            Session(),
            [],
            survey.CurrentStatus,
            ExobiologySnapshot.Empty);

        Assert.False(viewModel.ShouldShowMiniTrack);
        survey.AutoShowMiniTrack = true;
        Assert.True(viewModel.ShouldShowMiniTrack);
        Assert.Equal("#1", Assert.Single(viewModel.QuickTrackerGroups).Name);
        Assert.Same(viewModel.QuickTrackerGroups, viewModel.QuickTrackerGroups);

        ApplySurveyContext(
            survey,
            Status(StatusFlags.InSrv, focus: GuiFocus.ExternalPanel));
        await viewModel.ApplyUpdateAsync(
            Session(),
            [],
            survey.CurrentStatus,
            ExobiologySnapshot.Empty);
        Assert.False(viewModel.ShouldShowMiniTrack);

        ApplySurveyContext(
            survey,
            Status(StatusFlags.InSrv, focus: GuiFocus.RolePanel));
        await viewModel.ApplyUpdateAsync(
            Session(),
            [],
            survey.CurrentStatus,
            ExobiologySnapshot.Empty);
        Assert.True(viewModel.ShouldShowMiniTrack);
    }

    [Fact]
    public void RadarZoomUsesLegacyFactorBoundsAndAutomaticReset()
    {
        var (viewModel, _, _) = CreateViewModel();

        Assert.Equal(1, viewModel.RadarScale);
        Assert.Equal("ZOOM AUTO", viewModel.RadarScaleText);
        Assert.True(viewModel.AdjustRadarScale(zoomIn: true));
        Assert.Equal(1.25, viewModel.RadarScale);
        Assert.Equal("ZOOM 1.25×", viewModel.RadarScaleText);
        Assert.True(viewModel.AdjustRadarScale(zoomIn: false));
        Assert.Equal(1, viewModel.RadarScale);
        Assert.True(viewModel.ResetRadarScale());
        Assert.Equal("ZOOM AUTO", viewModel.RadarScaleText);

        for (var index = 0; index < 17; index++)
        {
            viewModel.AdjustRadarScale(zoomIn: true);
        }

        Assert.InRange(viewModel.RadarScale, 0.25, 10);
        Assert.False(viewModel.AdjustRadarScale(zoomIn: true));
    }

    [Fact]
    public async Task QuickTrackerChordTogglesCurrentSurfaceLocation()
    {
        var (viewModel, survey, _) = CreateViewModel();
        ApplySurveyContext(survey, Status(StatusFlags.InSrv));
        await viewModel.ApplyUpdateAsync(
            Session(),
            [],
            survey.CurrentStatus,
            ExobiologySnapshot.Empty);

        Assert.True(await viewModel.ToggleQuickTrackerAsync(1));
        Assert.Equal(
            new SurfaceCoordinate(0, 0),
            Assert.Single(viewModel.CurrentSurface!.Bookmarks["#1"]));
        Assert.Contains("added", viewModel.StatusText);
        Assert.False(viewModel.ShouldShowRadar);
        Assert.True(viewModel.ShouldShow);
        Assert.True(viewModel.IsTrackerOnly);

        Assert.True(await viewModel.ToggleQuickTrackerAsync(1));
        Assert.Empty(viewModel.CurrentSurface!.Bookmarks);
        Assert.Contains("removed", viewModel.StatusText);
    }

    private (SurfaceSurveyViewModel ViewModel, SystemSurveyViewModel Survey,
        SystemSurfaceStore Store) CreateViewModel()
    {
        var catalog = new ExobiologyReferenceCatalog(
            [new ExobiologyReference(
                2310101,
                "$Codex_Ent_Aleoids_01_B_Name;",
                "$Codex_Ent_Aleoids_01_Name;",
                "Aleoida Arcus - Yellow",
                7_252_500)]);
        var store = new SystemSurfaceStore(temporaryDirectory);
        var survey = new SystemSurveyViewModel(
            new SystemSurveySettingsStore(Path.Combine(
                temporaryDirectory,
                "ui-settings.json")));
        return (
            new SurfaceSurveyViewModel(
                survey,
                store,
                new SurfaceSurveyJournalTracker(store, catalog)),
            survey,
            store);
    }

    private static void ApplySurveyContext(
        SystemSurveyViewModel survey,
        EliteStatus status)
    {
        survey.ApplyUpdate(
            [
                Event(
                    """
                    {"event":"Location","StarSystem":"Test System","SystemAddress":42}
                    """),
                Event(
                    """
                    {"event":"Scan","ScanType":"Detailed","SystemAddress":42,"BodyName":"Test System 1","BodyID":7,"PlanetClass":"Rocky body","Landable":true,"Radius":1000}
                    """),
            ],
            status);
    }

    private static EliteStatus Status(
        StatusFlags mode,
        double altitude = 0,
        GuiFocus focus = GuiFocus.NoFocus)
    {
        return new EliteStatus
        {
            Flags = StatusFlags.HasLatLong | mode,
            Latitude = 0,
            Longitude = 0,
            PlanetRadius = 1_000,
            BodyName = "Test System 1",
            Altitude = altitude,
            GuiFocus = focus,
        };
    }

    private static BioSampleSnapshot Sample(SurfaceLocation location)
    {
        return new BioSampleSnapshot(
            location,
            150,
            Genus,
            "$Codex_Ent_Aleoids_01_Name;",
            "Active",
            2310101,
            "Test System 1");
    }

    private static SurfaceSurveySessionContext Session()
    {
        return new SurfaceSurveySessionContext(
            "F123",
            "Drew",
            "Test System",
            42,
            null);
    }

    private static SystemSurfaceContext BodyContext()
    {
        return new SystemSurfaceContext(
            "F123",
            "Drew",
            "Test System",
            42,
            null,
            7,
            "Test System 1",
            1_000);
    }

    private static JournalEventEnvelope Event(string json)
    {
        Assert.True(
            JournalEventEnvelope.TryParse(json, out var journalEvent, out var error),
            error);
        return journalEvent!;
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
