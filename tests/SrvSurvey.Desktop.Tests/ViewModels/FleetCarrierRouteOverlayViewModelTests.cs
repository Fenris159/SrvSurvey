using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class FleetCarrierRouteOverlayViewModelTests : IAsyncLifetime
{
    private readonly List<RouteWorkspaceViewModel> workspaces = [];
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-fc-overlay-tests-{Guid.NewGuid():N}"
    );

    /// <summary>
    /// Checks carrier logistics and its ETR as one compact overlay readout.
    /// </summary>
    [Fact]
    public async Task FormatsNextCarrierHopAsCompactLogisticsReadout()
    {
        var store = new FollowRouteStore(temporaryDirectory, FollowRouteKind.FleetCarrier);
        FollowRouteDocument document = (await store.CreateNewAsync("F123")) with
        {
            Name = "Carrier Test",
            IsActive = true,
            LastReachedIndex = 0,
            Kind = FollowRouteKind.FleetCarrier,
            Hops =
            [
                Hop("Sol", null),
                Hop(
                    "Col 359 Sector EE-X b16-1",
                    new FollowRouteCarrierHop(499.76, 21502.09, 1000, 2799, 93, true, true, true, 3892)
                ),
                Hop("Colonia", null),
            ],
        };
        await store.SaveAsAsync(document, "Carrier Test");
        var route = new RouteWorkspaceViewModel(
            new FollowRouteService(store),
            new RouteNameImporter(new EmptyResolver()),
            new EmptySpanshClient(),
            FollowRouteKind.FleetCarrier
        );
        workspaces.Add(route);
        await route.UpdateContextAsync("F123", "Sol", null, null);
        using var viewModel = new FleetCarrierRouteOverlayViewModel(
            route,
            OverlayPlatformCapabilities.ForHost(OverlayHostKind.Other)
        );

        Assert.Equal("HOP 1 / 2", viewModel.HopProgress);
        Assert.Equal("Col 359 Sector EE-X b16-1", viewModel.SystemName);
        Assert.Equal($"{499.76:N2} LY JUMP  •  {21502.09:N2} LY REMAINING", viewModel.JumpSummary);
        Assert.Equal("1 JUMP LEFT • ETR: 20min", viewModel.JumpsLeft);
        Assert.Equal($"{1000:N0} t", viewModel.FuelLeft);
        Assert.Equal($"{2799:N0} t", viewModel.TritiumInMarket);
        Assert.Equal($"{93:N0} t", viewModel.JumpFuel);
        Assert.True(viewModel.HasIcyRing);
        Assert.Equal("PRISTINE ICY RING", viewModel.IcyRingLabel);
        Assert.True(viewModel.HasRestockWarning);
        Assert.Equal($"{3892:N0} t", viewModel.RestockAmount);
    }

    /// <summary>
    /// Checks that an empty route offers no invented fuel, restock or time estimate.
    /// </summary>
    [Fact]
    public async Task FormatsEmptyRouteWithoutInventingCarrierLogistics()
    {
        var store = new FollowRouteStore(temporaryDirectory, FollowRouteKind.FleetCarrier);
        RouteWorkspaceViewModel route = CreateRoute(store);
        await route.UpdateContextAsync("F999", null, null, null);
        using var viewModel = new FleetCarrierRouteOverlayViewModel(
            route,
            OverlayPlatformCapabilities.ForHost(OverlayHostKind.Other)
        );

        Assert.Equal("NO ROUTE", viewModel.HopProgress);
        Assert.Empty(viewModel.JumpSummary);
        Assert.Equal("0 JUMPS LEFT", viewModel.JumpsLeft);
        Assert.False(route.HasCarrierEstimatedTimeRemaining);
        Assert.Equal("\u2014", viewModel.FuelLeft);
        Assert.Equal("\u2014", viewModel.TritiumInMarket);
        Assert.Equal("\u2014", viewModel.JumpFuel);
        Assert.False(viewModel.HasIcyRing);
        Assert.Equal("ICY RING", viewModel.IcyRingLabel);
        Assert.False(viewModel.HasRestockWarning);
        Assert.Equal("\u2014", viewModel.RestockAmount);
        Assert.False(viewModel.HasCountdown);
        Assert.False(viewModel.HasCountdownPhaseTime);
    }

    /// <summary>
    /// Checks that not started route labels its first system as start.
    /// </summary>
    [Fact]
    public async Task NotStartedRouteLabelsItsFirstSystemAsStart()
    {
        var store = new FollowRouteStore(temporaryDirectory, FollowRouteKind.FleetCarrier);
        FollowRouteDocument document = (await store.CreateNewAsync("F456")) with
        {
            Name = "Carrier Start",
            IsActive = true,
            LastReachedIndex = -1,
            Kind = FollowRouteKind.FleetCarrier,
            Hops = [Hop("Sol", null, 1), Hop("Colonia", null, 2)],
        };
        await store.SaveAsAsync(document, "Carrier Start");
        RouteWorkspaceViewModel route = CreateRoute(store);
        await route.UpdateContextAsync("F456", null, null, null);
        using var viewModel = new FleetCarrierRouteOverlayViewModel(
            route,
            OverlayPlatformCapabilities.ForHost(OverlayHostKind.Other)
        );

        Assert.Equal("START", viewModel.HopProgress);
        Assert.Equal("Sol", viewModel.SystemName);
        Assert.True(route.HasCarrierEstimatedTimeRemaining);
        Assert.Equal("ETR: 20min", route.CarrierEstimatedTimeRemaining);
    }

    /// <summary>
    /// Checks that final carrier arrival shows finished for three seconds.
    /// </summary>
    [Fact]
    public async Task FinalCarrierArrivalShowsFinishedForThreeSeconds()
    {
        var store = new FollowRouteStore(temporaryDirectory, FollowRouteKind.FleetCarrier);
        FollowRouteDocument document = (await store.CreateNewAsync("F321")) with
        {
            Name = "Carrier Finish",
            IsActive = true,
            LastReachedIndex = 1,
            Kind = FollowRouteKind.FleetCarrier,
            Hops = [Hop("Sol", null, 1), Hop("Second", null, 2), Hop("Colonia", null, 3)],
        };
        await store.SaveAsAsync(document, "Carrier Finish");
        var time = new MutableTimeProvider(
            DateTimeOffset.Parse("2026-08-12T12:00:00Z", global::System.Globalization.CultureInfo.InvariantCulture)
        );
        var route = new RouteWorkspaceViewModel(
            new FollowRouteService(store),
            new RouteNameImporter(new EmptyResolver()),
            new EmptySpanshClient(),
            FollowRouteKind.FleetCarrier,
            () => time.GetUtcNow()
        );
        workspaces.Add(route);
        await route.UpdateContextAsync("F321", "Second", 2, null);
        using var viewModel = new FleetCarrierRouteOverlayViewModel(
            route,
            OverlayPlatformCapabilities.ForHost(OverlayHostKind.Other),
            time
        );

        Assert.Equal("HOP 2 / 2", viewModel.HopProgress);
        await route.ApplyJournalEventsAsync([
            Parse(
                """
                {"event":"CarrierJump","StarSystem":"Colonia","SystemAddress":3}
                """
            ),
        ]);

        Assert.Equal("FINISHED", viewModel.HopProgress);
        Assert.Equal("Colonia", viewModel.SystemName);
        Assert.True(viewModel.ShouldShow);
        Assert.Empty(route.CarrierEstimatedTimeRemaining);
        Assert.False(route.HasCarrierEstimatedTimeRemaining);
        Assert.Equal("0 JUMPS LEFT", viewModel.JumpsLeft);

        time.Advance(TimeSpan.FromMilliseconds(2999));
        viewModel.AdvanceTimedTransitions();
        Assert.True(viewModel.ShouldShow);

        time.Advance(TimeSpan.FromMilliseconds(1));
        viewModel.AdvanceTimedTransitions();
        Assert.True(viewModel.ShouldShow);
        Assert.True(viewModel.IsPassive);
        Assert.Equal("COOLDOWN", viewModel.HopProgress);
    }

    /// <summary>
    /// Checks that route and countdown changes raise their complete property groups.
    /// </summary>
    [Fact]
    public async Task RouteAndCountdownChangesRaiseTheirCompletePropertyGroups()
    {
        var store = new FollowRouteStore(temporaryDirectory, FollowRouteKind.FleetCarrier);
        FollowRouteDocument document = (await store.CreateNewAsync("F789")) with
        {
            Name = "Carrier Notifications",
            IsActive = true,
            LastReachedIndex = 0,
            Kind = FollowRouteKind.FleetCarrier,
            Hops =
            [
                Hop("Sol", null, 1),
                Hop("Second", new FollowRouteCarrierHop(499, 1000, 500, null, 88, true, false, false, null), 2),
                Hop("Third", null, 3),
                Hop("Fourth", null, 4),
            ],
        };
        await store.SaveAsAsync(document, "Carrier Notifications");
        var now = new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
        var route = new RouteWorkspaceViewModel(
            new FollowRouteService(store),
            new RouteNameImporter(new EmptyResolver()),
            new EmptySpanshClient(),
            FollowRouteKind.FleetCarrier,
            () => now
        );
        workspaces.Add(route);
        await route.UpdateContextAsync("F789", "Sol", 1, null);
        var viewModel = new FleetCarrierRouteOverlayViewModel(
            route,
            OverlayPlatformCapabilities.ForHost(OverlayHostKind.Other)
        );
        var notifications = new List<string>();
        viewModel.PropertyChanged += (_, eventArgs) => notifications.Add(eventArgs.PropertyName!);

        Assert.Equal("2 JUMPS LEFT • ETR: 40min", viewModel.JumpsLeft);
        Assert.True(viewModel.HasIcyRing);
        Assert.Equal("ICY RING", viewModel.IcyRingLabel);
        await route.ApplyJournalEventsAsync([
            Parse(
                """
                {"event":"CarrierJump","StarSystem":"Second","SystemAddress":2}
                """
            ),
        ]);

        Assert.Contains(nameof(FleetCarrierRouteOverlayViewModel.HopProgress), notifications);
        Assert.Contains(nameof(FleetCarrierRouteOverlayViewModel.SystemName), notifications);
        Assert.Contains(nameof(FleetCarrierRouteOverlayViewModel.JumpSummary), notifications);
        Assert.Contains(nameof(FleetCarrierRouteOverlayViewModel.RestockAmount), notifications);
        Assert.Equal("1 JUMP LEFT • ETR: 20min", viewModel.JumpsLeft);
        Assert.Equal("ETR: 20min", route.CarrierEstimatedTimeRemaining);

        notifications.Clear();
        route.ApplyFleetCarrierJumpEvents([
            Parse(
                """
                {"timestamp":"2026-08-02T12:00:00Z","event":"CarrierJumpRequest","CarrierID":123,"SystemName":"Third","DepartureTime":"2026-08-02T12:15:00Z"}
                """
            ),
        ]);

        Assert.True(viewModel.HasCountdown);
        Assert.Equal("DEPARTURE TO THIRD", viewModel.CountdownTitle);
        Assert.Equal("15:00", viewModel.Countdown);
        Assert.Equal("JUMP INITIATION IN", viewModel.CountdownPhase);
        Assert.True(viewModel.HasCountdownPhaseTime);
        Assert.Contains(nameof(FleetCarrierRouteOverlayViewModel.HasCountdown), notifications);
        Assert.Contains(nameof(FleetCarrierRouteOverlayViewModel.CountdownTitle), notifications);
        Assert.Contains(nameof(FleetCarrierRouteOverlayViewModel.CountdownPhaseTime), notifications);

        now = now.AddMinutes(1);
        notifications.Clear();
        route.RefreshCarrierJumpCountdown();
        Assert.Equal("14:00", viewModel.Countdown);
        Assert.Equal("1 JUMP LEFT • ETR: 20min", viewModel.JumpsLeft);
        Assert.DoesNotContain(nameof(FleetCarrierRouteOverlayViewModel.JumpsLeft), notifications);

        notifications.Clear();
        viewModel.Dispose();
        now = now.AddMinutes(1);
        route.RefreshCarrierJumpCountdown();
        Assert.Empty(notifications);
    }

    /// <summary>
    /// Checks minute, hour and day boundaries and omission of zero-valued ETR units.
    /// </summary>
    [Theory]
    [InlineData(0, "")]
    [InlineData(1, "ETR: 20min")]
    [InlineData(3, "ETR: 1h")]
    [InlineData(44, "ETR: 14h 40min")]
    [InlineData(71, "ETR: 23h 40min")]
    [InlineData(72, "ETR: 1d")]
    [InlineData(73, "ETR: 1d 20min")]
    [InlineData(144, "ETR: 2d")]
    [InlineData(1000, "ETR: 13d 21h 20min")]
    public async Task CarrierEstimateUsesTwentyMinutesPerRemainingJump(int jumps, string expected)
    {
        var store = new FollowRouteStore(temporaryDirectory, FollowRouteKind.FleetCarrier);
        await store.SaveAsAsync(
            (await store.CreateNewAsync("F123")) with
            {
                IsActive = true,
                LastReachedIndex = -1,
                Hops = Enumerable.Range(0, jumps + 1).Select(index => Hop("System " + index, null)).ToArray(),
            },
            "Carrier estimate"
        );
        RouteWorkspaceViewModel route = CreateRoute(store);
        await route.UpdateContextAsync("F123", null, null, null);

        Assert.Equal(jumps, route.RemainingJumps);
        Assert.Equal(expected, route.CarrierEstimatedTimeRemaining);
        Assert.Equal(jumps > 0, route.HasCarrierEstimatedTimeRemaining);
        route.IsActive = false;
        Assert.Equal(expected, route.CarrierEstimatedTimeRemaining);
    }

    /// <summary>
    /// Checks live ETR updates, typography and final visibility in both overlay hosts.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EstimateBindingsRefreshInWorkspaceAndBothOverlayHosts(bool separateWindow)
    {
        var store = new FollowRouteStore(temporaryDirectory, FollowRouteKind.FleetCarrier);
        await store.SaveAsAsync(
            (await store.CreateNewAsync("F123")) with
            {
                IsActive = true,
                LastReachedIndex = 0,
                Hops = Enumerable.Range(0, 46).Select(index => Hop("System " + index, null)).ToArray(),
            },
            "Carrier estimate"
        );
        RouteWorkspaceViewModel route = CreateRoute(store);
        await route.UpdateContextAsync("F123", null, null, null);
        using var overlay = new FleetCarrierRouteOverlayViewModel(
            route,
            OverlayPlatformCapabilities.ForHost(OverlayHostKind.Other)
        );
        Window window = separateWindow
            ? new FleetCarrierRouteOverlayWindow(overlay)
            : new Window { Content = new FleetCarrierRouteOverlayPresentation { DataContext = overlay } };
        var workspace = new RouteWindow(route);
        try
        {
            window.Show();
            workspace.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            using WriteableBitmap? workspaceFrame = workspace.CaptureRenderedFrame();
            TextBlock summary = window
                .GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(block => block.Text == "44 JUMPS LEFT • ETR: 14h 40min");
            Assert.Contains("muted", summary.Classes);
            Assert.Contains("overlay-detail", summary.Classes);
            Assert.Contains("type-detail", summary.Classes);
            TextBlock readout = workspace.FindControl<TextBlock>("CarrierEstimatedTimeRemainingReadout")!;
            Assert.True(readout.IsEffectivelyVisible);
            Assert.Equal(route.CarrierEstimatedTimeRemaining, readout.Text);
            SaveEstimateFrame(window, separateWindow ? "carrier-overlay.png" : "carrier-inline.png");
            SaveEstimateFrame(workspace, "carrier-workspace.png");

            var changes = new List<string>();
            route.PropertyChanged += (_, args) => changes.Add(args.PropertyName!);
            route.SetProgressThrough(1, true);
            using WriteableBitmap? updated = window.CaptureRenderedFrame();
            using WriteableBitmap? updatedWorkspace = workspace.CaptureRenderedFrame();
            Assert.Equal("43 JUMPS LEFT • ETR: 14h 20min", summary.Text);
            Assert.Equal("ETR: 14h 20min", readout.Text);
            Assert.Contains(nameof(RouteWorkspaceViewModel.CarrierEstimatedTimeRemaining), changes);
            route.SetProgressThrough(0, false);
            Assert.Equal("ETR: 15h", route.CarrierEstimatedTimeRemaining);
            route.SetProgressThrough(45, true);
            using WriteableBitmap? completedWorkspace = workspace.CaptureRenderedFrame();
            using WriteableBitmap? completedOverlay = window.CaptureRenderedFrame();
            Assert.False(readout.IsVisible);
            Assert.Equal(string.Empty, readout.Text);
            Assert.Equal("0 JUMPS LEFT", summary.Text);
        }
        finally
        {
            workspace.Close();
            window.Close();
        }
    }

    /// <summary>
    /// Optionally captures ETR layouts for visual verification.
    /// </summary>
    private static void SaveEstimateFrame(Window window, string name)
    {
        string? output = Environment.GetEnvironmentVariable("SRVSURVEY_CARRIER_ETR_RENDER_OUTPUT");
        if (output is null)
        {
            return;
        }
        Directory.CreateDirectory(output);
        using WriteableBitmap frame = window.CaptureRenderedFrame()!;
        using FileStream stream = File.Create(Path.Combine(output, name));
        frame.Save(stream, PngBitmapEncoderOptions.Default);
    }

    /// <summary>
    /// Exercises live bindings through ticking, phase changes, and countdown visibility in both overlay hosts.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void LivePresentationUpdatesCountdownAndJumpStatus(bool separateWindow, bool initiallyScheduled)
    {
        var now = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
        var route = new RouteWorkspaceViewModel(
            new FollowRouteService(new FollowRouteStore(temporaryDirectory, FollowRouteKind.FleetCarrier)),
            new RouteNameImporter(new EmptyResolver()),
            new EmptySpanshClient(),
            FollowRouteKind.FleetCarrier,
            () => now
        );
        workspaces.Add(route);
        JournalEventEnvelope request = Parse(
            """
            {"timestamp":"2026-10-11T12:00:00Z","event":"CarrierJumpRequest","CarrierID":123,"SystemName":"Colonia","DepartureTime":"2026-10-11T12:15:00Z"}
            """
        );
        if (initiallyScheduled)
        {
            route.ApplyFleetCarrierJumpEvents([request]);
        }

        using var viewModel = new FleetCarrierRouteOverlayViewModel(
            route,
            OverlayPlatformCapabilities.ForHost(OverlayHostKind.Other)
        );
        Window window = separateWindow
            ? new FleetCarrierRouteOverlayWindow(viewModel)
            : new Window { Content = new FleetCarrierRouteOverlayPresentation { DataContext = viewModel } };
        try
        {
            window.Show();
            TextBlock countdown = window
                .GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(block => block.Classes.Contains("overlay-value"));
            StackPanel countdownPanel = countdown.FindAncestorOfType<StackPanel>()!;
            TextBlock[] phase = countdownPanel
                .GetVisualDescendants()
                .OfType<TextBlock>()
                .Where(block => block.Classes.Contains("overlay-detail"))
                .ToArray();
            Assert.Equal(initiallyScheduled, countdownPanel.IsVisible);
            if (!initiallyScheduled)
            {
                route.ApplyFleetCarrierJumpEvents([request]);
            }

            Assert.True(countdownPanel.IsVisible);
            Assert.Equal("15:00", countdown.Text);
            Assert.Equal("JUMP INITIATION IN", phase[0].Text);
            Assert.Equal("5:00", phase[1].Text);

            now = now.AddSeconds(1);
            route.RefreshCarrierJumpCountdown();
            Assert.Equal("14:59", route.CarrierJumpCountdownValue);
            Assert.Equal(route.CarrierJumpCountdownValue, countdown.Text);
            Assert.Equal("4:59", phase[1].Text);

            now = now.AddMinutes(5);
            route.RefreshCarrierJumpCountdown();
            Assert.Equal("9:59", countdown.Text);
            Assert.Equal("PAD LOCKDOWN IN", phase[0].Text);
            Assert.Equal("6:39", phase[1].Text);

            now = now.AddMinutes(7);
            route.RefreshCarrierJumpCountdown();
            Assert.Equal("2:59", countdown.Text);
            Assert.Equal("LANDING PADS LOCKED", phase[0].Text);
            Assert.False(phase[1].IsVisible);

            now = new DateTimeOffset(2026, 10, 11, 12, 15, 0, TimeSpan.Zero);
            route.RefreshCarrierJumpCountdown();
            Assert.Equal(string.Empty, countdown.Text);
            Assert.Equal("AWAITING ARRIVAL", phase[0].Text);
            route.ApplyFleetCarrierJumpEvents([
                Parse("""{"timestamp":"2026-10-11T12:15:00Z","event":"CarrierJump","StarSystem":"Colonia"}"""),
            ]);
            Assert.Equal("5:00", countdown.Text);
            Assert.Equal("CARRIER ARRIVED", phase[0].Text);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "JUMP COOLDOWN");

            now = now.AddSeconds(1);
            route.RefreshCarrierJumpCountdown();
            Assert.Equal("4:59", countdown.Text);

            now = now.AddMinutes(5);
            route.RefreshCarrierJumpCountdown();
            Assert.False(countdownPanel.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        foreach (RouteWorkspaceViewModel workspace in workspaces)
        {
            await workspace.FlushWorkspaceAsync();
        }

        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private static FollowRouteHop Hop(string name, FollowRouteCarrierHop? carrier, long? systemAddress = null)
    {
        return new FollowRouteHop(name, systemAddress, null, null, false, false, Carrier: carrier);
    }

    private RouteWorkspaceViewModel CreateRoute(FollowRouteStore store)
    {
        var route = new RouteWorkspaceViewModel(
            new FollowRouteService(store),
            new RouteNameImporter(new EmptyResolver()),
            new EmptySpanshClient(),
            FollowRouteKind.FleetCarrier
        );
        workspaces.Add(route);
        return route;
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(
            JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? journalEvent, out string? error),
            error
        );
        return journalEvent!;
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

    private sealed class MutableTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;

        public void Advance(TimeSpan duration)
        {
            value += duration;
        }
    }
}
