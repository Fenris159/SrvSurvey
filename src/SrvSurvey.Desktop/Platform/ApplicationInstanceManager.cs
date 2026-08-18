using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SrvSurvey.Desktop.Platform;

public sealed record ApplicationInstanceScan(
    int ConfirmedCount,
    int UnverifiedCount)
{
    public int TotalCount => checked(ConfirmedCount + UnverifiedCount);
}

public interface IApplicationInstanceManager
{
    Task<ApplicationInstanceScan> ScanOtherInstancesAsync(
        CancellationToken cancellationToken = default);

    Task<int> CountOtherInstancesAsync(
        CancellationToken cancellationToken = default);

    Task CloseOtherInstancesAsync(
        CancellationToken cancellationToken = default);
}

internal interface IApplicationInstanceProcessSource
{
    ApplicationInstanceDiscovery DiscoverOtherInstances();
}

internal sealed record ApplicationInstanceDiscovery(
    IReadOnlyList<IApplicationInstanceProcess> Confirmed,
    int UnverifiedCount) : IDisposable
{
    public void Dispose()
    {
        foreach (var process in Confirmed)
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

internal sealed class ApplicationInstanceManager
    : IApplicationInstanceManager, IAsyncDisposable
{
    private static readonly TimeSpan DefaultGracefulExitTimeout =
        TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultForcedExitTimeout =
        TimeSpan.FromSeconds(5);

    private readonly IApplicationInstanceProcessSource processSource;
    private readonly TimeSpan gracefulExitTimeout;
    private readonly TimeSpan forcedExitTimeout;

    public ApplicationInstanceManager()
        : this(
            new SystemApplicationInstanceProcessSource(),
            DefaultGracefulExitTimeout,
            DefaultForcedExitTimeout)
    {
    }

    public ApplicationInstanceManager(
        string dataDirectory,
        Func<Task> requestShutdown,
        Action<string>? log = null)
        : this(
            new SystemApplicationInstanceProcessSource(
                dataDirectory,
                requestShutdown,
                log),
            DefaultGracefulExitTimeout,
            DefaultForcedExitTimeout)
    {
    }

    internal ApplicationInstanceManager(
        IApplicationInstanceProcessSource processSource,
        TimeSpan gracefulExitTimeout,
        TimeSpan forcedExitTimeout)
    {
        this.processSource = processSource
            ?? throw new ArgumentNullException(nameof(processSource));
        ArgumentOutOfRangeException.ThrowIfLessThan(
            gracefulExitTimeout,
            TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            forcedExitTimeout,
            TimeSpan.Zero);

        this.gracefulExitTimeout = gracefulExitTimeout;
        this.forcedExitTimeout = forcedExitTimeout;
    }

    public Task<ApplicationInstanceScan> ScanOtherInstancesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var discovery = processSource.DiscoverOtherInstances();
        return Task.FromResult(new ApplicationInstanceScan(
            discovery.Confirmed.Count,
            discovery.UnverifiedCount));
    }

    public async Task<int> CountOtherInstancesAsync(
        CancellationToken cancellationToken = default)
    {
        var scan = await ScanOtherInstancesAsync(cancellationToken)
            .ConfigureAwait(false);
        return scan.TotalCount;
    }

    public async Task CloseOtherInstancesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using (var discovery = processSource.DiscoverOtherInstances())
        {
            var graceful = await RequestGracefulExitAsync(
                    discovery.Confirmed,
                    cancellationToken)
                .ConfigureAwait(false);
            ForceTerminate(discovery.Confirmed.Where(instance =>
                !graceful.Contains(instance.Id)));
            await WaitForExitAsync(
                    discovery.Confirmed,
                    gracefulExitTimeout,
                    cancellationToken)
                .ConfigureAwait(false);

            ForceTerminate(discovery.Confirmed.Where(instance => !instance.HasExited));
            await WaitForExitAsync(
                    discovery.Confirmed,
                    forcedExitTimeout,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        using var verification = processSource.DiscoverOtherInstances();
        var remaining = verification.Confirmed.Count(instance => !instance.HasExited);
        if (verification.UnverifiedCount > 0)
        {
            throw new IOException(
                $"Windows or Linux prevented SrvSurvey from verifying "
                + $"{verification.UnverifiedCount:N0} matching process(es). "
                + "Close every SrvSurvey-XP instance manually, then retry the update. "
                + "No installation files were changed.");
        }

        if (remaining > 0)
        {
            throw new IOException(
                $"Could not close {remaining:N0} other SrvSurvey instance(s). "
                + "The update was not started.");
        }
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
        CancellationToken cancellationToken)
    {
        var requested = new HashSet<int>();
        foreach (var instance in instances.Where(instance => !instance.HasExited))
        {
            if (await instance.RequestGracefulExitAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                requested.Add(instance.Id);
            }
        }

        return requested;
    }

    private static void ForceTerminate(
        IEnumerable<IApplicationInstanceProcess> instances)
    {
        foreach (var instance in instances)
        {
            instance.ForceTerminate();
        }
    }

    private static async Task WaitForExitAsync(
        IReadOnlyCollection<IApplicationInstanceProcess> instances,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var active = instances.Where(instance => !instance.HasExited).ToArray();
        if (active.Length == 0)
        {
            return;
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await Task.WhenAll(active.Select(instance =>
                    instance.WaitForExitAsync(timeoutSource.Token)))
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The caller performs a forced termination after the grace period.
        }
    }
}

internal sealed class SystemApplicationInstanceProcessSource
    : IApplicationInstanceProcessSource, IAsyncDisposable
{
    private readonly ApplicationInstanceRegistry? registry;
    private readonly Action<string>? log;

    public SystemApplicationInstanceProcessSource()
    {
    }

    public SystemApplicationInstanceProcessSource(
        string dataDirectory,
        Func<Task> requestShutdown,
        Action<string>? log)
    {
        this.log = log;
        registry = new ApplicationInstanceRegistry(
            dataDirectory,
            requestShutdown,
            log);
    }

    public ApplicationInstanceDiscovery DiscoverOtherInstances()
    {
        using var current = Process.GetCurrentProcess();
        var currentPath = ResolveCurrentPath();
        var restartManagerProcessIds = FindRestartManagerProcesses(currentPath);
        var records = registry?.ReadOtherRecords() ?? [];
        var recordsByProcess = records
            .GroupBy(record => record.ProcessId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var processes = CollectCandidateProcesses(
            current,
            records,
            restartManagerProcessIds);

        var confirmed = new List<IApplicationInstanceProcess>();
        var unverified = 0;
        foreach (var process in processes.Values)
        {
            var processRecords = recordsByProcess.GetValueOrDefault(process.Id) ?? [];
            var classification = ClassifyProcess(
                process,
                currentPath,
                current.ProcessName,
                processRecords,
                restartManagerProcessIds.Contains(process.Id));
            if (classification.IsConfirmed)
            {
                confirmed.Add(new SystemApplicationInstanceProcess(
                    process,
                    classification.Record?.PipeName));
                log?.Invoke(
                    $"Confirmed update instance PID {process.Id} using "
                    + $"{classification.Method}.");
                continue;
            }

            if (classification.IsUnverified)
            {
                unverified++;
                log?.Invoke(
                    $"Could not verify update candidate PID {process.Id}: "
                    + classification.Error);
            }
            else
            {
                log?.Invoke(
                    $"Ignored unrelated process PID {process.Id} at "
                    + $"'{classification.Path ?? classification.Record?.ExecutablePath ?? "unknown"}'.");
            }

            process.Dispose();
        }

        return new ApplicationInstanceDiscovery(confirmed, unverified);
    }

    private static string? ResolveCurrentPath()
    {
        return string.IsNullOrWhiteSpace(Environment.ProcessPath)
            ? null
            : ApplicationProcessPathResolver.Canonicalize(Environment.ProcessPath);
    }

    private IReadOnlySet<int> FindRestartManagerProcesses(string? currentPath)
    {
        return OperatingSystem.IsWindows() && currentPath is not null
            ? WindowsRestartManagerProcessFinder.FindLockingProcessIds(
                currentPath,
                log)
            : new HashSet<int>();
    }

    private Dictionary<int, Process> CollectCandidateProcesses(
        Process current,
        IReadOnlyList<ApplicationInstanceRecord> records,
        IReadOnlySet<int> restartManagerProcessIds)
    {
        var processes = new Dictionary<int, Process>();
        AddNamedProcesses(processes, current);
        AddRegisteredProcesses(processes, records, current.Id);
        AddRestartManagerProcesses(
            processes,
            restartManagerProcessIds,
            current.Id);
        return processes;
    }

    private static void AddNamedProcesses(
        IDictionary<int, Process> processes,
        Process current)
    {
        foreach (var process in Process.GetProcessesByName(current.ProcessName))
        {
            if (process.Id != current.Id && processes.TryAdd(process.Id, process))
            {
                continue;
            }

            process.Dispose();
        }
    }

    private void AddRegisteredProcesses(
        IDictionary<int, Process> processes,
        IEnumerable<ApplicationInstanceRecord> records,
        int currentProcessId)
    {
        foreach (var record in records)
        {
            if (record.ProcessId == currentProcessId
                || processes.ContainsKey(record.ProcessId))
            {
                continue;
            }

            if (TryOpenProcess(record.ProcessId, out var process))
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
        IDictionary<int, Process> processes,
        IEnumerable<int> processIds,
        int currentProcessId)
    {
        foreach (var processId in processIds)
        {
            if (processId == currentProcessId || processes.ContainsKey(processId))
            {
                continue;
            }

            if (TryOpenProcess(processId, out var process))
            {
                processes.Add(processId, process!);
            }
        }
    }

    private ProcessClassification ClassifyProcess(
        Process process,
        string? currentPath,
        string currentProcessName,
        IReadOnlyList<ApplicationInstanceRecord> records,
        bool restartManagerMatch)
    {
        var record = ValidateRecord(process, records);
        var resolved = ApplicationProcessPathResolver.TryResolve(
            process,
            out var candidatePath,
            out var method,
            out var error);
        var actualMatch = resolved && PathsMatch(
            candidatePath,
            currentPath,
            OperatingSystem.IsWindows());
        var registeredMatch = !resolved
            && record is not null
            && PathsMatch(
                record.ExecutablePath,
                currentPath,
                OperatingSystem.IsWindows());
        var sameProcessName = HasProcessName(process, currentProcessName);
        var confirmed = actualMatch
            || registeredMatch
            || (!resolved && sameProcessName && restartManagerMatch);
        var unverified = !confirmed
            && ((!resolved && sameProcessName) || restartManagerMatch);
        return new ProcessClassification(
            confirmed,
            unverified,
            record,
            candidatePath,
            method,
            error);
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
            return string.Equals(
                process.ProcessName,
                expectedName,
                StringComparison.OrdinalIgnoreCase);
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

    internal static bool PathsMatch(
        string? candidatePath,
        string? currentPath,
        bool isWindows)
    {
        if (string.IsNullOrWhiteSpace(candidatePath)
            || string.IsNullOrWhiteSpace(currentPath))
        {
            return false;
        }

        return string.Equals(
            candidatePath,
            currentPath,
            isWindows
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
    }

    private ApplicationInstanceRecord? ValidateRecord(
        Process process,
        IReadOnlyList<ApplicationInstanceRecord> records)
    {
        long startTicks;
        try
        {
            startTicks = process.StartTime.ToUniversalTime().Ticks;
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException)
        {
            log?.Invoke(
                $"Could not validate update registration for PID {process.Id}: "
                + exception.Message);
            return null;
        }

        ApplicationInstanceRecord? validated = null;
        foreach (var record in records)
        {
            if (Math.Abs(record.ProcessStartTimeUtcTicks - startTicks)
                <= TimeSpan.FromSeconds(1).Ticks)
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
        string? Error);
}

internal sealed partial class SystemApplicationInstanceProcess(
    Process process,
    string? pipeName = null) : IApplicationInstanceProcess
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

    public async Task<bool> RequestGracefulExitAsync(
        CancellationToken cancellationToken)
    {
        if (pipeName is not null
            && await ApplicationInstanceRegistry.RequestShutdownAsync(
                    pipeName,
                    cancellationToken)
                .ConfigureAwait(false))
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

            throw new PlatformNotSupportedException(
                "Automatic updates are supported only on Windows and Linux.");
        }
        catch (Exception exception) when (
            exception is Win32Exception
                or InvalidOperationException
                or NotSupportedException)
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
        catch (Exception exception) when (
            exception is Win32Exception
                or InvalidOperationException
                or NotSupportedException)
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
