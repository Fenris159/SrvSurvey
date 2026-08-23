using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using SrvSurvey.Core.Diagnostics.Replay;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.Runtime;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Runtime;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class DiagnosticReplayOverlayIntegrationTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-diagnostic-overlay-{Guid.NewGuid():N}");

    [AvaloniaFact]
    public async Task ProgressiveReplayDrivesNormalOverlayVisibilityAndExpiry()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var sourcePath = Path.Combine(temporaryDirectory, "Journal.source.log");
        await File.WriteAllLinesAsync(
            sourcePath,
            [
                "{\"timestamp\":\"2026-08-21T18:00:00Z\",\"event\":\"Commander\",\"Name\":\"Replay Cmdr\",\"FID\":\"F123456\"}",
                "{\"timestamp\":\"2026-08-21T18:00:01Z\",\"event\":\"Materials\",\"Raw\":[],\"Manufactured\":[],\"Encoded\":[{\"Name\":\"ancienttechnologicaldata\",\"Name_Localised\":\"Pattern Epsilon Obelisk Data\",\"Count\":4}]}",
                "{\"timestamp\":\"2026-08-21T18:00:02Z\",\"event\":\"MaterialCollected\",\"Category\":\"Encoded\",\"Name\":\"ancienttechnologicaldata\",\"Name_Localised\":\"Pattern Epsilon Obelisk Data\",\"Count\":3}",
            ]);
        var session = await new ReplaySessionManager().ImportAsync(
            sourcePath,
            Path.Combine(temporaryDirectory, "managed"),
            CancellationToken.None);
        var context = await DiagnosticReplayContext.LoadAsync(
            session.ManifestPath,
            CancellationToken.None);
        var player = new JournalReplayPlayer(session);
        var monitor = new JournalDirectoryMonitor(
            context.JournalDirectory,
            context.Commander.FrontierId);
        var time = new MutableTimeProvider(
            DateTimeOffset.Parse("2026-08-21T18:00:00Z"));
        var notification = new NotificationViewModel(
            new NotificationSettingsStore(
                context.AppDataPaths.UiSettingsPath),
            time);
        var registry = new OverlayWindowRegistry();
        var platform = new RecordingOverlayPlatform();
        using var coordinator = new NotificationOverlayCoordinator(
            notification,
            platform,
            context.CreateGameWindowTracker(),
            registry: registry);

        Assert.True(await player.StepAsync(CancellationToken.None));
        Assert.True(await player.StepAsync(CancellationToken.None));
        var bootstrap = await monitor.PollAsync(CancellationToken.None);
        notification.ApplyJournalEvents(
            bootstrap.JournalEvents,
            allowNotifications: !bootstrap.IsBootstrapRead);

        Assert.True(bootstrap.IsBootstrapRead);
        Assert.False(coordinator.IsVisible);
        Assert.Empty(notification.Messages);

        Assert.True(await player.StepAsync(CancellationToken.None));
        var live = await monitor.PollAsync(CancellationToken.None);
        notification.ApplyJournalEvents(
            live.JournalEvents,
            allowNotifications: !live.IsBootstrapRead);

        Assert.False(live.IsBootstrapRead);
        Assert.True(coordinator.IsVisible);
        var replayWindow = Assert.Single(platform.PreparedWindows);
        Assert.NotNull(replayWindow.CaptureRenderedFrame());
        Assert.True(Assert.Single(registry.Snapshot()).IsVisible);

        registry.SetUserVisibility("PlotFloatie", visible: false);
        Assert.False(Find(registry, "PlotFloatie").IsVisible);
        Assert.Equal(
            OverlayVisibilityReasons.UserDisabled,
            registry.GetDecision(replayWindow).Reasons);
        registry.SetUserVisibility("PlotFloatie", visible: true);
        Assert.True(Find(registry, "PlotFloatie").IsVisible);

        var surfaceWindow = new Window();
        registry.Register(surfaceWindow, "PlotGrounded");
        surfaceWindow.Show();
        Assert.True(surfaceWindow.IsVisible);
        registry.SetGalaxyMapContextActive(active: true);
        Assert.False(surfaceWindow.IsVisible);
        Assert.True(Find(registry, "PlotFloatie").IsVisible);
        registry.SetGalaxyMapContextActive(active: false);

        registry.SetGlobalSuppression(
            manualSuppressed: false,
            suitSuppressed: true,
            sessionSuppressed: false);
        Assert.All(registry.Snapshot(), item => Assert.False(item.IsVisible));
        registry.SetGlobalSuppression(false, false, false);

        var guardianWindow = new Window();
        var biologyWindow = new Window();
        registry.Register(guardianWindow, "PlotGuardians");
        registry.Register(biologyWindow, "PlotBioSystem");
        biologyWindow.Show();
        guardianWindow.Show();
        Assert.True(guardianWindow.IsVisible);
        Assert.False(biologyWindow.IsVisible);
        Assert.Equal(
            OverlayVisibilityReasons.PriorityObscured,
            registry.GetDecision(biologyWindow).Reasons);

        notification.Enabled = false;
        Assert.False(coordinator.IsVisible);
        notification.Enabled = true;
        notification.ApplyJournalEvents(
            live.JournalEvents,
            allowNotifications: true);
        Assert.True(coordinator.IsVisible);

        time.Advance(TimeSpan.FromSeconds(6));
        notification.Refresh();

        Assert.False(coordinator.IsVisible);
        Assert.Empty(notification.Messages);
        surfaceWindow.Close();
        guardianWindow.Close();
        biologyWindow.Close();
    }

    public void Dispose()
    {
        if (!Directory.Exists(temporaryDirectory))
        {
            return;
        }

        try
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
        catch (IOException)
        {
            // Cleanup must not mask an integration assertion failure.
        }
        catch (UnauthorizedAccessException)
        {
            // Cleanup must not mask an integration assertion failure.
        }
    }

    private static RegisteredOverlayWindow Find(
        OverlayWindowRegistry registry,
        string plotterName) => registry.Snapshot().Single(item =>
            item.PlotterName == plotterName);

    private sealed class MutableTimeProvider(DateTimeOffset value)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;

        public void Advance(TimeSpan duration)
        {
            value += duration;
        }
    }

    private sealed class RecordingOverlayPlatform : IOverlayPlatformService
    {
        public OverlayPlatformCapabilities Capabilities { get; } =
            OverlayPlatformCapabilities.ForHost(OverlayHostKind.Windows);

        public List<Window> PreparedWindows { get; } = [];

        public OverlayPreparationResult PreparePassiveWindow(Window window)
        {
            PreparedWindows.Add(window);
            return new OverlayPreparationResult(true, true, "Prepared");
        }

        public OverlayInteractionResult SetInteractive(
            Window window,
            bool interactive) => new(
                true,
                interactive,
                "Prepared");

        public void Dispose()
        {
        }
    }
}
