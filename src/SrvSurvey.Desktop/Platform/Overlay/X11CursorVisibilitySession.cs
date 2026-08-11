namespace SrvSurvey.Desktop.Platform.Overlay;

internal sealed class X11CursorVisibilitySession : IDisposable
{
    private readonly HashSet<nuint> interactionWindows;
    private readonly nuint cursor;
    private readonly nuint previousActiveWindow;
    private readonly X11CursorSessionOperations operations;
    private int disposed;

    public X11CursorVisibilitySession(
        IEnumerable<nuint> interactionWindows,
        nuint cursor,
        nuint previousActiveWindow,
        X11CursorSessionOperations operations)
    {
        ArgumentNullException.ThrowIfNull(interactionWindows);
        this.interactionWindows = interactionWindows.ToHashSet();
        this.cursor = cursor;
        this.previousActiveWindow = previousActiveWindow;
        this.operations = operations
            ?? throw new ArgumentNullException(nameof(operations));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        if (cursor != 0)
        {
            foreach (var window in interactionWindows)
            {
                _ = operations.UndefineCursor(window);
            }

            _ = operations.FreeCursor(cursor);
        }

        if (previousActiveWindow != 0
            && !interactionWindows.Contains(previousActiveWindow)
            && (interactionWindows.Contains(operations.GetActiveWindow())
                || interactionWindows.Contains(operations.GetFocusWindow())))
        {
            _ = operations.ActivateWindow(previousActiveWindow);
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
        Func<nuint, int> freeCursor)
    {
        GetActiveWindow = getActiveWindow
            ?? throw new ArgumentNullException(nameof(getActiveWindow));
        GetFocusWindow = getFocusWindow
            ?? throw new ArgumentNullException(nameof(getFocusWindow));
        ActivateWindow = activateWindow
            ?? throw new ArgumentNullException(nameof(activateWindow));
        UndefineCursor = undefineCursor
            ?? throw new ArgumentNullException(nameof(undefineCursor));
        FreeCursor = freeCursor
            ?? throw new ArgumentNullException(nameof(freeCursor));
    }

    public Func<nuint> GetActiveWindow { get; }

    public Func<nuint> GetFocusWindow { get; }

    public Func<nuint, bool> ActivateWindow { get; }

    public Func<nuint, int> UndefineCursor { get; }

    public Func<nuint, int> FreeCursor { get; }
}
