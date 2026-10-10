using System.Globalization;
using Avalonia;
using SrvSurvey.Desktop.Input;

namespace SrvSurvey.Desktop.Platform.Overlay;

internal sealed record GamescopeGameWindowBridge(int ProcessId, string Display, PixelRect HostBounds)
{
    internal const string MarkerPrefix = nameof(GamescopeGameWindowBridge) + ".";

    /// <summary>Returns the latest validated bridge for existing overlay placement.</summary>
    public static GamescopeGameWindowBridge? TryReadCurrent()
    {
        return TryRead(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"));
    }

    /// <summary>Maps a normal Steam-launched nested compositor through its verified desktop window.</summary>
    internal static GamescopeGameWindowBridge? DiscoverCurrent()
    {
        return TryReadCurrent() ?? Discover("/proc", X11GameWindowTracker.ReadDesktopWindows());
    }

    internal static GamescopeGameWindowBridge? Discover(string procDirectory, IReadOnlyList<GameWindowSnapshot> windows)
    {
        EliteKeyboardDisplay? game = EliteKeyboardDisplayDiscovery.Read(
            procDirectory,
            context: new KeyboardDisplayContext(null, windows, [])
        );
        int? parent = game is null
            ? null
            : EliteKeyboardDisplayDiscovery.FindGamescopeParent(procDirectory, game.ProcessId);
        GameWindowSnapshot[] matches = windows
            .Where(window => window.ProcessId == parent && parent is not null && window.IsAvailable && window.IsVisible)
            .ToArray();
        return game is not null && matches.Length == 1
            ? new GamescopeGameWindowBridge(parent!.Value, game.Display, matches[0].ClientBounds)
            : null;
    }

    internal static GamescopeGameWindowBridge? TryRead(string? runtimeDirectory)
    {
        return TryRead(runtimeDirectory, X11OverlayInteractionMarker.TryReadProcessStartTime);
    }

    /// <summary>Returns the newest validated bridge while retaining the existing single-bridge behavior.</summary>
    internal static GamescopeGameWindowBridge? TryRead(string? runtimeDirectory, Func<int, ulong?> readProcessStartTime)
    {
        IReadOnlyList<GamescopeGameWindowBridge> bridges = ReadAll(runtimeDirectory, readProcessStartTime);
        return bridges.Count > 0 ? bridges[0] : null;
    }

    /// <summary>Returns all live bridges so keyboard discovery can use the configured overlay monitor.</summary>
    internal static IReadOnlyList<GamescopeGameWindowBridge> ReadAll(
        string? runtimeDirectory,
        Func<int, ulong?> readProcessStartTime
    )
    {
        if (string.IsNullOrWhiteSpace(runtimeDirectory) || !Directory.Exists(runtimeDirectory))
        {
            return [];
        }

        try
        {
            return Directory
                .EnumerateFiles(runtimeDirectory, MarkerPrefix + "*")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Select(path => TryReadMarker(path, readProcessStartTime))
                .OfType<GamescopeGameWindowBridge>()
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static GamescopeGameWindowBridge? TryReadMarker(string path, Func<int, ulong?> readProcessStartTime)
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
                || readProcessStartTime(processId) != startTime
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
