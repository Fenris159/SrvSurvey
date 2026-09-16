using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class ApplicationStartupInstanceGateTests
{
    [Fact]
    public async Task AuthorizedCommanderInstanceSkipsDetectionAndPrompt()
    {
        var manager = new RecordingInstanceManager(new ApplicationInstanceScan(1, 0));
        var gate = new ApplicationStartupInstanceGate(manager);
        bool prompted = false;

        ApplicationStartupInstanceDecision result = await gate.EvaluateAsync(
            concurrentInstanceAuthorized: true,
            (_, _, _) =>
            {
                prompted = true;
                return Task.FromResult(false);
            }
        );

        Assert.Equal(ApplicationStartupInstanceDecision.Continue, result);
        Assert.Equal(0, manager.ScanCount);
        Assert.False(prompted);
    }

    [Fact]
    public async Task OrdinaryLaunchContinuesWithoutPromptWhenNoOtherInstanceExists()
    {
        var manager = new RecordingInstanceManager(new ApplicationInstanceScan(0, 0));
        var gate = new ApplicationStartupInstanceGate(manager);
        bool prompted = false;

        ApplicationStartupInstanceDecision result = await gate.EvaluateAsync(
            concurrentInstanceAuthorized: false,
            (_, _, _) =>
            {
                prompted = true;
                return Task.FromResult(false);
            }
        );

        Assert.Equal(ApplicationStartupInstanceDecision.Continue, result);
        Assert.Equal(1, manager.ScanCount);
        Assert.False(prompted);
    }

    [Fact]
    public async Task UnverifiedOnlyMatchesDoNotPromptOrBlockStartup()
    {
        var manager = new RecordingInstanceManager(new ApplicationInstanceScan(0, 2));
        var gate = new ApplicationStartupInstanceGate(manager);
        bool prompted = false;

        ApplicationStartupInstanceDecision result = await gate.EvaluateAsync(
            concurrentInstanceAuthorized: false,
            (_, _, _) =>
            {
                prompted = true;
                return Task.FromResult(false);
            }
        );

        Assert.Equal(ApplicationStartupInstanceDecision.Continue, result);
        Assert.Equal(1, manager.ScanCount);
        Assert.False(prompted);
    }

    [Fact]
    public async Task DecliningReplacementStopsTheNewInstance()
    {
        var manager = new RecordingInstanceManager(new ApplicationInstanceScan(1, 0));
        var gate = new ApplicationStartupInstanceGate(manager);

        ApplicationStartupInstanceDecision result = await gate.EvaluateAsync(
            concurrentInstanceAuthorized: false,
            (_, _, _) => Task.FromResult(false)
        );

        Assert.Equal(ApplicationStartupInstanceDecision.Exit, result);
        Assert.Equal(0, manager.CloseCount);
    }

    [Fact]
    public async Task AcceptingReplacementClosesTheOldInstanceBeforeContinuing()
    {
        var manager = new RecordingInstanceManager(new ApplicationInstanceScan(1, 0));
        var gate = new ApplicationStartupInstanceGate(manager);

        ApplicationStartupInstanceDecision result = await gate.EvaluateAsync(
            concurrentInstanceAuthorized: false,
            async (_, closeOtherInstances, _) =>
            {
                await closeOtherInstances();
                return true;
            }
        );

        Assert.Equal(ApplicationStartupInstanceDecision.ReplacedExisting, result);
        Assert.Equal(1, manager.CloseCount);
    }

    private sealed class RecordingInstanceManager(ApplicationInstanceScan scan) : IApplicationInstanceManager
    {
        public int ScanCount { get; private set; }

        public int CloseCount { get; private set; }

        public Task<ApplicationInstanceScan> ScanOtherInstancesAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ScanCount++;
            return Task.FromResult(scan);
        }

        public async Task<int> CountOtherInstancesAsync(CancellationToken cancellationToken = default)
        {
            ApplicationInstanceScan result = await ScanOtherInstancesAsync(cancellationToken);
            return result.TotalCount;
        }

        public Task CloseOtherInstancesAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CloseCount++;
            return Task.CompletedTask;
        }
    }
}
