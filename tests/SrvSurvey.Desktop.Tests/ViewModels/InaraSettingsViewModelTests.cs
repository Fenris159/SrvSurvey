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

    private InaraSettingsViewModel CreateViewModel()
    {
        return new InaraSettingsViewModel(
            new InaraSettingsStore(Path.Combine(
                temporaryDirectory,
                "ui-settings.json")),
            new CommanderProfileStore(temporaryDirectory));
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
