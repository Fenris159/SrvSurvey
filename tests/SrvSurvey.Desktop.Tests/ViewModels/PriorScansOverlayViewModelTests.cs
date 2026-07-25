using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class PriorScansOverlayViewModelTests : IDisposable
{
    private const string Species = "$Codex_Ent_Aleoids_01_Name;";
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-PriorScansOverlay-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task LoadsCurrentSystemOnceAndRecalculatesSurfaceNavigation()
    {
        var survey = CreateSurvey();
        var client = new StubClient(new CanonnSystemPoiResult(
            "Test",
            [
                Signal("1", 0, 0.01),
                Signal("2", 0, 0.02),
            ]));
        using var viewModel = CreateViewModel(survey, client);

        await viewModel.RefreshAsync();

        Assert.True(viewModel.ShouldShow);
        Assert.Equal("Test 1", viewModel.BodyName);
        Assert.Equal("HEADING 090°", viewModel.HeadingText);
        var species = Assert.Single(viewModel.Species);
        Assert.Equal("Aleoida Arcus - Green", species.DisplayName);
        Assert.True(species.HasTooSteepApproach);
        Assert.StartsWith("-", species.ApproachText);
        var target = Assert.Single(species.Targets);
        Assert.Equal(0, target.RelativeBearingDegrees, 6);
        Assert.Equal("175 m", target.DistanceText);
        Assert.Single(viewModel.RadarTargets);
        Assert.Equal(1, client.CallCount);
        Assert.Equal("CMDR Test", client.CommanderName);

        survey.ApplyUpdate([], SurfaceStatus(heading: 0));
        await viewModel.RefreshAsync();

        Assert.Equal(1, client.CallCount);
        Assert.Equal(
            90,
            Assert.Single(Assert.Single(viewModel.Species).Targets)
                .RelativeBearingDegrees,
            6);
    }

    [Fact]
    public async Task PreferencesFilterRowsAndControlRadarPresentation()
    {
        var survey = CreateSurvey();
        var client = new StubClient(new CanonnSystemPoiResult(
            "Test",
            [Signal("1", 0, 0.01)]));
        using var viewModel = CreateViewModel(survey, client);

        await viewModel.RefreshAsync();
        Assert.True(viewModel.ShowRadar);
        Assert.True(viewModel.UseSmallRadarCircles);

        survey.SkipPriorScansLowValue = true;
        survey.PriorScanMinimumValue = 8_000_000;
        Assert.Empty(viewModel.Species);
        Assert.False(viewModel.ShouldShow);

        survey.SkipPriorScansLowValue = false;
        survey.ShowCanonnSignalsOnRadar = false;
        survey.UseSmallCanonnRadarCircles = false;
        Assert.Single(viewModel.Species);
        Assert.False(viewModel.ShowRadar);
        Assert.False(viewModel.UseSmallRadarCircles);
    }

    [Fact]
    public async Task NetworkFailureIsContainedAndReported()
    {
        var survey = CreateSurvey();
        var client = new StubClient(new HttpRequestException("offline"));
        using var viewModel = CreateViewModel(
            survey,
            client);

        await viewModel.RefreshAsync();
        await viewModel.RefreshAsync();

        Assert.False(viewModel.ShouldShow);
        Assert.Empty(viewModel.Species);
        Assert.Contains("offline", viewModel.StatusText);
        Assert.Equal(1, client.CallCount);
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
        var survey = new SystemSurveyViewModel(new SystemSurveySettingsStore(
            Path.Combine(temporaryDirectory, "ui-settings.json")));
        survey.ApplyUpdate(
        [
            Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42}"""),
            Parse("""{"event":"Scan","ScanType":"Detailed","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"PlanetClass":"Rocky body","Radius":1000000}"""),
        ],
        SurfaceStatus(heading: 90));
        return survey;
    }

    private static PriorScansOverlayViewModel CreateViewModel(
        SystemSurveyViewModel survey,
        ICanonnSystemPoiClient client)
    {
        return new PriorScansOverlayViewModel(
            survey,
            client,
            new ExobiologyReferenceCatalog(
            [
                new ExobiologyReference(
                    2310101,
                    "$Codex_Ent_Aleoids_01_B_Name;",
                    Species,
                    "Aleoida Arcus - Green",
                    7_252_500,
                    HudCategory: "Biology"),
            ]),
            () => "CMDR Test",
            OverlayPlatformCapabilities.ForHost(OverlayHostKind.Windows));
    }

    private static EliteStatus SurfaceStatus(int heading)
    {
        return new EliteStatus
        {
            Flags = StatusFlags.HasLatLong | StatusFlags.InSrv,
            BodyName = "Test 1",
            PlanetRadius = 1_000_000,
            Heading = heading,
            Altitude = 1_000,
        };
    }

    private static CanonnSurfaceBiologySignal Signal(
        string body,
        double latitude,
        double longitude)
    {
        return new CanonnSurfaceBiologySignal(
            body,
            "Aleoida Arcus - Green",
            2310101,
            new SurfaceCoordinate(latitude, longitude),
            false);
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

    private sealed class StubClient : ICanonnSystemPoiClient
    {
        private readonly CanonnSystemPoiResult? result;
        private readonly Exception? exception;

        public StubClient(CanonnSystemPoiResult result)
        {
            this.result = result;
        }

        public StubClient(Exception exception)
        {
            this.exception = exception;
        }

        public int CallCount { get; private set; }

        public string? CommanderName { get; private set; }

        public Task<CanonnSystemPoiResult> GetAsync(
            string systemName,
            string commanderName,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            CommanderName = commanderName;
            return exception is null
                ? Task.FromResult(result!)
                : Task.FromException<CanonnSystemPoiResult>(exception);
        }
    }
}
