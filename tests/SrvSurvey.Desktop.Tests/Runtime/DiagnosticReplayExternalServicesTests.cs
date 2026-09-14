using SrvSurvey.Core.Frontier;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.Platform.Frontier;
using SrvSurvey.Desktop.Runtime;

namespace SrvSurvey.Desktop.Tests.Runtime;

public sealed class DiagnosticReplayExternalServicesTests
{
    [Fact]
    public async Task FrontierAccountServiceIsOfflineAndNonPersistent()
    {
        using var service = new DiagnosticReplayFrontierAccountService();
        service.SetActiveCommander("F123456", "Replay Cmdr");

        Assert.Empty(await service.GetLinkedCommandersAsync());
        FrontierAccountState state = await service.GetStateAsync();
        Assert.False(state.IsLinked);
        Assert.Null(state.Snapshot);
        await service.CancelConnectionAsync();
        await service.UnlinkAsync();
        Task<FrontierAccountSnapshot> connectTask = service.ConnectAsync();
        Task<FrontierAccountSnapshot> refreshTask = service.RefreshAsync();
        Assert.True(connectTask.IsFaulted);
        Assert.True(refreshTask.IsFaulted);
        InvalidOperationException connect = await Assert.ThrowsAsync<InvalidOperationException>(() => connectTask);
        InvalidOperationException refresh = await Assert.ThrowsAsync<InvalidOperationException>(() => refreshTask);
        Assert.Contains("unavailable", connect.Message);
        Assert.Equal(connect.Message, refresh.Message);
    }

    [Fact]
    public void GameWindowSwitcherNeverActivatesTheDesktop()
    {
        using var switcher = new DiagnosticReplayGameWindowSwitcher();

        Assert.Equal(1, switcher.GetAvailableWindowCount());
        Assert.False(switcher.TryActivateCurrent());
        Assert.False(switcher.TryActivateNext());
    }

    [Fact]
    public async Task ScreenshotProcessingReportsOnlyTriggeredEnabledWork()
    {
        var processor = new DiagnosticReplayScreenshotProcessingService();
        Assert.True(
            JournalEventEnvelope.TryParse("{\"event\":\"Screenshot\"}", out JournalEventEnvelope? screenshot, out _)
        );
        var disabled = ScreenshotProcessingPreferences.CreateDefaults();

        ScreenshotProcessingResult noWork = await processor.ProcessAsync([screenshot!], disabled, "Replay Cmdr");
        ScreenshotProcessingResult warning = await processor.ProcessAsync(
            [screenshot!],
            disabled with
            {
                Enabled = true,
            },
            "Replay Cmdr"
        );

        Assert.Empty(noWork.Conversions);
        Assert.Empty(noWork.Warnings);
        Assert.Empty(warning.Conversions);
        Assert.Single(warning.Warnings);
        Assert.Contains("unavailable", warning.Warnings[0]);
    }
}
