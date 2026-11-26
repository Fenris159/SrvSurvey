using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Platform;

public interface IEliteGameProcessDetector
{
    bool IsRunning();

    /// <summary>Returns the number of running Elite Dangerous game processes, or null when process enumeration is unavailable.</summary>
    int? TryGetRunningProcessCount() => IsRunning() ? 1 : 0;
}

public sealed class EliteGameProcessDetector : IEliteGameProcessDetector
{
    private const int LinuxTaskCommMaxLength = 15;
    private const string EliteExecutableName = "EliteDangerous64.exe";
    private const string EliteLinuxTruncatedCommName = "EliteDangerous6";
    private const string Wine64PreloaderName = "wine64-preloader";
    private static readonly TimeSpan DefaultCacheDuration = TimeSpan.FromSeconds(1);

    private readonly Func<bool> isLinux;
    private readonly string procDirectory;
    private readonly Func<string, bool> isNamedProcessRunning;
    private readonly Func<string, int?> getNamedProcessCount;
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan cacheDuration;
    private readonly Lock cacheLock = new();
    private bool hasCachedResult;
    private bool cachedResult;
    private long lastCheckedTimestamp;
    private string? lastDetectedPid;

    public EliteGameProcessDetector()
        : this(OperatingSystem.IsLinux, "/proc", IsSystemProcessRunning, GetSystemProcessCount) { }

    internal EliteGameProcessDetector(
        Func<bool> isLinux,
        string procDirectory,
        Func<string, bool>? isNamedProcessRunning = null,
        TimeProvider? timeProvider = null,
        TimeSpan? cacheDuration = null
    )
        : this(
            isLinux,
            procDirectory,
            isNamedProcessRunning ?? IsSystemProcessRunning,
            isNamedProcessRunning is null ? GetSystemProcessCount : name => isNamedProcessRunning(name) ? 1 : 0,
            timeProvider,
            cacheDuration
        ) { }

    internal EliteGameProcessDetector(
        Func<bool> isLinux,
        string procDirectory,
        Func<string, bool> isNamedProcessRunning,
        Func<string, int?> getNamedProcessCount,
        TimeProvider? timeProvider = null,
        TimeSpan? cacheDuration = null
    )
    {
        this.isLinux = isLinux ?? throw new ArgumentNullException(nameof(isLinux));
        this.procDirectory = procDirectory ?? throw new ArgumentNullException(nameof(procDirectory));
        this.isNamedProcessRunning =
            isNamedProcessRunning ?? throw new ArgumentNullException(nameof(isNamedProcessRunning));
        this.getNamedProcessCount =
            getNamedProcessCount ?? throw new ArgumentNullException(nameof(getNamedProcessCount));
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.cacheDuration = cacheDuration ?? DefaultCacheDuration;
    }

    public int? TryGetRunningProcessCount()
    {
        if (!isLinux())
        {
            return getNamedProcessCount(EliteGameWindowIdentity.WindowsProcessName);
        }

        return CountLinuxEliteProcesses(procDirectory);
    }

    public bool IsRunning()
    {
        lock (cacheLock)
        {
            long now = timeProvider.GetTimestamp();
            if (
                hasCachedResult
                && cacheDuration > TimeSpan.Zero
                && timeProvider.GetElapsedTime(lastCheckedTimestamp, now) < cacheDuration
            )
            {
                return cachedResult;
            }

            cachedResult = EvaluateRunning();
            hasCachedResult = true;
            lastCheckedTimestamp = now;
            return cachedResult;
        }
    }

    private bool EvaluateRunning()
    {
        if (!isLinux())
        {
            return isNamedProcessRunning(EliteGameWindowIdentity.WindowsProcessName);
        }

        if (lastDetectedPid is { } pid && IsLinuxEliteProcessDirectory(Path.Combine(procDirectory, pid)))
        {
            return true;
        }

        lastDetectedPid = FindLinuxEliteProcessId(procDirectory);
        return lastDetectedPid is not null;
    }

    internal static bool IsRunningOnLinux(string procRoot) => FindLinuxEliteProcessId(procRoot) is not null;

    private static string? FindLinuxEliteProcessId(string procRoot)
    {
        if (string.IsNullOrWhiteSpace(procRoot) || !Directory.Exists(procRoot))
        {
            return null;
        }

        try
        {
            foreach (string entry in Directory.EnumerateDirectories(procRoot))
            {
                string pid = Path.GetFileName(entry);
                if (
                    int.TryParse(pid, NumberStyles.None, CultureInfo.InvariantCulture, out _)
                    && IsLinuxEliteProcessDirectory(entry)
                )
                {
                    return pid;
                }
            }

            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static int? CountLinuxEliteProcesses(string procRoot)
    {
        if (string.IsNullOrWhiteSpace(procRoot) || !Directory.Exists(procRoot))
        {
            return null;
        }

        try
        {
            int count = 0;
            foreach (string entry in Directory.EnumerateDirectories(procRoot))
            {
                string pid = Path.GetFileName(entry);
                if (
                    int.TryParse(pid, NumberStyles.None, CultureInfo.InvariantCulture, out _)
                    && IsLinuxEliteProcessDirectory(entry)
                )
                {
                    count++;
                }
            }

            return count;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
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
        || comm.AsSpan()
            .Equals(Wine64PreloaderName.AsSpan(0, LinuxTaskCommMaxLength), StringComparison.OrdinalIgnoreCase)
        || string.Equals(comm, Wine64PreloaderName, StringComparison.OrdinalIgnoreCase)
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

    private static bool IsSystemProcessRunning(string processName) => GetSystemProcessCount(processName) is > 0;

    private static int? GetSystemProcessCount(string processName)
    {
        try
        {
            Process[] processes = Process.GetProcessesByName(processName);
            try
            {
                return processes.Length;
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
            return null;
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
