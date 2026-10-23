using System.Globalization;
using Avalonia;
using SrvSurvey.Desktop.Input;

namespace SrvSurvey.Desktop.Platform.Overlay;

internal sealed record GamescopeGameWindowBridge(int ProcessId, string Display, PixelRect HostBounds)
{
    internal const string MarkerPrefix = nameof(GamescopeGameWindowBridge) + ".";

    /// <summary>Desktop focus from the outer X11 window or its native Wayland publisher.</summary>
    internal bool? IsHostForeground { get; init; }

    internal bool HasNativeHostFocusPublisher { get; init; }

    internal ulong? ProcessStartTime { get; init; }

    /// <summary>Returns the latest validated bridge for existing overlay placement.</summary>
    public static GamescopeGameWindowBridge? TryReadCurrent()
    {
        return TryRead(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"));
    }

    /// <summary>Maps a normal Steam-launched nested compositor through its verified desktop window.</summary>
    internal static GamescopeGameWindowBridge? DiscoverCurrent()
    {
        return TryReadCurrent()
            ?? Discover(
                "/proc",
                X11GameWindowTracker.ReadDesktopWindows(),
                Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR")
            );
    }

    internal static GamescopeGameWindowBridge? Discover(
        string procDirectory,
        IReadOnlyList<GameWindowSnapshot> windows,
        string? runtimeDirectory = null,
        Func<int, ulong?>? readProcessStartTime = null
    )
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
        if (game is null || matches.Length != 1)
        {
            return null;
        }
        int processId = parent!.Value;
        readProcessStartTime ??= X11OverlayInteractionMarker.TryReadProcessStartTime;
        ulong? startTime = Path.IsPathFullyQualified(runtimeDirectory ?? string.Empty)
            ? readProcessStartTime(processId)
            : null;
        bool? nativeForeground = startTime is ulong start
            ? GamescopeGameWindowFocus.Read(runtimeDirectory!, processId, start, procDirectory)
            : null;
        if (startTime is not null && readProcessStartTime(processId) != startTime)
        {
            return null;
        }
        bool? hostForeground = matches[0].IsForeground;
        if (hostForeground == true && startTime is not null)
        {
            hostForeground = nativeForeground;
        }
        return new GamescopeGameWindowBridge(processId, game.Display, matches[0].ClientBounds)
        {
            IsHostForeground = hostForeground,
            HasNativeHostFocusPublisher = nativeForeground is not null,
            ProcessStartTime = startTime,
        };
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

            bool? hostForeground = GamescopeGameWindowFocus.Read(Path.GetDirectoryName(path)!, processId, startTime);
            return readProcessStartTime(processId) == startTime
                ? new GamescopeGameWindowBridge(processId, lines[1], new PixelRect(x, y, width, height))
                {
                    IsHostForeground = hostForeground,
                    HasNativeHostFocusPublisher = hostForeground is not null,
                    ProcessStartTime = startTime,
                }
                : null;
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
    private ulong? nestedProcessStartTime;
    private bool hasNativeHostFocusPublisher;
    private readonly HashSet<(int ProcessId, ulong StartTime)> nativeHostFocusPublishers = [];

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

        if (
            nestedTracker is null
            || nestedDisplay != bridge.Display
            || nestedProcessId != bridge.ProcessId
            || nestedProcessStartTime != bridge.ProcessStartTime
        )
        {
            ClearNestedTracker();
            nestedTracker = createNestedTracker(bridge.Display);
            nestedDisplay = bridge.Display;
            nestedProcessId = bridge.ProcessId;
            nestedProcessStartTime = bridge.ProcessStartTime;
        }

        if (bridge.ProcessStartTime is ulong startTime)
        {
            (int ProcessId, ulong StartTime) identity = (bridge.ProcessId, startTime);
            if (bridge.HasNativeHostFocusPublisher)
            {
                nativeHostFocusPublishers.Add(identity);
            }
            hasNativeHostFocusPublisher |= nativeHostFocusPublishers.Contains(identity);
        }
        hasNativeHostFocusPublisher |= bridge.HasNativeHostFocusPublisher;

        GameWindowSnapshot nestedSnapshot = nestedTracker?.GetSnapshot() ?? GameWindowSnapshot.Unavailable;
        return nestedSnapshot.IsAvailable && nestedSnapshot.IsVisible
            ? nestedSnapshot with
            {
                ClientBounds = bridge.HostBounds,
                IsForeground = nestedSnapshot.IsForeground && (bridge.IsHostForeground ?? !hasNativeHostFocusPublisher),
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
        nestedProcessStartTime = null;
        hasNativeHostFocusPublisher = false;
    }
}
