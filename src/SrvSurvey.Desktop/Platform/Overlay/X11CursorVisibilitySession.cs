using System.Diagnostics;

namespace SrvSurvey.Desktop.Platform.Overlay;

internal sealed class X11CursorVisibilitySession : IDisposable
{
    private readonly HashSet<nuint> interactionWindows;
    private readonly nuint cursor;
    private readonly nuint previousActiveWindow;
    private readonly X11CursorSessionOperations operations;
    private readonly IDisposable? interactionMarker;
    private int disposed;

    public X11CursorVisibilitySession(
        IEnumerable<nuint> interactionWindows,
        nuint cursor,
        nuint previousActiveWindow,
        X11CursorSessionOperations operations,
        IDisposable? interactionMarker = null
    )
    {
        ArgumentNullException.ThrowIfNull(interactionWindows);
        this.interactionWindows = interactionWindows.ToHashSet();
        this.cursor = cursor;
        this.previousActiveWindow = previousActiveWindow;
        this.operations = operations ?? throw new ArgumentNullException(nameof(operations));
        this.interactionMarker = interactionMarker;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        try
        {
            if (cursor != 0)
            {
                foreach (nuint window in interactionWindows)
                {
                    _ = operations.UndefineCursor(window);
                }

                _ = operations.FreeCursor(cursor);
            }

            if (
                previousActiveWindow != 0
                && !interactionWindows.Contains(previousActiveWindow)
                && (
                    interactionWindows.Contains(operations.GetActiveWindow())
                    || interactionWindows.Contains(operations.GetFocusWindow())
                )
            )
            {
                _ = operations.ActivateWindow(previousActiveWindow);
            }
        }
        finally
        {
            interactionMarker?.Dispose();
        }
    }
}

internal sealed class X11OverlayInteractionMarker : IDisposable
{
    private const string FilePrefix = nameof(X11OverlayInteractionMarker) + ".";
    private readonly string path;
    private int disposed;

    private X11OverlayInteractionMarker(string path)
    {
        this.path = path;
    }

    public static X11OverlayInteractionMarker? TryBeginCurrent()
    {
        string? runtimeDirectory = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (string.IsNullOrWhiteSpace(runtimeDirectory) || !Directory.Exists(runtimeDirectory))
        {
            return null;
        }

        try
        {
            return Begin(runtimeDirectory, Environment.ProcessId);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning(exception.ToString());
            return null;
        }
    }

    internal static X11OverlayInteractionMarker Begin(string runtimeDirectory, int processId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeDirectory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processId);
        string path = Path.Combine(runtimeDirectory, FilePrefix + processId);
        File.WriteAllText(path, string.Empty);
        return new X11OverlayInteractionMarker(path);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning(exception.ToString());
        }
    }
}

internal sealed class X11CursorSessionOperations
{
    public X11CursorSessionOperations(
        Func<nuint> getActiveWindow,
        Func<nuint> getFocusWindow,
        Func<nuint, bool> activateWindow,
        Func<nuint, int> undefineCursor,
        Func<nuint, int> freeCursor
    )
    {
        GetActiveWindow = getActiveWindow ?? throw new ArgumentNullException(nameof(getActiveWindow));
        GetFocusWindow = getFocusWindow ?? throw new ArgumentNullException(nameof(getFocusWindow));
        ActivateWindow = activateWindow ?? throw new ArgumentNullException(nameof(activateWindow));
        UndefineCursor = undefineCursor ?? throw new ArgumentNullException(nameof(undefineCursor));
        FreeCursor = freeCursor ?? throw new ArgumentNullException(nameof(freeCursor));
    }

    public Func<nuint> GetActiveWindow { get; }

    public Func<nuint> GetFocusWindow { get; }

    public Func<nuint, bool> ActivateWindow { get; }

    public Func<nuint, int> UndefineCursor { get; }

    public Func<nuint, int> FreeCursor { get; }
}
