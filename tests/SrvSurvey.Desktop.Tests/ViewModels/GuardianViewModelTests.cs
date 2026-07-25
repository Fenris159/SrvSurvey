using SrvSurvey.Core.Guardian;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class GuardianViewModelTests
{
    [Theory]
    [InlineData(GuardianSiteKind.Ruins, 1_001, 0.2)]
    [InlineData(GuardianSiteKind.Ruins, 801, 0.5)]
    [InlineData(GuardianSiteKind.Ruins, 800, 0.65)]
    [InlineData(GuardianSiteKind.Structure, 801, 0.2)]
    [InlineData(GuardianSiteKind.Structure, 501, 0.5)]
    [InlineData(GuardianSiteKind.Structure, 500, 1.5)]
    public void AutomaticMapScaleMatchesLegacyDistanceThresholds(
        GuardianSiteKind kind,
        double distance,
        double expected)
    {
        var actual = GuardianViewModel.CalculateAutomaticMapScale(
            kind,
            distance,
            onFoot: false,
            usingSrvTurret: false,
            mobileOnSurface: true,
            nearestObeliskDistance: 100,
            autoZoomNearObelisks: true,
            autoZoomInSrvTurret: true);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task GuardianSurveySharingPreparesAndCopiesBundle()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var surveyDirectory = Path.Combine(root, "guardian", "F123");
            Directory.CreateDirectory(surveyDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(surveyDirectory, "Unpublished Body-ruins-1.json"),
                """
                {"name":"GR Test","nameLocalised":"New Guardian Site","commander":"Drew","type":"Alpha","index":1,"systemAddress":42,"systemName":"Test","bodyId":1,"bodyName":"Unpublished Body","siteHeading":90}
                """);
            var viewModel = new GuardianViewModel(root);
            string? copied = null;
            viewModel.SetClipboardWriter(text =>
            {
                copied = text;
                return Task.CompletedTask;
            });
            await viewModel.LoadProfileAsync("F123", isOdyssey: true);

            await viewModel.PrepareShareBundleAsync();
            await viewModel.CopyShareArchivePathAsync();

            Assert.True(viewModel.HasShareArchive);
            Assert.True(File.Exists(viewModel.ShareArchivePath));
            Assert.Equal(viewModel.ShareArchivePath, copied);
            Assert.Contains("New Guardian Site", Assert.Single(viewModel.ShareSiteNames));
            Assert.Contains("copied to the clipboard", viewModel.ShareStatusMessage);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void FiltersAllLegacyGuardianReferenceFields()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var viewModel = new GuardianViewModel(root);

            Assert.Equal(759, viewModel.Rows.Count);
            Assert.Contains("759 of 759", viewModel.Summary);
            Assert.NotNull(viewModel.MapProjection);
            Assert.Equal(
                viewModel.SelectedSite?.Reference.SiteType,
                viewModel.MapProjection?.SiteType);
            Assert.NotEmpty(viewModel.MapProjection!.Points);
            Assert.Contains("Reference map only", viewModel.MapStatus);

            viewModel.SelectedKindFilter = "Ruins";
            viewModel.SelectedSiteTypeFilter = "Beta";
            viewModel.FilterText = "Synuefe";

            Assert.NotEmpty(viewModel.Rows);
            Assert.All(
                viewModel.Rows,
                row =>
                {
                    Assert.Equal(GuardianSiteKind.Ruins, row.Reference.Kind);
                    Assert.Equal("Beta", row.Reference.SiteType);
                    Assert.Contains(
                        "Synuefe",
                        row.Reference.SystemName,
                        StringComparison.OrdinalIgnoreCase);
                });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void CurrentPositionReordersRowsByGalacticDistance()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var viewModel = new GuardianViewModel(root);
            var target = viewModel.Rows.Single(
                row => row.Reference.Kind == GuardianSiteKind.Ruins
                    && row.Reference.SiteId == 1);

            viewModel.UpdateCurrentSystem("GR 1 system", target.Reference.Position);

            Assert.Equal(target.Reference, viewModel.Rows[0].Reference);
            Assert.Equal(0, viewModel.Rows[0].Distance);
            Assert.Contains("GR 1 system", viewModel.OriginStatus);
            Assert.Contains("GR 1", viewModel.MapTitle);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void GuardianTemplateDraftUpdatesMapPreviewAndDiscardRestoresIt()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var viewModel = new GuardianViewModel(root);
            viewModel.SelectedSite = viewModel.Rows.First(row =>
                row.Reference.Kind == GuardianSiteKind.Ruins);
            var originalPoint = viewModel.MapProjection!.Points[0];

            viewModel.TemplateAuthoring.StartCommand.Execute(null);
            viewModel.TemplateAuthoring.SelectedPoint =
                viewModel.TemplateAuthoring.Points.Single(point =>
                    point.Name == originalPoint.Name);
            viewModel.TemplateAuthoring.PointDistance =
                (decimal)originalPoint.Distance + 25;
            viewModel.TemplateAuthoring.ApplySelectedPointCommand.Execute(null);

            Assert.Equal(
                originalPoint.Distance + 25,
                viewModel.MapProjection.Points.Single(point =>
                    point.Name == originalPoint.Name).Distance,
                precision: 6);

            viewModel.TemplateAuthoring.RequestDiscardCommand.Execute(null);
            viewModel.TemplateAuthoring.ConfirmDiscardCommand.Execute(null);

            Assert.Equal(
                originalPoint.Distance,
                viewModel.MapProjection.Points.Single(point =>
                    point.Name == originalPoint.Name).Distance,
                precision: 6);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task CustomOriginLookupReordersRowsAndClearRestoresJournalOrigin()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var near = CreateReference(
                1,
                GuardianSiteKind.Beacon,
                "Near",
                1,
                new GalacticCoordinate(0, 0, 0));
            var far = CreateReference(
                2,
                GuardianSiteKind.Beacon,
                "Far",
                2,
                new GalacticCoordinate(100, 0, 0));
            var resolver = new StubStarSystemResolver(
            [
                new StarSystemReference(
                    "Far Origin",
                    100,
                    new GalacticCoordinate(100, 0, 0)),
            ]);
            var viewModel = new GuardianViewModel(
                root,
                new GuardianSiteCatalog([near, far]),
                new GuardianPublishedSiteCatalog([]),
                new GuardianSiteTemplateCatalog([]),
                systemResolver: resolver);
            viewModel.UpdateCurrentSystem("Near Origin", near.Position);

            Assert.Equal(near, viewModel.Rows[0].Reference);

            viewModel.OriginSystemName = "Far Origin";
            await viewModel.LookupOriginAsync();

            Assert.True(viewModel.HasCustomOrigin);
            Assert.Equal(far, viewModel.Rows[0].Reference);
            Assert.Equal(0, viewModel.Rows[0].Distance);
            Assert.Contains("custom origin Far Origin", viewModel.OriginStatus);
            Assert.Equal("Far Origin", Assert.Single(resolver.Queries));

            await viewModel.ClearCustomOriginAsync();

            Assert.False(viewModel.HasCustomOrigin);
            Assert.Equal(near, viewModel.Rows[0].Reference);
            Assert.Contains("Near Origin", viewModel.OriginStatus);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void RamTahCatalogLogsSupportNeededOnlySearchAndSurveyNavigation()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var ruins = CreateReference(
                1,
                GuardianSiteKind.Ruins,
                "Ruins",
                1,
                new GalacticCoordinate(0, 0, 0));
            var structure = CreateReference(
                2,
                GuardianSiteKind.Structure,
                "Structure",
                2,
                new GalacticCoordinate(1, 0, 0));
            var published = new GuardianPublishedSiteCatalog(
            [
                CreatePublishedSite(
                    ruins,
                    [
                        new GuardianObelisk("A01", "H1", false, []),
                        new GuardianObelisk("A02", "B1", false, []),
                    ]),
                CreatePublishedSite(
                    structure,
                    [new GuardianObelisk("A01", "#1", false, [])]),
            ]);
            var ramTah = new RamTahViewModel(new CommanderProfileStore(root));
            ramTah.LoadProfile(
                "F123",
                "Drew",
                true,
                new RamTahSnapshot(
                    RamTahMissionStatus.Active,
                    RamTahMissionStatus.NotStarted,
                    ["H1"],
                    []));
            var viewModel = new GuardianViewModel(
                root,
                new GuardianSiteCatalog([ruins, structure]),
                published,
                new GuardianSiteTemplateCatalog([]),
                ramTah);

            viewModel.IncludeRamTahLogs = true;

            Assert.Equal(
                ["B1", "H1"],
                viewModel.Rows.Single(row => row.Reference == ruins).RamTahLogCodes);
            Assert.Equal(
                ["#1"],
                viewModel.Rows.Single(row => row.Reference == structure).RamTahLogCodes);

            viewModel.ShowOnlyNeededRamTahLogs = true;

            Assert.Equal(
                ["B1"],
                viewModel.Rows.Single(row => row.Reference == ruins).RamTahLogCodes);
            Assert.Empty(
                viewModel.Rows.Single(row => row.Reference == structure).RamTahLogCodes);

            viewModel.FilterText = "Biology #1";
            Assert.Equal(ruins, Assert.Single(viewModel.Rows).Reference);
            Assert.True(viewModel.HasSelectedSurvey);
            Assert.Contains("canonn-signals", viewModel.SelectedCanonnUri?.ToString());
            Assert.Contains("/1", viewModel.SelectedSpanshUri?.ToString());
            Assert.Contains("systemID64=1", viewModel.SelectedEdsmUri?.ToString());

            viewModel.OpenSelectedSurveyCommand.Execute(null);
            Assert.Equal(1, viewModel.SelectedWorkspaceTabIndex);
            viewModel.OpenShareWorkspaceCommand.Execute(null);
            Assert.Equal(2, viewModel.SelectedWorkspaceTabIndex);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void GuardianSystemSummaryUsesLegacyModesAndDestinationState()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var viewModel = new GuardianViewModel(root);
            var target = viewModel.Rows.First(
                row => row.Reference.Kind == GuardianSiteKind.Ruins);
            viewModel.UpdateCurrentSystem(
                target.Reference.SystemName,
                target.Reference.Position);
            viewModel.UpdateStatus(new EliteStatus
            {
                Flags = StatusFlags.InMainShip | StatusFlags.Supercruise,
                Destination = new StatusDestination
                {
                    System = target.Reference.SystemAddress,
                    Body = target.Reference.BodyId,
                },
            });

            Assert.True(viewModel.ShouldShowGuardianSystemSummary);
            Assert.NotEmpty(viewModel.CurrentSystemSites);
            Assert.Contains(
                viewModel.CurrentSystemSites,
                row => row.Reference == target.Reference && row.IsDestination);

            viewModel.SuppressForActiveBuildProjects = true;
            viewModel.SetActiveBuildProjects(true);
            Assert.False(viewModel.ShouldShowGuardianSystemSummary);

            viewModel.SetActiveBuildProjects(false);
            viewModel.SetSystemSummaryObscured(true);
            Assert.False(viewModel.ShouldShowGuardianSystemSummary);

            viewModel.SetSystemSummaryObscured(false);
            viewModel.UpdateStatus(new EliteStatus
            {
                Flags = StatusFlags.InMainShip,
                GuiFocus = GuiFocus.RolePanel,
            });
            Assert.False(viewModel.ShouldShowGuardianSystemSummary);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task LoadsCommanderVisitsAndCopiesSelectedSiteFields()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var folder = Path.Combine(root, "guardian", "F123");
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(
                Path.Combine(folder, "site-ruins-1.json"),
                """
                {
                  "firstVisited":"2026-07-01T10:00:00Z",
                  "lastVisited":"2026-07-02T10:00:00Z",
                  "type":"Beta","index":1,
                  "systemAddress":3515254557027,
                  "systemName":"Synuefe XR-H d11-102",
                  "bodyId":13,
                  "bodyName":"Synuefe XR-H d11-102 1 b",
                  "siteHeading":332,"relicTowerHeading":93,
                  "location":{"lat":-46.576923,"long":133.985107},
                  "notes":"commander note"
                }
                """);
            var viewModel = new GuardianViewModel(root);

            await viewModel.LoadProfileAsync("F123", isOdyssey: true);
            viewModel.SelectedVisitFilter = "Visited";

            var row = Assert.Single(viewModel.Rows);
            Assert.True(row.Visit.IsVisited);
            Assert.Equal("commander note", row.Notes);
            Assert.Contains("1 site survey file", viewModel.StatusMessage);
            Assert.True(viewModel.SurveyEditor.IsAvailable);
            Assert.NotEmpty(viewModel.SurveyEditor.Points);

            string? copied = null;
            viewModel.SetClipboardWriter(text =>
            {
                copied = text;
                return Task.CompletedTask;
            });
            await viewModel.CopySystemAddressAsync();

            Assert.Equal("3515254557027", copied);
            Assert.Contains("Copied system address", viewModel.StatusMessage);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task SurfaceCopyReportsUnavailableBeaconCoordinates()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var catalog = new GuardianSiteCatalog(
            [
                new GuardianSiteReference(
                    0,
                    GuardianSiteKind.Beacon,
                    "Test",
                    1,
                    "A 1",
                    2,
                    "Beacon",
                    0,
                    100,
                    new GalacticCoordinate(0, 0, 0),
                    null,
                    null,
                    -1,
                    -1,
                    0,
                    null,
                    null,
                    null),
            ]);
            var viewModel = new GuardianViewModel(
                root,
                catalog,
                new GuardianPublishedSiteCatalog([]),
                new GuardianSiteTemplateCatalog([]));

            viewModel.SetClipboardWriter(_ => Task.CompletedTask);
            await viewModel.CopySurfaceLocationAsync();

            Assert.Contains("not available", viewModel.StatusMessage);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task LiveJournalVisitCreatesSurveyAndSelectsKnownSite()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var viewModel = new GuardianViewModel(root);
            await viewModel.LoadProfileAsync("F123", isOdyssey: true);
            Assert.Equal("No live Guardian site detected", viewModel.ActiveSiteTitle);
            Assert.Equal("WAITING", viewModel.ActiveSiteReference);

            await viewModel.ApplyJournalEventsAsync(
            [
                Parse(
                    """{"timestamp":"2026-07-24T10:00:00Z","event":"Location","StarSystem":"Synuefe XR-H d11-102","SystemAddress":3515254557027}"""),
                Parse(
                    """{"timestamp":"2026-07-24T10:05:00Z","event":"ApproachSettlement","Name":"$Ancient:#index=1;","Name_Localised":"Ancient Ruins (1)","SystemAddress":3515254557027,"BodyID":13,"BodyName":"Synuefe XR-H d11-102 1 b","Latitude":-46.576923,"Longitude":133.985107}"""),
            ],
            "Drew");

            Assert.True(viewModel.HasActiveSite);
            Assert.Equal("GR 1", viewModel.ActiveSite?.Reference?.DisplayId);
            Assert.Equal("Ancient Ruins (1)", viewModel.ActiveSiteTitle);
            Assert.Equal("GR 1", viewModel.ActiveSiteReference);
            Assert.Contains("Beta ruins", viewModel.ActiveSiteDescription);
            Assert.Equal("-46.576923, 133.985107", viewModel.ActiveSiteLocation);
            Assert.Equal("GR 1", viewModel.SelectedSite?.DisplayId);
            Assert.True(viewModel.SelectedSite?.Visit.IsVisited);
            Assert.Contains("Recorded the live Guardian site", viewModel.StatusMessage);

            var reader = new GuardianCommanderDataReader(root);
            var data = await reader.ReadAsync("F123", isOdyssey: true);
            var survey = Assert.Single(data.Surveys);
            Assert.Equal("Drew", survey.Commander);
            Assert.Equal("Beta", survey.SiteType);
            Assert.Equal(
                new GuardianSurfaceLocation(-46.576923, 133.985107),
                survey.Survey.Location);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task LiveDepartureClearsSiteWithoutRemovingRecordedVisit()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var viewModel = new GuardianViewModel(root);
            await viewModel.LoadProfileAsync("F123", isOdyssey: false);
            await viewModel.ApplyJournalEventsAsync(
            [
                Parse(
                    """{"timestamp":"2026-07-24T10:05:00Z","event":"ApproachSettlement","Name":"$Ancient_Tiny_001:#index=1;","Name_Localised":"Guardian Structure","SystemAddress":42,"BodyID":7,"BodyName":"Test A 1","Latitude":1,"Longitude":2}"""),
                Parse(
                    """{"timestamp":"2026-07-24T10:06:00Z","event":"SupercruiseEntry"}"""),
            ],
            "Drew");

            Assert.False(viewModel.HasActiveSite);
            Assert.Equal("No live Guardian site detected", viewModel.ActiveSiteTitle);
            var reader = new GuardianCommanderDataReader(root);
            var data = await reader.ReadAsync("F123", isOdyssey: false);
            var survey = Assert.Single(data.Surveys);
            Assert.Equal("Lacrosse", survey.SiteType);
            Assert.True(survey.Legacy);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task LiveObeliskTracksArtifactsScanStateAndRamTahProgress()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var reference = CreateProximityReference();
            var publishedObelisk = new GuardianObelisk(
                "A01",
                "H1",
                false,
                ["ca", "ca"]);
            var ramTah = new RamTahViewModel(new CommanderProfileStore(root));
            ramTah.LoadProfile(
                "F123",
                "Drew",
                true,
                new RamTahSnapshot(
                    RamTahMissionStatus.Active,
                    RamTahMissionStatus.NotStarted,
                    [],
                    []));
            var viewModel = new GuardianViewModel(
                root,
                new GuardianSiteCatalog([reference]),
                new GuardianPublishedSiteCatalog(
                [
                    new GuardianPublishedSite(
                        1,
                        GuardianSiteKind.Ruins,
                        reference.FullBodyName,
                        "Test",
                        1,
                        0,
                        -1,
                        new GuardianSurfaceLocation(0, 0),
                        new Dictionary<string, GuardianPoiStatus>(),
                        new Dictionary<string, int>(),
                        [publishedObelisk],
                        "A",
                        "test-ruins-1.json"),
                ]),
                new GuardianSiteTemplateCatalog(
                [
                    new GuardianSiteTemplate(
                        "Test",
                        "Test",
                        string.Empty,
                        new GuardianMapPoint(0, 0),
                        1,
                        [
                            new GuardianPointOfInterest(
                                "A01",
                                GuardianPoiType.Obelisk,
                                180,
                                10,
                                0),
                        ],
                        [],
                        new Dictionary<string, GuardianMapPoint>()),
                ]),
                ramTah);
            await viewModel.LoadProfileAsync("F123", isOdyssey: true);
            await viewModel.ApplyJournalEventsAsync(
            [
                Parse(
                    """{"timestamp":"2026-07-24T10:05:00Z","event":"ApproachSettlement","Name":"$Ancient:#index=1;","Name_Localised":"Ancient Ruins (1)","SystemAddress":42,"BodyID":7,"BodyName":"Test A 1","Latitude":0,"Longitude":0}"""),
            ],
            "Drew");
            viewModel.UpdateCargo(new CargoSnapshot(
                DateTimeOffset.UtcNow,
                "Cargo",
                "SRV",
                1,
                [new CargoItem("ancientcasket", "Guardian Casket", 1, 0)]));
            viewModel.UpdateStatus(StatusNorthOfSite(10));

            Assert.Equal("A01", viewModel.CurrentObelisk?.Name);
            Assert.Equal(3, viewModel.ActiveMapScale);
            Assert.True(viewModel.IsAutomaticMapZoom);
            Assert.Contains("0.0 m", viewModel.NearbyPointText);
            Assert.Contains("Guardian Casket 1/2", viewModel.CurrentObeliskRequirementsText);
            Assert.False(viewModel.HasCurrentObeliskArtifacts);
            Assert.True(viewModel.MapProjection?.Points.Single().IsActiveObelisk);
            Assert.True(viewModel.ActiveMapProjection?.Points.Single().IsActiveObelisk);
            Assert.True(viewModel.ShouldShowRamTahOverlay);
            Assert.True(viewModel.SurveyEditor.HasLiveMeasurement);
            Assert.Contains("10.0 m from origin", viewModel.SurveyEditor.LiveMeasurementText);
            var ramTahLog = Assert.Single(viewModel.CurrentRamTahLogs);
            Assert.Equal("H1", ramTahLog.LogCode);
            Assert.Equal("MISSING", ramTahLog.ArtifactStatus);
            Assert.Equal("A01", ramTahLog.ObeliskNamesText);

            viewModel.AutoZoomNearObelisks = false;
            Assert.Equal(0.65, viewModel.ActiveMapScale);
            Assert.True(viewModel.AdjustMapZoom(zoomIn: true));
            Assert.Equal(1.15, viewModel.ActiveMapScale);
            Assert.False(viewModel.IsAutomaticMapZoom);
            Assert.True(viewModel.AdjustMapZoom(zoomIn: false));
            Assert.Equal(0.65, viewModel.ActiveMapScale);
            Assert.True(viewModel.IsAutomaticMapZoom);
            Assert.True(viewModel.AdjustMapZoom(zoomIn: true));
            viewModel.EnableAutomaticMapZoom();

            viewModel.AutoZoomInSrvTurret = true;
            viewModel.UpdateStatus(StatusNorthOfSite(10) with
            {
                Flags = StatusFlags.HasLatLong
                    | StatusFlags.InSrv
                    | StatusFlags.SrvUsingTurretView,
                Heading = 90,
            });
            Assert.Equal(3, viewModel.ActiveMapScale);
            Assert.Equal(90, viewModel.ActiveMapRelativeHeading);
            viewModel.AutoZoomInSrvTurret = false;
            Assert.Equal(0.65, viewModel.ActiveMapScale);

            viewModel.UpdateStatus(StatusNorthOfSite(10) with
            {
                Flags = StatusFlags.HasLatLong,
                Flags2 = StatusFlags2.OnFoot,
            });
            Assert.Equal(2, viewModel.ActiveMapScale);

            viewModel.UpdateStatus(StatusNorthOfSite(900));
            Assert.Equal(0.5, viewModel.ActiveMapScale);
            viewModel.UpdateStatus(StatusNorthOfSite(1_100));
            Assert.Equal(0.2, viewModel.ActiveMapScale);
            viewModel.UpdateStatus(StatusNorthOfSite(10));

            await viewModel.ToggleCurrentObeliskScannedAsync();

            Assert.True(viewModel.CurrentObelisk?.Scanned);
            Assert.False(ramTah.IsLogCompleted(RamTahMission.AncientRuins, "H1"));
            Assert.Contains("required artifacts are missing", viewModel.StatusMessage);

            viewModel.UpdateCargo(new CargoSnapshot(
                DateTimeOffset.UtcNow,
                "Cargo",
                "SRV",
                2,
                [new CargoItem("ancientcasket", "Guardian Casket", 2, 0)]));
            Assert.Equal(
                "READY",
                Assert.Single(viewModel.CurrentRamTahLogs).ArtifactStatus);
            await viewModel.ToggleCurrentObeliskScannedAsync();
            await viewModel.ToggleCurrentObeliskScannedAsync();

            Assert.True(viewModel.CurrentObelisk?.Scanned);
            Assert.True(ramTah.IsLogCompleted(RamTahMission.AncientRuins, "H1"));
            Assert.Empty(viewModel.CurrentRamTahLogs);
            var saved = await new GuardianCommanderDataReader(root)
                .ReadAsync("F123", isOdyssey: true);
            Assert.True(Assert.Single(saved.Surveys).ActiveObelisks.Single().Scanned);

            await viewModel.ApplyJournalEventsAsync(
                [Parse("""{"timestamp":"2026-07-24T10:10:00Z","event":"SupercruiseEntry"}""")],
                "Drew");

            Assert.Null(viewModel.CurrentObelisk);
            Assert.Null(viewModel.Proximity);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-guardian-vm-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static GuardianSiteReference CreateProximityReference()
    {
        return new GuardianSiteReference(
            1,
            GuardianSiteKind.Ruins,
            "Test",
            42,
            "A 1",
            7,
            "Test",
            1,
            0,
            new GalacticCoordinate(0, 0, 0),
            0,
            0,
            0,
            -1,
            0,
            null,
            null,
            null);
    }

    private static GuardianSiteReference CreateReference(
        int siteId,
        GuardianSiteKind kind,
        string systemName,
        long systemAddress,
        GalacticCoordinate position)
    {
        return new GuardianSiteReference(
            siteId,
            kind,
            systemName,
            systemAddress,
            "A 1",
            1,
            kind == GuardianSiteKind.Beacon ? "Beacon" : "Test",
            1,
            100,
            position,
            0,
            0,
            0,
            -1,
            0,
            null,
            null,
            null);
    }

    private static GuardianPublishedSite CreatePublishedSite(
        GuardianSiteReference reference,
        IReadOnlyList<GuardianObelisk> obelisks)
    {
        return new GuardianPublishedSite(
            reference.SiteId,
            reference.Kind,
            reference.FullBodyName,
            reference.SiteType,
            reference.Index,
            0,
            -1,
            new GuardianSurfaceLocation(0, 0),
            new Dictionary<string, GuardianPoiStatus>(),
            new Dictionary<string, int>(),
            obelisks,
            string.Empty,
            $"{reference.SiteId}.json");
    }

    private static EliteStatus StatusNorthOfSite(double distance)
    {
        const double radius = 1_000_000;
        return new EliteStatus
        {
            Flags = StatusFlags.HasLatLong | StatusFlags.InSrv,
            Latitude = distance / radius * 180 / Math.PI,
            Longitude = 0,
            PlanetRadius = (decimal)radius,
        };
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

    private sealed class StubStarSystemResolver(
        IReadOnlyList<StarSystemReference> results) : IStarSystemResolver
    {
        public List<string> Queries { get; } = [];

        public Task<IReadOnlyList<StarSystemReference>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            return Task.FromResult(results);
        }
    }
}
