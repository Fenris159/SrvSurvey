using System.Globalization;
using Avalonia;

namespace SrvSurvey.Desktop.Platform.Overlay;

internal sealed record GamescopeGameWindowBridge(int ProcessId, string Display, PixelRect HostBounds)
{
    internal const string MarkerPrefix = nameof(GamescopeGameWindowBridge) + ".";

    public static GamescopeGameWindowBridge? TryReadCurrent()
    {
        return TryRead(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"));
    }

    internal static GamescopeGameWindowBridge? TryRead(string? runtimeDirectory)
    {
        if (string.IsNullOrWhiteSpace(runtimeDirectory) || !Directory.Exists(runtimeDirectory))
        {
            return null;
        }

        try
        {
            return Directory
                .EnumerateFiles(runtimeDirectory, MarkerPrefix + "*")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Select(TryReadMarker)
                .FirstOrDefault(marker => marker is not null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static GamescopeGameWindowBridge? TryReadMarker(string path)
    {
        try
        {
            string processIdText = Path.GetFileName(path)[MarkerPrefix.Length..];
            if (
                !int.TryParse(processIdText, NumberStyles.None, CultureInfo.InvariantCulture, out int processId)
                || processId <= 0
            )
            {
                return null;
            }

            string[] lines = File.ReadAllLines(path);
            if (
                lines.Length != 3
                || !ulong.TryParse(lines[0], NumberStyles.None, CultureInfo.InvariantCulture, out ulong startTime)
                || X11OverlayInteractionMarker.TryReadProcessStartTime(processId) != startTime
                || lines[1].Length < 2
                || lines[1][0] != ':'
            )
            {
                return null;
            }

            string[] parts = lines[2].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (
                parts.Length != 4
                || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)
                || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y)
                || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int width)
                || !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int height)
                || width <= 0
                || height <= 0
            )
            {
                return null;
            }

            return new GamescopeGameWindowBridge(processId, lines[1], new PixelRect(x, y, width, height));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

internal sealed class GamescopeGameWindowTracker : IGameWindowTracker
{
    private readonly IGameWindowTracker hostTracker;
    private readonly Func<GamescopeGameWindowBridge?> readBridge;
    private readonly Func<string, IGameWindowTracker?> createNestedTracker;
    private IGameWindowTracker? nestedTracker;
    private string? nestedDisplay;
    private int nestedProcessId;

    public GamescopeGameWindowTracker(
        IGameWindowTracker hostTracker,
        Func<GamescopeGameWindowBridge?> readBridge,
        Func<string, IGameWindowTracker?> createNestedTracker
    )
    {
        this.hostTracker = hostTracker ?? throw new ArgumentNullException(nameof(hostTracker));
        this.readBridge = readBridge ?? throw new ArgumentNullException(nameof(readBridge));
        this.createNestedTracker = createNestedTracker ?? throw new ArgumentNullException(nameof(createNestedTracker));
    }

    public GameWindowSnapshot GetSnapshot()
    {
        GameWindowSnapshot hostSnapshot = hostTracker.GetSnapshot();
        if (hostSnapshot.IsAvailable)
        {
            return hostSnapshot;
        }

        GamescopeGameWindowBridge? bridge = readBridge();
        if (bridge is null)
        {
            ClearNestedTracker();
            return hostSnapshot;
        }

        if (nestedTracker is null || nestedDisplay != bridge.Display || nestedProcessId != bridge.ProcessId)
        {
            ClearNestedTracker();
            nestedTracker = createNestedTracker(bridge.Display);
            nestedDisplay = bridge.Display;
            nestedProcessId = bridge.ProcessId;
        }

        GameWindowSnapshot nestedSnapshot = nestedTracker?.GetSnapshot() ?? GameWindowSnapshot.Unavailable;
        return nestedSnapshot.IsAvailable && nestedSnapshot.IsVisible
            ? nestedSnapshot with
            {
                ClientBounds = bridge.HostBounds,
            }
            : hostSnapshot;
    }

    public void Dispose()
    {
        ClearNestedTracker();
        hostTracker.Dispose();
    }

    private void ClearNestedTracker()
    {
        nestedTracker?.Dispose();
        nestedTracker = null;
        nestedDisplay = null;
        nestedProcessId = 0;
    }
}
