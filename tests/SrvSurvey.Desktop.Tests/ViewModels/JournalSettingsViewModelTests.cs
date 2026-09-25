using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class JournalSettingsViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-journal-settings-vm-tests-{Guid.NewGuid():N}"
    );

    [Fact]
    public async Task AvailableFolderIsSavedBeforeRestartIsRequested()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string journals = Path.Combine(temporaryDirectory, "journals");
        Directory.CreateDirectory(journals);
        JournalSettingsStore store = CreateStore();
        var viewModel = new JournalSettingsViewModel(store);
        bool restartRequested = false;
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
        string journals = Path.Combine(temporaryDirectory, "journals");
        Directory.CreateDirectory(journals);
        var viewModel = new JournalSettingsViewModel(CreateStore(), journals);

        Assert.True(viewModel.IsCommandLineOverride);
        Assert.Equal(journals, viewModel.DirectoryPath);
        Assert.False(viewModel.SaveAndRestartCommand.CanExecute(null));
        Assert.Contains("--journal-directory", viewModel.StatusMessage);
    }

    [Fact]
    public void AddEditAndDeletePersistMultipleJournalFolders()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string steam = Path.Combine(temporaryDirectory, "steam");
        string epic = Path.Combine(temporaryDirectory, "epic");
        string movedEpic = Path.Combine(temporaryDirectory, "moved-epic");
        Directory.CreateDirectory(steam);
        Directory.CreateDirectory(epic);
        Directory.CreateDirectory(movedEpic);
        JournalSettingsStore store = CreateStore();
        var viewModel = new JournalSettingsViewModel(store);

        viewModel.DirectoryPath = steam;
        viewModel.AddOrUpdateCommand.Execute(null);
        viewModel.DirectoryPath = epic;
        viewModel.AddOrUpdateCommand.Execute(null);
        Assert.Equal([steam, epic], store.Load().Directories);
        Assert.Equal(string.Empty, viewModel.DirectoryPath);

        viewModel.EditFolder(epic);
        Assert.Equal("Save change", viewModel.AddButtonLabel);
        viewModel.ClearSelection();
        viewModel.DirectoryPath = movedEpic;
        viewModel.AddOrUpdateCommand.Execute(null);
        Assert.Equal([steam, movedEpic], store.Load().Directories);

        viewModel.RemoveFolder(steam);
        Assert.Equal([movedEpic], store.Load().Directories);
        Assert.True(viewModel.RestartCommand.CanExecute(null));
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
        return new JournalSettingsStore(Path.Combine(temporaryDirectory, "ui-settings.json"));
    }
}
