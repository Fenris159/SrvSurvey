using System.Globalization;
using Avalonia;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Input;

/// <summary>A local X display belonging to a running Elite process.</summary>
internal sealed record EliteKeyboardDisplay(int ProcessId, string Display);

/// <summary>Supplies desktop geometry only when available, with the overlay monitor as a fallback preference.</summary>
internal sealed record KeyboardDisplayContext(
    PixelRect? OverlayMonitor,
    IReadOnlyList<GameWindowSnapshot> Windows,
    IReadOnlyList<GamescopeGameWindowBridge> Bridges
);

/// <summary>Finds Elite's display from same-user process metadata without scanning arbitrary X servers.</summary>
internal static class EliteKeyboardDisplayDiscovery
{
    /// <summary>Discovers the current game's local display, tolerating process exit and restricted procfs.</summary>
    public static EliteKeyboardDisplay? ReadCurrent() => ReadCurrent(null);

    /// <summary>Prefers the focused game and then a verified desktop or bridge mapping on the overlay monitor.</summary>
    public static EliteKeyboardDisplay? ReadCurrent(PixelRect? overlayMonitor) =>
        Read(
            "/proc",
            context: new KeyboardDisplayContext(
                overlayMonitor,
                X11GameWindowTracker.ReadDesktopWindows(),
                GamescopeGameWindowBridge.ReadAll(
                    Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"),
                    X11OverlayInteractionMarker.TryReadProcessStartTime
                )
            )
        );

    /// <summary>Checks native Wayland and X11 Elite processes even when they have no accessible DISPLAY.</summary>
    public static bool IsGameRunning() => Read("/proc", requireDisplay: false) is not null;

    /// <summary>Reads only Elite processes owned by the current user and accepts local display names.</summary>
    internal static EliteKeyboardDisplay? Read(
        string procDirectory,
        bool requireDisplay = true,
        KeyboardDisplayContext? context = null
    )
    {
        try
        {
            string? owner = ReadOwner(Path.Combine(procDirectory, "self"));
            if (owner is null)
            {
                return null;
            }
            var candidates = new List<EliteKeyboardDisplay>();
            foreach (string directory in Directory.EnumerateDirectories(procDirectory).Order(StringComparer.Ordinal))
            {
                if (
                    int.TryParse(
                        Path.GetFileName(directory),
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out int pid
                    )
                    && pid > 0
                    && ReadOwner(directory) == owner
                )
                {
                    EliteKeyboardDisplay? display = ReadGame(directory, pid, requireDisplay);
                    if (display is not null)
                    {
                        if (!requireDisplay)
                        {
                            return display;
                        }
                        candidates.Add(display);
                    }
                }
            }
            return SelectDisplay(candidates, procDirectory, context);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Process namespaces and access restrictions can make automatic discovery unavailable.
        }
        return null;
    }

    /// <summary>Uses focus first and monitor overlap second, refusing to guess between ambiguous clients.</summary>
    private static EliteKeyboardDisplay? SelectDisplay(
        List<EliteKeyboardDisplay> candidates,
        string procDirectory,
        KeyboardDisplayContext? context
    )
    {
        var mapped = candidates
            .Select(candidate =>
            {
                int? parent =
                    context?.Windows.Count > 0 ? FindGamescopeParent(procDirectory, candidate.ProcessId) : null;
                return new
                {
                    Candidate = candidate,
                    Windows = context
                        ?.Windows.Where(window => IsMatchingWindow(window, candidate.ProcessId, parent))
                        .ToArray()
                        ?? [],
                    Bounds = context
                        ?.Bridges.FirstOrDefault(bridge =>
                            NormalizeDisplay(bridge.Display) == NormalizeDisplay(candidate.Display)
                        )
                        ?.HostBounds,
                };
            })
            .ToArray();
        EliteKeyboardDisplay[] focused = mapped
            .Where(item => item.Windows.Any(window => window.IsForeground))
            .Select(item => item.Candidate)
            .ToArray();
        if (focused.Length == 1)
        {
            return focused[0];
        }
        if (context?.OverlayMonitor is PixelRect monitor)
        {
            var matches = mapped
                .Select(item => new
                {
                    item.Candidate,
                    Area = item
                        .Windows.Select(window => OverlapArea(window.ClientBounds, monitor))
                        .Append(item.Bounds is PixelRect bounds ? OverlapArea(bounds, monitor) : 0)
                        .Max(),
                })
                .Where(item => item.Area > 0)
                .OrderByDescending(item => item.Area)
                .ToArray();
            if (matches.Length == 1 || (matches.Length > 1 && matches[0].Area > matches[1].Area))
            {
                return matches[0].Candidate;
            }
        }
        return
            candidates.Count > 0
            && candidates
                .Select(candidate => NormalizeDisplay(candidate.Display))
                .Distinct(StringComparer.Ordinal)
                .Count() == 1
            ? candidates[0]
            : null;
    }

    /// <summary>Accepts visible desktop geometry belonging to Elite or its verified gamescope ancestor.</summary>
    private static bool IsMatchingWindow(GameWindowSnapshot window, int processId, int? parent) =>
        window.IsAvailable
        && window.IsVisible
        && (window.ProcessId == processId || (parent is not null && window.ProcessId == parent));

    /// <summary>Recognizes the default X11 screen suffix as the same input server.</summary>
    private static string NormalizeDisplay(string display) =>
        display.EndsWith(".0", StringComparison.Ordinal) ? display[..^2] : display;

    /// <summary>Matches only a gamescope ancestor, avoiding unrelated launchers shared by multiple game processes.</summary>
    private static int? FindGamescopeParent(string procDirectory, int processId)
    {
        var visited = new HashSet<int>();
        try
        {
            while (processId > 1 && visited.Add(processId))
            {
                string directory = Path.Combine(procDirectory, processId.ToString(CultureInfo.InvariantCulture));
                if (File.ReadAllText(Path.Combine(directory, "comm")).Trim() == "gamescope")
                {
                    return processId;
                }
                string? parent = File.ReadLines(Path.Combine(directory, "status"))
                    .FirstOrDefault(line => line.StartsWith("PPid:", StringComparison.Ordinal))
                    ?[5..].Trim();
                if (!int.TryParse(parent, NumberStyles.None, CultureInfo.InvariantCulture, out processId))
                {
                    break;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Restricted process trees cannot establish a desktop mapping.
        }
        return null;
    }

    /// <summary>Compares desktop rectangles using wide arithmetic for multi-monitor offsets.</summary>
    private static long OverlapArea(PixelRect bounds, PixelRect monitor) =>
        Math.Max(0L, Math.Min((long)bounds.Right, monitor.Right) - Math.Max(bounds.X, monitor.X))
        * Math.Max(0L, Math.Min((long)bounds.Bottom, monitor.Bottom) - Math.Max(bounds.Y, monitor.Y));

    /// <summary>Checks the executable identity before reading the game's DISPLAY environment value.</summary>
    private static EliteKeyboardDisplay? ReadGame(string directory, int pid, bool requireDisplay)
    {
        try
        {
            string name = File.ReadAllText(Path.Combine(directory, "comm")).Trim();
            if (name is not ("EliteDangerous64" or "EliteDangerous6" or "EliteDangerous64.exe"))
            {
                return null;
            }
            bool eliteExecutable = File.ReadAllText(Path.Combine(directory, "cmdline"))
                .Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .Any(argument =>
                    Path.GetFileName(argument.Replace('\\', '/')) is "EliteDangerous64.exe" or "EliteDangerous64"
                );
            if (!eliteExecutable)
            {
                return null;
            }
            if (!requireDisplay)
            {
                return new EliteKeyboardDisplay(pid, string.Empty);
            }
            string? display = File.ReadAllText(Path.Combine(directory, "environ"))
                .Split('\0')
                .FirstOrDefault(value => value.StartsWith("DISPLAY=", StringComparison.Ordinal))
                ?[8..];
            if (IsLocalDisplay(display))
            {
                return new EliteKeyboardDisplay(pid, display!);
            }
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Reads the real user ID rather than trusting environment variables supplied by a process.</summary>
    private static string? ReadOwner(string directory)
    {
        try
        {
            return File.ReadLines(Path.Combine(directory, "status"))
                .FirstOrDefault(line => line.StartsWith("Uid:", StringComparison.Ordinal))
                ?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .ElementAtOrDefault(1);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Rejects remote or malformed display names before any native connection is attempted.</summary>
    internal static bool IsLocalDisplay(string? display)
    {
        if (string.IsNullOrEmpty(display) || display[0] != ':')
        {
            return false;
        }
        string[] parts = display[1..].Split('.');
        return parts.Length is 1 or 2 && parts.All(part => part.Length > 0 && part.All(char.IsAsciiDigit));
    }
}
