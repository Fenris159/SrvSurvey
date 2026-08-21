using SrvSurvey.Core.Updates;
using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class ReleaseInstallationWorkflowTests
{
    [Fact]
    public async Task ExecuteAsyncOwnsTheGuardedOrderThroughHandoff()
    {
        var fixture = new WorkflowFixture();
        var progress = new RecordingProgress();

        var result = await fixture.CreateWorkflow().ExecuteAsync(
            fixture.Request,
            progress);

        Assert.Equal(ReleaseInstallationWorkflowStatus.HandoffStarted, result.Status);
        Assert.Equal(ReleaseInstallationCleanupStatus.Transferred, result.CleanupStatus);
        Assert.Equal(
            [
                "scan:BeforeDownload",
                "download",
                "stage",
                "prepare",
                "scan:BeforeHandoff",
                "handoff",
                "shutdown",
            ],
            fixture.Calls);
        Assert.Contains(
            progress.Values,
            value => value.Stage == ReleaseInstallationWorkflowStage.Downloading);
        Assert.Contains(
            progress.Values,
            value => value.Stage
                == ReleaseInstallationWorkflowStage.AwaitingApplicationExit);
    }

    [Fact]
    public async Task DecliningInitialInstanceConfirmationStopsBeforeDownload()
    {
        var fixture = new WorkflowFixture
        {
            Scans = new Queue<ApplicationInstanceScan>(
                [new ApplicationInstanceScan(1, 0)]),
            Confirm = _ => false,
        };

        var result = await fixture.CreateWorkflow().ExecuteAsync(fixture.Request);

        Assert.Equal(ReleaseInstallationWorkflowStatus.Rejected, result.Status);
        Assert.Equal(
            ReleaseInstallationRejectionReason.InstancesDeclined,
            result.RejectionReason);
        Assert.Equal(ReleaseInstallationCleanupStatus.NotRequired, result.CleanupStatus);
        Assert.Equal(
            ["scan:BeforeDownload", "confirm:BeforeDownload:1"],
            fixture.Calls);
    }

    [Fact]
    public async Task DecliningPreHandoffConfirmationCleansTheCandidate()
    {
        var fixture = new WorkflowFixture
        {
            Scans = new Queue<ApplicationInstanceScan>(
                [
                    new ApplicationInstanceScan(0, 0),
                    new ApplicationInstanceScan(1, 0),
                ]),
            Confirm = _ => false,
        };

        var result = await fixture.CreateWorkflow().ExecuteAsync(fixture.Request);

        Assert.Equal(ReleaseInstallationWorkflowStatus.Rejected, result.Status);
        Assert.Equal(ReleaseInstallationCleanupStatus.Succeeded, result.CleanupStatus);
        Assert.Equal(
            [
                "scan:BeforeDownload",
                "download",
                "stage",
                "prepare",
                "scan:BeforeHandoff",
                "confirm:BeforeHandoff:1",
                "abort",
            ],
            fixture.Calls);
    }

    [Fact]
    public async Task FailureAfterPreparationCleansTheCandidate()
    {
        var fixture = new WorkflowFixture
        {
            ScanFailureCall = 2,
            ScanFailure = new IOException("scan failed"),
        };

        var result = await fixture.CreateWorkflow().ExecuteAsync(fixture.Request);

        Assert.Equal(ReleaseInstallationWorkflowStatus.Failed, result.Status);
        Assert.Equal(ReleaseInstallationCleanupStatus.Succeeded, result.CleanupStatus);
        Assert.IsType<IOException>(result.Error);
        Assert.Equal("abort", fixture.Calls[^1]);
    }

    [Fact]
    public async Task DefiniteHandoffFailureCleansTheCandidate()
    {
        var fixture = new WorkflowFixture();
        fixture.HandoffResult = new ApplicationUpdateHandoffResult(
            ApplicationUpdateHandoffStatus.NotStarted,
            fixture.Plan,
            new IOException("helper did not start"));

        var result = await fixture.CreateWorkflow().ExecuteAsync(fixture.Request);

        Assert.Equal(ReleaseInstallationWorkflowStatus.Failed, result.Status);
        Assert.Equal(ReleaseInstallationCleanupStatus.Succeeded, result.CleanupStatus);
        Assert.Equal(["handoff", "abort"], fixture.Calls[^2..]);
    }

    [Fact]
    public async Task CleanupFailureBlocksAnotherAttempt()
    {
        var fixture = new WorkflowFixture
        {
            ScanFailureCall = 2,
            ScanFailure = new IOException("scan failed"),
            AbortFailure = new IOException("candidate is locked"),
        };
        var workflow = fixture.CreateWorkflow();

        var first = await workflow.ExecuteAsync(fixture.Request);
        var second = await workflow.ExecuteAsync(fixture.Request);

        Assert.Equal(ReleaseInstallationWorkflowStatus.CleanupFailed, first.Status);
        Assert.IsType<IOException>(first.Error);
        Assert.IsType<IOException>(first.CleanupError);
        Assert.Equal(ReleaseInstallationWorkflowStatus.Rejected, second.Status);
        Assert.Equal(ReleaseInstallationRejectionReason.Busy, second.RejectionReason);
    }

    [Fact]
    public async Task CancellationAfterPreparationCleansTheCandidate()
    {
        var fixture = new WorkflowFixture
        {
            ScanFailureCall = 2,
            ScanFailure = new OperationCanceledException("cancelled"),
        };

        var result = await fixture.CreateWorkflow().ExecuteAsync(fixture.Request);

        Assert.Equal(ReleaseInstallationWorkflowStatus.Cancelled, result.Status);
        Assert.Equal(ReleaseInstallationCleanupStatus.Succeeded, result.CleanupStatus);
        Assert.Equal("abort", fixture.Calls[^1]);
    }

    [Fact]
    public async Task ProgrammingFailureAfterPreparationCleansThenEscapes()
    {
        var fixture = new WorkflowFixture
        {
            ScanFailureCall = 2,
            ScanFailure = new ArgumentException("invalid adapter state"),
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.CreateWorkflow().ExecuteAsync(fixture.Request));

        Assert.Contains("invalid adapter state", exception.Message);
        Assert.Equal("abort", fixture.Calls[^1]);
    }

    [Fact]
    public async Task HelperAbortAfterTransferredOwnershipAllowsRetryAfterCleanup()
    {
        var fixture = new WorkflowFixture
        {
            CurrentProcessRunning = true,
            Outcome = new ReleaseInstallationOutcome(
                ReleaseInstallationOutcomeStatus.Aborted,
                Guid.Empty,
                new Version(1, 0, 0),
                DateTimeOffset.UtcNow,
                null,
                null,
                "parent remained active"),
        };
        fixture.Outcome = fixture.Outcome with
        {
            RequestId = fixture.Preparation.RequestId,
            Version = fixture.Preparation.Version,
        };
        var workflow = fixture.CreateWorkflow();

        var first = await workflow.ExecuteAsync(fixture.Request);
        var second = await workflow.ExecuteAsync(fixture.Request);

        Assert.Equal(ReleaseInstallationWorkflowStatus.Failed, first.Status);
        Assert.Equal(ReleaseInstallationCleanupStatus.Succeeded, first.CleanupStatus);
        Assert.Equal(ReleaseInstallationWorkflowStatus.Failed, second.Status);
        Assert.Equal(2, fixture.Calls.Count(call => call == "handoff"));
        Assert.DoesNotContain("abort", fixture.Calls);
    }

    [Fact]
    public async Task MissingOutcomeAfterOwnershipTransferBlocksRetry()
    {
        var fixture = new WorkflowFixture
        {
            CurrentProcessRunning = true,
            Outcome = null,
        };
        var workflow = fixture.CreateWorkflow();

        var first = await workflow.ExecuteAsync(fixture.Request);
        var second = await workflow.ExecuteAsync(fixture.Request);

        Assert.Equal(
            ReleaseInstallationWorkflowStatus.OwnershipUnresolved,
            first.Status);
        Assert.Equal(ReleaseInstallationCleanupStatus.Transferred, first.CleanupStatus);
        Assert.Equal(ReleaseInstallationWorkflowStatus.Rejected, second.Status);
        Assert.Equal(ReleaseInstallationRejectionReason.Busy, second.RejectionReason);
        Assert.DoesNotContain("abort", fixture.Calls);
    }

    [Fact]
    public async Task ProgrammingFailureAfterOwnershipTransferBlocksRetry()
    {
        var fixture = new WorkflowFixture
        {
            CurrentProcessProbeFailure = new ArgumentException(
                "invalid process state"),
        };
        var workflow = fixture.CreateWorkflow();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            workflow.ExecuteAsync(fixture.Request));
        var second = await workflow.ExecuteAsync(fixture.Request);

        Assert.Contains("invalid process state", exception.Message);
        Assert.Equal(ReleaseInstallationWorkflowStatus.Rejected, second.Status);
        Assert.Equal(ReleaseInstallationRejectionReason.Busy, second.RejectionReason);
        Assert.DoesNotContain("abort", fixture.Calls);
    }

    [Fact]
    public async Task ConcurrentAttemptIsRejectedByTheWorkflow()
    {
        var fixture = new WorkflowFixture
        {
            DownloadGate = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously),
            DownloadStarted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var workflow = fixture.CreateWorkflow();
        var first = workflow.ExecuteAsync(fixture.Request);
        await fixture.DownloadStarted.Task;

        var second = await workflow.ExecuteAsync(fixture.Request);
        fixture.DownloadGate.SetResult();
        var completedFirst = await first;

        Assert.Equal(ReleaseInstallationWorkflowStatus.Rejected, second.Status);
        Assert.Equal(ReleaseInstallationRejectionReason.Busy, second.RejectionReason);
        Assert.Equal(
            ReleaseInstallationWorkflowStatus.HandoffStarted,
            completedFirst.Status);
    }

    [Fact]
    public async Task UnsupportedCapabilityRejectsWithoutCallingAdapters()
    {
        var fixture = new WorkflowFixture
        {
            Capability = new ReleaseInstallationCapability(
                ReleaseInstallationCapabilityStatus.ReadOnlyAppImage),
        };

        var result = await fixture.CreateWorkflow().ExecuteAsync(fixture.Request);

        Assert.Equal(ReleaseInstallationWorkflowStatus.Rejected, result.Status);
        Assert.Equal(
            ReleaseInstallationRejectionReason.Unsupported,
            result.RejectionReason);
        Assert.Empty(fixture.Calls);
    }

    private sealed class RecordingProgress
        : IProgress<ReleaseInstallationWorkflowProgress>
    {
        public List<ReleaseInstallationWorkflowProgress> Values { get; } = [];

        public void Report(ReleaseInstallationWorkflowProgress value)
        {
            Values.Add(value);
        }
    }

    private sealed class WorkflowFixture
    {
        private int scanCount;

        public WorkflowFixture()
        {
            var requestId = Guid.NewGuid();
            Preparation = new ReleaseInstallationPreparation(
                requestId,
                new Version(2, 1, 3, 1),
                "win-x64",
                "C:\\SrvSurvey",
                "C:\\ready",
                "C:\\candidate",
                "C:\\backup",
                "C:\\failed",
                "SrvSurvey.Desktop.exe",
                new string('a', 64),
                new string('b', 64),
                false,
                ["--frontier-id", "F123"]);
            Plan = new ReleaseInstallationHandoffPlan(
                "C:\\plans\\plan.json",
                "C:\\plans\\helper-ready.json",
                "C:\\plans\\health.json",
                "C:\\plans\\outcome.json",
                DateTimeOffset.UtcNow,
                123,
                DateTimeOffset.UtcNow.UtcTicks,
                new string('c', 64),
                Preparation);
            HandoffResult = new ApplicationUpdateHandoffResult(
                ApplicationUpdateHandoffStatus.Started,
                Plan);
        }

        public List<string> Calls { get; } = [];

        public Queue<ApplicationInstanceScan> Scans { get; set; } = new(
            [
                new ApplicationInstanceScan(0, 0),
                new ApplicationInstanceScan(0, 0),
            ]);

        public Func<ReleaseInstallationCheckpoint, bool> Confirm { get; set; } =
            _ => true;

        public int? ScanFailureCall { get; init; }

        public Exception? ScanFailure { get; init; }

        public Exception? AbortFailure { get; init; }

        public TaskCompletionSource? DownloadGate { get; init; }

        public TaskCompletionSource? DownloadStarted { get; init; }

        public bool CurrentProcessRunning { get; init; }

        public Exception? CurrentProcessProbeFailure { get; init; }

        public bool CandidateExists { get; init; }

        public ReleaseInstallationOutcome? Outcome { get; set; }

        public ApplicationUpdateHandoffResult HandoffResult { get; set; }

        public ReleaseInstallationCapability Capability { get; init; } = new(
            ReleaseInstallationCapabilityStatus.Supported);

        public ReleaseInstallationPreparation Preparation { get; }

        public ReleaseInstallationHandoffPlan Plan { get; }

        public ReleaseInstallationRequest Request { get; } = new(
            new Version(2, 1, 3, 1),
            new CrossPlatformReleasePackage(
                "win-x64",
                "SrvSurvey.zip",
                "zip",
                1_024,
                new string('d', 64),
                new Uri("https://example.test/SrvSurvey.zip")));

        public ReleaseInstallationWorkflow CreateWorkflow()
        {
            return new ReleaseInstallationWorkflow(
                new ReleaseInstallationWorkflowAdapters(
                    new Downloader(this),
                    new Stager(this),
                    new Preparer(this),
                    new Handoff(this),
                    new InstanceManager(this),
                    ConfirmAsync),
                new ReleaseInstallationWorkflowContext(
                    "C:\\data",
                    "C:\\SrvSurvey",
                    Preparation.StartupArguments,
                    ShutdownAsync,
                    IsAppImage: false),
                new ReleaseInstallationWorkflowSeams(
                    new OutcomeMonitor(this),
                    () => CurrentProcessProbeFailure is null
                        ? CurrentProcessRunning
                        : throw CurrentProcessProbeFailure,
                    _ => CandidateExists,
                    Capability));
        }

        private Task<bool> ConfirmAsync(
            ApplicationInstanceScan scan,
            ReleaseInstallationCheckpoint checkpoint,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add($"confirm:{checkpoint}:{scan.TotalCount}");
            return Task.FromResult(Confirm(checkpoint));
        }

        private Task ShutdownAsync(CancellationToken cancellationToken)
        {
            Calls.Add("shutdown");
            return Task.CompletedTask;
        }

        private sealed class InstanceManager(WorkflowFixture owner)
            : IApplicationInstanceManager
        {
            public Task<ApplicationInstanceScan> ScanOtherInstancesAsync(
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var call = Interlocked.Increment(ref owner.scanCount);
                var checkpoint = call == 1
                    ? ReleaseInstallationCheckpoint.BeforeDownload
                    : ReleaseInstallationCheckpoint.BeforeHandoff;
                owner.Calls.Add($"scan:{checkpoint}");
                if (owner.ScanFailureCall == call && owner.ScanFailure is not null)
                {
                    throw owner.ScanFailure;
                }

                var scan = owner.Scans.Count > 0
                    ? owner.Scans.Dequeue()
                    : new ApplicationInstanceScan(0, 0);
                return Task.FromResult(scan);
            }

            public async Task<int> CountOtherInstancesAsync(
                CancellationToken cancellationToken = default)
            {
                var scan = await ScanOtherInstancesAsync(cancellationToken);
                return scan.TotalCount;
            }

            public Task CloseOtherInstancesAsync(
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                owner.Calls.Add("close");
                return Task.CompletedTask;
            }
        }

        private sealed class Downloader(WorkflowFixture owner)
            : IReleasePackageDownloadService
        {
            public async Task<ReleasePackageDownloadResult> DownloadAsync(
                ReleaseVersion version,
                CrossPlatformReleasePackage package,
                string dataDirectory,
                IProgress<ReleasePackageDownloadProgress>? progress = null,
                CancellationToken cancellationToken = default)
            {
                owner.Calls.Add("download");
                owner.DownloadStarted?.TrySetResult();
                if (owner.DownloadGate is not null)
                {
                    await owner.DownloadGate.Task.WaitAsync(cancellationToken);
                }

                progress?.Report(new ReleasePackageDownloadProgress(1_024, 1_024));
                return new ReleasePackageDownloadResult(
                    "C:\\data\\SrvSurvey.zip",
                    true,
                    1_024,
                    package.Sha256);
            }
        }

        private sealed class Stager(WorkflowFixture owner)
            : IReleasePackageStagingService
        {
            public Task<ReleasePackageStagingResult> StageAsync(
                ReleaseVersion version,
                CrossPlatformReleasePackage package,
                string archivePath,
                string dataDirectory,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                owner.Calls.Add("stage");
                return Task.FromResult(new ReleasePackageStagingResult(
                    "C:\\ready",
                    "C:\\ready\\SrvSurvey.Desktop.exe",
                    false,
                    12,
                    4_096,
                    new string('a', 64)));
            }

            public Task<ReleasePackageStagingResult> VerifyReadyAsync(
                ReleaseVersion version,
                string runtimeIdentifier,
                string readyDirectory,
                string manifestSha256,
                CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class Preparer(WorkflowFixture owner)
            : IReleaseInstallationPreparer
        {
            public Task<ReleaseInstallationPreparation> PrepareAsync(
                ReleaseVersion version,
                string runtimeIdentifier,
                string readyDirectory,
                string manifestSha256,
                string installationDirectory,
                IReadOnlyList<string> startupArguments,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                owner.Calls.Add("prepare");
                return Task.FromResult(owner.Preparation);
            }

            public Task AbortAsync(
                ReleaseInstallationPreparation preparation,
                CancellationToken cancellationToken = default)
            {
                owner.Calls.Add("abort");
                return owner.AbortFailure is null
                    ? Task.CompletedTask
                    : Task.FromException(owner.AbortFailure);
            }
        }

        private sealed class Handoff(WorkflowFixture owner)
            : IApplicationUpdateHandoff
        {
            public Task<ApplicationUpdateHandoffResult> StartHelperAttemptAsync(
                string dataDirectory,
                ReleaseInstallationPreparation preparation,
                string stagedEntryPoint,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                owner.Calls.Add("handoff");
                return Task.FromResult(owner.HandoffResult);
            }
        }

        private sealed class OutcomeMonitor(WorkflowFixture owner)
            : IReleaseInstallationOutcomeMonitor
        {
            public Task<ReleaseInstallationOutcome?> WaitForOutcomeAsync(
                ReleaseInstallationHandoffPlan plan,
                CancellationToken cancellationToken = default)
            {
                owner.Calls.Add("monitor");
                return Task.FromResult(owner.Outcome);
            }
        }
    }
}
