using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;
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
        var state = await service.GetStateAsync();
        Assert.False(state.IsLinked);
        Assert.Null(state.Snapshot);
        await service.CancelConnectionAsync();
        await service.UnlinkAsync();
        var connect = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ConnectAsync());
        var refresh = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RefreshAsync());
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
        Assert.True(JournalEventEnvelope.TryParse(
            "{\"event\":\"Screenshot\"}",
            out var screenshot,
            out _));
        var disabled = ScreenshotProcessingPreferences.CreateDefaults();

        var noWork = await processor.ProcessAsync(
            [screenshot!],
            disabled,
            "Replay Cmdr");
        var warning = await processor.ProcessAsync(
            [screenshot!],
            disabled with { Enabled = true },
            "Replay Cmdr");

        Assert.Empty(noWork.Conversions);
        Assert.Empty(noWork.Warnings);
        Assert.Empty(warning.Conversions);
        Assert.Single(warning.Warnings);
        Assert.Contains("unavailable", warning.Warnings[0]);
    }
}
