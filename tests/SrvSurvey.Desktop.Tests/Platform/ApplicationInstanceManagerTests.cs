using System.Diagnostics;
using System.Text.Json;
using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class ApplicationInstanceManagerTests
{
    [Fact]
    public async Task DefaultManagerCanInspectRunningProcesses()
    {
        var manager = new ApplicationInstanceManager();

        int count = await manager.CountOtherInstancesAsync();

        Assert.True(count >= 0);
    }

    [Fact]
    public void ConstructorRejectsInvalidDependenciesAndTimeouts()
    {
        var source = new StubProcessSource([]);

        Assert.Throws<ArgumentNullException>(() => new ApplicationInstanceManager(null!, TimeSpan.Zero, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ApplicationInstanceManager(source, TimeSpan.FromMilliseconds(-1), TimeSpan.Zero)
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ApplicationInstanceManager(source, TimeSpan.Zero, TimeSpan.FromMilliseconds(-1))
        );
    }

    [Fact]
    public async Task CountReturnsOtherInstancesAndDisposesHandles()
    {
        var first = new StubProcess(10);
        var second = new StubProcess(20);
        ApplicationInstanceManager manager = CreateManager(first, second);

        int count = await manager.CountOtherInstancesAsync();

        Assert.Equal(2, count);
        Assert.True(first.Disposed);
        Assert.True(second.Disposed);
    }

    [Fact]
    public async Task ScanAndCloseHonorPreCanceledTokens()
    {
        ApplicationInstanceManager manager = CreateManager(new StubProcess(10));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            manager.ScanOtherInstancesAsync(cancellation.Token)
        );
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            manager.CloseOtherInstancesAsync(cancellation.Token)
        );
    }

    [Fact]
    public async Task ScanIncludesUnverifiedMatchingProcesses()
    {
        var source = new StubProcessSource([], unverifiedCount: 2);
        var manager = new ApplicationInstanceManager(source, TimeSpan.Zero, TimeSpan.Zero);

        ApplicationInstanceScan scan = await manager.ScanOtherInstancesAsync();

        Assert.Equal(0, scan.ConfirmedCount);
        Assert.Equal(2, scan.UnverifiedCount);
        Assert.Equal(2, scan.TotalCount);
        await Assert.ThrowsAsync<IOException>(() => manager.CloseOtherInstancesAsync());
    }

    [Fact]
    public async Task CloseUsesGracefulExitAndImmediatelyForcesUnsupportedInstances()
    {
        var graceful = new StubProcess(10) { GracefulExitSupported = true, ExitWhenWaited = true };
        var forced = new StubProcess(20) { ForceExitSucceeds = true };
        ApplicationInstanceManager manager = CreateManager(graceful, forced);

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
        var process = new StubProcess(10) { GracefulExitSupported = true, ForceExitSucceeds = true };
        ApplicationInstanceManager manager = CreateManager(process);

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
        ApplicationInstanceManager manager = CreateManager(process);

        IOException exception = await Assert.ThrowsAsync<IOException>(() => manager.CloseOtherInstancesAsync());

        Assert.Contains("Could not close", exception.Message);
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
        bool expected
    )
    {
        Assert.Equal(expected, SystemApplicationInstanceProcessSource.PathsMatch(candidate, current, isWindows));
    }

    [Fact]
    public void ValidatedRegistrationConfirmsAnInstanceFromAnotherInstallPath()
    {
        Assert.True(
            SystemApplicationInstanceProcessSource.IsConfirmedProcess(
                actualPathMatch: false,
                hasValidatedRegistration: true,
                pathResolved: true,
                sameProcessName: false,
                restartManagerMatch: false
            )
        );
    }

    [Theory]
    [InlineData(false, false, true, false, false, false)] // Linux same-name unresolved → ignore
    [InlineData(false, false, true, false, true, true)] // Windows same-name unresolved → unverified
    [InlineData(false, true, true, true, true, true)] // Restart Manager without confirm → unverified
    [InlineData(true, false, true, false, true, false)] // Already confirmed → not unverified
    public void UnverifiedClassificationAvoidsLinuxNameOnlyFalsePositives(
        bool confirmed,
        bool pathResolved,
        bool sameProcessName,
        bool restartManagerMatch,
        bool isWindows,
        bool expected
    )
    {
        Assert.Equal(
            expected,
            SystemApplicationInstanceProcessSource.IsUnverifiedProcess(
                confirmed,
                pathResolved,
                sameProcessName,
                restartManagerMatch,
                isWindows
            )
        );
    }

    [Fact]
    public void SharedDotnetHostPathIsNotTreatedAsApplicationIdentityByItself()
    {
        Assert.True(SystemApplicationInstanceProcessSource.IsSharedRuntimeHost("dotnet"));
        Assert.True(SystemApplicationInstanceProcessSource.IsSharedRuntimeHost("DOTNET"));
        Assert.False(SystemApplicationInstanceProcessSource.IsSharedRuntimeHost("SrvSurvey.Desktop"));
        Assert.True(
            SystemApplicationInstanceProcessSource.CommandLineContainsIdentity(
                ["/home/ubuntu/.dotnet/dotnet", "/opt/SrvSurvey/SrvSurvey.Desktop.dll"],
                "/opt/SrvSurvey/SrvSurvey.Desktop.dll"
            )
        );
        Assert.False(
            SystemApplicationInstanceProcessSource.CommandLineContainsIdentity(
                ["/home/ubuntu/.dotnet/dotnet", "build"],
                "/opt/SrvSurvey/SrvSurvey.Desktop.dll"
            )
        );
    }

    [Fact]
    public void RegistrationMatchesResolvedCandidateAgainstItsRecordedExecutable()
    {
        var record = new ApplicationInstanceRecord(
            1,
            "SrvSurvey.XP",
            42,
            1,
            "/opt/SrvSurvey/SrvSurvey.Desktop",
            "SrvSurvey.XP.test.pipe"
        );

        Assert.True(
            SystemApplicationInstanceProcessSource.IsRegisteredPathMatch(
                record,
                pathResolved: true,
                "/opt/SrvSurvey/SrvSurvey.Desktop",
                isWindows: false
            )
        );
        Assert.False(
            SystemApplicationInstanceProcessSource.IsRegisteredPathMatch(
                record,
                pathResolved: true,
                "/opt/Other/SrvSurvey.Desktop",
                isWindows: false
            )
        );
    }

    [Fact]
    public void RegistrationRemainsAFallbackWhenCandidatePathCannotBeResolved()
    {
        var record = new ApplicationInstanceRecord(
            1,
            "SrvSurvey.XP",
            42,
            1,
            "/opt/SrvSurvey/SrvSurvey.Desktop",
            "SrvSurvey.XP.test.pipe"
        );

        Assert.True(
            SystemApplicationInstanceProcessSource.IsRegisteredPathMatch(
                record,
                pathResolved: false,
                candidatePath: null,
                isWindows: false
            )
        );
    }

    [Fact]
    public async Task SystemProcessWrapperHandlesAnExitedProcess()
    {
        using var process = Process.Start(
            new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
                Arguments = OperatingSystem.IsWindows() ? "/c exit 0" : "-c true",
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        );
        Assert.NotNull(process);
        await process.WaitForExitAsync();
        using var instance = new SystemApplicationInstanceProcess(process);

        Assert.Equal(process.Id, instance.Id);
        Assert.True(instance.HasExited);
        if (OperatingSystem.IsWindows())
        {
            Assert.False(await instance.RequestGracefulExitAsync(CancellationToken.None));
        }

        instance.ForceTerminate();
        await instance.WaitForExitAsync(CancellationToken.None);
    }

    [Fact]
    public void CurrentProcessPathCanBeResolvedAndCanonicalized()
    {
        using var process = Process.GetCurrentProcess();

        bool resolved = ApplicationProcessPathResolver.TryResolve(
            process,
            out string? path,
            out string? method,
            out string? error
        );

        Assert.True(resolved, error);
        Assert.NotNull(path);
        Assert.True(Path.IsPathFullyQualified(path));
        Assert.False(string.IsNullOrWhiteSpace(method));
    }

    [Fact]
    public void WindowsFallbackResolvesCurrentProcessAndRejectsMissingProcess()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows process path fallback requires Windows.");
        }

        Assert.True(
            ApplicationProcessPathResolver.TryResolveWindows(
                Environment.ProcessId,
                out string? path,
                out string? error
            ),
            error
        );
        Assert.NotNull(path);
        Assert.False(
            ApplicationProcessPathResolver.TryResolveWindows(
                int.MaxValue,
                out string? missingPath,
                out string? missingError
            )
        );
        Assert.Null(missingPath);
        Assert.False(string.IsNullOrWhiteSpace(missingError));
    }

    [Theory]
    [InlineData("\\\\?\\C:\\SrvSurvey\\SrvSurvey.Desktop.exe", "C:\\SrvSurvey\\SrvSurvey.Desktop.exe")]
    [InlineData("\\\\?\\UNC\\server\\share\\SrvSurvey.exe", "\\\\server\\share\\SrvSurvey.exe")]
    [InlineData("C:\\SrvSurvey\\SrvSurvey.Desktop.exe", "C:\\SrvSurvey\\SrvSurvey.Desktop.exe")]
    public void WindowsDevicePrefixesAreNormalized(string path, string expected)
    {
        Assert.Equal(expected, ApplicationProcessPathResolver.RemoveWindowsDevicePrefix(path));
    }

    [Fact]
    public void FinalWindowsPathReturnsFallbackForMissingFile()
    {
        Assert.False(
            ApplicationProcessPathResolver.TryGetFinalWindowsPath(
                Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
                out string? finalPath
            )
        );
        Assert.True(Path.IsPathFullyQualified(finalPath));
    }

    [Fact]
    public void LinuxResolverReportsUnavailableProcLinkOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("This regression exercises the unavailable Linux fallback.");
        }

        Assert.False(ApplicationProcessPathResolver.TryResolveLinux(int.MaxValue, out string? path, out string? error));
        Assert.Null(path);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void WindowsRestartManagerFindsTheCurrentExecutableOwner()
    {
        if (!OperatingSystem.IsWindows() || Environment.ProcessPath is null)
        {
            Assert.Skip("Restart Manager discovery requires Windows.");
        }

        IReadOnlySet<int> processIds = WindowsRestartManagerProcessFinder.FindLockingProcessIds(
            Environment.ProcessPath
        );

        Assert.Contains(Environment.ProcessId, processIds);
    }

    [Fact]
    public async Task CooperativeRegistryAcceptsVerifiedShutdownRequest()
    {
        string dataDirectory = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-instance-registry-tests-{Guid.NewGuid():N}"
        );
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await using var registry = new ApplicationInstanceRegistry(
                dataDirectory,
                () =>
                {
                    requested.TrySetResult();
                    return Task.CompletedTask;
                }
            );

            Assert.True(
                await ApplicationInstanceRegistry.RequestShutdownAsync(
                    registry.Current.PipeName,
                    CancellationToken.None
                )
            );
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

    [Fact]
    public async Task CooperativeRegistryDisposalIsIdempotent()
    {
        string dataDirectory = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-instance-registry-dispose-tests-{Guid.NewGuid():N}"
        );
        try
        {
            var registry = new ApplicationInstanceRegistry(dataDirectory, () => Task.CompletedTask);

            await registry.DisposeAsync();
            await registry.DisposeAsync();

            Assert.False(
                File.Exists(
                    Path.Combine(
                        dataDirectory,
                        "updates",
                        "instances",
                        $"{registry.Current.ProcessId}-{registry.Current.ProcessStartTimeUtcTicks}.json"
                    )
                )
            );
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RegistryIgnoresMalformedRecordsAndReturnsValidRecords()
    {
        string dataDirectory = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-instance-registry-read-tests-{Guid.NewGuid():N}"
        );
        try
        {
            await using var registry = new ApplicationInstanceRegistry(dataDirectory, () => Task.CompletedTask);
            string directory = Path.Combine(dataDirectory, "updates", "instances");
            var valid = new ApplicationInstanceRecord(
                1,
                "SrvSurvey.XP",
                int.MaxValue,
                1,
                Path.GetFullPath(Environment.ProcessPath!),
                "SrvSurvey.XP.test.pipe"
            );
            string validPath = Path.Combine(directory, "valid.json");
            await File.WriteAllTextAsync(validPath, JsonSerializer.Serialize(valid));
            string invalidPath = Path.Combine(directory, "invalid.json");
            await File.WriteAllTextAsync(invalidPath, "not-json");

            IReadOnlyList<ApplicationInstanceRecord> records = registry.ReadOtherRecords();

            Assert.Contains(valid, records);
            Assert.True(File.Exists(invalidPath));
            registry.RemoveStale(valid);
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task UnsafeOrUnavailablePipeNamesFailWithoutThrowing()
    {
        Assert.False(await ApplicationInstanceRegistry.RequestShutdownAsync("../unsafe", CancellationToken.None));
        Assert.False(
            await ApplicationInstanceRegistry.RequestShutdownAsync(
                $"SrvSurvey.XP.missing.{Guid.NewGuid():N}",
                CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task RegistryInitializationFailureFallsBackWithoutBlockingStartup()
    {
        string path = Path.Combine(Path.GetTempPath(), $"SrvSurvey-instance-registry-file-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(path, "not a directory");
        var messages = new List<string>();
        try
        {
            await using var manager = new ApplicationInstanceManager(path, () => Task.CompletedTask, messages.Add);

            Assert.Contains(
                messages,
                message => message.Contains("registration is unavailable", StringComparison.Ordinal)
            );
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static ApplicationInstanceManager CreateManager(params StubProcess[] processes)
    {
        return new ApplicationInstanceManager(
            new StubProcessSource(processes),
            TimeSpan.FromMilliseconds(1),
            TimeSpan.FromMilliseconds(1)
        );
    }

    private sealed class StubProcessSource(IReadOnlyList<StubProcess> processes, int unverifiedCount = 0)
        : IApplicationInstanceProcessSource
    {
        public ApplicationInstanceDiscovery DiscoverOtherInstances()
        {
            return new ApplicationInstanceDiscovery(
                processes.Cast<IApplicationInstanceProcess>().ToArray(),
                unverifiedCount
            );
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

        public Task<bool> RequestGracefulExitAsync(CancellationToken cancellationToken)
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
