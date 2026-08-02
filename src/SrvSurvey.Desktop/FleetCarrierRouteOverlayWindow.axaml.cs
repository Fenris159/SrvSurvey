using Avalonia.Controls;
using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class FleetCarrierRouteOverlayWindow : Window
{
    public FleetCarrierRouteOverlayWindow()
        : this(CreateDesignViewModel())
    {
    }

    public FleetCarrierRouteOverlayWindow(
        FleetCarrierRouteOverlayViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }

    private static FleetCarrierRouteOverlayViewModel CreateDesignViewModel()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "SrvSurvey-Fleet-Carrier-Route-Overlay-Design");
        return new FleetCarrierRouteOverlayViewModel(
            new RouteWorkspaceViewModel(
                new FollowRouteService(new FollowRouteStore(
                    temporaryDirectory,
                    FollowRouteKind.FleetCarrier)),
                new RouteNameImporter(new EmptySystemResolver()),
                new EmptySpanshRouteClient(),
                FollowRouteKind.FleetCarrier),
            OverlayPlatformCapabilities.DetectCurrent());
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
