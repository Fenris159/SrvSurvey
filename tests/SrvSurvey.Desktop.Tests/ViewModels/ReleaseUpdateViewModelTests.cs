using SrvSurvey.Core.Updates;
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
        Assert.Equal("2.0.95", viewModel.CurrentVersion);
        Assert.Equal("2.0.95.23", viewModel.LatestVersion);
        Assert.Contains("installation was not changed", viewModel.StatusMessage);
        Assert.False(viewModel.OpenReleaseCommand.CanExecute(null));
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

    private static ReleaseUpdateResult CreateResult(bool isAvailable)
    {
        var index = new PublishedDataIndex(
            new Version(2, 0, 95, 23),
            new Version(2, 0, 95, 0),
            7,
            4,
            10,
            48,
            68,
            15,
            1,
            1);
        return new ReleaseUpdateResult(
            new Version(2, 0, 95, 0),
            index.GitHubVersion,
            isAvailable,
            new Uri("https://example.test/releases"),
            index);
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
            Version currentVersion,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(result);
        }
    }

    private sealed class FailingService : IReleaseUpdateService
    {
        public Task<ReleaseUpdateResult> CheckAsync(
            Version currentVersion,
            CancellationToken cancellationToken = default)
        {
            throw new HttpRequestException("network unavailable");
        }
    }
}
