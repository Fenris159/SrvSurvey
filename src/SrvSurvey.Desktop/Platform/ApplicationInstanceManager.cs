using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SrvSurvey.Desktop.Platform;

public interface IApplicationInstanceManager
{
    Task<int> CountOtherInstancesAsync(
        CancellationToken cancellationToken = default);

    Task CloseOtherInstancesAsync(
        CancellationToken cancellationToken = default);
}

internal interface IApplicationInstanceProcessSource
{
    IReadOnlyList<IApplicationInstanceProcess> FindOtherInstances();
}

internal interface IApplicationInstanceProcess : IDisposable
{
    int Id { get; }

    bool HasExited { get; }

    bool RequestGracefulExit();

    void ForceTerminate();

    Task WaitForExitAsync(CancellationToken cancellationToken);
}

internal sealed class ApplicationInstanceManager
    : IApplicationInstanceManager
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

    internal ApplicationInstanceManager(
        IApplicationInstanceProcessSource processSource,
        TimeSpan gracefulExitTimeout,
        TimeSpan forcedExitTimeout)
    {
        this.processSource = processSource
            ?? throw new ArgumentNullException(nameof(processSource));
        if (gracefulExitTimeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(gracefulExitTimeout));
        }

        if (forcedExitTimeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(forcedExitTimeout));
        }

        this.gracefulExitTimeout = gracefulExitTimeout;
        this.forcedExitTimeout = forcedExitTimeout;
    }

    public Task<int> CountOtherInstancesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var instances = processSource.FindOtherInstances();
        try
        {
            return Task.FromResult(instances.Count);
        }
        finally
        {
            DisposeAll(instances);
        }
    }

    public async Task CloseOtherInstancesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var instances = processSource.FindOtherInstances();
        try
        {
            var graceful = RequestGracefulExit(instances);
            ForceTerminate(instances.Where(instance =>
                !graceful.Contains(instance.Id)));
            await WaitForExitAsync(
                    instances,
                    gracefulExitTimeout,
                    cancellationToken)
                .ConfigureAwait(false);

            ForceTerminate(instances.Where(instance => !instance.HasExited));
            await WaitForExitAsync(
                    instances,
                    forcedExitTimeout,
                    cancellationToken)
                .ConfigureAwait(false);

            var remaining = instances.Count(instance => !instance.HasExited);
            if (remaining > 0)
            {
                throw new IOException(
                    $"Could not close {remaining:N0} other SrvSurvey instance(s). "
                    + "The update was not started.");
            }
        }
        finally
        {
            DisposeAll(instances);
        }
    }

    private static HashSet<int> RequestGracefulExit(
        IEnumerable<IApplicationInstanceProcess> instances)
    {
        var requested = new HashSet<int>();
        foreach (var instance in instances.Where(instance => !instance.HasExited))
        {
            if (instance.RequestGracefulExit())
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

    private static void DisposeAll(
        IEnumerable<IApplicationInstanceProcess> instances)
    {
        foreach (var instance in instances)
        {
            instance.Dispose();
        }
    }
}

internal sealed class SystemApplicationInstanceProcessSource
    : IApplicationInstanceProcessSource
{
    public IReadOnlyList<IApplicationInstanceProcess> FindOtherInstances()
    {
        using var current = Process.GetCurrentProcess();
        var executablePath = Environment.ProcessPath;
        var matches = new List<IApplicationInstanceProcess>();
        foreach (var process in Process.GetProcessesByName(current.ProcessName))
        {
            if (process.Id != current.Id
                && MatchesExecutable(process, executablePath))
            {
                matches.Add(new SystemApplicationInstanceProcess(process));
            }
            else
            {
                process.Dispose();
            }
        }

        return matches;
    }

    internal static bool PathsMatch(
        string? candidatePath,
        string? currentPath,
        bool isWindows)
    {
        if (string.IsNullOrWhiteSpace(candidatePath)
            || string.IsNullOrWhiteSpace(currentPath))
        {
            return true;
        }

        return string.Equals(
            candidatePath,
            currentPath,
            isWindows
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
    }

    private static bool MatchesExecutable(
        Process process,
        string? executablePath)
    {
        try
        {
            return PathsMatch(
                process.MainModule?.FileName,
                executablePath,
                OperatingSystem.IsWindows());
        }
        catch (Exception exception) when (
            exception is Win32Exception
                or InvalidOperationException
                or NotSupportedException)
        {
            // The exact process name remains a safe fallback when the OS
            // does not permit reading another instance's executable path.
            return true;
        }
    }
}

internal sealed partial class SystemApplicationInstanceProcess(Process process)
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

    public bool RequestGracefulExit()
    {
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
