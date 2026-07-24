using SrvSurvey.Core.Guardian;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class GuardianViewModelTests
{
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
            Assert.Contains("0.0 m", viewModel.NearbyPointText);
            Assert.Contains("Guardian Casket 1/2", viewModel.CurrentObeliskRequirementsText);
            Assert.False(viewModel.HasCurrentObeliskArtifacts);
            Assert.True(viewModel.MapProjection?.Points.Single().IsActiveObelisk);

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
            await viewModel.ToggleCurrentObeliskScannedAsync();
            await viewModel.ToggleCurrentObeliskScannedAsync();

            Assert.True(viewModel.CurrentObelisk?.Scanned);
            Assert.True(ramTah.IsLogCompleted(RamTahMission.AncientRuins, "H1"));
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
}
