using System.Diagnostics;
using System.Globalization;

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
            RemoveStaleMarkers(runtimeDirectory);
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
        ulong startTime =
            TryReadProcessStartTime(processId)
            ?? throw new IOException($"Could not read the start time for process {processId}.");
        string path = Path.Combine(runtimeDirectory, FilePrefix + processId);
        string temporaryPath = Path.Combine(runtimeDirectory, $".{FilePrefix}{processId}.{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(temporaryPath, startTime.ToString(CultureInfo.InvariantCulture) + "\n");
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }

        return new X11OverlayInteractionMarker(path);
    }

    internal static void RemoveStaleMarkers(string runtimeDirectory)
    {
        foreach (string markerPath in Directory.EnumerateFiles(runtimeDirectory, FilePrefix + "*"))
        {
            try
            {
                string processIdText = Path.GetFileName(markerPath)[FilePrefix.Length..];
                bool validProcessId = int.TryParse(
                    processIdText,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int processId
                );
                bool validStartTime = ulong.TryParse(
                    File.ReadAllText(markerPath).Trim(),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out ulong markerStartTime
                );
                if (!validProcessId || !validStartTime || TryReadProcessStartTime(processId) != markerStartTime)
                {
                    File.Delete(markerPath);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Trace.TraceWarning(exception.ToString());
            }
        }
    }

    internal static ulong? TryReadProcessStartTime(int processId)
    {
        try
        {
            string statPath = Path.Combine("/proc", processId.ToString(CultureInfo.InvariantCulture), "stat");
            return ParseProcessStartTime(File.ReadAllText(statPath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static ulong? ParseProcessStartTime(string stat)
    {
        int commandEnd = stat.LastIndexOf(") ", StringComparison.Ordinal);
        if (commandEnd < 0)
        {
            return null;
        }

        // The remaining fields start at field 3 (state); starttime is field 22.
        string[] fields = stat[(commandEnd + 2)..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return
            fields.Length >= 20
            && ulong.TryParse(fields[19], NumberStyles.None, CultureInfo.InvariantCulture, out ulong startTime)
            ? startTime
            : null;
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
