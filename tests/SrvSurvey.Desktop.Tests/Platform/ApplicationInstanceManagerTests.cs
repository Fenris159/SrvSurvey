using System.Diagnostics;
using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class ApplicationInstanceManagerTests
{
    [Fact]
    public async Task DefaultManagerCanInspectRunningProcesses()
    {
        var manager = new ApplicationInstanceManager();

        var count = await manager.CountOtherInstancesAsync();

        Assert.True(count >= 0);
    }

    [Fact]
    public void ConstructorRejectsInvalidDependenciesAndTimeouts()
    {
        var source = new StubProcessSource([]);

        Assert.Throws<ArgumentNullException>(() => new ApplicationInstanceManager(
            null!,
            TimeSpan.Zero,
            TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ApplicationInstanceManager(
            source,
            TimeSpan.FromMilliseconds(-1),
            TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ApplicationInstanceManager(
            source,
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(-1)));
    }

    [Fact]
    public async Task CountReturnsOtherInstancesAndDisposesHandles()
    {
        var first = new StubProcess(10);
        var second = new StubProcess(20);
        var manager = CreateManager(first, second);

        var count = await manager.CountOtherInstancesAsync();

        Assert.Equal(2, count);
        Assert.True(first.Disposed);
        Assert.True(second.Disposed);
    }

    [Fact]
    public async Task ScanIncludesUnverifiedMatchingProcesses()
    {
        var source = new StubProcessSource([], unverifiedCount: 2);
        var manager = new ApplicationInstanceManager(
            source,
            TimeSpan.Zero,
            TimeSpan.Zero);

        var scan = await manager.ScanOtherInstancesAsync();

        Assert.Equal(0, scan.ConfirmedCount);
        Assert.Equal(2, scan.UnverifiedCount);
        Assert.Equal(2, scan.TotalCount);
        await Assert.ThrowsAsync<IOException>(
            () => manager.CloseOtherInstancesAsync());
    }

    [Fact]
    public async Task CloseUsesGracefulExitAndImmediatelyForcesUnsupportedInstances()
    {
        var graceful = new StubProcess(10)
        {
            GracefulExitSupported = true,
            ExitWhenWaited = true,
        };
        var forced = new StubProcess(20)
        {
            ForceExitSucceeds = true,
        };
        var manager = CreateManager(graceful, forced);

        await manager.CloseOtherInstancesAsync();

        Assert.Equal(1, graceful.GracefulExitRequests);
        Assert.Equal(0, graceful.ForceExitRequests);
        Assert.Equal(1, forced.GracefulExitRequests);
        Assert.Equal(1, forced.ForceExitRequests);
        Assert.All([graceful, forced], process => Assert.True(process.Disposed));
    }

    [Fact]
    public async Task CloseForcesAnInstanceThatIgnoresTheGracePeriod()
    {
        var process = new StubProcess(10)
        {
            GracefulExitSupported = true,
            ForceExitSucceeds = true,
        };
        var manager = CreateManager(process);

        await manager.CloseOtherInstancesAsync();

        Assert.Equal(1, process.GracefulExitRequests);
        Assert.Equal(1, process.ForceExitRequests);
        Assert.True(process.HasExited);
        Assert.True(process.Disposed);
    }

    [Fact]
    public async Task CloseFailsSafelyWhenAnInstanceCannotBeTerminated()
    {
        var process = new StubProcess(10);
        var manager = CreateManager(process);

        var exception = await Assert.ThrowsAsync<IOException>(
            () => manager.CloseOtherInstancesAsync());

        Assert.Contains("update was not started", exception.Message);
        Assert.Equal(2, process.ForceExitRequests);
        Assert.True(process.Disposed);
    }

    [Theory]
    [InlineData("C:\\SrvSurvey\\SrvSurvey.Desktop.exe", "c:\\srvsurvey\\srvsurvey.desktop.exe", true, true)]
    [InlineData("/opt/SrvSurvey/SrvSurvey.Desktop", "/opt/srvsurvey/SrvSurvey.Desktop", false, false)]
    [InlineData("/opt/SrvSurvey/SrvSurvey.Desktop", "/opt/SrvSurvey/SrvSurvey.Desktop", false, true)]
    [InlineData(null, "/opt/SrvSurvey/SrvSurvey.Desktop", false, false)]
    [InlineData("/opt/SrvSurvey/SrvSurvey.Desktop", null, false, false)]
    [InlineData("", "/opt/SrvSurvey/SrvSurvey.Desktop", false, false)]
    public void ExecutableMatchingUsesPlatformPathSemantics(
        string? candidate,
        string? current,
        bool isWindows,
        bool expected)
    {
        Assert.Equal(
            expected,
            SystemApplicationInstanceProcessSource.PathsMatch(
                candidate,
                current,
                isWindows));
    }

    [Fact]
    public async Task SystemProcessWrapperHandlesAnExitedProcess()
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
            Arguments = OperatingSystem.IsWindows() ? "/c exit 0" : "-c true",
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        Assert.NotNull(process);
        await process.WaitForExitAsync();
        using var instance = new SystemApplicationInstanceProcess(process);

        Assert.Equal(process.Id, instance.Id);
        Assert.True(instance.HasExited);
        if (OperatingSystem.IsWindows())
        {
            Assert.False(await instance.RequestGracefulExitAsync(
                CancellationToken.None));
        }

        instance.ForceTerminate();
        await instance.WaitForExitAsync(CancellationToken.None);
    }

    [Fact]
    public void CurrentProcessPathCanBeResolvedAndCanonicalized()
    {
        using var process = Process.GetCurrentProcess();

        var resolved = ApplicationProcessPathResolver.TryResolve(
            process,
            out var path,
            out var method,
            out var error);

        Assert.True(resolved, error);
        Assert.NotNull(path);
        Assert.True(Path.IsPathFullyQualified(path!));
        Assert.False(string.IsNullOrWhiteSpace(method));
    }

    [Fact]
    public void WindowsRestartManagerFindsTheCurrentExecutableOwner()
    {
        if (!OperatingSystem.IsWindows() || Environment.ProcessPath is null)
        {
            return;
        }

        var processIds = WindowsRestartManagerProcessFinder
            .FindLockingProcessIds(Environment.ProcessPath);

        Assert.Contains(Environment.ProcessId, processIds);
    }

    [Fact]
    public async Task CooperativeRegistryAcceptsVerifiedShutdownRequest()
    {
        var dataDirectory = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-instance-registry-tests-{Guid.NewGuid():N}");
        var requested = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await using var registry = new ApplicationInstanceRegistry(
                dataDirectory,
                () =>
                {
                    requested.TrySetResult();
                    return Task.CompletedTask;
                });

            Assert.True(await ApplicationInstanceRegistry.RequestShutdownAsync(
                registry.Current.PipeName,
                CancellationToken.None));
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
        }
    }

    private static ApplicationInstanceManager CreateManager(
        params StubProcess[] processes)
    {
        return new ApplicationInstanceManager(
            new StubProcessSource(processes),
            TimeSpan.FromMilliseconds(1),
            TimeSpan.FromMilliseconds(1));
    }

    private sealed class StubProcessSource(
        IReadOnlyList<StubProcess> processes,
        int unverifiedCount = 0) : IApplicationInstanceProcessSource
    {
        public ApplicationInstanceDiscovery DiscoverOtherInstances()
        {
            return new ApplicationInstanceDiscovery(
                processes.Cast<IApplicationInstanceProcess>().ToArray(),
                unverifiedCount);
        }
    }

    private sealed class StubProcess(int id) : IApplicationInstanceProcess
    {
        public int Id { get; } = id;

        public bool HasExited { get; private set; }

        public bool GracefulExitSupported { get; init; }

        public bool ExitWhenWaited { get; init; }

        public bool ForceExitSucceeds { get; init; }

        public int GracefulExitRequests { get; private set; }

        public int ForceExitRequests { get; private set; }

        public bool Disposed { get; private set; }

        public Task<bool> RequestGracefulExitAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GracefulExitRequests++;
            return Task.FromResult(GracefulExitSupported);
        }

        public void ForceTerminate()
        {
            ForceExitRequests++;
            HasExited = ForceExitSucceeds;
        }

        public async Task WaitForExitAsync(CancellationToken cancellationToken)
        {
            if (ExitWhenWaited)
            {
                HasExited = true;
                return;
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }
}
