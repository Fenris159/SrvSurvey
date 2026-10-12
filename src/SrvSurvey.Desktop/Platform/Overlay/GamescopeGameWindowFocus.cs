using System.Globalization;

namespace SrvSurvey.Desktop.Platform.Overlay;

/// <summary>Reads native Wayland activation published by the owning Gamescope process.</summary>
internal static class GamescopeGameWindowFocus
{
    internal const string MarkerPrefix = nameof(GamescopeGameWindowFocus) + ".";

    internal static bool? Read(string runtimeDirectory, int processId, ulong startTime, string procDirectory = "/proc")
    {
        string marker = Path.Combine(runtimeDirectory, MarkerPrefix + processId.ToString(CultureInfo.InvariantCulture));
        if (File.Exists(marker))
        {
            return ReadState(marker, startTime);
        }

        string? advertisedPath = ReadAdvertisedPath(procDirectory, processId);
        return advertisedPath is null ? null : Path.IsPathFullyQualified(advertisedPath) && ReadState(advertisedPath);
    }

    private static string? ReadAdvertisedPath(string procDirectory, int processId)
    {
        try
        {
            string[] environment = File.ReadAllText(
                    Path.Combine(procDirectory, processId.ToString(CultureInfo.InvariantCulture), "environ")
                )
                .Split('\0');
            string? entry =
                environment.FirstOrDefault(value =>
                    value.StartsWith("GAMESCOPE_HOST_FOCUS_FILE=", StringComparison.Ordinal)
                )
                ?? environment.FirstOrDefault(value =>
                    value.StartsWith("ELITE_HOST_FOCUS_FILE=", StringComparison.Ordinal)
                );
            return entry is null || entry.EndsWith('=') ? null : entry[(entry.IndexOf('=') + 1)..];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool ReadState(string path, ulong? startTime = null)
    {
        try
        {
            string[] lines = File.ReadAllLines(path);
            return startTime is null
                ? lines.Length == 1 && lines[0] == "1"
                : lines.Length == 2
                    && ulong.TryParse(
                        lines[0],
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out ulong recordedStart
                    )
                    && recordedStart == startTime
                    && lines[1] == "1";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A declared publisher that cannot be read must not claim desktop focus.
            return false;
        }
    }
}
