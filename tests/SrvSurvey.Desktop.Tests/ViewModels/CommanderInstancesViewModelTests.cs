using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class CommanderInstancesViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-commander-instance-tests-{Guid.NewGuid():N}"
    );

    [Fact]
    public async Task RefreshExcludesCurrentAndLaunchesSelectedCommander()
    {
        Directory.CreateDirectory(temporaryDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(temporaryDirectory, "F123-live.json"),
            "{\"fid\":\"F123\",\"commander\":\"Drew\"}"
        );
        await File.WriteAllTextAsync(
            Path.Combine(temporaryDirectory, "F456-live.json"),
            "{\"fid\":\"F456\",\"commander\":\"Raven\"}"
        );
        var launcher = new RecordingLauncher();
        var switcher = new RecordingSwitcher();
        var journalDirectory = Path.Combine(temporaryDirectory, "journals");
        Directory.CreateDirectory(journalDirectory);
        var viewModel = new CommanderInstancesViewModel(
            new CommanderProfileCatalog(temporaryDirectory),
            launcher,
            journalDirectory,
            "F123",
            switcher
        );
        Assert.Equal(2, viewModel.AvailableGameWindowCount);
        Assert.True(viewModel.HasMultipleGameWindows);
        viewModel.UpdateCurrent("F123", "Drew");

        await viewModel.RefreshAsync();
        await viewModel.LaunchSelectedAsync();
        var switched = viewModel.SwitchToNextGameWindow();

        var option = Assert.Single(viewModel.Commanders);
        Assert.Equal("F456", option.FrontierId);
        Assert.Same(option, viewModel.SelectedCommander);
        Assert.Equal("Drew (F123)", viewModel.CurrentCommander);
        Assert.Equal("~ Drew ~", viewModel.MultiGameOverlayLabel);
        Assert.Equal("F456", launcher.FrontierId);
        Assert.Equal(journalDirectory, launcher.JournalDirectory);
        Assert.True(switched);
        Assert.Equal(1, switcher.ActivationCount);
        Assert.Contains("Focused the next", viewModel.StatusMessage);

        switcher.AvailableWindowCount = 1;
        viewModel.RefreshGameWindowCount();

        Assert.Equal(1, viewModel.AvailableGameWindowCount);
        Assert.False(viewModel.HasMultipleGameWindows);
    }

    [Fact]
    public async Task LaunchesJournalDiscoveredCommanderFromItsOwnPrefix()
    {
        var profileDirectory = Path.Combine(temporaryDirectory, "profiles");
        var steam = Path.Combine(temporaryDirectory, "steam");
        var epic = Path.Combine(temporaryDirectory, "epic");
        Directory.CreateDirectory(profileDirectory);
        Directory.CreateDirectory(steam);
        Directory.CreateDirectory(epic);
        await File.WriteAllTextAsync(
            Path.Combine(steam, "Journal.2026-09-01T100000.01.log"),
            "{\"event\":\"Commander\",\"Name\":\"Steam Cmdr\",\"FID\":\"F123\"}\n"
        );
        await File.WriteAllTextAsync(
            Path.Combine(epic, "Journal.2026-09-01T110000.01.log"),
            "{\"event\":\"Commander\",\"Name\":\"Epic Cmdr\",\"FID\":\"F456\"}\n"
        );
        var launcher = new RecordingLauncher();
        var viewModel = new CommanderInstancesViewModel(
            new CommanderProfileCatalog(profileDirectory, [steam, epic]),
            launcher,
            steam,
            "F123",
            new RecordingSwitcher()
        );

        await viewModel.RefreshAsync();
        await viewModel.LaunchSelectedAsync();

        var option = Assert.Single(viewModel.Commanders);
        Assert.Equal("F456", option.FrontierId);
        Assert.Equal(epic, option.JournalDirectory);
        Assert.Equal(epic, launcher.JournalDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private sealed class RecordingLauncher : ICommanderInstanceLauncher
    {
        public string? FrontierId { get; private set; }

        public string? JournalDirectory { get; private set; }

        public Task LaunchAsync(
            string frontierId,
            string journalDirectory,
            CancellationToken cancellationToken = default
        )
        {
            FrontierId = frontierId;
            JournalDirectory = journalDirectory;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingSwitcher : IGameWindowSwitcher
    {
        public int ActivationCount { get; private set; }

        public int AvailableWindowCount { get; set; } = 2;

        public int GetAvailableWindowCount() => AvailableWindowCount;

        public bool TryActivateCurrent()
        {
            return true;
        }

        public bool TryActivateNext()
        {
            ActivationCount++;
            return true;
        }

        public void Dispose() { }
    }
}
