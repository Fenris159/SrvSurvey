using Avalonia;
using SrvSurvey.Core.Diagnostics.Replay;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Runtime;

internal sealed class DiagnosticReplayContext
{
    private DiagnosticReplayContext(DiagnosticReplaySession session)
    {
        Session = session;
        AppDataPaths = new AppDataPaths(
            session.ConfigDirectory,
            session.DataDirectory,
            session.CacheDirectory,
            []);
    }

    public DiagnosticReplaySession Session { get; }

    public AppDataPaths AppDataPaths { get; }

    public string JournalDirectory =>
        Path.GetDirectoryName(Session.PlaybackJournalPath)
        ?? throw new InvalidDataException(
            "The diagnostic playback journal has no containing directory.");

    public string LogsDirectory => Session.LogsDirectory;

    public ReplayCommander Commander => Session.Commander;

    public bool ExternalEffectsAllowed => false;

    public static async Task<DiagnosticReplayContext> LoadAsync(
        string manifestPath,
        CancellationToken cancellationToken)
    {
        var session = await DiagnosticReplaySession.LoadAsync(
            manifestPath,
            cancellationToken);
        ReplayPresentationSnapshotStore.Apply(session);
        return new DiagnosticReplayContext(session);
    }

    public IGameWindowTracker CreateGameWindowTracker()
    {
        var bounds = Session.PresentationSnapshot is { } presentation
            ? new PixelRect(
                0,
                0,
                presentation.ViewportWidth,
                presentation.ViewportHeight)
            : new PixelRect(0, 0, 1920, 1080);
        return new DiagnosticGameWindowTracker(
            bounds);
    }

    public HttpClient CreateNetworkClient()
    {
        return new HttpClient(new DiagnosticReplayNetworkHandler())
        {
            Timeout = TimeSpan.FromSeconds(1),
        };
    }

    private sealed class DiagnosticGameWindowTracker(PixelRect bounds)
        : IGameWindowTracker
    {
        private readonly GameWindowSnapshot snapshot = new(
            new nint(1),
            null,
            bounds,
            IsVisible: true,
            IsForeground: true);

        public GameWindowSnapshot GetSnapshot() => snapshot;

        public void Dispose()
        {
        }
    }

    private sealed class DiagnosticReplayNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new HttpRequestException(
                "Network access is disabled during diagnostic replay. "
                + $"Blocked {request.Method} request to {request.RequestUri?.Host ?? "an external service"}.");
        }
    }
}

internal sealed record DesktopStartupContext(
    AppDataPaths AppDataPaths,
    DiagnosticReplayContext? DiagnosticReplay)
{
    public bool IsDiagnosticReplay => DiagnosticReplay is not null;

    public static async Task<DesktopStartupContext> ResolveAsync(
        IReadOnlyList<string> arguments,
        Func<AppDataPaths> normalPathsFactory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(normalPathsFactory);
        var replayManifest = StartupOptions.GetDiagnosticReplayManifest(arguments);
        if (replayManifest is null
            && StartupOptions.HasDiagnosticReplayOption(arguments))
        {
            throw new ArgumentException(
                "The --diagnostic-replay option requires a replay-session manifest path.",
                nameof(arguments));
        }

        if (replayManifest is null)
        {
            return new DesktopStartupContext(normalPathsFactory(), null);
        }

        var diagnosticReplay = await DiagnosticReplayContext.LoadAsync(
            replayManifest,
            cancellationToken);
        return new DesktopStartupContext(
            diagnosticReplay.AppDataPaths,
            diagnosticReplay);
    }
}
