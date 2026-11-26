using SrvSurvey.Core.Updates;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class ReferenceDataUpdateViewModelTests
{
    [Fact]
    public async Task RefreshAsyncReportsActivatedCatalogsAndVerifiedBackup()
    {
        var result = new PublishedReferenceUpdateResult(
            PublishedReferenceVersions.Empty,
            new PublishedReferenceVersions(10, 7, 4, 48, 68, 15, 0, 1),
            ["Codex reference", "biology criteria"],
            [],
            "/profiles/reference-backups/verified"
        );
        var viewModel = new ReferenceDataUpdateViewModel(
            new StubService(result),
            Path.GetTempPath(),
            "Embedded catalogs active."
        );

        await viewModel.RefreshAsync();

        Assert.True(viewModel.IsRestartRequired);
        Assert.Contains("Codex reference", viewModel.UpdatedCatalogs);
        Assert.Equal("Backup created this session: /profiles/reference-backups/verified", viewModel.BackupDirectory);
        Assert.Contains("Restart SrvSurvey", viewModel.StatusMessage);
    }

    [Fact]
    public async Task CurrentCatalogsExplainThatNoUpdateOrBackupWasNeeded()
    {
        var result = new PublishedReferenceUpdateResult(
            PublishedReferenceVersions.Empty,
            PublishedReferenceVersions.Empty,
            [],
            [],
            null
        );
        var viewModel = new ReferenceDataUpdateViewModel(new StubService(result), Path.GetTempPath(), "Ready");

        await viewModel.RefreshAsync();

        Assert.Contains("None needed; already current", viewModel.UpdatedCatalogs);
        Assert.Contains("Not needed; no catalogs were replaced", viewModel.BackupDirectory);
        Assert.Equal("Published reference data is current.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task RestartCommandUsesConfiguredControlledRestart()
    {
        var result = new PublishedReferenceUpdateResult(
            PublishedReferenceVersions.Empty,
            PublishedReferenceVersions.Empty,
            ["Codex reference"],
            [],
            null
        );
        var viewModel = new ReferenceDataUpdateViewModel(new StubService(result), Path.GetTempPath(), "Ready");
        bool restarted = false;
        viewModel.SetRestartHandler(() =>
        {
            restarted = true;
            return Task.CompletedTask;
        });
        await viewModel.RefreshAsync();

        viewModel.RestartCommand.Execute(null);
        await WaitUntilAsync(() => restarted);

        Assert.True(restarted);
    }

    [Fact]
    public async Task RefreshFailureReportsThatPlayerFilesWereNotChanged()
    {
        var log = new List<string>();
        var viewModel = new ReferenceDataUpdateViewModel(new FailingService(), Path.GetTempPath(), "Ready", log.Add);

        await viewModel.RefreshAsync();

        Assert.False(viewModel.IsRestartRequired);
        Assert.Equal(
            "Catalogs updated this session: None; refresh failed before activation.",
            viewModel.UpdatedCatalogs
        );
        Assert.Equal(
            "Backup created this session: Not needed; existing reference data remains active.",
            viewModel.BackupDirectory
        );
        Assert.Contains("failed safely", viewModel.StatusMessage);
        Assert.Contains("survey files were not changed", viewModel.StatusMessage);
        Assert.Single(log);
    }

    [Fact]
    public async Task SessionStateRoundTripsAcrossCommanderSwitchAfterRefresh()
    {
        var source = new ReferenceDataUpdateViewModel(
            new StubService(
                new PublishedReferenceUpdateResult(
                    PublishedReferenceVersions.Empty,
                    PublishedReferenceVersions.Empty,
                    ["codex.json"],
                    [],
                    "/tmp/backup"
                )
            ),
            Path.GetTempPath(),
            "Ready"
        );
        Assert.False(source.CaptureSessionState().HasCompletedRefresh);

        await source.RefreshAsync();
        ReferenceDataUpdateSessionState captured = source.CaptureSessionState();
        Assert.True(captured.HasCompletedRefresh);

        var replacement = new ReferenceDataUpdateViewModel(new FailingService(), Path.GetTempPath(), "Ready");
        Assert.False(replacement.TryRestoreSessionState(null));
        Assert.True(replacement.TryRestoreSessionState(captured));
        Assert.True(replacement.IsRestartRequired);
        Assert.Equal(source.StatusMessage, replacement.StatusMessage);
        Assert.Equal(source.UpdatedCatalogs, replacement.UpdatedCatalogs);
        Assert.Equal(source.BackupDirectory, replacement.BackupDirectory);
    }

    [Fact]
    public async Task PendingRefreshJoinsInFlightWorkSoCommanderSwitchDoesNotStartConcurrentRefresh()
    {
        var gate = new TaskCompletionSource<PublishedReferenceUpdateResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        int invocations = 0;
        var source = new ReferenceDataUpdateViewModel(
            new GatedService(() =>
            {
                invocations++;
                return gate.Task;
            }),
            Path.GetTempPath(),
            "Ready"
        );

        Task firstRefresh = source.RefreshAsync();
        Task joinedRefresh = source.RefreshAsync();
        Assert.Same(firstRefresh, joinedRefresh);
        Assert.Equal(1, invocations);

        Task<ReferenceDataUpdateSessionState>? pending = source.CapturePendingRefreshAsync();
        Assert.NotNull(pending);
        Assert.False(pending.IsCompleted);

        gate.SetResult(
            new PublishedReferenceUpdateResult(
                PublishedReferenceVersions.Empty,
                PublishedReferenceVersions.Empty,
                ["codex.json"],
                [],
                "/tmp/backup"
            )
        );
        ReferenceDataUpdateSessionState resolved = await pending;
        await firstRefresh;
        Assert.True(resolved.HasCompletedRefresh);
        Assert.True(resolved.IsRestartRequired);
        Assert.Null(source.CapturePendingRefreshAsync());
    }

    [Fact]
    public async Task AttachedPendingRefreshSerializesManualRefreshAndPropagatesRestartAfterPriorCompletedState()
    {
        var gate = new TaskCompletionSource<PublishedReferenceUpdateResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        int serviceCalls = 0;
        var sourceService = new GatedService(() =>
        {
            serviceCalls++;
            return serviceCalls == 1
                ? Task.FromResult(
                    new PublishedReferenceUpdateResult(
                        PublishedReferenceVersions.Empty,
                        PublishedReferenceVersions.Empty,
                        [],
                        [],
                        null
                    )
                )
                : gate.Task;
        });
        var source = new ReferenceDataUpdateViewModel(sourceService, Path.GetTempPath(), "Initial");
        await source.RefreshAsync();
        ReferenceDataUpdateSessionState priorCompleted = source.CaptureSessionState();
        Assert.True(priorCompleted.HasCompletedRefresh);
        Assert.False(priorCompleted.IsRestartRequired);

        // Start a second (manual) refresh and switch commanders before it completes.
        Task secondRefresh = source.RefreshAsync();
        Task<ReferenceDataUpdateSessionState>? pending = source.CapturePendingRefreshAsync();
        Assert.NotNull(pending);

        int replacementCalls = 0;
        var replacement = new ReferenceDataUpdateViewModel(
            new GatedService(() =>
            {
                replacementCalls++;
                return Task.FromResult(
                    new PublishedReferenceUpdateResult(
                        PublishedReferenceVersions.Empty,
                        PublishedReferenceVersions.Empty,
                        [],
                        [],
                        null
                    )
                );
            }),
            Path.GetTempPath(),
            "Initial"
        );
        replacement.TryRestoreSessionState(priorCompleted);
        Task attached = replacement.AttachPendingRefreshAsync(pending);

        // Replacement exposes busy state, disables RefreshCommand, shares the single task, and forwards it across chained switches.
        Assert.True(replacement.IsRefreshing);
        Assert.False(replacement.RefreshCommand.CanExecute(null));
        Assert.Same(attached, replacement.RefreshAsync());
        Assert.Same(pending, replacement.CapturePendingRefreshAsync());
        Assert.Equal(0, replacementCalls);

        gate.SetResult(
            new PublishedReferenceUpdateResult(
                PublishedReferenceVersions.Empty,
                PublishedReferenceVersions.Empty,
                ["codex.json"],
                [],
                "/tmp/backup"
            )
        );
        await Task.WhenAll(secondRefresh, attached);

        Assert.False(replacement.IsRefreshing);
        Assert.True(replacement.RefreshCommand.CanExecute(null));
        Assert.True(replacement.IsRestartRequired);
        Assert.Contains("codex.json", replacement.UpdatedCatalogs);
        Assert.Equal(0, replacementCalls);
        Assert.Null(replacement.CapturePendingRefreshAsync());
    }

    private sealed class GatedService(Func<Task<PublishedReferenceUpdateResult>> refresh)
        : IPublishedReferenceUpdateService
    {
        public Task<PublishedReferenceUpdateResult> RefreshAsync(
            string dataDirectory,
            CancellationToken cancellationToken = default
        ) => refresh();
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        DateTime timeout = DateTime.UtcNow.AddSeconds(2);
        while (!predicate() && DateTime.UtcNow < timeout)
        {
            await Task.Delay(10);
        }
    }

    private sealed class StubService(PublishedReferenceUpdateResult result) : IPublishedReferenceUpdateService
    {
        public Task<PublishedReferenceUpdateResult> RefreshAsync(
            string dataDirectory,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(result);
        }
    }

    private sealed class FailingService : IPublishedReferenceUpdateService
    {
        public Task<PublishedReferenceUpdateResult> RefreshAsync(
            string dataDirectory,
            CancellationToken cancellationToken = default
        )
        {
            throw new InvalidDataException("candidate was truncated");
        }
    }
}
