using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class SurfaceSurveyOverlayViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-surface-overlay-tests-{Guid.NewGuid():N}");

    [Fact]
    public void TracksLegacySizeMappingAndPassivePreparation()
    {
        var (surfaceSurvey, survey) = CreateSurfaceSurvey();
        using var ownedSurfaceSurvey = surfaceSurvey;
        using var viewModel = new SurfaceSurveyOverlayViewModel(
            surfaceSurvey,
            OverlayPlatformCapabilities.ForHost(OverlayHostKind.Windows));
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, eventArgs) =>
            changed.Add(eventArgs.PropertyName);

        Assert.Equal(380, viewModel.WindowWidth);
        Assert.Equal(500, viewModel.WindowHeight);
        Assert.Equal("PASSIVE", viewModel.InputMode);

        survey.SurfaceRadarSize = 0;

        Assert.Equal(250, viewModel.WindowWidth);
        Assert.Equal(400, viewModel.WindowHeight);
        Assert.Contains(nameof(viewModel.WindowWidth), changed);
        Assert.Contains(nameof(viewModel.WindowHeight), changed);

        viewModel.ApplyPreparation(new OverlayPreparationResult(
            IsPrepared: true,
            IsClickThrough: false,
            "Click-through was rejected."));

        Assert.Equal("BLOCKED", viewModel.InputMode);
        Assert.Equal("Click-through was rejected.", viewModel.PlatformStatus);
    }

    private (SurfaceSurveyViewModel SurfaceSurvey, SystemSurveyViewModel Survey)
        CreateSurfaceSurvey()
    {
        var survey = new SystemSurveyViewModel(
            new SystemSurveySettingsStore(Path.Combine(
                temporaryDirectory,
                "ui-settings.json")));
        var store = new SystemSurfaceStore(temporaryDirectory);
        return (
            new SurfaceSurveyViewModel(
                survey,
                store,
                new SurfaceSurveyJournalTracker(
                    store,
                    new ExobiologyReferenceCatalog([]))),
            survey);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
