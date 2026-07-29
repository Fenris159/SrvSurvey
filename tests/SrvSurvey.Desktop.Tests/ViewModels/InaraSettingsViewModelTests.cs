using SrvSurvey.Core.Inara;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class InaraSettingsViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-InaraViewModel-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void PublicationPreferencesAreOptInAndPersistImmediately()
    {
        var viewModel = CreateViewModel();

        Assert.False(viewModel.UploadEnabled);
        Assert.False(viewModel.DeveloperTestMode);

        viewModel.UploadEnabled = true;
        viewModel.DeveloperTestMode = true;

        var reloaded = CreateViewModel();
        Assert.True(reloaded.UploadEnabled);
        Assert.True(reloaded.DeveloperTestMode);
    }

    [Fact]
    public async Task PersonalKeyIsSavedOnlyToTheCommanderProfile()
    {
        var viewModel = CreateViewModel();
        viewModel.UploadEnabled = true;
        viewModel.SetCommanderProfile(
            "F123",
            "Test Commander",
            isOdyssey: true,
            inaraApiKey: null);
        viewModel.ApiKey = "  personal-key  ";

        Assert.True(viewModel.SaveApiKeyCommand.CanExecute(null));
        viewModel.SaveApiKeyCommand.Execute(null);
        await WaitForAsync(() => viewModel.HasStoredApiKey);

        var profile = await new CommanderProfileStore(temporaryDirectory)
            .LoadAsync("F123", isOdyssey: true);
        Assert.Equal("personal-key", profile.Data?.InaraApiKey);
        Assert.DoesNotContain(
            "personal-key",
            File.ReadAllText(Path.Combine(
                temporaryDirectory,
                "ui-settings.json")));
    }

    [Fact]
    public async Task SaveCompletionCannotOverwriteAProfileLoadedDuringTheWrite()
    {
        var saveStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSave = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        string? savedFrontierId = null;
        string? savedApiKey = null;
        async Task SaveAsync(
            string frontierId,
            string? commanderName,
            bool isOdyssey,
            string? apiKey,
            CancellationToken cancellationToken)
        {
            savedFrontierId = frontierId;
            savedApiKey = apiKey;
            saveStarted.TrySetResult();
            await releaseSave.Task.WaitAsync(cancellationToken);
        }

        var viewModel = CreateViewModel(SaveAsync);
        viewModel.SetCommanderProfile(
            "F123",
            "First Commander",
            isOdyssey: true,
            inaraApiKey: null);
        viewModel.ApiKey = "first-key";
        viewModel.SaveApiKeyCommand.Execute(null);
        await saveStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        viewModel.SetCommanderProfile(
            "F456",
            "Second Commander",
            isOdyssey: true,
            inaraApiKey: "second-key");
        var saveFinished = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.SaveApiKeyCommand.CanExecuteChanged += (_, _) =>
            saveFinished.TrySetResult();
        releaseSave.TrySetResult();
        await saveFinished.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal("F123", savedFrontierId);
        Assert.Equal("first-key", savedApiKey);
        Assert.Equal("second-key", viewModel.ApiKey);
        Assert.Equal("second-key", viewModel.StoredApiKey);
        Assert.Contains("Second Commander", viewModel.CredentialStatus);
        Assert.DoesNotContain("First Commander", viewModel.CredentialStatus);
    }

    [Fact]
    public void PublicationResultIsPresentedWithoutExposingCredentials()
    {
        var viewModel = CreateViewModel();

        viewModel.ReportPublicationResult(new InaraPublicationResult(
            QueuedEventCount: 2,
            AcceptedEventCount: 0,
            PendingEventCount: 2,
            QueuedEventNames: ["getCommanderProfile"],
            Warnings: []));

        Assert.Contains("Queued 2 Inara event", viewModel.PublicationStatus);
        Assert.True(viewModel.HasPublicationStatus);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private InaraSettingsViewModel CreateViewModel(
        Func<string, string?, bool, string?, CancellationToken, Task>?
            saveInaraApiKeyAsync = null)
    {
        return new InaraSettingsViewModel(
            new InaraSettingsStore(Path.Combine(
                temporaryDirectory,
                "ui-settings.json")),
            new CommanderProfileStore(temporaryDirectory),
            saveInaraApiKeyAsync);
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
