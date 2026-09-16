using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SrvSurvey.Desktop.Platform;

public sealed record ApplicationInstanceScan(int ConfirmedCount, int UnverifiedCount)
{
    public int TotalCount => checked(ConfirmedCount + UnverifiedCount);
}

public interface IApplicationInstanceManager
{
    Task<ApplicationInstanceScan> ScanOtherInstancesAsync(CancellationToken cancellationToken = default);

    Task<int> CountOtherInstancesAsync(CancellationToken cancellationToken = default);

    Task CloseOtherInstancesAsync(CancellationToken cancellationToken = default);
}

internal interface IApplicationInstanceProcessSource
{
    ApplicationInstanceDiscovery DiscoverOtherInstances();
}

internal sealed record ApplicationInstanceDiscovery(
    IReadOnlyList<IApplicationInstanceProcess> Confirmed,
    int UnverifiedCount
) : IDisposable
{
    public void Dispose()
    {
        foreach (IApplicationInstanceProcess process in Confirmed)
        {
            process.Dispose();
        }
    }
}

internal interface IApplicationInstanceProcess : IDisposable
{
    int Id { get; }

    bool HasExited { get; }

    Task<bool> RequestGracefulExitAsync(CancellationToken cancellationToken);

    void ForceTerminate();

    Task WaitForExitAsync(CancellationToken cancellationToken);
}

internal sealed class ApplicationInstanceManager : IApplicationInstanceManager, IAsyncDisposable
{
    private static readonly TimeSpan DefaultGracefulExitTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultForcedExitTimeout = TimeSpan.FromSeconds(5);

    private readonly IApplicationInstanceProcessSource processSource;
    private readonly TimeSpan gracefulExitTimeout;
    private readonly TimeSpan forcedExitTimeout;

    public ApplicationInstanceManager()
        : this(new SystemApplicationInstanceProcessSource(), DefaultGracefulExitTimeout, DefaultForcedExitTimeout) { }

    public ApplicationInstanceManager(string dataDirectory, Func<Task> requestShutdown, Action<string>? log = null)
        : this(
            new SystemApplicationInstanceProcessSource(dataDirectory, requestShutdown, log),
            DefaultGracefulExitTimeout,
            DefaultForcedExitTimeout
        ) { }

    internal ApplicationInstanceManager(
        IApplicationInstanceProcessSource processSource,
        TimeSpan gracefulExitTimeout,
        TimeSpan forcedExitTimeout
    )
    {
        this.processSource = processSource ?? throw new ArgumentNullException(nameof(processSource));
        ArgumentOutOfRangeException.ThrowIfLessThan(gracefulExitTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(forcedExitTimeout, TimeSpan.Zero);

        this.gracefulExitTimeout = gracefulExitTimeout;
        this.forcedExitTimeout = forcedExitTimeout;
    }

    public async Task<ApplicationInstanceScan> ScanOtherInstancesAsync(CancellationToken cancellationToken = default)
    {
        using ApplicationInstanceDiscovery discovery = await DiscoverOtherInstancesAsync(cancellationToken)
            .ConfigureAwait(false);
        return new ApplicationInstanceScan(discovery.Confirmed.Count, discovery.UnverifiedCount);
    }

    public async Task<int> CountOtherInstancesAsync(CancellationToken cancellationToken = default)
    {
        ApplicationInstanceScan scan = await ScanOtherInstancesAsync(cancellationToken).ConfigureAwait(false);
        return scan.TotalCount;
    }

    public async Task CloseOtherInstancesAsync(CancellationToken cancellationToken = default)
    {
        using (
            ApplicationInstanceDiscovery discovery = await DiscoverOtherInstancesAsync(cancellationToken)
                .ConfigureAwait(false)
        )
        {
            HashSet<int> graceful = await RequestGracefulExitAsync(discovery.Confirmed, cancellationToken)
                .ConfigureAwait(false);
            ForceTerminate(discovery.Confirmed.Where(instance => !graceful.Contains(instance.Id)));
            await WaitForExitAsync(discovery.Confirmed, gracefulExitTimeout, cancellationToken).ConfigureAwait(false);

            ForceTerminate(discovery.Confirmed.Where(instance => !instance.HasExited));
            await WaitForExitAsync(discovery.Confirmed, forcedExitTimeout, cancellationToken).ConfigureAwait(false);
        }

        using ApplicationInstanceDiscovery verification = await DiscoverOtherInstancesAsync(cancellationToken)
            .ConfigureAwait(false);
        int remaining = verification.Confirmed.Count(instance => !instance.HasExited);
        if (verification.UnverifiedCount > 0)
        {
            throw new IOException(
                $"Windows or Linux prevented SrvSurvey from verifying "
                    + $"{verification.UnverifiedCount:N0} matching process(es). "
                    + "Close every SrvSurvey-XP instance manually, then try again."
            );
        }

        if (remaining > 0)
        {
            throw new IOException($"Could not close {remaining:N0} other SrvSurvey instance(s).");
        }
    }

    private Task<ApplicationInstanceDiscovery> DiscoverOtherInstancesAsync(CancellationToken cancellationToken)
    {
        return Task.Run(processSource.DiscoverOtherInstances, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (processSource is IAsyncDisposable disposable)
        {
            await disposable.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task<HashSet<int>> RequestGracefulExitAsync(
        IEnumerable<IApplicationInstanceProcess> instances,
        CancellationToken cancellationToken
    )
    {
        var requested = new HashSet<int>();
        foreach (IApplicationInstanceProcess? instance in instances.Where(instance => !instance.HasExited))
        {
            if (await instance.RequestGracefulExitAsync(cancellationToken).ConfigureAwait(false))
            {
                requested.Add(instance.Id);
            }
        }

        return requested;
    }

    private static void ForceTerminate(IEnumerable<IApplicationInstanceProcess> instances)
    {
        foreach (IApplicationInstanceProcess instance in instances)
        {
            instance.ForceTerminate();
        }
    }

    private static async Task WaitForExitAsync(
        IReadOnlyCollection<IApplicationInstanceProcess> instances,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        IApplicationInstanceProcess[] active = instances.Where(instance => !instance.HasExited).ToArray();
        if (active.Length == 0)
        {
            return;
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await Task.WhenAll(active.Select(instance => instance.WaitForExitAsync(timeoutSource.Token)))
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The caller performs a forced termination after the grace period.
        }
    }
}

internal sealed class SystemApplicationInstanceProcessSource : IApplicationInstanceProcessSource, IAsyncDisposable
{
    private readonly ApplicationInstanceRegistry? registry;
    private readonly Action<string>? log;

    public SystemApplicationInstanceProcessSource() { }

    public SystemApplicationInstanceProcessSource(string dataDirectory, Func<Task> requestShutdown, Action<string>? log)
    {
        this.log = log;
        try
        {
            registry = new ApplicationInstanceRegistry(dataDirectory, requestShutdown, log);
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or InvalidOperationException
                        or NotSupportedException
            )
        {
            try
            {
                log?.Invoke(
                    "Update instance registration is unavailable; "
                        + "continuing with process-name and executable-path discovery. "
                        + exception.Message
                );
            }
            catch (Exception logException) when (logException is IOException or InvalidOperationException)
            {
                // Registry failure must not prevent application startup.
            }
        }
    }

    public ApplicationInstanceDiscovery DiscoverOtherInstances()
    {
        using var current = Process.GetCurrentProcess();
        string? currentPath = ResolveCurrentPath();
        string? currentApplicationIdentity = ResolveCurrentApplicationIdentity();
        IReadOnlySet<int> restartManagerProcessIds = FindRestartManagerProcesses(currentPath);
        IReadOnlyList<ApplicationInstanceRecord> records = registry?.ReadOtherRecords() ?? [];
        var recordsByProcess = records
            .GroupBy(record => record.ProcessId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        Dictionary<int, Process> processes = CollectCandidateProcesses(current, records, restartManagerProcessIds);

        var confirmed = new List<IApplicationInstanceProcess>();
        int unverified = 0;
        try
        {
            foreach (int processId in processes.Keys.ToArray())
            {
                Process process = processes[processId];
                ApplicationInstanceRecord[] processRecords = recordsByProcess.GetValueOrDefault(process.Id) ?? [];
                ProcessClassification classification = ClassifyProcess(
                    process,
                    currentPath,
                    current.ProcessName,
                    currentApplicationIdentity,
                    processRecords,
                    restartManagerProcessIds.Contains(process.Id)
                );
                if (classification.IsConfirmed)
                {
                    confirmed.Add(new SystemApplicationInstanceProcess(process, classification.Record?.PipeName));
                    processes.Remove(processId);
                    log?.Invoke($"Confirmed update instance PID {process.Id} using " + $"{classification.Method}.");
                    continue;
                }

                if (classification.IsUnverified)
                {
                    unverified++;
                    log?.Invoke($"Could not verify update candidate PID {process.Id}: " + classification.Error);
                }
                else
                {
                    log?.Invoke(
                        $"Ignored unrelated process PID {process.Id} at "
                            + $"'{classification.Path ?? classification.Record?.ExecutablePath ?? "unknown"}'."
                    );
                }

                process.Dispose();
                processes.Remove(processId);
            }

            return new ApplicationInstanceDiscovery(confirmed, unverified);
        }
        catch
        {
            foreach (IApplicationInstanceProcess process in confirmed)
            {
                process.Dispose();
            }

            foreach (Process process in processes.Values)
            {
                process.Dispose();
            }

            throw;
        }
    }

    private static string? ResolveCurrentPath()
    {
        // AppImage sets APPIMAGE to the stable image path; ProcessPath points at the temporary mount.
        string? processPath = Environment.GetEnvironmentVariable("APPIMAGE");
        if (string.IsNullOrWhiteSpace(processPath))
        {
            processPath = Environment.ProcessPath;
        }

        return string.IsNullOrWhiteSpace(processPath) ? null : ApplicationProcessPathResolver.Canonicalize(processPath);
    }

    private IReadOnlySet<int> FindRestartManagerProcesses(string? currentPath)
    {
        return OperatingSystem.IsWindows() && currentPath is not null
            ? WindowsRestartManagerProcessFinder.FindLockingProcessIds(currentPath, log)
            : new HashSet<int>();
    }

    private Dictionary<int, Process> CollectCandidateProcesses(
        Process current,
        IReadOnlyList<ApplicationInstanceRecord> records,
        IReadOnlySet<int> restartManagerProcessIds
    )
    {
        var processes = new Dictionary<int, Process>();
        AddNamedProcesses(processes, current);
        AddRegisteredProcesses(processes, records, current.Id);
        AddRestartManagerProcesses(processes, restartManagerProcessIds, current.Id);
        return processes;
    }

    private static void AddNamedProcesses(Dictionary<int, Process> processes, Process current)
    {
        foreach (Process process in Process.GetProcessesByName(current.ProcessName))
        {
            if (process.Id != current.Id && processes.TryAdd(process.Id, process))
            {
                continue;
            }

            process.Dispose();
        }
    }

    private void AddRegisteredProcesses(
        Dictionary<int, Process> processes,
        IEnumerable<ApplicationInstanceRecord> records,
        int currentProcessId
    )
    {
        foreach (ApplicationInstanceRecord record in records)
        {
            if (record.ProcessId == currentProcessId || processes.ContainsKey(record.ProcessId))
            {
                continue;
            }

            if (TryOpenProcess(record.ProcessId, out Process? process))
            {
                processes.Add(record.ProcessId, process!);
            }
            else
            {
                registry?.RemoveStale(record);
            }
        }
    }

    private static void AddRestartManagerProcesses(
        Dictionary<int, Process> processes,
        IEnumerable<int> processIds,
        int currentProcessId
    )
    {
        foreach (int processId in processIds)
        {
            if (processId == currentProcessId || processes.ContainsKey(processId))
            {
                continue;
            }

            if (TryOpenProcess(processId, out Process? process))
            {
                processes.Add(processId, process!);
            }
        }
    }

    private ProcessClassification ClassifyProcess(
        Process process,
        string? currentPath,
        string currentProcessName,
        string? currentApplicationIdentity,
        IReadOnlyList<ApplicationInstanceRecord> records,
        bool restartManagerMatch
    )
    {
        ApplicationInstanceRecord? record = ValidateRecord(process, records);
        bool resolved = ApplicationProcessPathResolver.TryResolve(
            process,
            out string? candidatePath,
            out string? method,
            out string? error
        );
        bool isWindows = OperatingSystem.IsWindows();
        bool pathMatch = ResolvePathMatch(process.Id, candidatePath, currentPath, resolved, isWindows);
        bool actualMatch = IsActualApplicationMatch(process, pathMatch, currentProcessName, currentApplicationIdentity);
        bool registeredMatch = IsRegisteredPathMatch(record, resolved, candidatePath, isWindows);
        bool sameProcessName = HasProcessName(process, currentProcessName);
        bool confirmed = IsConfirmedProcess(
            actualMatch,
            registeredMatch,
            resolved,
            sameProcessName,
            restartManagerMatch
        );
        bool unverified = IsUnverifiedProcess(confirmed, resolved, sameProcessName, restartManagerMatch, isWindows);
        return new ProcessClassification(confirmed, unverified, record, candidatePath, method, error);
    }

    private static bool ResolvePathMatch(
        int processId,
        string? candidatePath,
        string? currentPath,
        bool resolved,
        bool isWindows
    )
    {
        if (resolved && PathsMatch(candidatePath, currentPath, isWindows))
        {
            return true;
        }

        // Two AppImage launches of the same file share APPIMAGE but not the temporary mount path.
        if (
            !isWindows
            && currentPath is not null
            && ApplicationProcessPathResolver.TryReadLinuxEnvironmentValue(
                processId,
                "APPIMAGE",
                out string? otherAppImage,
                out _
            )
        )
        {
            return PathsMatch(
                ApplicationProcessPathResolver.Canonicalize(otherAppImage!),
                currentPath,
                isWindows: false
            );
        }

        return false;
    }

    private static bool IsActualApplicationMatch(
        Process process,
        bool pathMatch,
        string currentProcessName,
        string? currentApplicationIdentity
    )
    {
        if (!pathMatch)
        {
            return false;
        }

        // Every `dotnet`-hosted process shares the host executable path.
        if (!IsSharedRuntimeHost(currentProcessName))
        {
            return true;
        }

        return SharesApplicationIdentity(process, currentApplicationIdentity);
    }

    internal static bool IsSharedRuntimeHost(string processName) =>
        processName.Equals("dotnet", StringComparison.OrdinalIgnoreCase);

    internal static bool SharesApplicationIdentity(Process process, string? currentApplicationIdentity)
    {
        ArgumentNullException.ThrowIfNull(process);
        if (string.IsNullOrWhiteSpace(currentApplicationIdentity))
        {
            return false;
        }

        if (!OperatingSystem.IsLinux())
        {
            // Windows does not expose a reliable same-user cmdline API here; shared-host
            // confirmation falls back to validated registration instead of host-path matches.
            return false;
        }

        if (
            !ApplicationProcessPathResolver.TryReadLinuxCommandLine(
                process.Id,
                out IReadOnlyList<string> arguments,
                out _
            )
        )
        {
            return false;
        }

        string? workingDirectory = null;
        _ = ApplicationProcessPathResolver.TryReadLinuxWorkingDirectory(process.Id, out workingDirectory, out _);
        return CommandLineContainsIdentity(arguments, currentApplicationIdentity, workingDirectory);
    }

    internal static bool CommandLineContainsIdentity(
        IReadOnlyList<string> arguments,
        string applicationIdentity,
        string? workingDirectory = null
    )
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationIdentity);
        string canonicalIdentity = ApplicationProcessPathResolver.Canonicalize(applicationIdentity);
        foreach (string argument in arguments)
        {
            if (string.IsNullOrWhiteSpace(argument) || argument.StartsWith('-'))
            {
                continue;
            }

            try
            {
                if (
                    !TryResolveCommandLinePath(argument, workingDirectory, out string? absolutePath)
                    || absolutePath is null
                )
                {
                    continue;
                }

                string candidate = ApplicationProcessPathResolver.Canonicalize(absolutePath);
                if (PathsMatch(candidate, canonicalIdentity, OperatingSystem.IsWindows()))
                {
                    return true;
                }
            }
            catch (Exception exception)
                when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // Command-line tokens are not always filesystem paths.
            }
        }

        return false;
    }

    internal static bool TryResolveCommandLinePath(string argument, string? workingDirectory, out string? absolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(argument);
        if (Path.IsPathRooted(argument))
        {
            absolutePath = argument;
            return true;
        }

        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            absolutePath = null;
            return false;
        }

        absolutePath = Path.GetFullPath(argument, workingDirectory);
        return Path.IsPathRooted(absolutePath);
    }

    internal static string? ResolveCurrentApplicationIdentity()
    {
        string? entryAssemblyPath = System.Reflection.Assembly.GetEntryAssembly()?.Location;
        if (!string.IsNullOrWhiteSpace(entryAssemblyPath))
        {
            return ApplicationProcessPathResolver.Canonicalize(entryAssemblyPath);
        }

        foreach (
            string argument in Environment
                .GetCommandLineArgs()
                .Where(candidate =>
                    candidate.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    && candidate.Contains("SrvSurvey", StringComparison.OrdinalIgnoreCase)
                )
        )
        {
            try
            {
                return ApplicationProcessPathResolver.Canonicalize(argument);
            }
            catch (Exception exception)
                when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return null;
            }
        }

        return null;
    }

    private static bool TryOpenProcess(int processId, out Process? process)
    {
        try
        {
            process = Process.GetProcessById(processId);
            return true;
        }
        catch (ArgumentException)
        {
            process = null;
            return false;
        }
    }

    private static bool HasProcessName(Process process, string expectedName)
    {
        try
        {
            return string.Equals(process.ProcessName, expectedName, StringComparison.OrdinalIgnoreCase);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public ValueTask DisposeAsync()
    {
        return registry?.DisposeAsync() ?? ValueTask.CompletedTask;
    }

    internal static bool PathsMatch(string? candidatePath, string? currentPath, bool isWindows)
    {
        if (string.IsNullOrWhiteSpace(candidatePath) || string.IsNullOrWhiteSpace(currentPath))
        {
            return false;
        }

        return string.Equals(
            candidatePath,
            currentPath,
            isWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal
        );
    }

    internal static bool IsConfirmedProcess(
        bool actualPathMatch,
        bool hasValidatedRegistration,
        bool pathResolved,
        bool sameProcessName,
        bool restartManagerMatch
    ) => actualPathMatch || hasValidatedRegistration || (!pathResolved && sameProcessName && restartManagerMatch);

    internal static bool IsUnverifiedProcess(
        bool confirmed,
        bool pathResolved,
        bool sameProcessName,
        bool restartManagerMatch,
        bool isWindows
    )
    {
        if (confirmed)
        {
            return false;
        }

        // Restart Manager hits remain unverified without a resolvable path.
        // On Linux, same-name + unresolved alone is too weak (permission races / short-lived PIDs).
        return restartManagerMatch || (!pathResolved && sameProcessName && isWindows);
    }

    internal static bool IsRegisteredPathMatch(
        ApplicationInstanceRecord? record,
        bool pathResolved,
        string? candidatePath,
        bool isWindows
    ) => record is not null && (!pathResolved || PathsMatch(candidatePath, record.ExecutablePath, isWindows));

    private ApplicationInstanceRecord? ValidateRecord(Process process, IReadOnlyList<ApplicationInstanceRecord> records)
    {
        long startTicks;
        try
        {
            startTicks = process.StartTime.ToUniversalTime().Ticks;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            log?.Invoke($"Could not validate update registration for PID {process.Id}: " + exception.Message);
            return null;
        }

        ApplicationInstanceRecord? validated = null;
        foreach (ApplicationInstanceRecord record in records)
        {
            if (Math.Abs(record.ProcessStartTimeUtcTicks - startTicks) <= TimeSpan.FromSeconds(1).Ticks)
            {
                validated = record;
            }
            else
            {
                registry?.RemoveStale(record);
            }
        }

        return validated;
    }

    private sealed record ProcessClassification(
        bool IsConfirmed,
        bool IsUnverified,
        ApplicationInstanceRecord? Record,
        string? Path,
        string Method,
        string? Error
    );
}

internal sealed partial class SystemApplicationInstanceProcess(Process process, string? pipeName = null)
    : IApplicationInstanceProcess
{
    private const int LinuxTerminateSignal = 15;
    private const int LinuxNoSuchProcess = 3;

    public int Id => process.Id;

    public bool HasExited
    {
        get
        {
            try
            {
                return process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }
    }

    public async Task<bool> RequestGracefulExitAsync(CancellationToken cancellationToken)
    {
        if (
            pipeName is not null
            && await ApplicationInstanceRegistry.RequestShutdownAsync(pipeName, cancellationToken).ConfigureAwait(false)
        )
        {
            return true;
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                return process.CloseMainWindow();
            }

            if (OperatingSystem.IsLinux())
            {
                if (SendSignal(process.Id, LinuxTerminateSignal) == 0)
                {
                    return true;
                }

                return Marshal.GetLastPInvokeError() == LinuxNoSuchProcess;
            }

            throw new PlatformNotSupportedException("Automatic updates are supported only on Windows and Linux.");
        }
        catch (Exception exception)
            when (exception is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return HasExited;
        }
    }

    public void ForceTerminate()
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception)
            when (exception is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            // The manager verifies that every process actually exited.
        }
    }

    public async Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // The process exited between the live check and the wait call.
        }
    }

    public void Dispose()
    {
        process.Dispose();
    }

    [LibraryImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static partial int SendSignal(int processId, int signal);
}
