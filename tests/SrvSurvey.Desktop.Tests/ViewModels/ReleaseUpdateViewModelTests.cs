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
        Assert.False(viewModel.OpenReleaseCommand.CanExecute(null));
    }

    [Fact]
    public async Task ConfirmedInstallRunsGuardedPipelineBeforeShutdown()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-update-view-model-tests-{Guid.NewGuid():N}");
        var installationDirectory = Path.Combine(temporaryDirectory, "install");
        Directory.CreateDirectory(installationDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(installationDirectory, "release-package.json"),
            "{}");
        var calls = new List<string>();
        var shutdown = false;
        try
        {
            var viewModel = new ReleaseUpdateViewModel(
                new StubService(CreateResult(isAvailable: true)),
                new Version(2, 0, 95, 0));
            viewModel.ConfigureInstaller(
                new StubDownloader(calls),
                new StubStagingService(calls),
                new StubPreparer(calls),
                new StubHandoff(calls),
                temporaryDirectory,
                installationDirectory,
                ["--frontier-id", "F123"],
                () =>
                {
                    calls.Add("shutdown");
                    shutdown = true;
                    return Task.CompletedTask;
                });
            await viewModel.CheckAsync();
            viewModel.InstallConfirmed = true;

            await viewModel.InstallAsync();

            Assert.Equal(
                ["download", "stage", "prepare", "handoff", "shutdown"],
                calls);
            Assert.True(shutdown);
            Assert.True(viewModel.IsInstalling);
            Assert.False(viewModel.InstallConfirmed);
            Assert.Equal(100, viewModel.InstallProgressPercent);
            Assert.Contains("helper is waiting", viewModel.StatusMessage);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ReadOnlyBundleKeepsReleaseAvailableWithoutOfferingReplacement()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-update-view-model-tests-{Guid.NewGuid():N}");
        var installationDirectory = Path.Combine(temporaryDirectory, "install");
        Directory.CreateDirectory(installationDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(installationDirectory, "release-package.json"),
            "{}");
        try
        {
            var viewModel = new ReleaseUpdateViewModel(
                new StubService(CreateResult(isAvailable: true)),
                new Version(2, 0, 95, 0));
            var calls = new List<string>();
            viewModel.ConfigureInstaller(
                new StubDownloader(calls),
                new StubStagingService(calls),
                new StubPreparer(calls),
                new StubHandoff(calls),
                temporaryDirectory,
                installationDirectory,
                [],
                () => Task.CompletedTask,
                "This AppImage is mounted read-only and cannot replace itself; use Open releases to download the new AppImage.",
                isAppImage: true);

            await viewModel.CheckAsync();

            Assert.True(viewModel.IsUpdateAvailable);
            Assert.False(viewModel.CanInstallCurrentInstallation);
            Assert.True(viewModel.ShowInstallUnavailable);
            Assert.False(viewModel.ShowGenericInstallUnavailable);
            Assert.True(viewModel.ShowAppImageManualInstall);
            Assert.False(viewModel.InstallCommand.CanExecute(null));
            Assert.Contains("AppImage is mounted read-only", viewModel.StatusMessage);
            Assert.Contains("Open releases", viewModel.StatusMessage);
            Assert.Contains(
                "replace your existing AppImage",
                ReleaseUpdateViewModel.AppImageManualInstallInstructions);
            Assert.Empty(calls);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
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
            ReleaseChannel.Development);
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

    private sealed class StubDownloader(List<string> calls)
        : IReleasePackageDownloadService
    {
        public Task<ReleasePackageDownloadResult> DownloadAsync(
            ReleaseVersion version,
            CrossPlatformReleasePackage package,
            string dataDirectory,
            IProgress<ReleasePackageDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            calls.Add("download");
            progress?.Report(new ReleasePackageDownloadProgress(1_024, 1_024));
            return Task.FromResult(new ReleasePackageDownloadResult(
                Path.Combine(dataDirectory, package.ArchiveName),
                true,
                package.Size,
                package.Sha256));
        }
    }

    private sealed class StubStagingService(List<string> calls)
        : IReleasePackageStagingService
    {
        public Task<ReleasePackageStagingResult> StageAsync(
            ReleaseVersion version,
            CrossPlatformReleasePackage package,
            string archivePath,
            string dataDirectory,
            CancellationToken cancellationToken = default)
        {
            calls.Add("stage");
            var ready = Path.Combine(dataDirectory, "ready");
            return Task.FromResult(new ReleasePackageStagingResult(
                ready,
                Path.Combine(ready, "SrvSurvey.Desktop.exe"),
                false,
                12,
                4_096,
                new string('c', 64)));
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

    private sealed class StubPreparer(List<string> calls)
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
            calls.Add("prepare");
            var requestId = Guid.NewGuid();
            var parent = Directory.GetParent(installationDirectory)!.FullName;
            return Task.FromResult(new ReleaseInstallationPreparation(
                requestId,
                version,
                runtimeIdentifier,
                installationDirectory,
                Path.Combine(parent, $".install-update-{requestId:N}"),
                Path.Combine(parent, $".install-backup-{requestId:N}"),
                Path.Combine(parent, $".install-failed-{requestId:N}"),
                "SrvSurvey.Desktop.exe",
                manifestSha256,
                new string('d', 64),
                startupArguments));
        }
    }

    private sealed class StubHandoff(List<string> calls)
        : IApplicationUpdateHandoffService
    {
        public Task<ReleaseInstallationHandoffPlan> StartHelperAsync(
            string dataDirectory,
            ReleaseInstallationPreparation preparation,
            string stagedEntryPoint,
            CancellationToken cancellationToken = default)
        {
            calls.Add("handoff");
            var planDirectory = Path.Combine(dataDirectory, "plan");
            return Task.FromResult(new ReleaseInstallationHandoffPlan(
                Path.Combine(planDirectory, "plan.json"),
                Path.Combine(planDirectory, "health.json"),
                Path.Combine(planDirectory, "outcome.json"),
                DateTimeOffset.UtcNow,
                123,
                DateTimeOffset.UtcNow.UtcTicks,
                new string('e', 64),
                preparation));
        }
    }
}
