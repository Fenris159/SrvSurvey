using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class DesktopBehaviorViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-desktop-behavior-vm-tests-{Guid.NewGuid():N}");

    [Fact]
    public void StartupAndMinimizeFocusTheCurrentCommanderWindow()
    {
        var switcher = new RecordingSwitcher();
        var viewModel = CreateViewModel(switcher);

        Assert.True(viewModel.RequestStartupFocus());
        Assert.True(viewModel.RequestMinimizeFocus());

        Assert.Equal(2, switcher.CurrentActivationCount);
        Assert.Equal(0, switcher.NextActivationCount);
    }

    [Fact]
    public void OnlyLiveFsdJumpUsesOptionalFocusPolicy()
    {
        var switcher = new RecordingSwitcher();
        var viewModel = CreateViewModel(switcher);
        viewModel.FocusGameAfterFsdJump = true;
        var jump = Parse("{\"event\":\"FSDJump\"}");

        viewModel.ApplyJournalEvents([jump], isBootstrapRead: true);
        viewModel.ApplyJournalEvents(
            [Parse("{\"event\":\"Scan\"}")],
            isBootstrapRead: false);
        Assert.Equal(0, switcher.CurrentActivationCount);

        viewModel.ApplyJournalEvents([jump], isBootstrapRead: false);

        Assert.Equal(1, switcher.CurrentActivationCount);
        Assert.Equal(0, switcher.NextActivationCount);
    }

    [Fact]
    public void MissingGameWindowReportsNonFatalStatus()
    {
        var switcher = new RecordingSwitcher { Result = false };
        var viewModel = CreateViewModel(switcher);

        Assert.False(viewModel.RequestStartupFocus());

        Assert.Contains("no matching game window", viewModel.StatusMessage);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private DesktopBehaviorViewModel CreateViewModel(
        IGameWindowSwitcher switcher)
    {
        return new DesktopBehaviorViewModel(
            new DesktopBehaviorSettingsStore(Path.Combine(
                temporaryDirectory,
                "ui-settings.json")),
            switcher);
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(JournalEventEnvelope.TryParse(json, out var result, out _));
        return result!;
    }

    private sealed class RecordingSwitcher : IGameWindowSwitcher
    {
        public bool Result { get; init; } = true;

        public int CurrentActivationCount { get; private set; }

        public int NextActivationCount { get; private set; }

        public bool TryActivateCurrent()
        {
            CurrentActivationCount++;
            return Result;
        }

        public bool TryActivateNext()
        {
            NextActivationCount++;
            return Result;
        }

        public void Dispose()
        {
        }
    }
}
