using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class SphericalSearchOverlayViewModelTests : IAsyncLifetime
{
    private readonly List<BoxelSearchSession> sessions = [];
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-search-overlay-tests-{Guid.NewGuid():N}");

    [Fact]
    public void WrapsAllThreeLegacySlicesAndReportsPreparation()
    {
        var sphere = new SphereLimitViewModel(
            new CommanderProfileStore(temporaryDirectory),
            new EmptyStarResolver());
        var boxel = CreateBoxel(
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

    [Fact]
    public async Task ManualBoxelCopyGuidanceTracksTheConfiguredShortcut()
    {
        var capabilities = OverlayPlatformCapabilities.ForHost(
            OverlayHostKind.Windows);
        var inputSettings = new GlobalInputSettingsViewModel(
            new GlobalInputSettingsStore(Path.Combine(
                temporaryDirectory,
                "ui-settings.json")),
            capabilities);
        var copyBinding = inputSettings.Bindings.Single(binding =>
            binding.Definition.Action == GlobalInputAction.CopyNextBoxel);
        copyBinding.Chord = "ALT X";
        var sphere = new SphereLimitViewModel(
            new CommanderProfileStore(temporaryDirectory),
            new EmptyStarResolver());
        var boxel = CreateBoxel(
            new CommanderProfileStore(temporaryDirectory),
            new LegacySystemDataReader(temporaryDirectory),
            new EmptyBoxelStore(temporaryDirectory),
            new EmptyBoxelResolver());
        await boxel.LoadProfileAsync(
            "F123",
            "Drew",
            true,
            BoxelSearchSnapshot.Empty);
        boxel.TopBoxelText = "Praea Euq IL-P c5-0";
        boxel.LowMassCode = "c";
        await boxel.ActivateAsync();
        var route = new RouteWorkspaceViewModel(
            new FollowRouteService(new FollowRouteStore(temporaryDirectory)),
            new RouteNameImporter(new EmptyStarResolver()),
            new EmptyRouteClient());
        using var viewModel = new SphericalSearchOverlayViewModel(
            sphere,
            boxel,
            route,
            capabilities,
            inputSettings: inputSettings);
        var notifications = new List<string?>();
        viewModel.PropertyChanged += (_, eventArgs) =>
            notifications.Add(eventArgs.PropertyName);

        Assert.Equal("MANUAL COPY - ALT X", viewModel.BoxelClipboardStatus);

        copyBinding.Chord = "CTRL SHIFT X";

        Assert.Equal("MANUAL COPY - CTRL SHIFT X", viewModel.BoxelClipboardStatus);
        Assert.Contains(nameof(viewModel.BoxelClipboardStatus), notifications);

        copyBinding.Chord = string.Empty;

        Assert.Equal("MANUAL COPY - NOT SET", viewModel.BoxelClipboardStatus);
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        foreach (var session in sessions.AsEnumerable().Reverse())
        {
            await session.DisposeAsync();
        }

        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private BoxelSearchViewModel CreateBoxel(
        CommanderProfileStore profileStore,
        LegacySystemDataReader localSystemReader,
        EmptyBoxelStore emptyBoxelStore,
        IBoxelSystemResolver systemResolver)
    {
        var viewModel = BoxelSearchViewModelTestFactory.Create(
            profileStore,
            localSystemReader,
            emptyBoxelStore,
            systemResolver,
            out var session);
        sessions.Add(session);
        return viewModel;
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
