using SrvSurvey.Core.Updates;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class ReleaseUpdateViewModelTests
{
    [Fact]
    public async Task CheckAsyncPublishesAvailableReleaseWithoutInstalling()
    {
        var service = new StubService(CreateResult(isAvailable: true));
        var viewModel = new ReleaseUpdateViewModel(
            service,
            new Version(2, 0, 95, 0));

        await viewModel.CheckAsync();

        Assert.True(viewModel.IsUpdateAvailable);
        Assert.Equal("2.0.95.0", viewModel.CurrentVersion);
        Assert.Equal("2.0.95.23", viewModel.LatestVersion);
        Assert.Contains("unpackaged build", viewModel.StatusMessage);
        Assert.True(viewModel.HasReleaseNotes);
        Assert.Contains("A useful change", viewModel.ReleaseNotes);
        Assert.False(viewModel.OpenReleaseCommand.CanExecute(null));
    }

    [Fact]
    public async Task ConfirmedInstallProjectsWorkflowProgressAndHandoff()
    {
        var workflow = new StubWorkflow(
            ReleaseInstallationCapabilityStatus.Supported,
            new ReleaseInstallationWorkflowResult(
                ReleaseInstallationWorkflowStatus.HandoffStarted,
                ReleaseInstallationWorkflowStage.AwaitingApplicationExit,
                ReleaseInstallationCleanupStatus.Transferred),
            [
                new ReleaseInstallationWorkflowProgress(
                    ReleaseInstallationWorkflowStage.Downloading)
                {
                    DownloadedBytes = 1_024,
                    TotalBytes = 1_024,
                },
                new ReleaseInstallationWorkflowProgress(
                    ReleaseInstallationWorkflowStage.ValidatingArchive),
                new ReleaseInstallationWorkflowProgress(
                    ReleaseInstallationWorkflowStage.PreparingRollback)
                {
                    StagedFileCount = 12,
                },
                new ReleaseInstallationWorkflowProgress(
                    ReleaseInstallationWorkflowStage.AwaitingApplicationExit),
            ]);
        var viewModel = new ReleaseUpdateViewModel(
            new StubService(CreateResult(isAvailable: true)),
            new Version(2, 0, 95, 0));
        viewModel.ConfigureInstallationWorkflow(workflow);
        await viewModel.CheckAsync();
        viewModel.InstallConfirmed = true;

        await viewModel.InstallAsync();

        Assert.Single(workflow.Requests);
        Assert.True(viewModel.IsInstalling);
        Assert.False(viewModel.InstallConfirmed);
        Assert.Equal(100, viewModel.InstallProgressPercent);
        Assert.Contains("Close SrvSurvey", viewModel.StatusMessage);
    }

    [Fact]
    public async Task ReadOnlyBundleKeepsReleaseAvailableWithoutOfferingReplacement()
    {
        var workflow = new StubWorkflow(
            ReleaseInstallationCapabilityStatus.ReadOnlyAppImage);
        var viewModel = new ReleaseUpdateViewModel(
            new StubService(CreateResult(isAvailable: true)),
            new Version(2, 0, 95, 0));
        viewModel.ConfigureInstallationWorkflow(workflow);

        await viewModel.CheckAsync();

        Assert.True(viewModel.IsUpdateAvailable);
        Assert.False(viewModel.CanInstallCurrentInstallation);
        Assert.True(viewModel.ShowInstallUnavailable);
        Assert.False(viewModel.ShowGenericInstallUnavailable);
        Assert.True(viewModel.ShowAppImageManualInstall);
        Assert.False(viewModel.InstallCommand.CanExecute(null));
        Assert.Contains("AppImage is mounted read-only", viewModel.StatusMessage);
        Assert.Contains("selected release", viewModel.StatusMessage);
        Assert.Contains(
            "replace your existing AppImage",
            ReleaseUpdateViewModel.AppImageManualInstallInstructions);
        Assert.Empty(workflow.Requests);
    }

    [Fact]
    public async Task InstanceProgressProjectsConfirmationAndClosingText()
    {
        string? confirmationText = null;
        string? closingText = null;
        ReleaseUpdateViewModel? viewModel = null;
        var workflow = new StubWorkflow(
            ReleaseInstallationCapabilityStatus.Supported,
            new ReleaseInstallationWorkflowResult(
                ReleaseInstallationWorkflowStatus.Rejected,
                ReleaseInstallationWorkflowStage.ScanningInstances,
                ReleaseInstallationCleanupStatus.NotRequired,
                ReleaseInstallationRejectionReason.InstancesDeclined),
            [
                new ReleaseInstallationWorkflowProgress(
                    ReleaseInstallationWorkflowStage.AwaitingInstanceConfirmation)
                {
                    Checkpoint = ReleaseInstallationCheckpoint.BeforeDownload,
                    InstanceScan = new ApplicationInstanceScan(2, 0),
                },
                new ReleaseInstallationWorkflowProgress(
                    ReleaseInstallationWorkflowStage.ClosingInstances)
                {
                    Checkpoint = ReleaseInstallationCheckpoint.BeforeDownload,
                    InstanceScan = new ApplicationInstanceScan(2, 0),
                },
            ],
            index =>
            {
                if (index == 0)
                {
                    confirmationText = viewModel?.StatusMessage;
                }
                else
                {
                    closingText = viewModel?.InstallProgressText;
                }
            });
        viewModel = new ReleaseUpdateViewModel(
            new StubService(CreateResult(isAvailable: true)),
            new Version(2, 0, 95, 0));
        viewModel.ConfigureInstallationWorkflow(workflow);
        await viewModel.CheckAsync();
        viewModel.InstallConfirmed = true;

        await viewModel.InstallAsync();

        Assert.Contains("2 other SrvSurvey instances", confirmationText);
        Assert.Equal("Closing 2 other SrvSurvey instances...", closingText);
    }

    [Fact]
    public async Task DecliningMultipleInstanceWarningDoesNotCloseOrDownload()
    {
        var workflow = new StubWorkflow(
            ReleaseInstallationCapabilityStatus.Supported,
            new ReleaseInstallationWorkflowResult(
                ReleaseInstallationWorkflowStatus.Rejected,
                ReleaseInstallationWorkflowStage.ScanningInstances,
                ReleaseInstallationCleanupStatus.NotRequired,
                ReleaseInstallationRejectionReason.InstancesDeclined));
        var viewModel = new ReleaseUpdateViewModel(
            new StubService(CreateResult(isAvailable: true)),
            new Version(2, 0, 95, 0));
        viewModel.ConfigureInstallationWorkflow(workflow);
        await viewModel.CheckAsync();
        viewModel.InstallConfirmed = true;

        await viewModel.InstallAsync();

        Assert.False(viewModel.IsInstalling);
        Assert.True(viewModel.InstallConfirmed);
        Assert.Equal("Update canceled before download.", viewModel.InstallProgressText);
        Assert.Contains("no files were changed", viewModel.StatusMessage);
    }

    [Fact]
    public async Task UnverifiedInstanceWarningExplainsSafeUpdateBlock()
    {
        string? warningStatus = null;
        ReleaseUpdateViewModel? viewModel = null;
        var workflow = new StubWorkflow(
            ReleaseInstallationCapabilityStatus.Supported,
            new ReleaseInstallationWorkflowResult(
                ReleaseInstallationWorkflowStatus.Failed,
                ReleaseInstallationWorkflowStage.ClosingInstances,
                ReleaseInstallationCleanupStatus.NotRequired,
                Error: new IOException(
                    "A matching SrvSurvey process remains unverified.")),
            [
                new ReleaseInstallationWorkflowProgress(
                    ReleaseInstallationWorkflowStage.AwaitingInstanceConfirmation)
                {
                    Checkpoint = ReleaseInstallationCheckpoint.BeforeDownload,
                    InstanceScan = new ApplicationInstanceScan(0, 1),
                },
            ],
            _ => warningStatus = viewModel?.StatusMessage);
        viewModel = new ReleaseUpdateViewModel(
            new StubService(CreateResult(isAvailable: true)),
            new Version(2, 0, 95, 0));
        viewModel.ConfigureInstallationWorkflow(workflow);
        await viewModel.CheckAsync();
        viewModel.InstallConfirmed = true;

        await viewModel.InstallAsync();

        Assert.Contains("would not let it verify", warningStatus);
        Assert.Contains("remains unverified", viewModel.StatusMessage);
        Assert.Equal("Update preparation stopped safely.", viewModel.InstallProgressText);
    }

    [Fact]
    public async Task DecliningPreHandoffRecheckAbortsPreparedCandidate()
    {
        var workflow = new StubWorkflow(
            ReleaseInstallationCapabilityStatus.Supported,
            new ReleaseInstallationWorkflowResult(
                ReleaseInstallationWorkflowStatus.Rejected,
                ReleaseInstallationWorkflowStage.ScanningInstances,
                ReleaseInstallationCleanupStatus.Succeeded,
                ReleaseInstallationRejectionReason.InstancesDeclined));
        var viewModel = new ReleaseUpdateViewModel(
            new StubService(CreateResult(isAvailable: true)),
            new Version(2, 0, 95, 0));
        viewModel.ConfigureInstallationWorkflow(workflow);
        await viewModel.CheckAsync();
        viewModel.InstallConfirmed = true;

        await viewModel.InstallAsync();

        Assert.Equal(
            "Update canceled before installation handoff.",
            viewModel.InstallProgressText);
        Assert.Contains("prepared candidate was removed", viewModel.StatusMessage);
    }

    [Fact]
    public async Task ConfirmedHelperTimeoutAllowsAConfirmedRetry()
    {
        var workflow = new StubWorkflow(
            ReleaseInstallationCapabilityStatus.Supported,
            new ReleaseInstallationWorkflowResult(
                ReleaseInstallationWorkflowStatus.Failed,
                ReleaseInstallationWorkflowStage.AwaitingApplicationExit,
                ReleaseInstallationCleanupStatus.Succeeded,
                Error: new IOException("Parent remained active.")));
        var viewModel = new ReleaseUpdateViewModel(
            new StubService(CreateResult(isAvailable: true)),
            new Version(2, 0, 95, 0));
        viewModel.ConfigureInstallationWorkflow(workflow);
        await viewModel.CheckAsync();
        viewModel.InstallConfirmed = true;

        await viewModel.InstallAsync();

        Assert.False(viewModel.IsInstalling);
        Assert.False(viewModel.InstallConfirmed);
        Assert.Contains("did not close", viewModel.StatusMessage);
        Assert.Contains("Confirm again to retry", viewModel.StatusMessage);
    }

    [Fact]
    public async Task UnresolvedHelperOwnershipKeepsInstallationDisabled()
    {
        var workflow = new StubWorkflow(
            ReleaseInstallationCapabilityStatus.Supported,
            new ReleaseInstallationWorkflowResult(
                ReleaseInstallationWorkflowStatus.OwnershipUnresolved,
                ReleaseInstallationWorkflowStage.AwaitingApplicationExit,
                ReleaseInstallationCleanupStatus.Transferred,
                Error: new InvalidDataException("Outcome could not be read.")));
        var viewModel = new ReleaseUpdateViewModel(
            new StubService(CreateResult(isAvailable: true)),
            new Version(2, 0, 95, 0));
        viewModel.ConfigureInstallationWorkflow(workflow);
        await viewModel.CheckAsync();
        viewModel.InstallConfirmed = true;

        await viewModel.InstallAsync();

        Assert.True(viewModel.IsInstalling);
        Assert.False(viewModel.InstallConfirmed);
        Assert.False(viewModel.InstallCommand.CanExecute(null));
        Assert.Contains("status could not be confirmed", viewModel.StatusMessage);
    }

    [Fact]
    public async Task CleanupFailureKeepsInstallationDisabledAndPreservesErrors()
    {
        var workflow = new StubWorkflow(
            ReleaseInstallationCapabilityStatus.Supported,
            new ReleaseInstallationWorkflowResult(
                ReleaseInstallationWorkflowStatus.CleanupFailed,
                ReleaseInstallationWorkflowStage.ScanningInstances,
                ReleaseInstallationCleanupStatus.Failed,
                Error: new IOException("Instance scan failed."),
                CleanupError: new UnauthorizedAccessException(
                    "Candidate is locked.")));
        var viewModel = new ReleaseUpdateViewModel(
            new StubService(CreateResult(isAvailable: true)),
            new Version(2, 0, 95, 0));
        viewModel.ConfigureInstallationWorkflow(workflow);
        await viewModel.CheckAsync();
        viewModel.InstallConfirmed = true;

        await viewModel.InstallAsync();

        Assert.True(viewModel.IsInstalling);
        Assert.False(viewModel.InstallConfirmed);
        Assert.Contains("Update Diagnostics", viewModel.StatusMessage);
        Assert.Contains("Instance scan failed", viewModel.StatusMessage);
        Assert.Contains("Candidate is locked", viewModel.StatusMessage);
    }

    [Fact]
    public async Task WorkflowExceptionRestoresControlsWithGuardedStatus()
    {
        var workflow = new StubWorkflow(
            ReleaseInstallationCapabilityStatus.Supported,
            exception: new IOException("workflow adapter failed"));
        var viewModel = new ReleaseUpdateViewModel(
            new StubService(CreateResult(isAvailable: true)),
            new Version(2, 0, 95, 0));
        viewModel.ConfigureInstallationWorkflow(workflow);
        await viewModel.CheckAsync();
        viewModel.InstallConfirmed = true;

        await viewModel.InstallAsync();

        Assert.False(viewModel.IsInstalling);
        Assert.True(viewModel.InstallCommand.CanExecute(null));
        Assert.Contains("workflow adapter failed", viewModel.StatusMessage);
        Assert.Contains("Update Diagnostics", viewModel.StatusMessage);
    }

    [Fact]
    public async Task CancelledWorkflowRestoresControls()
    {
        var workflow = new StubWorkflow(
            ReleaseInstallationCapabilityStatus.Supported,
            new ReleaseInstallationWorkflowResult(
                ReleaseInstallationWorkflowStatus.Cancelled,
                ReleaseInstallationWorkflowStage.Downloading,
                ReleaseInstallationCleanupStatus.Succeeded));
        var viewModel = new ReleaseUpdateViewModel(
            new StubService(CreateResult(isAvailable: true)),
            new Version(2, 0, 95, 0));
        viewModel.ConfigureInstallationWorkflow(workflow);
        await viewModel.CheckAsync();
        viewModel.InstallConfirmed = true;

        await viewModel.InstallAsync();

        Assert.False(viewModel.IsInstalling);
        Assert.Equal("Update preparation stopped safely.", viewModel.InstallProgressText);
        Assert.Contains("was canceled", viewModel.StatusMessage);
    }

    [Fact]
    public async Task UnknownWorkflowResultRestoresControlsWithDiagnosticStatus()
    {
        var workflow = new StubWorkflow(
            ReleaseInstallationCapabilityStatus.Supported,
            new ReleaseInstallationWorkflowResult(
                (ReleaseInstallationWorkflowStatus)(-1),
                ReleaseInstallationWorkflowStage.None,
                ReleaseInstallationCleanupStatus.NotRequired));
        var viewModel = new ReleaseUpdateViewModel(
            new StubService(CreateResult(isAvailable: true)),
            new Version(2, 0, 95, 0));
        viewModel.ConfigureInstallationWorkflow(workflow);
        await viewModel.CheckAsync();
        viewModel.InstallConfirmed = true;

        await viewModel.InstallAsync();

        Assert.False(viewModel.IsInstalling);
        Assert.Contains("Unsupported installation result", viewModel.StatusMessage);
    }

    [Fact]
    public async Task OpenReleaseUsesConfiguredPlatformLauncher()
    {
        var viewModel = new ReleaseUpdateViewModel(
            new StubService(CreateResult(isAvailable: true)),
            new Version(2, 0, 95, 0));
        Uri? openedUri = null;
        viewModel.SetUriLauncher(uri =>
        {
            openedUri = uri;
            return Task.FromResult(true);
        });
        await viewModel.CheckAsync();

        viewModel.OpenReleaseCommand.Execute(null);
        await WaitUntilAsync(() => openedUri is not null);

        Assert.Equal("https://example.test/releases", openedUri?.AbsoluteUri);
    }

    [Fact]
    public async Task CheckFailureLeavesReleaseUnavailableAndReportsNoMutation()
    {
        var viewModel = new ReleaseUpdateViewModel(
            new FailingService(),
            new Version(2, 0, 95, 0));

        await viewModel.CheckAsync();

        Assert.False(viewModel.IsUpdateAvailable);
        Assert.Equal("Unavailable", viewModel.LatestVersion);
        Assert.Contains("profile were not changed", viewModel.StatusMessage);
    }

    [Fact]
    public async Task PreviousRollbackOutcomeSurvivesAutomaticUpdateCheck()
    {
        var viewModel = new ReleaseUpdateViewModel(
            new StubService(CreateResult(isAvailable: false)),
            new Version(2, 0, 95, 0));
        viewModel.SetPreviousInstallationOutcome(new ReleaseInstallationOutcome(
            ReleaseInstallationOutcomeStatus.RolledBack,
            Guid.NewGuid(),
            new Version(2, 0, 95, 23),
            DateTimeOffset.UtcNow,
            null,
            "C:\\failed",
            "Replacement health timed out."));

        await viewModel.CheckAsync();

        Assert.Contains("was rolled back", viewModel.StatusMessage);
        Assert.Contains("previous installation was restored", viewModel.StatusMessage);
        Assert.Contains("health timed out", viewModel.StatusMessage);
    }

    [Fact]
    public async Task MissingStableXpReleaseIsShownAsNotAvailable()
    {
        var service = new StubService(new ReleaseUpdateResult(
            new Version(2, 1, 3, 9),
            null,
            false,
            new Uri("https://github.com/njthomson/SrvSurvey/releases"),
            null,
            ReleaseChannel.Stable));
        var viewModel = new ReleaseUpdateViewModel(
            service,
            new Version(2, 1, 3, 9));
        viewModel.UseDevelopmentReleases = false;

        await WaitUntilAsync(() => viewModel.LatestVersion == "N/A");

        Assert.Equal("N/A", viewModel.LatestVersion);
        Assert.False(viewModel.IsUpdateAvailable);
        Assert.Contains("no stable SrvSurvey-XP release", viewModel.StatusMessage);
        Assert.Contains("njthomson/SrvSurvey", viewModel.StatusMessage);
    }

    [Fact]
    public async Task ChannelDefaultsToDevelopmentAndOptOutIsPersistedAndRechecked()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-release-channel-tests-{Guid.NewGuid():N}");
        var settingsPath = Path.Combine(temporaryDirectory, "ui-settings.json");
        var settings = new ReleaseUpdateSettingsStore(settingsPath);
        var service = new RecordingService();
        try
        {
            var viewModel = new ReleaseUpdateViewModel(
                service,
                new Version(2, 1, 3, 9),
                settings);

            Assert.True(viewModel.UseDevelopmentReleases);
            Assert.Contains("Fenris159/SrvSurvey", viewModel.ReleaseSourceDescription);

            viewModel.UseDevelopmentReleases = false;
            await WaitUntilAsync(() => service.Channels.Contains(ReleaseChannel.Stable));

            Assert.False(settings.LoadUseDevelopmentReleases());
            Assert.Contains("njthomson/SrvSurvey", viewModel.ReleaseSourceDescription);
            Assert.Contains(ReleaseChannel.Stable, service.Channels);
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, true);
            }
        }
    }

    [Fact]
    public async Task UpdateNotificationCanOpenDiagnosticsAndDismiss()
    {
        var viewModel = new ReleaseUpdateViewModel(
            new StubService(CreateResult(isAvailable: true)),
            new Version(2, 0, 95, 0));
        var navigated = false;
        viewModel.SetDiagnosticsNavigator(() => navigated = true);

        await viewModel.CheckAsync();

        Assert.True(viewModel.ShouldShowUpdateNotification);
        Assert.Contains("development channel", viewModel.UpdateNotificationText);
        Assert.True(viewModel.OpenUpdateDiagnosticsCommand.CanExecute(null));

        viewModel.OpenUpdateDiagnosticsCommand.Execute(null);
        await WaitUntilAsync(() => navigated && !viewModel.ShouldShowUpdateNotification);

        Assert.True(navigated);
        Assert.False(viewModel.ShouldShowUpdateNotification);
    }

    private static ReleaseUpdateResult CreateResult(bool isAvailable)
    {
        return new ReleaseUpdateResult(
            new Version(2, 0, 95, 0),
            new Version(2, 0, 95, 23),
            isAvailable,
            new Uri("https://example.test/releases"),
            isAvailable
                ? new CrossPlatformReleasePackage(
                    "win-x64",
                    "SrvSurvey-XP-2.0.95.23-win-x64.zip",
                    "zip",
                    1_024,
                    new string('a', 64),
                    new Uri("https://example.test/package.zip"))
                : null,
            ReleaseChannel.Development,
            "## What's changed\n\n- A useful change.");
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var timeout = DateTime.UtcNow.AddSeconds(2);
        while (!predicate() && DateTime.UtcNow < timeout)
        {
            await Task.Delay(10);
        }
    }

    private sealed class StubService(ReleaseUpdateResult result)
        : IReleaseUpdateService
    {
        public Task<ReleaseUpdateResult> CheckAsync(
            ReleaseVersion currentVersion,
            ReleaseChannel channel,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(result);
        }
    }

    private sealed class FailingService : IReleaseUpdateService
    {
        public Task<ReleaseUpdateResult> CheckAsync(
            ReleaseVersion currentVersion,
            ReleaseChannel channel,
            CancellationToken cancellationToken = default)
        {
            throw new HttpRequestException("network unavailable");
        }
    }

    private sealed class RecordingService : IReleaseUpdateService
    {
        public List<ReleaseChannel> Channels { get; } = [];

        public Task<ReleaseUpdateResult> CheckAsync(
            ReleaseVersion currentVersion,
            ReleaseChannel channel,
            CancellationToken cancellationToken = default)
        {
            Channels.Add(channel);
            return Task.FromResult(new ReleaseUpdateResult(
                currentVersion,
                null,
                false,
                channel == ReleaseChannel.Development
                    ? ReleaseUpdateService.DevelopmentReleaseUri
                    : ReleaseUpdateService.StableReleaseUri,
                null,
                channel));
        }
    }

    private sealed class StubWorkflow : IReleaseInstallationWorkflow
    {
        private readonly ReleaseInstallationWorkflowResult result;
        private readonly IReadOnlyList<ReleaseInstallationWorkflowProgress> progress;
        private readonly Action<int>? afterProgress;
        private readonly Exception? exception;

        public StubWorkflow(
            ReleaseInstallationCapabilityStatus capabilityStatus,
            ReleaseInstallationWorkflowResult? result = null,
            IReadOnlyList<ReleaseInstallationWorkflowProgress>? progress = null,
            Action<int>? afterProgress = null,
            Exception? exception = null)
        {
            Capability = new ReleaseInstallationCapability(capabilityStatus);
            this.result = result ?? new ReleaseInstallationWorkflowResult(
                ReleaseInstallationWorkflowStatus.Rejected,
                ReleaseInstallationWorkflowStage.None,
                ReleaseInstallationCleanupStatus.NotRequired,
                ReleaseInstallationRejectionReason.Unsupported);
            this.progress = progress ?? [];
            this.afterProgress = afterProgress;
            this.exception = exception;
        }

        public ReleaseInstallationCapability Capability { get; }

        public List<ReleaseInstallationRequest> Requests { get; } = [];

        public Task<ReleaseInstallationWorkflowResult> ExecuteAsync(
            ReleaseInstallationRequest request,
            IProgress<ReleaseInstallationWorkflowProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            for (var index = 0; index < this.progress.Count; index++)
            {
                progress?.Report(this.progress[index]);
                afterProgress?.Invoke(index);
            }

            if (exception is not null)
            {
                return Task.FromException<ReleaseInstallationWorkflowResult>(
                    exception);
            }

            return Task.FromResult(result);
        }
    }
}
