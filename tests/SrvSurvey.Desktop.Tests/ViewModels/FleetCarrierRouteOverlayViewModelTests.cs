using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class FleetCarrierRouteOverlayViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-fc-overlay-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task FormatsNextCarrierHopAsCompactLogisticsReadout()
    {
        var store = new FollowRouteStore(
            temporaryDirectory,
            FollowRouteKind.FleetCarrier);
        var document = (await store.CreateNewAsync("F123")) with
        {
            Name = "Carrier Test",
            IsActive = true,
            LastReachedIndex = 0,
            Kind = FollowRouteKind.FleetCarrier,
            Hops =
            [
                Hop("Sol", null),
                Hop("Col 359 Sector EE-X b16-1", new FollowRouteCarrierHop(
                    499.76,
                    21502.09,
                    1000,
                    2799,
                    93,
                    true,
                    true,
                    true,
                    3892)),
                Hop("Colonia", null),
            ],
        };
        await store.SaveAsAsync(document, "Carrier Test");
        var route = new RouteWorkspaceViewModel(
            new FollowRouteService(store),
            new RouteNameImporter(new EmptyResolver()),
            new EmptySpanshClient(),
            FollowRouteKind.FleetCarrier);
        await route.UpdateContextAsync("F123", "Sol", null, null);
        using var viewModel = new FleetCarrierRouteOverlayViewModel(
            route,
            OverlayPlatformCapabilities.ForHost(OverlayHostKind.Other));

        Assert.Equal("HOP 2 / 3", viewModel.HopProgress);
        Assert.Equal("Col 359 Sector EE-X b16-1", viewModel.SystemName);
        Assert.Equal(
            $"{499.76:N2} LY JUMP  •  {21502.09:N2} LY REMAINING",
            viewModel.JumpSummary);
        Assert.Equal("1 JUMP LEFT", viewModel.JumpsLeft);
        Assert.Equal($"{1000:N0} t", viewModel.FuelLeft);
        Assert.Equal($"{2799:N0} t", viewModel.TritiumInMarket);
        Assert.Equal($"{93:N0} t", viewModel.JumpFuel);
        Assert.True(viewModel.HasIcyRing);
        Assert.Equal("PRISTINE ICY RING", viewModel.IcyRingLabel);
        Assert.True(viewModel.HasRestockWarning);
        Assert.Equal($"{3892:N0} t", viewModel.RestockAmount);
    }

    [Fact]
    public async Task FormatsEmptyRouteWithoutInventingCarrierLogistics()
    {
        var store = new FollowRouteStore(
            temporaryDirectory,
            FollowRouteKind.FleetCarrier);
        var route = CreateRoute(store);
        await route.UpdateContextAsync("F999", null, null, null);
        using var viewModel = new FleetCarrierRouteOverlayViewModel(
            route,
            OverlayPlatformCapabilities.ForHost(OverlayHostKind.Other));

        Assert.Equal("NO ROUTE", viewModel.HopProgress);
        Assert.Equal("\u2014 LY JUMP  \u2022  \u2014 LY REMAINING", viewModel.JumpSummary);
        Assert.Equal("0 JUMPS LEFT", viewModel.JumpsLeft);
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

    [Fact]
    public async Task RouteAndCountdownChangesRaiseTheirCompletePropertyGroups()
    {
        var store = new FollowRouteStore(
            temporaryDirectory,
            FollowRouteKind.FleetCarrier);
        var document = (await store.CreateNewAsync("F789")) with
        {
            Name = "Carrier Notifications",
            IsActive = true,
            LastReachedIndex = 0,
            Kind = FollowRouteKind.FleetCarrier,
            Hops =
            [
                Hop("Sol", null, 1),
                Hop("Second", new FollowRouteCarrierHop(
                    499,
                    1000,
                    500,
                    null,
                    88,
                    true,
                    false,
                    false,
                    null), 2),
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
            () => now);
        await route.UpdateContextAsync("F789", "Sol", 1, null);
        var viewModel = new FleetCarrierRouteOverlayViewModel(
            route,
            OverlayPlatformCapabilities.ForHost(OverlayHostKind.Other));
        var notifications = new List<string>();
        viewModel.PropertyChanged += (_, eventArgs) =>
            notifications.Add(eventArgs.PropertyName!);

        Assert.Equal("2 JUMPS LEFT", viewModel.JumpsLeft);
        Assert.True(viewModel.HasIcyRing);
        Assert.Equal("ICY RING", viewModel.IcyRingLabel);
        await route.ApplyJournalEventsAsync(
        [
            Parse(
                """
                {"event":"CarrierJump","StarSystem":"Second","SystemAddress":2}
                """),
        ]);

        Assert.Contains(nameof(FleetCarrierRouteOverlayViewModel.HopProgress), notifications);
        Assert.Contains(nameof(FleetCarrierRouteOverlayViewModel.SystemName), notifications);
        Assert.Contains(nameof(FleetCarrierRouteOverlayViewModel.JumpSummary), notifications);
        Assert.Contains(nameof(FleetCarrierRouteOverlayViewModel.RestockAmount), notifications);

        notifications.Clear();
        route.ApplyFleetCarrierJumpEvents(
        [
            Parse(
                """
                {"timestamp":"2026-08-02T12:00:00Z","event":"CarrierJumpRequest","CarrierID":123,"SystemName":"Third","DepartureTime":"2026-08-02T12:15:00Z"}
                """),
        ]);

        Assert.True(viewModel.HasCountdown);
        Assert.Equal("DEPARTURE TO THIRD", viewModel.CountdownTitle);
        Assert.Equal("15:00", viewModel.Countdown);
        Assert.Equal("JUMP INITIATION IN", viewModel.CountdownPhase);
        Assert.True(viewModel.HasCountdownPhaseTime);
        Assert.Contains(nameof(FleetCarrierRouteOverlayViewModel.HasCountdown), notifications);
        Assert.Contains(nameof(FleetCarrierRouteOverlayViewModel.CountdownTitle), notifications);
        Assert.Contains(nameof(FleetCarrierRouteOverlayViewModel.CountdownPhaseTime), notifications);

        notifications.Clear();
        viewModel.Dispose();
        now = now.AddMinutes(1);
        route.RefreshCarrierJumpCountdown();
        Assert.Empty(notifications);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private static FollowRouteHop Hop(
        string name,
        FollowRouteCarrierHop? carrier,
        long? systemAddress = null)
    {
        return new FollowRouteHop(
            name,
            systemAddress,
            null,
            null,
            false,
            false,
            Carrier: carrier);
    }

    private static RouteWorkspaceViewModel CreateRoute(FollowRouteStore store)
    {
        return new RouteWorkspaceViewModel(
            new FollowRouteService(store),
            new RouteNameImporter(new EmptyResolver()),
            new EmptySpanshClient(),
            FollowRouteKind.FleetCarrier);
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(
            JournalEventEnvelope.TryParse(json, out var journalEvent, out var error),
            error);
        return journalEvent!;
    }

    private sealed class EmptyResolver : IStarSystemResolver
    {
        public Task<IReadOnlyList<StarSystemReference>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<StarSystemReference>>([]);
        }
    }

    private sealed class EmptySpanshClient : ISpanshRouteClient
    {
        public Task<IReadOnlyList<FollowRouteHop>> GetRouteAsync(
            SpanshRouteReference route,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<FollowRouteHop>>([]);
        }
    }
}
