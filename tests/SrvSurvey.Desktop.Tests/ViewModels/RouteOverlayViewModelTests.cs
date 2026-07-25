using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class RouteOverlayViewModelTests
{
    [Fact]
    public void ReportsPlatformPreparationAndInputMode()
    {
        var route = new RouteWorkspaceViewModel(
            new FollowRouteService(
                new FollowRouteStore(Path.GetTempPath())),
            new RouteNameImporter(new EmptySystemResolver()),
            new EmptySpanshRouteClient());
        var viewModel = new RouteOverlayViewModel(
            route,
            OverlayPlatformCapabilities.ForHost(OverlayHostKind.Windows));

        Assert.Equal("PASSIVE", viewModel.InputMode);

        viewModel.ApplyPreparation(new OverlayPreparationResult(
            IsPrepared: true,
            IsClickThrough: false,
            "Click-through failed."));

        Assert.Equal("BLOCKED", viewModel.InputMode);
        Assert.Equal("Click-through failed.", viewModel.PlatformStatus);
    }

    private sealed class EmptySystemResolver : IStarSystemResolver
    {
        public Task<IReadOnlyList<StarSystemReference>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<StarSystemReference>>([]);
        }
    }

    private sealed class EmptySpanshRouteClient : ISpanshRouteClient
    {
        public Task<IReadOnlyList<FollowRouteHop>> GetRouteAsync(
            SpanshRouteReference route,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<FollowRouteHop>>([]);
        }
    }
}
