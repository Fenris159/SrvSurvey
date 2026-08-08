using Avalonia.Controls;
using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Controls;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class RouteBioOverlayWindow : Window
{
    private readonly RouteBioOverlayViewModel viewModel;

    public RouteBioOverlayWindow()
        : this(CreateDesignViewModel())
    {
    }

    public RouteBioOverlayWindow(RouteBioOverlayViewModel viewModel)
    {
        this.viewModel = viewModel
            ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = viewModel;
    }

    private static RouteBioOverlayViewModel CreateDesignViewModel()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "SrvSurvey-Route-Bio-Overlay-Design");
        return new RouteBioOverlayViewModel(
            new RouteWorkspaceViewModel(
                new FollowRouteService(new FollowRouteStore(temporaryDirectory)),
                new RouteNameImporter(new EmptySystemResolver()),
                new EmptySpanshRouteClient()),
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
