using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class ApplicationInstanceManagerTests
{
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
    [InlineData(null, "/opt/SrvSurvey/SrvSurvey.Desktop", false, true)]
    public void ExecutableMatchingUsesPlatformPathSemantics(
        string? candidate,
        string current,
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

    private static ApplicationInstanceManager CreateManager(
        params StubProcess[] processes)
    {
        return new ApplicationInstanceManager(
            new StubProcessSource(processes),
            TimeSpan.FromMilliseconds(1),
            TimeSpan.FromMilliseconds(1));
    }

    private sealed class StubProcessSource(
        IReadOnlyList<StubProcess> processes) : IApplicationInstanceProcessSource
    {
        public IReadOnlyList<IApplicationInstanceProcess> FindOtherInstances()
        {
            return processes.Cast<IApplicationInstanceProcess>().ToArray();
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

        public bool RequestGracefulExit()
        {
            GracefulExitRequests++;
            return GracefulExitSupported;
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
