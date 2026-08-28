using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Guardian;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class GuardianUncataloguedSelectionTests
{
    [Fact]
    public async Task DefaultDistanceUsesSolEvenWhileCommanderIsAtSelectedSite()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-guardian-selection-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var position = new GalacticCoordinate(3, 4, 0);
            var survey = new GuardianCommanderSiteSurvey(
                string.Empty,
                "$Ancient:#index=1;",
                "Ancient Ruins (1)",
                "Drew",
                DateTimeOffset.Parse("2026-08-27T12:00:00Z"),
                DateTimeOffset.Parse("2026-08-27T12:00:00Z"),
                "Beta",
                1,
                9000000000004,
                "Diagnostic Uncatalogued",
                7,
                "Diagnostic Uncatalogued A 1",
                string.Empty,
                false,
                new GuardianSurveyData
                {
                    SiteType = "Beta",
                    SiteHeading = 0,
                    Location = new GuardianSurfaceLocation(10, 20),
                },
                [],
                new HashSet<char>())
            {
                LocalSiteId = 1,
                CatalogBodyName = "A 1",
                StarPosition = position,
                DistanceToArrivalLs = 1234.5,
            };
            await new GuardianCommanderSurveyStore(root).SaveAsync(
                "F123",
                isOdyssey: true,
                survey);
            var viewModel = new GuardianViewModel(root);
            await viewModel.LoadProfileAsync("F123", isOdyssey: true);

            viewModel.UpdateCurrentSystem("Diagnostic Uncatalogued", position);

            var row = Assert.Single(
                viewModel.Rows,
                candidate => candidate.Reference.IsCommanderOnly);
            Assert.Equal(5, row.Distance);
            Assert.Equal(5m, viewModel.SurveyEditor.DistanceLy);
            Assert.True(viewModel.SurveyEditor.CanEditDistanceLy);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task InitialSiteTypeRevealsAndOpensActiveUncataloguedSurvey()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-guardian-selection-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var viewModel = new GuardianViewModel(root);
            await viewModel.LoadProfileAsync("F123", isOdyssey: true);
            var previous = Assert.IsType<GuardianSiteRowViewModel>(viewModel.SelectedSite);
            viewModel.FilterText = previous.Reference.SystemName;

            await viewModel.ApplyJournalEventsAsync(
            [
                Parse("""{"event":"Location","StarSystem":"Diagnostic Uncatalogued","SystemAddress":9000000000001}"""),
                Parse("""{"event":"ApproachSettlement","Name":"$Ancient:#index=1;","Name_Localised":"Ancient Ruins (1)","SystemAddress":9000000000001,"BodyID":7,"BodyName":"Diagnostic Uncatalogued A 1","Latitude":10,"Longitude":20}"""),
            ],
            "Drew");

            Assert.Equal(previous.Reference, viewModel.SelectedSite?.Reference);
            await viewModel.ApplyJournalEventsAsync(
                [Parse("""{"event":"SendText","Message":"b"}""")],
                "Drew");

            Assert.Equal(string.Empty, viewModel.FilterText);
            Assert.Equal(
                9000000000001,
                viewModel.SelectedSite?.Reference.SystemAddress);
            Assert.Equal(1, viewModel.SelectedWorkspaceTabIndex);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task SavingHeadingPreservesSelectionAndEnablesLiveMapAuthoring()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-guardian-selection-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var viewModel = new GuardianViewModel(root);
            await viewModel.LoadProfileAsync("F123", isOdyssey: true);
            await viewModel.ApplyJournalEventsAsync(
            [
                Parse("""{"event":"Location","StarSystem":"Diagnostic Uncatalogued","SystemAddress":9000000000002}"""),
                Parse("""{"event":"ApproachSettlement","Name":"$Ancient:#index=1;","Name_Localised":"Ancient Ruins (1)","SystemAddress":9000000000002,"BodyID":7,"BodyName":"Diagnostic Uncatalogued A 1","Latitude":10,"Longitude":20}"""),
                Parse("""{"event":"SendText","Message":"b"}"""),
            ],
            "Drew");
            viewModel.SurveyEditor.SiteHeading = 0;

            await viewModel.SurveyEditor.SaveAsync();
            viewModel.UpdateStatus(new EliteStatus
            {
                Flags = StatusFlags.HasLatLong | StatusFlags.InSrv,
                Latitude = 10.00001,
                Longitude = 20,
                PlanetRadius = 1_000_000,
                BodyName = "Diagnostic Uncatalogued A 1",
            });

            Assert.Equal(
                9000000000002,
                viewModel.SelectedSite?.Reference.SystemAddress);
            Assert.NotNull(viewModel.Proximity);
            Assert.NotNull(viewModel.SelectedMapCommanderPosition);
            Assert.True(viewModel.TemplateAuthoring.HasLiveMeasurement);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task NewLocalSiteUsesJournalPositionAndBodyArrivalDistance()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-guardian-selection-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var viewModel = new GuardianViewModel(root);
            await viewModel.LoadProfileAsync("F123", isOdyssey: true);
            viewModel.UpdateCurrentSystem(
                "Diagnostic Uncatalogued",
                new GalacticCoordinate(100.5, 200.25, -300.75));
            await viewModel.ApplyJournalEventsAsync(
            [
                Parse("""{"event":"Location","StarSystem":"Diagnostic Uncatalogued","SystemAddress":9000000000003,"StarPos":[100.5,200.25,-300.75]}"""),
                Parse("""{"event":"Scan","ScanType":"Detailed","StarSystem":"Diagnostic Uncatalogued","SystemAddress":9000000000003,"BodyID":7,"BodyName":"Diagnostic Uncatalogued A 1","DistanceFromArrivalLS":1234.5}"""),
                Parse("""{"event":"ApproachSettlement","Name":"$Ancient:#index=1;","Name_Localised":"Ancient Ruins (1)","SystemAddress":9000000000003,"BodyID":7,"BodyName":"Diagnostic Uncatalogued A 1","Latitude":10,"Longitude":20}"""),
            ],
            "Drew");

            var local = Assert.Single(
                viewModel.Rows,
                row => row.Reference.IsCommanderOnly);
            Assert.Equal(1234.5, local.Reference.DistanceToArrival);
            Assert.Equal(
                new GalacticCoordinate(100.5, 200.25, -300.75),
                local.Reference.Position);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(
            JournalEventEnvelope.TryParse(json, out var parsed, out var error),
            error);
        return Assert.IsType<JournalEventEnvelope>(parsed);
    }
}
