using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class JournalProjectionPipelineTests
{
    [Fact]
    public async Task ChangedTickRunsFullProjectionsInRegistrationOrder()
    {
        var calls = new List<string>();
        JournalProjectionPipeline<RecordingTick> pipeline = new JournalProjectionPipeline<RecordingTick>()
            .Idle((_, _) => calls.Add("idle"))
            .Full((_, _) => calls.Add("full sync 1"))
            .FullAsync(
                async (_, _) =>
                {
                    await Task.Yield();
                    calls.Add("full async 2");
                }
            )
            .Full((_, _) => calls.Add("full sync 3"));

        await pipeline.ApplyAsync(Context(LiveChange()));

        Assert.Equal(["full sync 1", "full async 2", "full sync 3"], calls);
    }

    [Fact]
    public async Task IdlePollRunsOnlyIdleProjectionsInRegistrationOrder()
    {
        var calls = new List<string>();
        JournalProjectionPipeline<RecordingTick> pipeline = new JournalProjectionPipeline<RecordingTick>()
            .Idle((_, _) => calls.Add("idle sync 1"))
            .Full((_, _) => calls.Add("full"))
            .IdleAsync(
                async (_, _) =>
                {
                    await Task.Yield();
                    calls.Add("idle async 2");
                }
            );

        JournalProjectionContext context = Context(Unchanged());
        await pipeline.ApplyAsync(context);

        Assert.True(context.IsIdle);
        Assert.Equal(["idle sync 1", "idle async 2"], calls);
    }

    [Fact]
    public async Task ManualRefreshWithoutChangesRunsFullProjections()
    {
        var calls = new List<string>();
        JournalProjectionPipeline<RecordingTick> pipeline = new JournalProjectionPipeline<RecordingTick>()
            .Idle((_, _) => calls.Add("idle"))
            .Full((_, _) => calls.Add("full"));

        JournalProjectionContext context = Context(Unchanged(), isManualRefresh: true);
        await pipeline.ApplyAsync(context);

        Assert.False(context.IsIdle);
        Assert.Equal(["full"], calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EveryProjectionReceivesTheSameTickContext(bool isBootstrapRead)
    {
        using var cancellation = new CancellationTokenSource();
        JournalProjectionContext context = Context(
            isBootstrapRead ? BootstrapRead() : LiveChange(),
            cancellationToken: cancellation.Token
        );
        var observed = new List<JournalProjectionContext>();
        JournalProjectionPipeline<RecordingTick> pipeline = new JournalProjectionPipeline<RecordingTick>()
            .Full((received, _) => observed.Add(received))
            .FullAsync(
                (received, _) =>
                {
                    observed.Add(received);
                    return Task.CompletedTask;
                }
            );

        await pipeline.ApplyAsync(context);

        Assert.Equal(2, observed.Count);
        Assert.All(observed, received => Assert.Same(context, received));
        Assert.Equal(isBootstrapRead, context.IsBootstrapRead);
        Assert.Equal(!isBootstrapRead, context.IsLive);
        Assert.Equal(cancellation.Token, context.CancellationToken);
        Assert.Same(context.Update.JournalEvents, context.JournalEvents);
    }

    [Fact]
    public async Task AsyncProjectionCompletesBeforeTheNextProjectionStarts()
    {
        var calls = new List<string>();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        JournalProjectionPipeline<RecordingTick> pipeline = new JournalProjectionPipeline<RecordingTick>()
            .FullAsync(
                async (_, _) =>
                {
                    calls.Add("first started");
                    await gate.Task;
                    calls.Add("first completed");
                }
            )
            .Full((_, _) => calls.Add("second"));

        Task tick = pipeline.ApplyAsync(Context(LiveChange()));

        Assert.False(tick.IsCompleted);
        Assert.Equal(["first started"], calls);
        gate.SetResult();
        await tick;
        Assert.Equal(["first started", "first completed", "second"], calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FirstFailureEndsTheTickAndPropagatesUnchanged(bool failAsynchronously)
    {
        var calls = new List<string>();
        var failure = new IOException("journal locked");
        var pipeline = new JournalProjectionPipeline<RecordingTick>();
        pipeline.Full((_, _) => calls.Add("before"));
        if (failAsynchronously)
        {
            pipeline.FullAsync(
                async (_, _) =>
                {
                    await Task.Yield();
                    throw failure;
                }
            );
        }
        else
        {
            pipeline.Full((_, _) => throw failure);
        }

        pipeline.Full((_, _) => calls.Add("after"));

        IOException thrown = await Assert.ThrowsAsync<IOException>(() => pipeline.ApplyAsync(Context(LiveChange())));

        Assert.Same(failure, thrown);
        Assert.Equal(["before"], calls);
        await Assert.ThrowsAsync<IOException>(() => pipeline.ApplyAsync(Context(LiveChange())));
        Assert.Equal(["before", "before"], calls);
    }

    [Fact]
    public async Task LaterProjectionsReadHandoffsFromEarlierProjectionsOfTheSameTickOnly()
    {
        var observed = new List<string[]>();
        JournalProjectionPipeline<RecordingTick> pipeline = new JournalProjectionPipeline<RecordingTick>()
            .Full((context, tick) => tick.Handoffs.Add(context.IsBootstrapRead ? "bootstrap" : "live"))
            .FullAsync(
                (_, tick) =>
                {
                    tick.Handoffs.Add("async");
                    return Task.CompletedTask;
                }
            )
            .Full((_, tick) => observed.Add([.. tick.Handoffs]));

        await pipeline.ApplyAsync(Context(BootstrapRead()));
        await pipeline.ApplyAsync(Context(LiveChange()));

        string[][] expected =
        [
            ["bootstrap", "async"],
            ["live", "async"],
        ];
        Assert.Equal(expected, observed);
    }

    [Fact]
    public async Task RegistrationAndApplicationRejectMissingArguments()
    {
        var pipeline = new JournalProjectionPipeline<RecordingTick>();

        Assert.Throws<ArgumentNullException>(() => pipeline.Idle(null!));
        Assert.Throws<ArgumentNullException>(() => pipeline.IdleAsync(null!));
        Assert.Throws<ArgumentNullException>(() => pipeline.Full(null!));
        Assert.Throws<ArgumentNullException>(() => pipeline.FullAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => pipeline.ApplyAsync(null!));
    }

    private static JournalProjectionContext Context(
        JournalMonitorUpdate update,
        bool isManualRefresh = false,
        CancellationToken cancellationToken = default
    ) => new(update, isManualRefresh, cancellationToken);

    private static JournalMonitorUpdate Unchanged() =>
        new(null, [], null, null, null, null, [], IsBootstrapRead: false);

    private static JournalMonitorUpdate LiveChange() => Unchanged() with { SessionContextChanged = true };

    private static JournalMonitorUpdate BootstrapRead() => Unchanged() with { IsBootstrapRead = true };

    private sealed class RecordingTick
    {
        public List<string> Handoffs { get; } = [];
    }
}
