using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Platform;

public interface IEliteGameProcessDetector
{
    bool IsRunning();
}

public sealed class EliteGameProcessDetector : IEliteGameProcessDetector
{
    private const string EliteExecutableName = "EliteDangerous64.exe";
    private const string EliteLinuxTruncatedCommName = "EliteDangerous6";

    private readonly Func<bool> isLinux;
    private readonly string procDirectory;
    private readonly Func<string, bool> isNamedProcessRunning;

    public EliteGameProcessDetector()
        : this(OperatingSystem.IsLinux, "/proc", IsSystemProcessRunning) { }

    internal EliteGameProcessDetector(
        Func<bool> isLinux,
        string procDirectory,
        Func<string, bool>? isNamedProcessRunning = null
    )
    {
        this.isLinux = isLinux ?? throw new ArgumentNullException(nameof(isLinux));
        this.procDirectory = procDirectory ?? throw new ArgumentNullException(nameof(procDirectory));
        this.isNamedProcessRunning = isNamedProcessRunning ?? IsSystemProcessRunning;
    }

    public bool IsRunning()
    {
        if (isLinux())
        {
            return IsRunningOnLinux(procDirectory);
        }

        return isNamedProcessRunning(EliteGameWindowIdentity.WindowsProcessName);
    }

    internal static bool IsRunningOnLinux(string procRoot)
    {
        if (string.IsNullOrWhiteSpace(procRoot) || !Directory.Exists(procRoot))
        {
            return false;
        }

        try
        {
            return Directory
                .EnumerateDirectories(procRoot)
                .Where(entry =>
                    int.TryParse(Path.GetFileName(entry), NumberStyles.None, CultureInfo.InvariantCulture, out _)
                )
                .Any(IsLinuxEliteProcessDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal static bool ContainsEliteExecutable(string cmdline) =>
        !string.IsNullOrEmpty(cmdline)
        && cmdline.Split('\0', StringSplitOptions.RemoveEmptyEntries).Any(MatchesEliteExecutableArgument);

    private static bool IsLinuxEliteProcessDirectory(string processDirectory)
    {
        try
        {
            string comm = File.ReadAllText(Path.Combine(processDirectory, "comm")).Trim();
            if (!IsLinuxCandidateComm(comm))
            {
                return false;
            }

            string cmdline = File.ReadAllText(Path.Combine(processDirectory, "cmdline"));
            return ContainsEliteExecutable(cmdline);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsLinuxCandidateComm(string comm) =>
        string.Equals(comm, EliteGameWindowIdentity.WindowsProcessName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(comm, EliteLinuxTruncatedCommName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(comm, EliteExecutableName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(comm, "wine64-preloader", StringComparison.OrdinalIgnoreCase)
        || string.Equals(comm, "wine-preloader", StringComparison.OrdinalIgnoreCase)
        || string.Equals(comm, "wine64", StringComparison.OrdinalIgnoreCase)
        || string.Equals(comm, "wine", StringComparison.OrdinalIgnoreCase);

    private static bool MatchesEliteExecutableArgument(string argument)
    {
        string trimmed = argument.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (IsEliteExecutablePath(trimmed.Trim('"')))
        {
            return true;
        }

        // Wine and Proton often flatten the entire Windows command line (with spaces and flags) into argv[0].
        if (trimmed[0] == '"')
        {
            int closingQuote = trimmed.IndexOf('"', 1);
            return closingQuote > 1 && IsEliteExecutablePath(trimmed[1..closingQuote]);
        }

        int exeIndex = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIndex >= 0)
        {
            int end = exeIndex + 4;
            if (end == trimmed.Length || char.IsWhiteSpace(trimmed[end]))
            {
                return IsEliteExecutablePath(trimmed[..end]);
            }
        }

        return false;
    }

    private static bool IsEliteExecutablePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string fileName = Path.GetFileName(path.Replace('\\', '/'));
        return string.Equals(fileName, EliteExecutableName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, EliteGameWindowIdentity.WindowsProcessName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSystemProcessRunning(string processName)
    {
        try
        {
            Process[] processes = Process.GetProcessesByName(processName);
            try
            {
                return processes.Length > 0;
            }
            finally
            {
                foreach (Process process in processes)
                {
                    process.Dispose();
                }
            }
        }
        catch (Exception exception)
            when (exception is InvalidOperationException or NotSupportedException or Win32Exception)
        {
            return false;
        }
    }
}

public static class VisitedStarsCacheTargetLocator
{
    public static string? ResolveCurrent(string frontierId)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        return ResolveWindows(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), frontierId);
    }

    public static string? ResolveWindows(string localApplicationData, string frontierId)
    {
        if (
            string.IsNullOrWhiteSpace(localApplicationData)
            || frontierId.Length < 2
            || frontierId[0] is not ('F' or 'f')
            || !frontierId[1..].All(char.IsAsciiDigit)
        )
        {
            return null;
        }

        return Path.Combine(
            Path.GetFullPath(localApplicationData),
            "Frontier Developments",
            "Elite Dangerous",
            frontierId[1..],
            VisitedStarsCacheService.CacheFileName
        );
    }
}
