using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Localization;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class LocalizationViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-localization-view-model-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task SelectingLanguagePersistsAndUsesControlledRestart()
    {
        LocalizationCatalog.Initialize("en");
        var settingsPath = Path.Combine(temporaryDirectory, "ui-settings.json");
        var store = new LocalizationSettingsStore(
            settingsPath,
            Path.Combine(temporaryDirectory, "profile"));
        var viewModel = new LocalizationViewModel(store);
        var restarted = false;
        viewModel.SetRestartHandler(() =>
        {
            restarted = true;
            return Task.CompletedTask;
        });

        viewModel.SelectedLanguage = viewModel.Languages.Single(
            language => language.Code == "es");

        Assert.True(viewModel.IsRestartRequired);
        Assert.True(viewModel.RestartCommand.CanExecute(null));
        Assert.Contains("Restart SrvSurvey", viewModel.StatusMessage);
        Assert.Equal("es", store.Load());

        viewModel.RestartCommand.Execute(null);
        await WaitUntilAsync(() => restarted);

        Assert.True(restarted);
    }

    public void Dispose()
    {
        LocalizationCatalog.Initialize("en");
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var timeout = DateTime.UtcNow.AddSeconds(2);
        while (!predicate() && DateTime.UtcNow < timeout)
        {
            await Task.Delay(10);
        }
    }
}
