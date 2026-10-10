using Avalonia;
using SrvSurvey.Desktop.Input;

namespace SrvSurvey.Desktop.Platform.Overlay;

/// <summary>Tracks Elite on its own verified XWayland server and projects it into the compositor output.</summary>
internal sealed class GamescopeExternalGameWindowTracker(
    Func<GamescopeFocusSnapshot> readFocus,
    Func<string?, EliteKeyboardDisplay?> discover,
    Func<string, IGameWindowTracker?> createTracker,
    IDisposable connection,
    Func<long>? milliseconds = null
) : IGameWindowTracker
{
    private readonly Func<long> milliseconds = milliseconds ?? (() => Environment.TickCount64);
    private IGameWindowTracker? gameTracker;
    private EliteKeyboardDisplay? game;
    private long nextDiscovery;
    private bool disposed;

    public static IGameWindowTracker? TryCreate(GamescopeOverlaySession session)
    {
        var primary = GamescopeX11Connection.TryOpen(session.Display);
        if (primary is null)
        {
            return null;
        }
        return new GamescopeExternalGameWindowTracker(
            primary.ReadFocus,
            preferred => EliteKeyboardDisplayDiscovery.Read("/proc", preferredDisplay: preferred),
            display => CreateVerifiedTracker(session, display),
            primary
        );
    }

    private static IGameWindowTracker? CreateVerifiedTracker(GamescopeOverlaySession session, string display)
    {
        GamescopeDisplayIdentity? identity = GamescopeOverlaySession.Probe(display);
        return
            identity is not null
            && (session.Identity.ProcessId is null || identity.ProcessId == session.Identity.ProcessId)
            ? X11GameWindowTracker.TryCreate(display)
            : null;
    }

    public GameWindowSnapshot GetSnapshot()
    {
        if (disposed)
        {
            return GameWindowSnapshot.Unavailable;
        }
        GamescopeFocusSnapshot focus = readFocus();
        long now = milliseconds();
        if (now >= nextDiscovery)
        {
            nextDiscovery = now + 500;
            EliteKeyboardDisplay? current = discover(focus.Display);
            if (current != game || gameTracker is null)
            {
                ClearGame();
                game = current;
                gameTracker = current is null ? null : createTracker(current.Display);
            }
        }
        GameWindowSnapshot snapshot = gameTracker?.GetSnapshot() ?? GameWindowSnapshot.Unavailable;
        if (!snapshot.IsAvailable)
        {
            ClearGame();
            return GameWindowSnapshot.Unavailable;
        }
        PixelRect bounds = GamescopeViewport.Project(snapshot.ClientBounds, snapshot.DisplayBounds, focus);
        bool focused =
            game is not null
            && SameDisplay(game.Display, focus.Display)
            && unchecked((nuint)snapshot.NativeHandle) == focus.Window;
        return snapshot with
        {
            ClientBounds = bounds,
            OverlayCanvasBounds = focus.OutputBounds,
            IsVisible = snapshot.IsVisible && focused && bounds.Width > 0 && bounds.Height > 0,
            IsForeground =
                focused && (focus.InputApp == focus.GraphicsApp || SameDisplay(game!.Display, focus.KeyboardDisplay)),
        };
    }

    internal static bool SameDisplay(string display, string? other) =>
        string.Equals(display.Split('.')[0], other?.Split('.')[0], StringComparison.Ordinal);

    private void ClearGame()
    {
        gameTracker?.Dispose();
        gameTracker = null;
        game = null;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        ClearGame();
        connection.Dispose();
    }
}

/// <summary>Mirrors Gamescope's nested-to-output scaling without scaling the HUD's own pixels.</summary>
internal static class GamescopeViewport
{
    internal static PixelRect Project(PixelRect source, PixelRect? nestedBounds, GamescopeFocusSnapshot focus)
    {
        PixelRect output = focus.OutputBounds;
        PixelRect nested = nestedBounds ?? output;
        if (
            source.Width <= 0
            || source.Height <= 0
            || output.Width <= 0
            || output.Height <= 0
            || nested.Width <= 0
            || nested.Height <= 0
        )
        {
            return default;
        }
        double scaleX;
        double scaleY;
        if (focus.ScalingMode == 4) // STRETCH
        {
            scaleX = (double)output.Width / source.Width;
            scaleY = (double)output.Height / source.Height;
        }
        else
        {
            double x = (double)nested.Width / source.Width;
            double y = (double)nested.Height / source.Height;
            double scale =
                (focus.ScalingMode == 3 ? Math.Max(x, y) : Math.Min(x, y))
                * Math.Min((double)output.Width / nested.Width, (double)output.Height / nested.Height);
            if (focus.ScalingMode == 1 && scale > 1) // INTEGER
            {
                scale = Math.Floor(scale);
            }
            scaleX = scaleY = scale;
        }
        int width = (int)Math.Round(source.Width * scaleX);
        int height = (int)Math.Round(source.Height * scaleY);
        return new PixelRect(
            output.X + (output.Width - width) / 2,
            output.Y + (output.Height - height) / 2,
            width,
            height
        );
    }
}
