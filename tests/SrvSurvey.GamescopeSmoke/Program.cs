using System.Text.Json;
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
        if (args.Contains("--desktop-tracking", StringComparer.Ordinal))
        {
            RunDesktopTracking();
            return;
        }
        AppBuilder
            .Configure<SmokeApplication>()
            .UsePlatformDetect()
            .With(SrvSurvey.Desktop.Program.CreateX11Options(useSoftwareRendering: true))
            .LogToTrace()
            .StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Samples the actual desktop tracker without starting a Gamescope overlay session.</summary>
    private static void RunDesktopTracking()
    {
        using IOverlayPlatformService platform = OverlayPlatformService.CreateCurrent();
        using IGameScreenCapture capture = GameScreenCapture.CreateCurrent();
        if (
            GamescopeOverlaySession.Current is not null
            || platform.Capabilities.UsesGamescopeExternalOverlay
            || !capture.IsAvailable
        )
        {
            throw new InvalidOperationException(
                "The desktop probe requires the ordinary X11 overlay and capture path."
            );
        }
        using IGameWindowTracker tracker = GameWindowTracker.CreateCurrent();
        Console.WriteLine("DESKTOP_READY external=false capture=true");
        while (Console.ReadLine() is { } command)
        {
            if (command != "snapshot")
            {
                throw new InvalidOperationException("Only snapshot commands are supported.");
            }
            GameWindowSnapshot game = tracker.GetSnapshot();
            Console.WriteLine(
                JsonSerializer.Serialize(
                    new
                    {
                        Available = game.IsAvailable,
                        Visible = game.IsVisible,
                        Foreground = game.IsForeground,
                        game.ProcessId,
                        NativeHandle = (long)game.NativeHandle,
                        game.ClientBounds.X,
                        game.ClientBounds.Y,
                        game.ClientBounds.Width,
                        game.ClientBounds.Height,
                    }
                )
            );
        }
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
        bool interactive = desktop.Args?.Contains("--interactive", StringComparer.Ordinal) == true;
        int pointerCount = 0;
        var marker = new Border { Background = Brushes.Lime };
        marker.PointerPressed += (_, _) => Console.WriteLine($"HUD POINTER count={++pointerCount}");
        var source = new Window
        {
            Width = 64,
            Height = 64,
            Position = new PixelPoint(16, 16),
            WindowDecorations = WindowDecorations.None,
            ShowActivated = false,
            ShowInTaskbar = false,
            Content = marker,
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
            ++ticks;
            if (interactive)
            {
                if (ticks is 4 or 20)
                {
                    bool active = ticks == 4;
                    OverlayInteractionResult result = controller.SetInteractive(source, active);
                    Console.WriteLine(
                        $"LIVE {(active ? "on" : "off")} prepared={result.IsPrepared} interactive={result.IsInteractive}: {result.Status}"
                    );
                    if (!result.IsPrepared || result.IsInteractive != active)
                    {
                        desktop.Shutdown(3);
                    }
                }
                if (ticks == 24)
                {
                    registry.SetPresentationVisible(source, false);
                    Console.WriteLine("SOURCE hidden");
                }
                if (ticks == 26)
                {
                    registry.SetPresentationVisible(source, true);
                    Console.WriteLine("SOURCE shown");
                }
            }
            if (ticks == (interactive ? 36 : 30))
            {
                desktop.Shutdown(game.IsVisible && (!interactive || pointerCount > 0) ? 0 : 2);
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
