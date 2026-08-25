using SrvSurvey.Core.Edsm;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class EdsmSettingsViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-EdsmViewModel-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ApiKeyIsSavedWithTheCurrentCommanderName()
    {
        var viewModel = CreateViewModel();
        viewModel.SetCommanderProfile(
            "F123",
            "Game Commander",
            isOdyssey: true,
            savedApiKey: null);

        Assert.False(viewModel.SaveCredentialsCommand.CanExecute(null));

        viewModel.ApiKey = "  personal-key  ";
        Assert.True(viewModel.SaveCredentialsCommand.CanExecute(null));
        viewModel.SaveCredentialsCommand.Execute(null);
        await WaitForAsync(() => viewModel.HasStoredCredentials);

        var profile = await new CommanderProfileStore(temporaryDirectory)
            .LoadAsync("F123", isOdyssey: true);
        Assert.Equal("Game Commander", profile.Data?.EdsmCommanderName);
        Assert.Equal("personal-key", profile.Data?.EdsmApiKey);
        Assert.False(File.Exists(Path.Combine(
            temporaryDirectory,
            "ui-settings.json")));
    }

    [Fact]
    public async Task ClearingCredentialsRequiresConfirmationAndRaisesAChange()
    {
        var viewModel = CreateViewModel();
        viewModel.SetCommanderProfile(
            "F123",
            "Game Commander",
            isOdyssey: true,
            savedApiKey: "personal-key");
        var changes = 0;
        viewModel.CredentialsChanged += (_, _) => changes++;

        viewModel.RequestClearCredentialsCommand.Execute(null);

        Assert.True(viewModel.IsClearCredentialsConfirmationVisible);
        Assert.Equal("personal-key", viewModel.StoredApiKey);

        viewModel.ConfirmClearCredentialsCommand.Execute(null);
        await WaitForAsync(() => !viewModel.HasStoredCredentials);

        var profile = await new CommanderProfileStore(temporaryDirectory)
            .LoadAsync("F123", isOdyssey: true);
        Assert.Null(profile.Data?.EdsmCommanderName);
        Assert.Null(profile.Data?.EdsmApiKey);
        Assert.Equal(1, changes);
        Assert.False(viewModel.IsClearCredentialsConfirmationVisible);
    }

    [Fact]
    public async Task SaveCompletionCannotOverwriteAProfileLoadedDuringTheWrite()
    {
        var saveStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSave = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        string? savedFrontierId = null;
        string? savedEdsmName = null;
        string? savedApiKey = null;
        async Task SaveAsync(
            string frontierId,
            string? activeCommanderName,
            bool isOdyssey,
            string? edsmCommanderName,
            string? apiKey,
            CancellationToken cancellationToken)
        {
            savedFrontierId = frontierId;
            savedEdsmName = edsmCommanderName;
            savedApiKey = apiKey;
            saveStarted.TrySetResult();
            await releaseSave.Task.WaitAsync(cancellationToken);
        }

        var viewModel = CreateViewModel(SaveAsync);
        var changes = 0;
        viewModel.CredentialsChanged += (_, _) => changes++;
        viewModel.SetCommanderProfile(
            "F123",
            "First Commander",
            isOdyssey: true,
            savedApiKey: null);
        viewModel.ApiKey = "first-key";
        viewModel.SaveCredentialsCommand.Execute(null);
        await saveStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        viewModel.SetCommanderProfile(
            "F456",
            "Second Commander",
            isOdyssey: true,
            savedApiKey: "second-key");
        var saveFinished = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.SaveCredentialsCommand.CanExecuteChanged += (_, _) =>
            saveFinished.TrySetResult();
        releaseSave.TrySetResult();
        await saveFinished.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal("F123", savedFrontierId);
        Assert.Equal("First Commander", savedEdsmName);
        Assert.Equal("first-key", savedApiKey);
        Assert.Equal("Second Commander", viewModel.UploadCommanderName);
        Assert.Equal("second-key", viewModel.ApiKey);
        Assert.Contains("Second Commander", viewModel.CredentialStatus);
        Assert.DoesNotContain("First Commander", viewModel.CredentialStatus);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void PublicationResultIsPresentedWithoutExposingCredentials()
    {
        var viewModel = CreateViewModel();

        viewModel.ReportPublicationResult(new EdsmPublicationResult(
            QueuedEventCount: 2,
            AcceptedEventCount: 0,
            PendingEventCount: 2,
            QueuedEventNames: ["FSDJump"],
            Warnings: []));

        Assert.Contains("Queued 2 EDSM journal event", viewModel.PublicationStatus);
        Assert.True(viewModel.HasPublicationStatus);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private EdsmSettingsViewModel CreateViewModel(
        Func<
            string,
            string?,
            bool,
            string?,
            string?,
            CancellationToken,
            Task>? saveCredentialsAsync = null)
    {
        return new EdsmSettingsViewModel(
            new CommanderProfileStore(temporaryDirectory),
            saveCredentialsAsync);
    }

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        var timeout = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
        while (!predicate())
        {
            Assert.True(
                DateTimeOffset.UtcNow < timeout,
                "The asynchronous command did not complete.");
            await Task.Delay(10);
        }
    }
}
