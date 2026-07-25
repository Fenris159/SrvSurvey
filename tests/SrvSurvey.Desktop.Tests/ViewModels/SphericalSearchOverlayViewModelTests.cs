using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class SphericalSearchOverlayViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-search-overlay-tests-{Guid.NewGuid():N}");

    [Fact]
    public void WrapsAllThreeLegacySlicesAndReportsPreparation()
    {
        var sphere = new SphereLimitViewModel(
            new CommanderProfileStore(temporaryDirectory),
            new EmptyStarResolver());
        var boxel = new BoxelSearchViewModel(
            new CommanderProfileStore(temporaryDirectory),
            new LegacySystemDataReader(temporaryDirectory),
            new EmptyBoxelStore(temporaryDirectory),
            new EmptyBoxelResolver());
        var route = new RouteWorkspaceViewModel(
            new FollowRouteService(new FollowRouteStore(temporaryDirectory)),
            new RouteNameImporter(new EmptyStarResolver()),
            new EmptyRouteClient());
        var viewModel = new SphericalSearchOverlayViewModel(
            sphere,
            boxel,
            route,
            OverlayPlatformCapabilities.ForHost(OverlayHostKind.Windows));

        Assert.Same(sphere, viewModel.Sphere);
        Assert.Same(boxel, viewModel.Boxel);
        Assert.Same(route, viewModel.Route);
        Assert.Equal("PASSIVE", viewModel.InputMode);

        viewModel.ApplyPreparation(new OverlayPreparationResult(
            IsPrepared: true,
            IsClickThrough: false,
            "Click-through was rejected."));

        Assert.Equal("BLOCKED", viewModel.InputMode);
        Assert.Equal("Click-through was rejected.", viewModel.PlatformStatus);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private sealed class EmptyStarResolver : IStarSystemResolver
    {
        public Task<IReadOnlyList<StarSystemReference>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<StarSystemReference>>([]);
        }
    }

    private sealed class EmptyBoxelResolver : IBoxelSystemResolver
    {
        public Task<IReadOnlyList<BoxelSystemObservation>> SearchAsync(
            BoxelAddress boxel,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<BoxelSystemObservation>>([]);
        }
    }

    private sealed class EmptyRouteClient : ISpanshRouteClient
    {
        public Task<IReadOnlyList<FollowRouteHop>> GetRouteAsync(
            SpanshRouteReference route,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<FollowRouteHop>>([]);
        }
    }
}
