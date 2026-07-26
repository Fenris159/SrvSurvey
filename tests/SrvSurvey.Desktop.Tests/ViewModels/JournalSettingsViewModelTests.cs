using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class JournalSettingsViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-journal-settings-vm-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task AvailableFolderIsSavedBeforeRestartIsRequested()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var journals = Path.Combine(temporaryDirectory, "journals");
        Directory.CreateDirectory(journals);
        var store = CreateStore();
        var viewModel = new JournalSettingsViewModel(store);
        var restartRequested = false;
        viewModel.RestartRequested += () =>
        {
            Assert.Equal(journals, store.Load().Directory);
            restartRequested = true;
            return Task.CompletedTask;
        };

        viewModel.DirectoryPath = journals;
        await viewModel.SaveAndRestartAsync();

        Assert.True(restartRequested);
        Assert.Contains("restarting SrvSurvey", viewModel.StatusMessage);
    }

    [Fact]
    public void MissingFolderCannotBeApplied()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var viewModel = new JournalSettingsViewModel(CreateStore());

        viewModel.DirectoryPath = Path.Combine(temporaryDirectory, "missing");

        Assert.False(viewModel.SaveAndRestartCommand.CanExecute(null));
        Assert.Contains("unavailable", viewModel.StatusMessage);
    }

    [Fact]
    public void CommandLineFolderCannotBeOverriddenInTheRunningInstance()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var journals = Path.Combine(temporaryDirectory, "journals");
        Directory.CreateDirectory(journals);
        var viewModel = new JournalSettingsViewModel(CreateStore(), journals);

        Assert.True(viewModel.IsCommandLineOverride);
        Assert.Equal(journals, viewModel.DirectoryPath);
        Assert.False(viewModel.SaveAndRestartCommand.CanExecute(null));
        Assert.Contains("--journal-directory", viewModel.StatusMessage);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private JournalSettingsStore CreateStore()
    {
        return new JournalSettingsStore(
            Path.Combine(temporaryDirectory, "ui-settings.json"));
    }
}
