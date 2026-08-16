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
            "/profiles/reference-backups/verified");
        var viewModel = new ReferenceDataUpdateViewModel(
            new StubService(result),
            Path.GetTempPath(),
            "Embedded catalogs active.");

        await viewModel.RefreshAsync();

        Assert.True(viewModel.IsRestartRequired);
        Assert.Contains("Codex reference", viewModel.UpdatedCatalogs);
        Assert.Equal(
            "Backup created this session: /profiles/reference-backups/verified",
            viewModel.BackupDirectory);
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
            null);
        var viewModel = new ReferenceDataUpdateViewModel(
            new StubService(result),
            Path.GetTempPath(),
            "Ready");

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
            null);
        var viewModel = new ReferenceDataUpdateViewModel(
            new StubService(result),
            Path.GetTempPath(),
            "Ready");
        var restarted = false;
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
        var viewModel = new ReferenceDataUpdateViewModel(
            new FailingService(),
            Path.GetTempPath(),
            "Ready",
            log.Add);

        await viewModel.RefreshAsync();

        Assert.False(viewModel.IsRestartRequired);
        Assert.Contains("failed safely", viewModel.StatusMessage);
        Assert.Contains("survey files were not changed", viewModel.StatusMessage);
        Assert.Single(log);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var timeout = DateTime.UtcNow.AddSeconds(2);
        while (!predicate() && DateTime.UtcNow < timeout)
        {
            await Task.Delay(10);
        }
    }

    private sealed class StubService(PublishedReferenceUpdateResult result)
        : IPublishedReferenceUpdateService
    {
        public Task<PublishedReferenceUpdateResult> RefreshAsync(
            string dataDirectory,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(result);
        }
    }

    private sealed class FailingService : IPublishedReferenceUpdateService
    {
        public Task<PublishedReferenceUpdateResult> RefreshAsync(
            string dataDirectory,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidDataException("candidate was truncated");
        }
    }
}
