using SrvSurvey.Core.Guardian;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class GuardianViewModelTests
{
    private static readonly string[] RuinsSiteTypes =
        ["Alpha", "Beta", "Gamma"];

    [Theory]
    [InlineData("Robolobster", "Fighter blueprint")]
    [InlineData("Turtle", "Module blueprint")]
    [InlineData("Bowl", "Weapon blueprint")]
    [InlineData("Lacrosse", "no blueprint category")]
    public void StructureApproachIdentifiesLegacyBlueprintCategory(
        string siteType,
        string expected)
    {
        Assert.Contains(
            expected,
            GuardianViewModel.GetGuardianBlueprintText(siteType));
    }

    [Fact]
    public void DisableAlignmentGridPreferencesInvertShowFlagsAndPersist()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "SrvSurvey-GuardianDisableGrids-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var settingsPath = Path.Combine(root, "ui-settings.json");
            var store = new GuardianOverlaySettingsStore(settingsPath);
            var viewModel = new GuardianViewModel(
                root,
                new GuardianViewModelOptions
                {
                    OverlaySettingsStore = store,
                });

            Assert.True(viewModel.ShowRuinsMeasurementGrid);
            Assert.False(viewModel.DisableRuinsMeasurementGrid);
            Assert.True(viewModel.ShowAerialAlignmentGrid);
            Assert.False(viewModel.DisableAerialAlignmentGrid);

            viewModel.DisableRuinsMeasurementGrid = true;
            viewModel.DisableAerialAlignmentGrid = true;

            Assert.True(viewModel.DisableRuinsMeasurementGrid);
            Assert.False(viewModel.ShowRuinsMeasurementGrid);
            Assert.True(viewModel.DisableAerialAlignmentGrid);
            Assert.False(viewModel.ShowAerialAlignmentGrid);

            var saved = store.Load();
            Assert.True(saved.DisableRuinsMeasurementGrid);
            Assert.True(saved.DisableAerialAlignmentGrid);

            viewModel.DisableRuinsMeasurementGrid = false;
            viewModel.DisableAerialAlignmentGrid = false;

            Assert.False(viewModel.DisableRuinsMeasurementGrid);
            Assert.True(viewModel.ShowRuinsMeasurementGrid);
            Assert.False(viewModel.DisableAerialAlignmentGrid);
            Assert.True(viewModel.ShowAerialAlignmentGrid);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Theory]
    [InlineData("Alpha", "$Ancient:#index=1;", "alpha-heading-guide.png")]
    [InlineData("Beta", "$Ancient:#index=1;", "beta-heading-guide.png")]
    [InlineData("Gamma", "$Ancient:#index=1;", "gamma-heading-guide.png")]
    [InlineData("Crossroads", "$Ancient:#index=1;", "crossroads-heading-guide.png")]
    [InlineData("Fistbump", "$Ancient:#index=1;", "fistbump-heading-guide.png")]
    [InlineData("Lacrosse", "$Ancient:#index=1;", "lacrosse-heading-guide.png")]
    [InlineData("Unknown", "$Ancient_Medium:#index=1;", "data-port-heading-guide.png")]
    [InlineData("Unknown", "$Ancient_Small:#index=1;", "data-port-heading-guide.png")]
    public void HeadingGuidanceSelectsTheLegacySiteAsset(
        string siteType,
        string siteName,
        string expected)
    {
        Assert.EndsWith(
            expected,
            GuardianViewModel.GetHeadingGuideAssetPath(siteType, siteName));
    }

    [Theory]
    [InlineData("ancientbiologicaldata")]
    [InlineData("ancientlanguagedata")]
    [InlineData("ancientculturaldata")]
    [InlineData("ancienttechnologicaldata")]
    [InlineData("ancienthistoricaldata")]
    public void GuardianEncodedMaterialCapacityMatchesLegacySet(string name)
    {
        Assert.True(GuardianViewModel.HasFullGuardianEncodedMaterial(Parse(
            $$"""{"event":"Materials","Encoded":[{"Name":"{{name}}","Count":150}]}""")));
        Assert.False(GuardianViewModel.HasFullGuardianEncodedMaterial(Parse(
            $$"""{"event":"Materials","Encoded":[{"Name":"{{name}}","Count":149}]}""")));
    }

    [Theory]
    [InlineData("B1", "Biology #1")]
    [InlineData("C20", "Culture #20")]
    [InlineData("#1", "#1")]
    public void RamTahLogNamesMatchLegacyFormatting(string code, string expected)
    {
        Assert.Equal(expected, GuardianViewModel.GetLogDisplayName(code));
    }

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
            new GuardianAutomaticMapScaleOptions
            {
                SiteKind = kind,
                DistanceFromSite = distance,
                OnFoot = false,
                UsingSrvTurret = false,
                MobileOnSurface = true,
                NearestObeliskDistance = 100,
                AutoZoomNearObelisks = true,
                AutoZoomInSrvTurret = true,
            });

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
            var viewModel = new GuardianViewModel(root,
                new GuardianViewModelOptions
                {
                    SystemResolver = resolver,
                    References = new GuardianSiteCatalog([near, far]),
                    PublishedSites = new GuardianPublishedSiteCatalog([]),
                    Templates = new GuardianSiteTemplateCatalog([]),
                });
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
                new GalacticCoordinate(0, 0, 0)) with
            { SiteType = "Beta" };
            var structure = CreateReference(
                2,
                GuardianSiteKind.Structure,
                "Structure",
                2,
                new GalacticCoordinate(1, 0, 0)) with
            { SiteType = "Bowl" };
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
            var viewModel = new GuardianViewModel(root,
                new GuardianViewModelOptions
                {
                    References = new GuardianSiteCatalog([ruins, structure]),
                    PublishedSites = published,
                    Templates = new GuardianSiteTemplateCatalog([]),
                    RamTah = ramTah,
                });

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

            viewModel.UpdateCurrentSystem("Ruins", ruins.Position);
            var systemRuins = Assert.Single(viewModel.CurrentSystemSites);
            Assert.Equal(["B1"], systemRuins.RamTahLogCodes);
            Assert.False(systemRuins.HasBlueprint);
            viewModel.UpdateCurrentSystem("Structure", structure.Position);
            var systemStructure = Assert.Single(viewModel.CurrentSystemSites);
            Assert.True(systemStructure.HasBlueprint);
            Assert.Contains("Weapon blueprint", systemStructure.BlueprintText);

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
    public async Task GuardianSystemSummaryUsesLegacyModesAndDestinationState()
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
            Assert.Same(
                viewModel.CurrentSystemSites,
                viewModel.CurrentSystemSites);
            Assert.Contains(
                viewModel.CurrentSystemSites,
                row => row.Reference == target.Reference && row.IsDestination);

            viewModel.AutoShowGuardianSummary = false;
            Assert.False(viewModel.ShouldShowGuardianSystemSummary);
            viewModel.AutoShowGuardianSummary = true;
            Assert.True(viewModel.ShouldShowGuardianSystemSummary);

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
            });
            Assert.False(viewModel.ShouldShowGuardianSystemSummary);

            await viewModel.ApplyJournalEventsAsync(
                [Parse("""{"event":"Music","MusicTrack":"SystemMap"}""")],
                null);
            Assert.True(viewModel.ShouldShowGuardianSystemSummary);

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

            await viewModel.CopyBodyNameAsync();
            Assert.Equal("Synuefe XR-H d11-102 1 b", copied);
            Assert.Contains("Copied body name", viewModel.StatusMessage);

            await viewModel.CopyNotesAsync();
            Assert.Equal("commander note", copied);
            Assert.Contains("Copied commander notes", viewModel.StatusMessage);
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
            var viewModel = new GuardianViewModel(root,
                new GuardianViewModelOptions
                {
                    References = catalog,
                    PublishedSites = new GuardianPublishedSiteCatalog([]),
                    Templates = new GuardianSiteTemplateCatalog([]),
                });

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
    public void SiteBrowserRestoresLegacyImageIndicatorAndColumnSorting()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var screenshotRoot = Path.Combine(root, "screenshots");
            var alpha = CreateReference(
                1,
                GuardianSiteKind.Structure,
                "Alpha",
                1,
                new GalacticCoordinate(0, 0, 0));
            var zulu = CreateReference(
                2,
                GuardianSiteKind.Structure,
                "Zulu",
                2,
                new GalacticCoordinate(1, 0, 0));
            var viewModel = new GuardianViewModel(root,
                new GuardianViewModelOptions
                {
                    ScreenshotTargetFolderProvider = () => screenshotRoot,
                    References = new GuardianSiteCatalog([alpha, zulu]),
                    PublishedSites = new GuardianPublishedSiteCatalog([]),
                    Templates = new GuardianSiteTemplateCatalog([]),
                });

            Assert.All(viewModel.Rows, row => Assert.False(row.HasImages));
            var zuluFolder = Path.Combine(screenshotRoot, zulu.SystemName);
            Directory.CreateDirectory(zuluFolder);
            File.WriteAllText(
                Path.Combine(
                    zuluFolder,
                    $"{zulu.FullBodyName} (2026-08-03 120000), {zulu.SiteType}.png"),
                string.Empty);

            viewModel.RefreshScreenshotAvailability();
            Assert.True(viewModel.Rows.Single(row =>
                row.Reference == zulu).HasImages);
            Assert.False(viewModel.Rows.Single(row =>
                row.Reference == alpha).HasImages);
            Assert.Equal("▲", viewModel.DistanceSortIndicator);
            Assert.Equal(string.Empty, viewModel.ImagesSortIndicator);

            viewModel.SortSitesCommand.Execute("Images");
            Assert.False(viewModel.Rows[0].HasImages);
            Assert.Equal(string.Empty, viewModel.DistanceSortIndicator);
            Assert.Equal("▲", viewModel.ImagesSortIndicator);
            viewModel.SortSitesCommand.Execute("Images");
            Assert.True(viewModel.Rows[0].HasImages);
            Assert.Contains("images descending", viewModel.SortStatusText);
            Assert.Equal("▼", viewModel.ImagesSortIndicator);

            viewModel.SortSitesCommand.Execute("Id");
            Assert.Equal(alpha, viewModel.Rows[0].Reference);
            Assert.Equal("▲", viewModel.IdSortIndicator);
            Assert.Equal(string.Empty, viewModel.ImagesSortIndicator);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task PublishedSiteHydratesLiveSurveyAndCodexMarksNearbyRelic()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var reference = CreateProximityReference();
            var published = CreatePublishedSite(reference, []) with
            {
                SiteHeading = 90,
                ObeliskGroups = "A",
            };
            var viewModel = new GuardianViewModel(root,
                new GuardianViewModelOptions
                {
                    References = new GuardianSiteCatalog([reference]),
                    PublishedSites = new GuardianPublishedSiteCatalog([published]),
                    Templates = new GuardianSiteTemplateCatalog(
                [
                    new GuardianSiteTemplate(
                        "Test",
                        "Test",
                        string.Empty,
                        new GuardianMapPoint(0, 0),
                        1,
                        [
                            new GuardianPointOfInterest(
                                "t1",
                                GuardianPoiType.Relic,
                                90,
                                10,
                                0),
                        ],
                        [],
                        new Dictionary<string, GuardianMapPoint>()),
                ]),
                });
            await viewModel.LoadProfileAsync("F123", isOdyssey: true);
            await viewModel.ApplyJournalEventsAsync(
                [Parse(
                    """{"event":"ApproachSettlement","Name":"$Ancient:#index=1;","SystemAddress":42,"BodyID":7,"BodyName":"Test A 1","Latitude":0,"Longitude":0}""")],
                "Drew");

            Assert.Equal(GuardianLiveMapMode.Map, viewModel.LiveMapMode);
            Assert.Equal("Test", viewModel.ResolvedActiveSiteType);
            var saved = await new GuardianCommanderDataReader(root)
                .ReadAsync("F123", isOdyssey: true);
            Assert.Equal(90, Assert.Single(saved.Surveys).Survey.SiteHeading);
            Assert.Contains('A', Assert.Single(saved.Surveys).ObeliskGroups);

            viewModel.UpdateStatus(StatusNorthOfSite(10));
            await viewModel.ApplyJournalEventsAsync(
                [Parse("""{"event":"CodexEntry","Name":"$Codex_Ent_Unknown_Name;"}""")],
                "Drew");
            saved = await new GuardianCommanderDataReader(root)
                .ReadAsync("F123", isOdyssey: true);
            Assert.Equal(
                GuardianPoiStatus.Present,
                Assert.Single(saved.Surveys).Survey.PoiStatuses["t1"]);

            viewModel.UpdateStatus(StatusNorthOfSite(10) with
            {
                Flags = StatusFlags.HasLatLong,
                Flags2 = StatusFlags2.OnFoot
                    | StatusFlags2.OnFootOnPlanet
                    | StatusFlags2.OnFootExterior,
                SelectedWeapon = "$humanoid_companalyser_name;",
            });
            Assert.True(viewModel.IsGuardianOnFootRelicVisible);
            Assert.False(viewModel.IsGuardianPoiChoiceVisible);
            Assert.Contains("RELIC TOWER", viewModel.GuardianStatusTitle);
            Assert.Contains("shields", viewModel.GuardianOnFootFooter);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task GuardianBeaconCodexEntryCreatesCommanderBeaconVisit()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var viewModel = new GuardianViewModel(root,
                new GuardianViewModelOptions
                {
                    References = new GuardianSiteCatalog([]),
                    PublishedSites = new GuardianPublishedSiteCatalog([]),
                    Templates = new GuardianSiteTemplateCatalog([]),
                });
            await viewModel.LoadProfileAsync("F123", isOdyssey: true);

            await viewModel.ApplyJournalEventsAsync(
                [Parse(
                    """{"timestamp":"2026-08-03T12:00:00Z","event":"CodexEntry","Name":"$Codex_Ent_Guardian_Beacons_Name;","System":"Test System","SystemAddress":42,"BodyID":7,"BodyName":"Test System A 1","Latitude":1.25,"Longitude":-2.5}""")],
                "Drew");

            var data = await new GuardianCommanderDataReader(root)
                .ReadAsync("F123", isOdyssey: true);
            var beacon = Assert.Single(data.Beacons);
            Assert.Equal("Test System", beacon.SystemName);
            Assert.Equal(
                new GuardianSurfaceLocation(1.25, -2.5),
                Assert.Single(beacon.ScannedLocations).Value);
            Assert.Contains(viewModel.Rows, row =>
                row.Reference.DisplayId == "GB LOCAL"
                && row.Visit.HasCommanderData);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task DoubleCockpitModeToggleSavesGuardianHeadingOnlyWhenLive()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var reference = CreateProximityReference();
            var viewModel = new GuardianViewModel(root,
                new GuardianViewModelOptions
                {
                    References = new GuardianSiteCatalog([reference]),
                    PublishedSites = new GuardianPublishedSiteCatalog(
                    [CreatePublishedSite(reference, [])]),
                    Templates = new GuardianSiteTemplateCatalog(
                [
                    new GuardianSiteTemplate(
                        "Test",
                        "Test",
                        string.Empty,
                        new GuardianMapPoint(0, 0),
                        1,
                        [],
                        [],
                        new Dictionary<string, GuardianMapPoint>()),
                ]),
                });
            await viewModel.LoadProfileAsync("F123", isOdyssey: true);
            await viewModel.ApplyJournalEventsAsync(
            [
                Parse(
                    """{"event":"ApproachSettlement","Name":"$Ancient:#index=1;","Name_Localised":"Ancient Ruins (1)","SystemAddress":42,"BodyID":7,"BodyName":"Test A 1","Latitude":0,"Longitude":0}"""),
            ],
            "Drew");
            Assert.Equal(GuardianLiveMapMode.Heading, viewModel.LiveMapMode);
            var started = new DateTimeOffset(
                2026,
                7,
                25,
                12,
                0,
                0,
                TimeSpan.Zero);
            var normal = StatusNorthOfSite(10) with { Heading = 123 };
            var analysis = normal with
            {
                Flags = normal.Flags | StatusFlags.HudInAnalysisMode,
            };

            await viewModel.UpdateStatusAsync(
                normal,
                allowGesture: true,
                observedAt: started);
            await viewModel.UpdateStatusAsync(
                analysis,
                allowGesture: true,
                observedAt: started.AddSeconds(1));
            Assert.True(viewModel.IsBlinkGesturePrimed);
            await viewModel.UpdateStatusAsync(
                normal,
                allowGesture: true,
                observedAt: started.AddSeconds(2));

            var saved = await new GuardianCommanderDataReader(root)
                .ReadAsync("F123", isOdyssey: true);
            Assert.Equal(123, Assert.Single(saved.Surveys).Survey.SiteHeading);
            Assert.Equal(GuardianLiveMapMode.Map, viewModel.LiveMapMode);
            Assert.Contains("blink gesture", viewModel.StatusMessage);

            await viewModel.ApplyJournalEventsAsync(
                [Parse("""{"event":"SendText","Message":".heading 90"}""")],
                "Drew");
            viewModel.UpdateStatus(normal);
            await viewModel.UpdateStatusAsync(
                analysis,
                allowGesture: true,
                observedAt: started.AddSeconds(3));
            await viewModel.UpdateStatusAsync(
                normal,
                allowGesture: false,
                observedAt: started.AddSeconds(4));
            saved = await new GuardianCommanderDataReader(root)
                .ReadAsync("F123", isOdyssey: true);
            Assert.Equal(90, Assert.Single(saved.Surveys).Survey.SiteHeading);

            await viewModel.ApplyJournalEventsAsync(
                [Parse("""{"event":"SendText","Message":".heading 0"}""")],
                "Drew");
            Assert.Equal(GuardianLiveMapMode.Heading, viewModel.LiveMapMode);
            await viewModel.ApplyJournalEventsAsync(
                [Parse("""{"event":"SendText","Message":".map"}""")],
                "Drew");
            Assert.Equal(GuardianLiveMapMode.Map, viewModel.LiveMapMode);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task FireGroupChoosesRuinsTypeAndDoubleToggleConfirmsIt()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var reference = CreateProximityReference() with { SiteType = "" };
            var templates = RuinsSiteTypes
                .Select(type => new GuardianSiteTemplate(
                    type,
                    type,
                    string.Empty,
                    new GuardianMapPoint(0, 0),
                    1,
                    [],
                    [],
                    new Dictionary<string, GuardianMapPoint>()))
                .ToArray();
            var viewModel = new GuardianViewModel(root,
                new GuardianViewModelOptions
                {
                    References = new GuardianSiteCatalog([reference]),
                    PublishedSites = new GuardianPublishedSiteCatalog([]),
                    Templates = new GuardianSiteTemplateCatalog(templates),
                });
            await viewModel.LoadProfileAsync("F123", isOdyssey: true);
            await viewModel.ApplyJournalEventsAsync(
                [Parse(
                    """{"event":"ApproachSettlement","Name":"$Ancient:#index=1;","SystemAddress":42,"BodyID":7,"BodyName":"Test A 1","Latitude":0,"Longitude":0}""")],
                "Drew");

            Assert.Equal(GuardianLiveMapMode.SiteType, viewModel.LiveMapMode);
            Assert.Contains(
                "active fire group",
                viewModel.GuardianStatusDetail,
                StringComparison.Ordinal);
            var started = new DateTimeOffset(
                2026,
                8,
                12,
                12,
                0,
                0,
                TimeSpan.Zero);
            var normal = StatusNorthOfSite(10) with { FireGroup = 1 };
            var analysis = normal with
            {
                Flags = normal.Flags | StatusFlags.HudInAnalysisMode,
            };

            await viewModel.UpdateStatusAsync(normal, true, started);
            Assert.True(viewModel.IsGuardianChoiceTwoSelected);
            Assert.Equal(GuardianLiveMapMode.SiteType, viewModel.LiveMapMode);
            var saved = await new GuardianCommanderDataReader(root)
                .ReadAsync("F123", isOdyssey: true);
            Assert.NotEqual("Beta", Assert.Single(saved.Surveys).SiteType);
            await viewModel.UpdateStatusAsync(
                analysis,
                true,
                started.AddSeconds(1));
            await viewModel.UpdateStatusAsync(
                normal,
                true,
                started.AddSeconds(2));

            saved = await new GuardianCommanderDataReader(root)
                .ReadAsync("F123", isOdyssey: true);
            Assert.Equal("Beta", Assert.Single(saved.Surveys).SiteType);
            Assert.Equal(GuardianLiveMapMode.Heading, viewModel.LiveMapMode);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task UnknownStructureSiteTypeGuidanceUsesSiteCommand()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var viewModel = new GuardianViewModel(root,
                new GuardianViewModelOptions
                {
                    References = new GuardianSiteCatalog([]),
                    PublishedSites = new GuardianPublishedSiteCatalog([]),
                    Templates = new GuardianSiteTemplateCatalog([]),
                });
            await viewModel.LoadProfileAsync("F123", isOdyssey: true);
            await viewModel.ApplyJournalEventsAsync(
                [Parse(
                    """{"event":"ApproachSettlement","Name":"$Ancient_Tiny_999:#index=1;","SystemAddress":42,"BodyID":7,"BodyName":"Test A 1","Latitude":0,"Longitude":0}""")],
                "Drew");

            Assert.Equal(GuardianSiteKind.Structure, viewModel.ActiveSite?.Kind);
            Assert.Equal(GuardianLiveMapMode.SiteType, viewModel.LiveMapMode);
            Assert.Contains(".site <type>", viewModel.GuardianStatusDetail);
            Assert.DoesNotContain(
                "fire group",
                viewModel.GuardianStatusDetail,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task FireGroupChoosesPointStateAndDoubleToggleConfirmsIt()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var reference = CreateProximityReference() with { SiteHeading = 90 };
            var published = CreatePublishedSite(reference, []) with
            {
                SiteHeading = 90,
            };
            var viewModel = new GuardianViewModel(root,
                new GuardianViewModelOptions
                {
                    References = new GuardianSiteCatalog([reference]),
                    PublishedSites = new GuardianPublishedSiteCatalog(
                        [published]),
                    Templates = new GuardianSiteTemplateCatalog(
                    [
                        new GuardianSiteTemplate(
                            "Test",
                            "Test",
                            string.Empty,
                            new GuardianMapPoint(0, 0),
                            1,
                            [
                                new GuardianPointOfInterest(
                                    "c1",
                                    GuardianPoiType.Casket,
                                    0,
                                    10,
                                    0),
                            ],
                            [],
                            new Dictionary<string, GuardianMapPoint>()),
                    ]),
                });
            await viewModel.LoadProfileAsync("F123", isOdyssey: true);
            await viewModel.ApplyJournalEventsAsync(
                [Parse(
                    """{"event":"ApproachSettlement","Name":"$Ancient:#index=1;","SystemAddress":42,"BodyID":7,"BodyName":"Test A 1","Latitude":0,"Longitude":0}""")],
                "Drew");

            var started = new DateTimeOffset(
                2026,
                8,
                12,
                12,
                0,
                0,
                TimeSpan.Zero);
            var normal = StatusNorthOfSite(10) with { FireGroup = 2 };
            var analysis = normal with
            {
                Flags = normal.Flags | StatusFlags.HudInAnalysisMode,
            };
            await viewModel.UpdateStatusAsync(normal, true, started);

            Assert.True(viewModel.IsGuardianPoiChoiceVisible);
            Assert.True(viewModel.IsGuardianChoiceThreeSelected);
            var saved = await new GuardianCommanderDataReader(root)
                .ReadAsync("F123", isOdyssey: true);
            var initialSurvey = Assert.Single(saved.Surveys).Survey;
            Assert.False(
                initialSurvey.PoiStatuses.TryGetValue(
                    "c1",
                    out var initialStatus)
                && initialStatus == GuardianPoiStatus.Empty);
            await viewModel.UpdateStatusAsync(
                analysis,
                true,
                started.AddSeconds(1));
            await viewModel.UpdateStatusAsync(
                normal,
                true,
                started.AddSeconds(2));

            saved = await new GuardianCommanderDataReader(root)
                .ReadAsync("F123", isOdyssey: true);
            Assert.Equal(
                GuardianPoiStatus.Empty,
                Assert.Single(saved.Surveys).Survey.PoiStatuses["c1"]);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ConfiguredGuardianGestureIsReflectedByOverlayGuidance()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var viewModel = new GuardianViewModel(root,
                new GuardianViewModelOptions
                {
                    GesturePreferences = new GuardianGesturePreferences(
                    StatusFlags.LightsOn,
                    1_500),
                });

            viewModel.UpdateStatus(new EliteStatus
            {
                Flags = StatusFlags.InMainShip,
            });
            Assert.Contains("lights", viewModel.BlinkGestureText);
            Assert.Contains("lights", viewModel.GuardianChoiceGestureText);

            viewModel.UpdateStatus(new EliteStatus
            {
                Flags2 = StatusFlags2.OnFoot
                    | StatusFlags2.OnFootExterior,
            });
            Assert.Contains("shields", viewModel.BlinkGestureText);
            Assert.Contains("shields", viewModel.GuardianChoiceGestureText);
            Assert.Contains("shields", viewModel.GuardianMaterialCapacityWarning);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task StatusOverlayKeepsNearbyObeliskVisibleOutsideScanRange()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var reference = CreateProximityReference();
            var obelisk = new GuardianObelisk("A01", "H1", false, ["ca"]);
            var published = CreatePublishedSite(reference, [obelisk]) with
            {
                SiteHeading = 90,
            };
            var viewModel = new GuardianViewModel(root,
                new GuardianViewModelOptions
                {
                    References = new GuardianSiteCatalog([reference]),
                    PublishedSites = new GuardianPublishedSiteCatalog([published]),
                    Templates = new GuardianSiteTemplateCatalog(
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
                });
            await viewModel.LoadProfileAsync("F123", isOdyssey: true);
            await viewModel.ApplyJournalEventsAsync(
                [Parse(
                    """{"event":"ApproachSettlement","Name":"$Ancient:#index=1;","SystemAddress":42,"BodyID":7,"BodyName":"Test A 1","Latitude":0,"Longitude":0}""")],
                "Drew");

            viewModel.UpdateStatus(StatusEastOfSite(40));

            Assert.Equal(GuardianLiveMapMode.Map, viewModel.LiveMapMode);
            Assert.Null(viewModel.CurrentObelisk);
            Assert.True(viewModel.IsGuardianObeliskVisible);
            Assert.Contains("A01", viewModel.GuardianStatusObeliskTitle);
            Assert.Contains("25 m", viewModel.GuardianStatusObeliskFooter);
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
            var viewModel = new GuardianViewModel(root,
                new GuardianViewModelOptions
                {
                    References = new GuardianSiteCatalog([reference]),
                    PublishedSites = new GuardianPublishedSiteCatalog(
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
                    Templates = new GuardianSiteTemplateCatalog(
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
                    new GuardianSiteTemplate(
                        "Alpha",
                        "Alpha",
                        string.Empty,
                        new GuardianMapPoint(0, 0),
                        1,
                        [],
                        [],
                        new Dictionary<string, GuardianMapPoint>()),
                ]),
                    RamTah = ramTah,
                });
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
            Assert.True(viewModel.ShouldShowLiveSiteOverlay);
            Assert.True(viewModel.ShouldShowGuardianStatusOverlay);
            Assert.True(viewModel.IsLiveStatusVisible);
            viewModel.SetLiveStatusObscured(true);
            Assert.False(viewModel.IsLiveStatusVisible);
            Assert.True(viewModel.ShouldShowLiveSiteOverlay);
            Assert.False(viewModel.ShouldShowGuardianStatusOverlay);
            viewModel.SetLiveStatusObscured(false);
            Assert.True(viewModel.IsLiveStatusVisible);
            Assert.True(viewModel.ShouldShowGuardianStatusOverlay);
            Assert.Equal(3, viewModel.ActiveMapScale);
            Assert.True(viewModel.IsAutomaticMapZoom);
            Assert.Contains("0.0 m", viewModel.NearbyPointText);
            Assert.Contains("Guardian Casket 1/2", viewModel.CurrentObeliskRequirementsText);
            Assert.False(viewModel.HasCurrentObeliskArtifacts);
            Assert.True(viewModel.MapProjection?.Points.Single().IsActiveObelisk);
            Assert.True(viewModel.ActiveMapProjection?.Points.Single().IsActiveObelisk);
            Assert.True(viewModel.ShouldShowRamTahOverlay);
            viewModel.AutoShowRamTah = false;
            Assert.False(viewModel.ShouldShowRamTahOverlay);
            viewModel.AutoShowRamTah = true;
            Assert.True(viewModel.ShouldShowRamTahOverlay);
            viewModel.EnableGuardianSites = false;
            Assert.False(viewModel.ShouldShowLiveSiteOverlay);
            Assert.False(viewModel.ShouldShowGuardianStatusOverlay);
            Assert.False(viewModel.ShouldShowRamTahOverlay);
            viewModel.EnableGuardianSites = true;
            Assert.True(viewModel.ShouldShowLiveSiteOverlay);
            Assert.True(viewModel.ShouldShowGuardianStatusOverlay);
            Assert.True(viewModel.ShouldShowRamTahOverlay);
            viewModel.UpdateStatus(StatusNorthOfSite(10) with
            {
                GuiFocus = GuiFocus.RolePanel,
            });
            Assert.True(viewModel.ShouldShowLiveSiteOverlay);
            Assert.True(viewModel.ShouldShowGuardianStatusOverlay);
            Assert.False(viewModel.ShouldShowRamTahOverlay);
            viewModel.UpdateStatus(StatusNorthOfSite(10) with
            {
                GuiFocus = GuiFocus.InternalPanel,
            });
            Assert.False(viewModel.ShouldShowLiveSiteOverlay);
            Assert.False(viewModel.ShouldShowGuardianStatusOverlay);
            Assert.True(viewModel.ShouldShowRamTahOverlay);
            viewModel.UpdateStatus(StatusNorthOfSite(10));
            Assert.True(viewModel.SurveyEditor.HasLiveMeasurement);
            Assert.Contains("10.0 m from origin", viewModel.SurveyEditor.LiveMeasurementText);
            var ramTahLog = Assert.Single(viewModel.CurrentRamTahLogs);
            Assert.Same(
                viewModel.CurrentRamTahLogs,
                viewModel.CurrentRamTahLogs);
            Assert.Equal("H1", ramTahLog.LogCode);
            Assert.Equal("MISSING", ramTahLog.ArtifactStatus);
            Assert.Equal("A01", ramTahLog.ObeliskNamesText);
            Assert.True(ramTahLog.IsCurrentObelisk);
            Assert.False(ramTahLog.IsTargetObelisk);
            await viewModel.ApplyJournalEventsAsync(
                [Parse("""{"event":"SendText","Message":".to A01"}""")],
                "Drew");
            Assert.True(Assert.Single(
                viewModel.CurrentRamTahLogs).IsTargetObelisk);

            await viewModel.ApplyJournalEventsAsync(
                [Parse(
                    """{"event":"Materials","Encoded":[{"Name":"ancientbiologicaldata","Count":150}]}""")],
                "Drew");
            Assert.True(viewModel.AreGuardianEncodedMaterialsFull);
            Assert.True(viewModel.HasGuardianMaterialCapacityWarning);
            viewModel.UpdateOverlayAnimation(DateTimeOffset.UnixEpoch);
            var firstCapacityWarning = viewModel.GuardianMaterialCapacityWarning;
            viewModel.UpdateOverlayAnimation(
                DateTimeOffset.UnixEpoch.AddMilliseconds(750));
            Assert.NotEqual(
                firstCapacityWarning,
                viewModel.GuardianMaterialCapacityWarning);

            await viewModel.ApplyJournalEventsAsync(
                [Parse("""{"event":"CollectCargo","Type":"ancientcasket"}""")],
                "Drew");
            Assert.True(viewModel.HasCurrentObeliskArtifacts);
            viewModel.ClearCargo();
            Assert.False(viewModel.HasCurrentObeliskArtifacts);
            viewModel.UpdateCargo(new CargoSnapshot(
                DateTimeOffset.UtcNow,
                "Cargo",
                "SRV",
                1,
                [new CargoItem("ancientcasket", "Guardian Casket", 1, 0)]));

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

            await viewModel.ApplyJournalEventsAsync(
                [Parse("""{"event":"SendText","Message":".os"}""")],
                "Drew");

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
            Assert.False(viewModel.CurrentObelisk?.Scanned);
            ramTah.LoadProfile(
                "F123",
                "Drew",
                true,
                RamTahSnapshot.Empty);
            await viewModel.ApplyJournalEventsAsync(
            [
                Parse(
                    """{"event":"MissionAccepted","Name":"Mission_TheDead_name"}"""),
                Parse(
                    """{"event":"MaterialCollected","Name":"guardian_powercell","Count":1}"""),
            ],
            "Drew");

            Assert.True(viewModel.CurrentObelisk?.Scanned);
            Assert.True(ramTah.IsAncientRuinsMissionActive);
            Assert.True(ramTah.IsLogCompleted(RamTahMission.AncientRuins, "H1"));
            Assert.Empty(viewModel.CurrentRamTahLogs);
            var saved = await new GuardianCommanderDataReader(root)
                .ReadAsync("F123", isOdyssey: true);
            Assert.True(Assert.Single(saved.Surveys).ActiveObelisks.Single().Scanned);

            Assert.Equal(GuardianLiveMapMode.Heading, viewModel.LiveMapMode);
            Assert.Equal(GuardianAlignmentMode.Buttress, viewModel.AlignmentMode);
            Assert.Equal(20, viewModel.AlignmentTargetAltitude);
            Assert.True(viewModel.IsAlignmentVisible);
            viewModel.ShowRuinsMeasurementGrid = false;
            Assert.False(viewModel.IsAlignmentVisible);
            viewModel.ShowRuinsMeasurementGrid = true;
            await viewModel.ApplyJournalEventsAsync(
            [
                Parse("""{"event":"SendText","Message":".heading 90"}"""),
                Parse("""{"event":"SendText","Message":".note Mixed Case Note"}"""),
                Parse("""{"event":"SendText","Message":".tower"}"""),
                Parse("""{"event":"SendText","Message":".to A01"}"""),
                Parse("""{"event":"SendText","Message":"z 18"}"""),
            ],
            "Drew");

            Assert.Equal(GuardianLiveMapMode.Map, viewModel.LiveMapMode);
            Assert.Equal("A01", viewModel.TargetObeliskName);
            Assert.Equal(18, viewModel.ActiveMapScale);
            Assert.False(viewModel.IsAutomaticMapZoom);
            saved = await new GuardianCommanderDataReader(root)
                .ReadAsync("F123", isOdyssey: true);
            var commandSurvey = Assert.Single(saved.Surveys);
            Assert.Equal(90, commandSurvey.Survey.SiteHeading);
            Assert.Equal(0, commandSurvey.Survey.RelicTowerHeading);
            Assert.Contains("Mixed Case Note", commandSurvey.Notes);

            await viewModel.ApplyJournalEventsAsync(
                [Parse("""{"event":"SendText","Message":".note ignored bootstrap"}""")],
                "Drew",
                allowLiveCommands: false);
            saved = await new GuardianCommanderDataReader(root)
                .ReadAsync("F123", isOdyssey: true);
            Assert.DoesNotContain("ignored bootstrap", Assert.Single(saved.Surveys).Notes);

            await viewModel.ApplyJournalEventsAsync(
                [Parse("""{"event":"SendText","Message":"z"}""")],
                "Drew");
            Assert.True(viewModel.IsAutomaticMapZoom);

            viewModel.UpdateStatus(StatusNorthOfSite(20) with { Heading = 123 });
            await viewModel.ApplyJournalEventsAsync(
                [Parse("""{"event":"SendText","Message":".add orb"}""")],
                "Drew");
            saved = await new GuardianCommanderDataReader(root)
                .ReadAsync("F123", isOdyssey: true);
            var rawPoint = Assert.Single(
                Assert.Single(saved.Surveys).Survey.RawPointsOfInterest!);
            Assert.Equal("x1", rawPoint.Name);
            Assert.Equal(GuardianPoiType.Orb, rawPoint.Type);
            Assert.Equal(90, rawPoint.Angle, precision: 6);
            Assert.Equal(20, rawPoint.Distance, precision: 1);
            Assert.Equal(33, rawPoint.Rotation, precision: 6);

            await viewModel.ApplyJournalEventsAsync(
                [Parse("""{"event":"SendText","Message":".empty"}""")],
                "Drew");
            saved = await new GuardianCommanderDataReader(root)
                .ReadAsync("F123", isOdyssey: true);
            Assert.Equal(
                GuardianPoiStatus.Empty,
                Assert.Single(saved.Surveys).Survey.PoiStatuses["x1"]);

            await viewModel.ApplyJournalEventsAsync(
            [
                Parse("""{"event":"SendText","Message":".remove"}"""),
                Parse("""{"event":"SendText","Message":".site Alpha"}"""),
            ],
            "Drew");
            saved = await new GuardianCommanderDataReader(root)
                .ReadAsync("F123", isOdyssey: true);
            commandSurvey = Assert.Single(saved.Surveys);
            Assert.Equal("Alpha", commandSurvey.SiteType);
            Assert.Null(commandSurvey.Survey.RawPointsOfInterest);
            Assert.DoesNotContain("x1", commandSurvey.Survey.PoiStatuses.Keys);

            await viewModel.ApplyJournalEventsAsync(
                [Parse("""{"event":"SendText","Message":".aerial"}""")],
                "Drew");
            Assert.Equal(GuardianLiveMapMode.Origin, viewModel.LiveMapMode);
            Assert.True(viewModel.IsGuardianOriginVisible);
            Assert.False(viewModel.IsGuardianNoPointVisible);
            Assert.Equal("ALIGN SITE ORIGIN", viewModel.GuardianStatusTitle);
            viewModel.UpdateStatus(StatusNorthOfSite(20) with
            {
                Flags = StatusFlags.HasLatLong | StatusFlags.InMainShip,
                Altitude = 1_200,
                Heading = 45,
            });
            Assert.Equal(GuardianAlignmentMode.Alpha, viewModel.AlignmentMode);
            Assert.Equal(1_200, viewModel.AlignmentTargetAltitude);
            Assert.Equal(0.8, viewModel.AlignmentOpacity);
            Assert.True(viewModel.IsAlignmentVisible);

            viewModel.UpdateStatus(StatusNorthOfSite(20) with
            {
                Flags = StatusFlags.HasLatLong | StatusFlags.InMainShip,
                Flags2 = StatusFlags2.GlideMode,
                Altitude = 1_000,
                Heading = 45,
            });
            Assert.Equal(0.1, viewModel.AlignmentOpacity, precision: 6);
            Assert.True(viewModel.IsGlideApproach);
            Assert.False(viewModel.ShouldShowLiveSiteOverlay);
            Assert.True(viewModel.ShouldShowGuardianStatusOverlay);
            Assert.Contains("RUINS", viewModel.GlideApproachTitle);
            Assert.Contains("Alpha", viewModel.GlideApproachText);
            viewModel.SuppressForActiveBuildProjects = true;
            viewModel.SetActiveBuildProjects(true);
            Assert.False(viewModel.ShouldShowLiveSiteOverlay);
            Assert.False(viewModel.ShouldShowGuardianStatusOverlay);
            viewModel.SetActiveBuildProjects(false);
            Assert.False(viewModel.ShouldShowLiveSiteOverlay);
            Assert.True(viewModel.ShouldShowGuardianStatusOverlay);
            viewModel.ShowAerialAlignmentGrid = false;
            Assert.False(viewModel.IsAlignmentVisible);
            viewModel.ShowAerialAlignmentGrid = true;

            viewModel.UpdateStatus(StatusNorthOfSite(20) with
            {
                Flags = StatusFlags.HasLatLong
                    | StatusFlags.InMainShip
                    | StatusFlags.Supercruise,
                Altitude = 1_000,
            });
            Assert.False(viewModel.ShouldShowLiveSiteOverlay);
            Assert.False(viewModel.ShouldShowGuardianStatusOverlay);

            viewModel.UpdateStatus(StatusNorthOfSite(20) with
            {
                Flags = StatusFlags.HasLatLong | StatusFlags.InMainShip,
                Flags2 = StatusFlags2.GlideMode,
                Altitude = 1_000,
            });
            Assert.False(viewModel.ShouldShowLiveSiteOverlay);
            Assert.True(viewModel.ShouldShowGuardianStatusOverlay);

            await viewModel.ApplyJournalEventsAsync(
            [
                Parse("""{"event":"SendText","Message":".site Test"}"""),
                Parse("""{"event":"SendText","Message":".map"}"""),
            ],
            "Drew");
            Assert.Equal(GuardianLiveMapMode.Map, viewModel.LiveMapMode);

            var screenshot = Parse(
                """{"timestamp":"2026-07-24T10:09:59Z","event":"Screenshot","Filename":"Screenshot_0001.bmp","System":"Test","Body":"Test A 1","Latitude":0,"Longitude":0,"Altitude":1200}""");
            var screenshotContexts = await viewModel.ApplyJournalEventsAsync(
                [
                    screenshot,
                    Parse("""{"timestamp":"2026-07-24T10:10:00Z","event":"SupercruiseEntry"}"""),
                ],
                "Drew");

            var screenshotContext = Assert.Single(screenshotContexts).Value;
            Assert.Equal(GuardianSiteKind.Ruins, screenshotContext.SiteKind);
            Assert.Equal(1, screenshotContext.SiteIndex);
            Assert.Equal("Test", screenshotContext.SiteType);
            Assert.Equal("Ancient Ruins (1)", screenshotContext.SiteName);
            Assert.Null(viewModel.CurrentObelisk);
            Assert.Null(viewModel.Proximity);
            Assert.Null(viewModel.SelectedMapCommanderPosition);
            Assert.Null(viewModel.TargetObeliskName);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task LegacyPointStatusCommandsPersistPresentAbsentAndEmpty()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var reference = CreateProximityReference() with { SiteHeading = 90 };
            var published = CreatePublishedSite(reference, []) with
            {
                SiteHeading = 90,
            };
            var viewModel = new GuardianViewModel(root,
                new GuardianViewModelOptions
                {
                    References = new GuardianSiteCatalog([reference]),
                    PublishedSites = new GuardianPublishedSiteCatalog([published]),
                    Templates = new GuardianSiteTemplateCatalog(
                [
                    new GuardianSiteTemplate(
                        "Test",
                        "Test",
                        string.Empty,
                        new GuardianMapPoint(0, 0),
                        1,
                        [
                            new GuardianPointOfInterest(
                                "p1",
                                GuardianPoiType.Orb,
                                0,
                                0,
                                0),
                            new GuardianPointOfInterest(
                                "p2",
                                GuardianPoiType.Tablet,
                                180,
                                100,
                                0),
                        ],
                        [],
                        new Dictionary<string, GuardianMapPoint>()),
                ]),
                });
            await viewModel.LoadProfileAsync("F123", isOdyssey: true);
            await viewModel.ApplyJournalEventsAsync(
                [Parse(
                    """{"event":"ApproachSettlement","Name":"$Ancient:#index=1;","Name_Localised":"Ancient Ruins (1)","SystemAddress":42,"BodyID":7,"BodyName":"Test A 1","Latitude":0,"Longitude":0}""")],
                "Drew");
            viewModel.UpdateStatus(StatusNorthOfSite(0));
            Assert.Equal("p1", viewModel.Proximity?.NearestPoint?.Point.Name);
            Assert.Same(
                viewModel.Proximity,
                viewModel.SelectedMapCommanderPosition);
            Assert.Equal("p1", viewModel.SelectedMapTargetPointName);
            Assert.Equal("p1", viewModel.SelectedMapPointName);
            Assert.Equal("p1", viewModel.ActiveMapSelectedPointName);
            Assert.Null(viewModel.SurveyEditor.SelectedPointName);

            viewModel.UpdateStatus(StatusNorthOfSite(76));
            Assert.Null(viewModel.SelectedMapPointName);
            Assert.Null(viewModel.ActiveMapSelectedPointName);

            viewModel.UpdateStatus(StatusNorthOfSite(0));
            Assert.Equal("p1", viewModel.SelectedMapPointName);
            Assert.Equal("p1", viewModel.ActiveMapSelectedPointName);

            foreach (var (command, expected) in new[]
                     {
                         (".p", GuardianPoiStatus.Present),
                         (".m", GuardianPoiStatus.Absent),
                         (".e", GuardianPoiStatus.Empty),
                     })
            {
                await viewModel.ApplyJournalEventsAsync(
                    [Parse($$"""{"event":"SendText","Message":"{{command}}"}""")],
                    "Drew");
                var saved = await new GuardianCommanderDataReader(
                        root,
                        new GuardianPublishedSiteCatalog([published]))
                    .ReadAsync("F123", isOdyssey: true);
                Assert.Equal(
                    expected,
                    Assert.Single(saved.Surveys).Survey.PoiStatuses["p1"]);
            }

            var changed = new List<string?>();
            viewModel.PropertyChanged += (_, args) => changed.Add(
                args.PropertyName);
            viewModel.SurveyEditor.SelectedPointName = "p2";

            Assert.Equal("p2", viewModel.SelectedMapPointName);
            Assert.Equal("p2", viewModel.ActiveMapSelectedPointName);
            Assert.Contains(nameof(viewModel.SelectedMapPointName), changed);
            Assert.Contains(nameof(viewModel.ActiveMapSelectedPointName), changed);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task SurfaceOriginEditsPreviewCommanderPositionAndResetImmediately()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var reference = CreateProximityReference();
            var template = new GuardianSiteTemplate(
                "Test",
                "Test",
                string.Empty,
                new GuardianMapPoint(0, 0),
                1,
                [],
                [],
                new Dictionary<string, GuardianMapPoint>());
            var viewModel = new GuardianViewModel(
                root,
                new GuardianViewModelOptions
                {
                    References = new GuardianSiteCatalog([reference]),
                    Templates = new GuardianSiteTemplateCatalog([template]),
                });
            await viewModel.LoadProfileAsync("F123", isOdyssey: true);
            await viewModel.ApplyJournalEventsAsync(
                [Parse(
                    """{"event":"ApproachSettlement","Name":"$Ancient:#index=1;","Name_Localised":"Ancient Ruins (1)","SystemAddress":42,"BodyID":7,"BodyName":"Test A 1","Latitude":0,"Longitude":0}""")],
                "Drew");
            var status = StatusNorthOfSite(10);
            viewModel.UpdateStatus(status);
            Assert.Equal(10d, viewModel.Proximity!.DistanceFromSite, 3);

            viewModel.SurveyEditor.SurfaceLatitude = (decimal)status.Latitude;

            Assert.Equal(0d, viewModel.Proximity!.DistanceFromSite, 3);
            viewModel.SurveyEditor.ResetCoordinatesCommand.Execute(null);
            Assert.Equal(10d, viewModel.Proximity!.DistanceFromSite, 3);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task MapSelectionDrivesTemplateCoordinatePreview()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var reference = CreateProximityReference();
            var template = new GuardianSiteTemplate(
                "Test",
                "Test",
                string.Empty,
                new GuardianMapPoint(0, 0),
                1,
                [
                    new GuardianPointOfInterest(
                        "p1",
                        GuardianPoiType.Orb,
                        0,
                        10,
                        0),
                ],
                [],
                new Dictionary<string, GuardianMapPoint>());
            var viewModel = new GuardianViewModel(
                root,
                new GuardianViewModelOptions
                {
                    References = new GuardianSiteCatalog([reference]),
                    Templates = new GuardianSiteTemplateCatalog([template]),
                });
            var visitedAt = DateTimeOffset.Parse("2026-08-23T12:00:00Z");
            await new GuardianCommanderSurveyStore(root).SaveAsync(
                "F123",
                isOdyssey: true,
                new GuardianCommanderSiteSurvey(
                    string.Empty,
                    "$Ancient:#index=1;",
                    "Ancient Ruins (1)",
                    "Drew",
                    visitedAt,
                    visitedAt,
                    "Test",
                    1,
                    42,
                    "Test",
                    7,
                    "Test A 1",
                    string.Empty,
                    false,
                    new GuardianSurveyData
                    {
                        SiteType = "Test",
                        Location = new GuardianSurfaceLocation(0, 0),
                    },
                    [],
                    new HashSet<char>()));
            await viewModel.LoadProfileAsync("F123", isOdyssey: true);

            Assert.True(viewModel.SurveyEditor.IsAvailable);
            viewModel.SurveyEditor.SelectedPointName = "p1";
            Assert.Equal("p1", viewModel.TemplateAuthoring.SelectedPoint?.Name);

            viewModel.TemplateAuthoring.StartCommand.Execute(null);
            viewModel.TemplateAuthoring.PointName = "p1-edited";
            viewModel.TemplateAuthoring.PointDistance = 10.1m;

            Assert.Equal(
                10.1,
                viewModel.MapProjection?.Points.Single(point =>
                    point.Name == "p1-edited").Distance);

            viewModel.SurveyEditor.SelectedPointName = null;

            Assert.Contains(
                viewModel.MapProjection!.Points,
                point => point.Name == "p1" && point.Distance == 10);
            Assert.DoesNotContain(
                viewModel.MapProjection.Points,
                point => point.Name == "p1-edited");

            viewModel.SurveyEditor.SelectedPointName = "p1";
            viewModel.TemplateAuthoring.PointName = "p1-edited";
            viewModel.TemplateAuthoring.PointDistance = 10.1m;
            viewModel.TemplateAuthoring.ApplySelectedPointCommand.Execute(null);

            Assert.Equal(
                "p1-edited",
                viewModel.SurveyEditor.SelectedPointName);
            Assert.Equal(
                10.1,
                viewModel.MapProjection?.Points.Single(point =>
                    point.Name == "p1-edited").Distance);
            Assert.Contains(
                "10.1 m",
                viewModel.SurveyEditor.SelectedPoint?.PositionText,
                StringComparison.Ordinal);
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

    private static EliteStatus StatusEastOfSite(double distance)
    {
        const double radius = 1_000_000;
        return new EliteStatus
        {
            Flags = StatusFlags.HasLatLong | StatusFlags.InSrv,
            Latitude = 0,
            Longitude = distance / radius * 180 / Math.PI,
            PlanetRadius = (decimal)radius,
        };
    }

    [Fact]
    public async Task MusicAndFileheaderTracksChangeGuardianModeState()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var viewModel = new GuardianViewModel(root,
                new GuardianViewModelOptions
                {
                    References = new GuardianSiteCatalog([]),
                    PublishedSites = new GuardianPublishedSiteCatalog([]),
                    Templates = new GuardianSiteTemplateCatalog([]),
                });
            await viewModel.LoadProfileAsync("F123", isOdyssey: true);

            await viewModel.ApplyJournalEventsAsync(
                [Parse("""{"event":"Music","MusicTrack":"GalaxyMap"}""")],
                "Drew");
            Assert.Equal(
                OverlayGameMode.GalaxyMap,
                OverlayGameModeResolver.Resolve(
                    new EliteStatus { Flags = StatusFlags.InMainShip },
                    musicTrack: "GalaxyMap"));

            await viewModel.ApplyJournalEventsAsync(
                [Parse("""{"event":"Fileheader","part":1,"language":"English/UK","gameversion":"4.0","build":"r300000"}""")],
                "Drew");
            await viewModel.ApplyJournalEventsAsync(
                [Parse("""{"event":"LoadGame","Commander":"Drew"}""")],
                "Drew");
            await viewModel.ApplyJournalEventsAsync(
                [Parse("""{"event":"Music","MusicTrack":"SystemMap"}""")],
                "Drew");
            await viewModel.ApplyJournalEventsAsync(
                [Parse("""{"event":"Music","MusicTrack":"SystemMap"}""")],
                "Drew");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task GuardianBeaconScanReportsMissingSystemAddressAndName()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var viewModel = new GuardianViewModel(root,
                new GuardianViewModelOptions
                {
                    References = new GuardianSiteCatalog([]),
                    PublishedSites = new GuardianPublishedSiteCatalog([]),
                    Templates = new GuardianSiteTemplateCatalog([]),
                });
            await viewModel.LoadProfileAsync("F123", isOdyssey: true);

            await viewModel.ApplyJournalEventsAsync(
                [Parse(
                    """{"event":"CodexEntry","Name":"$Codex_Ent_Guardian_Beacons_Name;","BodyID":7,"BodyName":"Test A 1"}""")],
                "Drew");
            Assert.Contains("system address", viewModel.StatusMessage);

            await viewModel.ApplyJournalEventsAsync(
                [Parse(
                    """{"event":"CodexEntry","Name":"$Codex_Ent_Guardian_Beacons_Name;","SystemAddress":42,"BodyID":7,"BodyName":"Test A 1"}""")],
                "Drew");
            Assert.Contains("system name", viewModel.StatusMessage);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task GuardianBeaconScanMergesExistingVisitLocations()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var store = new GuardianCommanderBeaconStore(root);
            var firstScan = DateTimeOffset.Parse("2026-08-01T10:00:00Z");
            await store.SaveAsync(
                "F123",
                true,
                new GuardianCommanderBeaconVisit(
                    string.Empty,
                    firstScan,
                    firstScan,
                    "Test System",
                    42,
                    "Test System A 1",
                    7,
                    "kept notes",
                    false,
                    new Dictionary<DateTimeOffset, GuardianSurfaceLocation>
                    {
                        [firstScan] = new GuardianSurfaceLocation(1, 2),
                    }));

            var viewModel = new GuardianViewModel(root,
                new GuardianViewModelOptions
                {
                    References = new GuardianSiteCatalog([]),
                    PublishedSites = new GuardianPublishedSiteCatalog([]),
                    Templates = new GuardianSiteTemplateCatalog([]),
                });
            await viewModel.LoadProfileAsync("F123", isOdyssey: true);
            viewModel.UpdateStatus(new EliteStatus
            {
                Flags = StatusFlags.HasLatLong,
                Latitude = 9.5,
                Longitude = -8.25,
                PlanetRadius = 1_000_000,
            });

            await viewModel.ApplyJournalEventsAsync(
                [Parse(
                    """{"timestamp":"2026-08-03T12:00:00Z","event":"CodexEntry","Name":"$Codex_Ent_Guardian_Beacons_Name;","System":"Test System","SystemAddress":42,"BodyID":7,"BodyName":"Test System A 1"}""")],
                "Drew");

            var data = await new GuardianCommanderDataReader(root)
                .ReadAsync("F123", isOdyssey: true);
            var beacon = Assert.Single(data.Beacons);
            Assert.Equal("kept notes", beacon.Notes);
            Assert.Equal(firstScan, beacon.FirstVisited);
            Assert.Equal(2, beacon.ScannedLocations.Count);
            Assert.Contains(
                new GuardianSurfaceLocation(9.5, -8.25),
                beacon.ScannedLocations.Values);
            Assert.Contains("Recorded Guardian beacon scan", viewModel.StatusMessage);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task NonBeaconCodexDoesNotCreateBeaconVisit()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var viewModel = new GuardianViewModel(root,
                new GuardianViewModelOptions
                {
                    References = new GuardianSiteCatalog([]),
                    PublishedSites = new GuardianPublishedSiteCatalog([]),
                    Templates = new GuardianSiteTemplateCatalog([]),
                });
            await viewModel.LoadProfileAsync("F123", isOdyssey: true);

            await viewModel.ApplyJournalEventsAsync(
                [Parse(
                    """{"event":"CodexEntry","Name":"$Codex_Ent_Something_Else;","System":"Test","SystemAddress":42}""")],
                "Drew");

            var data = await new GuardianCommanderDataReader(root)
                .ReadAsync("F123", isOdyssey: true);
            Assert.Empty(data.Beacons);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task LiveApproachSettlementSaveFailureReportsStatusMessage()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var reference = CreateProximityReference();
            var viewModel = new GuardianViewModel(root,
                new GuardianViewModelOptions
                {
                    References = new GuardianSiteCatalog([reference]),
                    PublishedSites = new GuardianPublishedSiteCatalog(
                    [CreatePublishedSite(reference, [])]),
                    Templates = new GuardianSiteTemplateCatalog(
                [
                    new GuardianSiteTemplate(
                        "Test",
                        "Test",
                        string.Empty,
                        new GuardianMapPoint(0, 0),
                        1,
                        [],
                        [],
                        new Dictionary<string, GuardianMapPoint>()),
                ]),
                });
            await viewModel.LoadProfileAsync("F123", isOdyssey: true);

            // Replace the writable survey path with a file so SaveAsync fails.
            var surveyFolder = Path.Combine(root, "guardian", "F123");
            Directory.CreateDirectory(Path.GetDirectoryName(surveyFolder)!);
            await File.WriteAllTextAsync(surveyFolder, "not-a-directory");

            await viewModel.ApplyJournalEventsAsync(
                [Parse(
                    """{"event":"ApproachSettlement","Name":"$Ancient:#index=1;","SystemAddress":42,"BodyID":7,"BodyName":"Test A 1","Latitude":0,"Longitude":0}""")],
                "Drew");

            Assert.Contains("could not be saved", viewModel.StatusMessage);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void LegacySurveyLineFormatsProgressStates()
    {
        var incomplete = CreateProximityReference();
        var incompleteVisit = new GuardianSiteVisit(
            incomplete,
            FirstVisited: DateTimeOffset.UtcNow,
            LastVisited: DateTimeOffset.UtcNow,
            Notes: string.Empty,
            SurveyProgress: 40,
            IsSurveyComplete: false,
            CommanderFilePath: null,
            HasCommanderData: true,
            Completion: null,
            RecordedObeliskOrLocationCount: 1);
        var incompleteRow = new GuardianSiteRowViewModel(
            incompleteVisit,
            distance: null,
            ramTahLogCodes: [],
            hasImages: false);
        Assert.Equal("\u25ba Survey: Incomplete", incompleteRow.LegacySurveyLine);

        var notStartedVisit = incompleteVisit with
        {
            SurveyProgress = 0,
            RecordedObeliskOrLocationCount = 0,
        };
        var notStartedRow = new GuardianSiteRowViewModel(
            notStartedVisit,
            distance: null,
            ramTahLogCodes: [],
            hasImages: false);
        Assert.Equal("\u25ba Survey: Not started", notStartedRow.LegacySurveyLine);

        var completeVisit = incompleteVisit with
        {
            SurveyProgress = 100,
            IsSurveyComplete = true,
        };
        var completeRow = new GuardianSiteRowViewModel(
            completeVisit,
            distance: null,
            ramTahLogCodes: [],
            hasImages: false);
        Assert.Equal(string.Empty, completeRow.LegacySurveyLine);
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
