using SrvSurvey.Core.Exobiology;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class PriorScansOverlayViewModelTests
{
    /// <summary>Unchanged surface telemetry keeps the nested species rows and radar collections alive.</summary>
    [Fact]
    public async Task OverlayRefreshRetainsUnchangedPriorScanRows()
    {
        SystemSurveyViewModel survey = CreateSurvey();
        var client = new StubClient(new CanonnSystemPoiResult("Test", [Signal("1", 0, 0.01)]));
        using PriorScansOverlayViewModel model = CreateViewModel(survey, client);
        await model.RefreshAsync();
        IReadOnlyList<PriorScanSpeciesViewModel> species = model.Species;
        IReadOnlyList<PriorScanRadarTargetViewModel> radar = model.RadarTargets;
        IReadOnlyList<PriorScanSurfaceMarkerViewModel> surface = model.SurfaceMarkers;
        Assert.NotEmpty(species);
        survey.ApplyUpdate([], SurfaceStatus(heading: 90));
        await model.RefreshAsync();
        Assert.Same(species, model.Species);
        Assert.Same(radar, model.RadarTargets);
        Assert.Same(surface, model.SurfaceMarkers);
    }
}
