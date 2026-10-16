using SrvSurvey.Core.Frontier;
using SrvSurvey.Core.Routes;
using SrvSurvey.Desktop.Platform.Frontier;
using SrvSurvey.Desktop.Tests.ProfileSync;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class RouteCarrierRefreshIntegrationTests
{
    [Theory]
    [InlineData("failure")]
    [InlineData("unchanged")]
    [InlineData("unlinked")]
    [InlineData("busy")]
    [InlineData("other commander")]
    [InlineData("success")]
    public async Task RouteRefreshReportsWhetherTheProductionProfileActuallyRefreshed(string outcome)
    {
        using var root = new ProfileSyncTestRoot();
        var service = new AccountService { Outcome = outcome };
        using var profile = new CommanderProfileViewModel(service);
        await using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(
            null,
            builder => builder.WithAppDataPaths(root.Paths).WithFrontierProfile(profile)
        );
        await profile.SetCommanderContextAsync("F1", "CMDR", refreshIfOpen: true);
        await profile.OpenAsync();
        var store = new FollowRouteStore(root.Paths.DataDirectory, FollowRouteKind.FleetCarrier);
        await store.SaveAsAsync(
            new FollowRouteDocument(
                "F1",
                store.GetPath("F1"),
                true,
                false,
                0,
                [new("A", 1, new(0, 0, 0), null, false, false), new("B", 2, new(100, 0, 0), null, false, false)],
                Kind: FollowRouteKind.FleetCarrier
            ),
            "Refresh test"
        );
        await main.FleetCarrierRoute.UpdateContextAsync("F1", "A", 1, null);
        Assert.True(main.FleetCarrierRoute.HasRoute);
        if (outcome == "other commander")
        {
            await profile.SelectCommanderAsync(FrontierCommanderSelectionOption.Available("F2", "Other"));
        }
        var release = new TaskCompletionSource<FrontierAccountSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        Task pending = Task.CompletedTask;
        if (outcome == "busy")
        {
            service.PendingRefresh = release.Task;
            pending = profile.RefreshAsync();
            Assert.True(profile.IsBusy);
        }
        try
        {
            await main.FleetCarrierRoute.RefreshAsync();
            if (outcome == "success")
            {
                Assert.Contains("recalculated", main.FleetCarrierRoute.StatusMessage);
                Assert.NotSame(service.Snapshot, profile.Snapshot);
            }
            else
            {
                Assert.Contains("could not be refreshed", main.FleetCarrierRoute.StatusMessage);
                Assert.DoesNotContain("recalculated", main.FleetCarrierRoute.StatusMessage);
            }
            Assert.Equal(1, main.FleetCarrierRoute.ReachedCount);
            Assert.Equal("B", main.FleetCarrierRoute.NextHopName);
        }
        finally
        {
            release.TrySetResult(service.Snapshot);
            await pending;
        }
    }

    private sealed class AccountService : IFrontierAccountService
    {
        public required string Outcome { get; init; }
        public Task<FrontierAccountSnapshot>? PendingRefresh { get; set; }
        public FrontierAccountSnapshot Snapshot { get; } =
            new("CMDR", 0, 0, false, true, "A", "", null, [], [], [], null, DateTimeOffset.UtcNow);

        public event EventHandler? AuthorizationCallbackReceived
        {
            add
            { /* This fixture does not emit authorization callbacks. */
            }
            remove
            { /* This fixture does not emit authorization callbacks. */
            }
        }

        public void SetActiveCommander(string? frontierId, string? commanderName) { }

        public Task<IReadOnlyList<FrontierLinkedCommander>> GetLinkedCommandersAsync(
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<FrontierLinkedCommander>>([]);

        public Task<FrontierAccountState> GetStateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new FrontierAccountState(Outcome != "unlinked", Snapshot, Snapshot.FetchedAt));

        public Task<FrontierAccountSnapshot> RefreshAsync(CancellationToken cancellationToken = default) =>
            RefreshAsync(false, cancellationToken);

        public Task<FrontierAccountSnapshot> RefreshAsync(
            bool forceCarrierRefresh,
            CancellationToken cancellationToken = default
        )
        {
            if (PendingRefresh is not null)
            {
                return PendingRefresh;
            }
            if (Outcome == "failure")
            {
                return Task.FromException<FrontierAccountSnapshot>(new IOException("Carrier refresh unavailable"));
            }
            FrontierAccountSnapshot result =
                Outcome == "success" ? Snapshot with { FetchedAt = Snapshot.FetchedAt.AddMinutes(1) } : Snapshot;
            return Task.FromResult(result);
        }

        public Task<FrontierAccountSnapshot> ConnectAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task CancelConnectionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UnlinkAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Dispose() { }
    }
}
