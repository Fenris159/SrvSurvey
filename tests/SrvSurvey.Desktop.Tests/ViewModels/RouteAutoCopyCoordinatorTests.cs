using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class RouteAutoCopyCoordinatorTests : IAsyncLifetime
{
    private readonly List<BoxelSearchSession> sessions = [];
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-route-autocopy-tests-{Guid.NewGuid():N}"
    );

    [Fact]
    public async Task ClaimingClipboardDisablesAndPersistsTheCompetingRoute()
    {
        RouteWorkspaceViewModel standard = await CreateWorkspaceAsync(FollowRouteKind.Standard);
        RouteWorkspaceViewModel carrier = await CreateWorkspaceAsync(FollowRouteKind.FleetCarrier);
        BoxelSearchViewModel boxel = CreateInactiveBoxel();
        using var coordinator = new RouteAutoCopyCoordinator(standard, carrier, boxel.Session);

        Assert.True(standard.ShouldAutoCopyNextHop);
        Assert.True(carrier.ShouldAutoCopyNextHop);

        await coordinator.ClaimAsync(standard);

        Assert.True(standard.AutoCopy);
        Assert.False(carrier.AutoCopy);

        await carrier.SetAutoCopyAsync(true);
        await coordinator.ClaimAsync(carrier);

        Assert.True(carrier.AutoCopy);
        Assert.False(standard.AutoCopy);

        FollowRouteLoadResult standardSaved = await new FollowRouteStore(temporaryDirectory).LoadAsync("F123");
        FollowRouteLoadResult carrierSaved = await new FollowRouteStore(
            temporaryDirectory,
            FollowRouteKind.FleetCarrier
        ).LoadAsync("F123");
        Assert.False(standardSaved.Route!.AutoCopy);
        Assert.True(carrierSaved.Route!.AutoCopy);
    }

    [Fact]
    public async Task InactiveRouteSelectionStillOwnsTheAutoCopySetting()
    {
        RouteWorkspaceViewModel standard = await CreateWorkspaceAsync(FollowRouteKind.Standard);
        RouteWorkspaceViewModel carrier = await CreateWorkspaceAsync(FollowRouteKind.FleetCarrier, isActive: false);
        using var coordinator = new RouteAutoCopyCoordinator(standard, carrier, CreateInactiveBoxel().Session);

        await coordinator.ClaimAsync(carrier);

        Assert.False(standard.ShouldAutoCopyNextHop);
        Assert.False(carrier.ShouldAutoCopyNextHop);
        Assert.False(standard.AutoCopy);
        Assert.True(carrier.AutoCopy);
    }

    [Fact]
    public async Task LatePropertyChangeClaimIsIgnoredAfterDisposal()
    {
        RouteWorkspaceViewModel standard = await CreateWorkspaceAsync(FollowRouteKind.Standard);
        RouteWorkspaceViewModel carrier = await CreateWorkspaceAsync(FollowRouteKind.FleetCarrier);
        var coordinator = new RouteAutoCopyCoordinator(standard, carrier, CreateInactiveBoxel().Session);
        coordinator.Dispose();

        await coordinator.ClaimAfterPropertyChangeAsync(standard);

        Assert.True(standard.AutoCopy);
        Assert.True(carrier.AutoCopy);
    }

    [Fact]
    public async Task BoxelAndBothRouteTypesShareOneAutoCopyOwner()
    {
        RouteWorkspaceViewModel standard = await CreateWorkspaceAsync(FollowRouteKind.Standard);
        RouteWorkspaceViewModel carrier = await CreateWorkspaceAsync(FollowRouteKind.FleetCarrier);
        BoxelSearchViewModel boxel = await CreateConfiguredBoxelAsync(autoCopy: true);
        using var coordinator = new RouteAutoCopyCoordinator(standard, carrier, boxel.Session);

        await coordinator.ReconcileAsync();

        Assert.True(boxel.AutoCopy);
        Assert.False(standard.AutoCopy);
        Assert.False(carrier.AutoCopy);

        await standard.SetAutoCopyAsync(true);
        await WaitUntilAsync(() => !boxel.AutoCopy);
        // The selection event starts an asynchronous ownership claim. Entering
        // the coordinator once more drains that work before the test removes
        // its profile directory.
        await coordinator.ClaimAsync(standard);

        Assert.True(standard.AutoCopy);
        Assert.False(carrier.AutoCopy);
        Assert.False(boxel.AutoCopy);
        CommanderProfileLoadResult savedProfile = await new CommanderProfileStore(temporaryDirectory).LoadAsync(
            "F123",
            true
        );
        Assert.False(savedProfile.Data!.BoxelSearch.AutoCopy);
    }

    [AvaloniaFact]
    public async Task SelectingBoxelAutoCopyAutomaticallyClearsBothRouteSelections()
    {
        RouteWorkspaceViewModel standard = await CreateWorkspaceAsync(FollowRouteKind.Standard);
        RouteWorkspaceViewModel carrier = await CreateWorkspaceAsync(FollowRouteKind.FleetCarrier);
        BoxelSearchViewModel boxel = await CreateConfiguredBoxelAsync(autoCopy: false);
        using var coordinator = new RouteAutoCopyCoordinator(standard, carrier, boxel.Session);
        await coordinator.ClaimAsync(standard);

        await Task.Run(() => boxel.AutoCopy = true);
        await WaitUntilAsync(() => !standard.AutoCopy && !carrier.AutoCopy);
        await coordinator.ClaimAsync(boxel.Session);
        var profileStore = new CommanderProfileStore(temporaryDirectory);
        await WaitUntilAsync(async () =>
            (await profileStore.LoadAsync("F123", true)).Data?.BoxelSearch.AutoCopy == true
        );

        Assert.True(boxel.AutoCopy);
        FollowRouteLoadResult standardSaved = await new FollowRouteStore(temporaryDirectory).LoadAsync("F123");
        FollowRouteLoadResult carrierSaved = await new FollowRouteStore(
            temporaryDirectory,
            FollowRouteKind.FleetCarrier
        ).LoadAsync("F123");
        Assert.False(standardSaved.Route!.AutoCopy);
        Assert.False(carrierSaved.Route!.AutoCopy);
    }

    [AvaloniaFact]
    public async Task BoundBoxelControlsAndRouteSelectionsKeepOneGalaxyMapClipboardOwner()
    {
        var writes = new List<string>();
        RouteWorkspaceViewModel standard = await CreateWorkspaceAsync(FollowRouteKind.Standard);
        RouteWorkspaceViewModel carrier = await CreateWorkspaceAsync(FollowRouteKind.FleetCarrier);
        standard.SetClipboardWriter(text => RecordCopyAsync("Route Manager", text));
        carrier.SetClipboardWriter(text => RecordCopyAsync("FC Routes", text));
        BoxelSearchViewModel boxel = await CreateConfiguredBoxelAsync(
            autoCopy: false,
            active: true,
            clipboardWriter: text => RecordCopyAsync("Boxel", text)
        );
        await boxel.UpdateCurrentSystemAsync("Praea Euq IL-P c5-0", new GalacticCoordinate(1, 2, 3));
        using var coordinator = new RouteAutoCopyCoordinator(standard, carrier, boxel.Session);
        await coordinator.ReconcileAsync();
        var workspaceToggle = new CheckBox();
        var overlayToggle = new CheckBox();
        using IDisposable workspaceBinding = workspaceToggle.Bind(
            ToggleButton.IsCheckedProperty,
            new Binding(nameof(BoxelSearchViewModel.AutoCopy)) { Source = boxel, Mode = BindingMode.TwoWay }
        );
        using IDisposable overlayBinding = overlayToggle.Bind(
            ToggleButton.IsCheckedProperty,
            new Binding(nameof(BoxelSearchViewModel.AutoCopy)) { Source = boxel, Mode = BindingMode.TwoWay }
        );

        overlayToggle.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
        await boxel.RefreshCurrentAsync();
        await coordinator.ClaimAsync(boxel.Session);
        Assert.True(workspaceToggle.IsChecked);
        Assert.True(overlayToggle.IsChecked);
        Assert.False(standard.AutoCopy);
        Assert.False(carrier.AutoCopy);
        await AssertOwnerWritesAsync("Boxel:Praea Euq IL-P c5-0");

        await standard.SetAutoCopyAsync(true);
        await coordinator.ClaimAsync(standard);
        await WaitUntilAsync(() => workspaceToggle.IsChecked == false && overlayToggle.IsChecked == false);
        Assert.False(workspaceToggle.IsChecked);
        Assert.False(overlayToggle.IsChecked);
        Assert.True(standard.AutoCopy);
        Assert.False(carrier.AutoCopy);
        await AssertOwnerWritesAsync("Route Manager:Achenar");

        await carrier.SetAutoCopyAsync(true);
        await coordinator.ClaimAsync(carrier);
        await WaitUntilAsync(() => workspaceToggle.IsChecked == false && overlayToggle.IsChecked == false);
        Assert.False(workspaceToggle.IsChecked);
        Assert.False(overlayToggle.IsChecked);
        Assert.False(standard.AutoCopy);
        Assert.True(carrier.AutoCopy);
        await AssertOwnerWritesAsync("FC Routes:Achenar");

        workspaceToggle.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
        await boxel.RefreshCurrentAsync();
        await coordinator.ClaimAsync(boxel.Session);
        Assert.True(workspaceToggle.IsChecked);
        Assert.True(overlayToggle.IsChecked);
        Assert.False(standard.AutoCopy);
        Assert.False(carrier.AutoCopy);
        await AssertOwnerWritesAsync("Boxel:Praea Euq IL-P c5-0");

        Task RecordCopyAsync(string source, string text)
        {
            writes.Add(source + ":" + text);
            return Task.CompletedTask;
        }

        async Task AssertOwnerWritesAsync(string expected)
        {
            var closed = new EliteStatus { GuiFocus = GuiFocus.NoFocus };
            await standard.UpdateStatusAsync(closed);
            await carrier.UpdateStatusAsync(closed);
            await boxel.UpdateStatusAsync(closed);
            writes.Clear();
            var open = new EliteStatus { GuiFocus = GuiFocus.GalaxyMap };
            await standard.UpdateStatusAsync(open);
            await carrier.UpdateStatusAsync(open);
            await boxel.UpdateStatusAsync(
                open,
                allowAutoCopy: !standard.ShouldAutoCopyNextHop && !carrier.ShouldAutoCopyNextHop
            );
            Assert.Equal(expected, Assert.Single(writes));
        }
    }

    [Fact]
    public async Task ReconcileClearsImplicitSelectionsWithoutSavedRoutes()
    {
        RouteWorkspaceViewModel standard = await CreateUnsavedWorkspaceAsync(FollowRouteKind.Standard);
        RouteWorkspaceViewModel carrier = await CreateUnsavedWorkspaceAsync(FollowRouteKind.FleetCarrier);
        using var coordinator = new RouteAutoCopyCoordinator(standard, carrier, CreateInactiveBoxel().Session);

        Assert.True(standard.AutoCopy);
        Assert.True(carrier.AutoCopy);

        await coordinator.ReconcileAsync();

        Assert.False(standard.AutoCopy);
        Assert.False(carrier.AutoCopy);
        Assert.False(standard.IsDirty);
        Assert.False(carrier.IsDirty);
    }

    private async Task<RouteWorkspaceViewModel> CreateWorkspaceAsync(FollowRouteKind kind, bool isActive = true)
    {
        var store = new FollowRouteStore(temporaryDirectory, kind);
        await store.SaveAsAsync(
            (await store.CreateNewAsync("F123")) with
            {
                IsActive = isActive,
                AutoCopy = true,
                LastReachedIndex = 0,
                Hops =
                [
                    new FollowRouteHop("Sol", 1, null, null, false, false),
                    new FollowRouteHop("Achenar", 2, null, null, false, false),
                ],
            },
            kind == FollowRouteKind.FleetCarrier ? "Carrier Route" : "Standard Route"
        );
        var workspace = new RouteWorkspaceViewModel(
            new FollowRouteService(store),
            new RouteNameImporter(new EmptyResolver()),
            new EmptySpanshClient(),
            kind
        );
        await workspace.UpdateContextAsync("F123", "Sol", 1, null);
        return workspace;
    }

    private async Task<RouteWorkspaceViewModel> CreateUnsavedWorkspaceAsync(FollowRouteKind kind)
    {
        var store = new FollowRouteStore(temporaryDirectory, kind);
        var workspace = new RouteWorkspaceViewModel(
            new FollowRouteService(store),
            new RouteNameImporter(new EmptyResolver()),
            new EmptySpanshClient(),
            kind
        );
        await workspace.UpdateContextAsync("F123", "Sol", 1, null);
        return workspace;
    }

    private BoxelSearchViewModel CreateInactiveBoxel(Func<string, Task>? clipboardWriter = null)
    {
        BoxelSearchViewModel viewModel = BoxelSearchViewModelTestFactory.Create(
            new CommanderProfileStore(temporaryDirectory),
            new LegacySystemDataReader(temporaryDirectory),
            new EmptyBoxelStore(temporaryDirectory),
            new EmptyBoxelResolver(),
            out BoxelSearchSession? session,
            clipboardWriter
        );
        sessions.Add(session);
        return viewModel;
    }

    private async Task<BoxelSearchViewModel> CreateConfiguredBoxelAsync(
        bool autoCopy,
        bool active = false,
        Func<string, Task>? clipboardWriter = null
    )
    {
        BoxelSearchViewModel boxel = CreateInactiveBoxel(clipboardWriter);
        var top = BoxelAddress.Parse("Praea Euq IL-P c5-0");
        await boxel.LoadProfileAsync(
            "F123",
            "Drew",
            true,
            new BoxelSearchSnapshot
            {
                Active = active,
                TopBoxel = top,
                Current = top,
                StartedOn = DateTimeOffset.Parse(
                    "2026-08-01T00:00:00Z",
                    global::System.Globalization.CultureInfo.InvariantCulture
                ),
                CurrentCount = 1,
                LowMassCode = 'c',
                AutoCopy = autoCopy,
            }
        );
        return boxel;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition() && !timeout.IsCancellationRequested)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        bool satisfied = await condition();
        while (!satisfied && !timeout.IsCancellationRequested)
        {
            await Task.Delay(10);
            satisfied = await condition();
        }

        Assert.True(satisfied);
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        foreach (BoxelSearchSession? session in sessions.AsEnumerable().Reverse())
        {
            await session.DisposeAsync();
        }

        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private sealed class EmptyResolver : IStarSystemResolver
    {
        public Task<IReadOnlyList<StarSystemReference>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult<IReadOnlyList<StarSystemReference>>([]);
        }
    }

    private sealed class EmptySpanshClient : ISpanshRouteClient
    {
        public Task<IReadOnlyList<FollowRouteHop>> GetRouteAsync(
            SpanshRouteReference route,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult<IReadOnlyList<FollowRouteHop>>([]);
        }
    }

    private sealed class EmptyBoxelResolver : IBoxelSystemResolver
    {
        public Task<IReadOnlyList<BoxelSystemObservation>> SearchAsync(
            BoxelAddress boxel,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult<IReadOnlyList<BoxelSystemObservation>>([]);
        }
    }
}
