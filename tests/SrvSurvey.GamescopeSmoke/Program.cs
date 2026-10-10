using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
using SrvSurvey.Desktop;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.GamescopeSmoke;

/// <summary>Runs the production native service and combined controller in an isolated Gamescope session.</summary>
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        _ = X11Native.TryInitializeThreading();
        GamescopeOverlaySession.InitializeCurrent(Console.WriteLine);
        AppBuilder
            .Configure<SmokeApplication>()
            .UsePlatformDetect()
            .With(SrvSurvey.Desktop.Program.CreateX11Options(useSoftwareRendering: true))
            .LogToTrace()
            .StartWithClassicDesktopLifetime(args);
    }
}

internal sealed class SmokeApplication : Application
{
    private IOverlayPlatformService? platform;
    private IGameWindowTracker? tracker;
    private CombinedOverlayPresentationController? controller;
    private DispatcherTimer? timer;

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            throw new InvalidOperationException("A classic desktop lifetime is required.");
        }
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        platform = OverlayPlatformService.CreateCurrent();
        using IGameScreenCapture capture = GameScreenCapture.CreateCurrent();
        if (capture.IsAvailable)
        {
            throw new InvalidOperationException("The primary X11 root must not advertise game capture across servers.");
        }
        Console.WriteLine(capture.UnavailableReason);
        tracker = GameWindowTracker.CreateCurrent();
        var registry = new OverlayWindowRegistry();
        controller = new CombinedOverlayPresentationController(platform, tracker, registry);
        var source = new Window
        {
            Width = 64,
            Height = 64,
            Position = new PixelPoint(16, 16),
            WindowDecorations = WindowDecorations.None,
            ShowActivated = false,
            ShowInTaskbar = false,
            Content = new Border { Background = Brushes.Lime },
        };
        registry.Register(source, "PlotJumpInfo");
        _ = controller.PreparePassiveWindow(source);
        source.Show();
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        int ticks = 0;
        timer.Tick += (_, _) =>
        {
            GameWindowSnapshot game = tracker.GetSnapshot();
            Console.WriteLine(
                $"GAME visible={game.IsVisible} foreground={game.IsForeground} bounds={game.ClientBounds} canvas={game.OverlayCanvasBounds}"
            );
            if (++ticks == 30)
            {
                desktop.Shutdown(game.IsVisible ? 0 : 2);
            }
        };
        timer.Start();
        desktop.Exit += (_, _) =>
        {
            timer.Stop();
            controller.Dispose();
            tracker.Dispose();
            platform.Dispose();
        };
        base.OnFrameworkInitializationCompleted();
    }
}
