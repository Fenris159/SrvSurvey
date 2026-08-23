using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class VisitedStarsCacheViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-visited-stars-vm-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task RefreshSelectsCurrentCommanderAndSwapRequiresConfirmation()
    {
        Directory.CreateDirectory(temporaryDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(temporaryDirectory, "F123-live.json"),
            "{\"fid\":\"F123\",\"commander\":\"Drew\"}");
        var target = Path.Combine(
            temporaryDirectory,
            VisitedStarsCacheService.CacheFileName);
        await File.WriteAllBytesAsync(target, [1, 2, 3]);
        var service = new RecordingService();
        var viewModel = new VisitedStarsCacheViewModel(
            new CommanderProfileCatalog(temporaryDirectory),
            service,
            _ => target,
            () => false);
        viewModel.UpdateContext("F123", "Drew", "Sol");

        await viewModel.RefreshAsync();
        await viewModel.SwapAsync();

        Assert.Equal("F123", viewModel.SelectedCommander?.FrontierId);
        Assert.Equal("Sol", viewModel.SystemName);
        Assert.Equal(target, viewModel.TargetPath);
        Assert.Equal("Confirm swap", viewModel.SwapButtonText);
        Assert.Equal(0, service.SwapCount);

        await viewModel.SwapAsync();

        Assert.Equal(1, service.SwapCount);
        Assert.Equal("Sol", service.SystemName);
        Assert.Equal(target, service.TargetPath);
        Assert.Equal("Back up and swap", viewModel.SwapButtonText);
        Assert.Contains("Swap complete", viewModel.StatusMessage);
    }

    [Fact]
    public async Task RestoreRequiresBackupAndConfirmation()
    {
        Directory.CreateDirectory(temporaryDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(temporaryDirectory, "F123-live.json"),
            "{\"fid\":\"F123\",\"commander\":\"Drew\"}");
        var target = Path.Combine(
            temporaryDirectory,
            VisitedStarsCacheService.CacheFileName);
        await File.WriteAllBytesAsync(target, [1, 2, 3]);
        await File.WriteAllBytesAsync(
            VisitedStarsCacheService.GetBackupPath(target),
            [9, 8, 7]);
        var service = new RecordingService();
        var viewModel = new VisitedStarsCacheViewModel(
            new CommanderProfileCatalog(temporaryDirectory),
            service,
            _ => target,
            () => false);

        await viewModel.RefreshAsync();
        await viewModel.RestoreAsync();
        await viewModel.RestoreAsync();

        Assert.Equal(1, service.RestoreCount);
        Assert.Equal(target, service.TargetPath);
        Assert.Contains("restored", viewModel.StatusMessage);
    }

    [Fact]
    public async Task RunningGameDisablesFileMutations()
    {
        Directory.CreateDirectory(temporaryDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(temporaryDirectory, "F123-live.json"),
            "{\"fid\":\"F123\",\"commander\":\"Drew\"}");
        var target = Path.Combine(
            temporaryDirectory,
            VisitedStarsCacheService.CacheFileName);
        await File.WriteAllBytesAsync(target, [1]);
        var service = new RecordingService();
        var viewModel = new VisitedStarsCacheViewModel(
            new CommanderProfileCatalog(temporaryDirectory),
            service,
            _ => target,
            () => true);
        viewModel.SystemName = "Sol";

        await viewModel.RefreshAsync();

        Assert.True(viewModel.GameIsRunning);
        Assert.False(viewModel.SwapCommand.CanExecute(null));
        Assert.Contains("Close Elite Dangerous", viewModel.GameStateMessage);
    }

    [Fact]
    public async Task DiagnosticReplayDisablesCacheTargetsAndMutations()
    {
        Directory.CreateDirectory(temporaryDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(temporaryDirectory, "F123-replay.json"),
            "{\"fid\":\"F123\",\"commander\":\"Imported\"}");
        var service = new RecordingService();
        var targetResolverCalls = 0;
        var viewModel = new VisitedStarsCacheViewModel(
            new CommanderProfileCatalog(temporaryDirectory),
            service,
            _ =>
            {
                targetResolverCalls++;
                return Path.Combine(
                    temporaryDirectory,
                    VisitedStarsCacheService.CacheFileName);
            },
            () => true,
            externalEffectsAllowed: false);

        await viewModel.RefreshAsync();
        await viewModel.SwapAsync();
        await viewModel.RestoreAsync();

        Assert.False(viewModel.GameIsRunning);
        Assert.Equal(0, targetResolverCalls);
        Assert.Equal(0, service.SwapCount);
        Assert.Equal(0, service.RestoreCount);
        Assert.False(viewModel.SwapCommand.CanExecute(null));
        Assert.False(viewModel.RestoreCommand.CanExecute(null));
        Assert.Contains("diagnostic replay", viewModel.StatusMessage);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private sealed class RecordingService : IVisitedStarsCacheService
    {
        public int SwapCount { get; private set; }

        public int RestoreCount { get; private set; }

        public string? SystemName { get; private set; }

        public string? TargetPath { get; private set; }

        public Task<VisitedStarsCacheSwapResult> SwapAsync(
            string systemName,
            string targetPath,
            CancellationToken cancellationToken = default)
        {
            SwapCount++;
            SystemName = systemName;
            TargetPath = targetPath;
            return Task.FromResult(new VisitedStarsCacheSwapResult(
                targetPath,
                VisitedStarsCacheService.GetBackupPath(targetPath),
                Path.Combine(
                    Path.GetDirectoryName(targetPath)!,
                    "download.dat"),
                new string('1', 64),
                new string('2', 64)));
        }

        public Task<VisitedStarsCacheRestoreResult> RestoreAsync(
            string targetPath,
            CancellationToken cancellationToken = default)
        {
            RestoreCount++;
            TargetPath = targetPath;
            return Task.FromResult(new VisitedStarsCacheRestoreResult(
                targetPath,
                VisitedStarsCacheService.GetBackupPath(targetPath),
                new string('1', 64)));
        }
    }
}
