using System.Text.Json;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Search;

public sealed class MiningSearchSessionTests
{
    private sealed record Snapshot(string Status);

    [Fact]
    public async Task CompletedSearchReportsStartAndCompletionAroundOneDiagnosticWindow()
    {
        var provider = new StubMiningSearchProvider();
        using MiningSearchSession<Snapshot> session = Create(provider);
        var outcomes = new List<MiningSearchOutcomeKind>();
        var busy = new List<bool>();
        session.BusyChanged += () => busy.Add(session.IsBusy);

        await session.RunAsync(
            _ =>
            {
                Assert.True(session.IsBusy);
                return Task.CompletedTask;
            },
            outcome => outcomes.Add(outcome.Kind)
        );

        Assert.Equal([MiningSearchOutcomeKind.Started, MiningSearchOutcomeKind.Completed], outcomes);
        Assert.Equal([true, false], busy);
        Assert.Equal(1, provider.DiagnosticResets);
        Assert.Equal(1, provider.DiagnosticFlushes);
    }

    [Fact]
    public async Task NewerSearchSupersedesTheOlderOneWithoutLettingItReport()
    {
        using MiningSearchSession<Snapshot> session = Create(new StubMiningSearchProvider());
        var first = new List<MiningSearchOutcomeKind>();
        var second = new List<MiningSearchOutcomeKind>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task older = session.RunAsync(
            async token =>
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, token);
            },
            outcome => first.Add(outcome.Kind)
        );
        await started.Task;

        await session.RunAsync(_ => Task.CompletedTask, outcome => second.Add(outcome.Kind));
        await older;

        Assert.Equal([MiningSearchOutcomeKind.Started], first);
        Assert.Equal([MiningSearchOutcomeKind.Started, MiningSearchOutcomeKind.Completed], second);
        Assert.False(session.IsBusy);
    }

    [Fact]
    public async Task AbandonedSearchCannotReportOrReleaseALaterBusyState()
    {
        using MiningSearchSession<Snapshot> session = Create(new StubMiningSearchProvider());
        var outcomes = new List<MiningSearchOutcomeKind>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task abandoned = session.RunAsync(
            async _ =>
            {
                started.SetResult();
                await release.Task;
            },
            outcome => outcomes.Add(outcome.Kind)
        );
        await started.Task;

        session.Abandon();
        Assert.False(session.IsBusy);
        release.SetResult();
        await abandoned;

        Assert.Equal([MiningSearchOutcomeKind.Started], outcomes);
        Assert.False(session.IsBusy);
    }

    [Fact]
    public async Task ProgressFromCanceledOrSupersededSearchCannotChangeThePresentation()
    {
        using MiningSearchSession<Snapshot> session = Create(new StubMiningSearchProvider());
        var firstRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken firstToken = default;
        CancellationToken secondToken = default;
        string presentation = "";
        Task first = session.RunAsync(
            async token =>
            {
                firstToken = token;
                await firstRelease.Task;
                session.Publish(() => presentation = "stale result", token);
            },
            _ => { }
        );
        Task second = session.RunAsync(
            async token =>
            {
                secondToken = token;
                secondStarted.SetResult();
                await secondRelease.Task;
            },
            _ => { }
        );

        await secondStarted.Task;
        session.Publish(() => presentation = "stale progress", firstToken);
        session.Publish(() => presentation = "current progress", secondToken);
        Assert.Equal("current progress", presentation);
        session.Cancel();
        session.Publish(() => presentation = "canceled progress", secondToken);
        firstRelease.SetResult();
        secondRelease.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal("current progress", presentation);
        session.Publish(() => presentation = "completed progress", secondToken);
        Assert.Equal("current progress", presentation);
    }

    [Fact]
    public async Task CancellationIsCheckedBeforeWorkAndAfterWorkThatIgnoresIt()
    {
        using MiningSearchSession<Snapshot> session = Create(new StubMiningSearchProvider());
        var outcomes = new List<MiningSearchOutcomeKind>();
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        bool invoked = false;
        await session.RunAsync(
            _ =>
            {
                invoked = true;
                return Task.CompletedTask;
            },
            outcome => outcomes.Add(outcome.Kind),
            cancellationToken: canceled.Token
        );
        await session.RunAsync(
            _ =>
            {
                session.Cancel();
                return Task.CompletedTask;
            },
            outcome => outcomes.Add(outcome.Kind)
        );

        Assert.False(invoked);
        Assert.Equal(
            [
                MiningSearchOutcomeKind.Started,
                MiningSearchOutcomeKind.Canceled,
                MiningSearchOutcomeKind.Started,
                MiningSearchOutcomeKind.Canceled,
            ],
            outcomes
        );
    }

    [Fact]
    public async Task AbandonedReplacementDoesNotStartAfterWaitingForThePreviousCancellation()
    {
        using MiningSearchSession<Snapshot> session = Create(new StubMiningSearchProvider());
        var cancellationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWork = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseCancellation = new ManualResetEventSlim();
        bool replacementStarted = false;
        Task first = session.RunAsync(
            async token =>
            {
                using CancellationTokenRegistration registration = token.Register(() =>
                {
                    cancellationEntered.TrySetResult();
                    releaseCancellation.Wait(CancellationToken.None);
                });
                await releaseWork.Task;
            },
            _ => { }
        );
        Task replacement = session.RunAsync(
            _ =>
            {
                replacementStarted = true;
                return Task.CompletedTask;
            },
            _ => { }
        );
        try
        {
            await cancellationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            session.Abandon();
        }
        finally
        {
            releaseCancellation.Set();
            releaseWork.SetResult();
        }

        await Task.WhenAll(first, replacement);
        Assert.False(replacementStarted);
        Assert.False(session.IsBusy);
    }

    [Fact]
    public async Task CancellationAndTimeoutReportCanceled()
    {
        using MiningSearchSession<Snapshot> session = Create(new StubMiningSearchProvider());
        var outcomes = new List<MiningSearchOutcomeKind>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task canceled = session.RunAsync(
            async token =>
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, token);
            },
            outcome => outcomes.Add(outcome.Kind)
        );
        await started.Task;
        session.Cancel();
        await canceled;

        await session.RunAsync(
            token => Task.Delay(Timeout.Infinite, token),
            outcome => outcomes.Add(outcome.Kind),
            new MiningSearchRunOptions(TimeSpan.FromMilliseconds(10))
        );

        Assert.Equal(
            [
                MiningSearchOutcomeKind.Started,
                MiningSearchOutcomeKind.Canceled,
                MiningSearchOutcomeKind.Started,
                MiningSearchOutcomeKind.Canceled,
            ],
            outcomes
        );
    }

    [Fact]
    public async Task ProviderFailuresFailAndArgumentErrorsCarryTheirMessage()
    {
        using MiningSearchSession<Snapshot> session = Create(new StubMiningSearchProvider());
        var outcomes = new List<MiningSearchOutcome>();
        Exception[] failures =
        [
            new HttpRequestException("offline"),
            new JsonException("bad"),
            new IOException("disk"),
            new InvalidDataException("data"),
        ];

        foreach (Exception failure in failures)
        {
            await session.RunAsync(_ => throw failure, outcomes.Add);
        }

        await session.RunAsync(_ => throw new ArgumentException("Choose a commodity."), outcomes.Add);

        Assert.Equal(
            Enumerable.Repeat(MiningSearchOutcomeKind.Failed, 4).Append(MiningSearchOutcomeKind.Rejected),
            outcomes.Where(outcome => outcome.Kind != MiningSearchOutcomeKind.Started).Select(outcome => outcome.Kind)
        );
        Assert.Equal("Choose a commodity.", outcomes[^1].Message);
    }

    [Fact]
    public async Task InvalidOperationFailsOnlyWhenTheSearchOptsIn()
    {
        var provider = new StubMiningSearchProvider();
        using MiningSearchSession<Snapshot> session = Create(provider);
        var outcomes = new List<MiningSearchOutcomeKind>();

        await session.RunAsync(
            _ => throw new InvalidOperationException(),
            outcome => outcomes.Add(outcome.Kind),
            new MiningSearchRunOptions(InvalidOperationFails: true)
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.RunAsync(_ => throw new InvalidOperationException(), outcome => outcomes.Add(outcome.Kind))
        );

        Assert.Equal(
            [MiningSearchOutcomeKind.Started, MiningSearchOutcomeKind.Failed, MiningSearchOutcomeKind.Started],
            outcomes
        );
        Assert.False(session.IsBusy);
        Assert.Equal(2, provider.DiagnosticFlushes);
    }

    [Fact]
    public void FilterKeyMatchesTheJsonOfTheFilterValues()
    {
        string reference = "Sol";
        using var session = new MiningSearchSession<Snapshot>(
            new StubMiningSearchProvider(),
            "surface",
            () => new { Reference = reference.ToUpperInvariant(), Radius = 40d }
        );

        Assert.Equal(JsonSerializer.Serialize(new { Reference = "SOL", Radius = 40d }), session.FilterKey());
        reference = "Lave";
        Assert.Contains("LAVE", session.FilterKey(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SavedResultsRestoreByFilterKeyAndClearWhenMissing()
    {
        string reference = "Sol";
        var store = new InMemoryMiningSearchResultStore();
        using var session = new MiningSearchSession<Snapshot>(
            new StubMiningSearchProvider(),
            "surface",
            () => new { Reference = reference }
        );
        var restored = new List<string>();
        int cleared = 0;
        session.RestoreSaved(saved => restored.Add(saved.Status), () => cleared++);
        Assert.Null(session.LoadLast());
        session.Save(new Snapshot("unsaved"));
        Assert.Equal(0, store.Loads);

        session.UseStore(store, "planetary");
        session.Save(new Snapshot("Sol rows"));
        reference = "Lave";
        session.RestoreSaved(saved => restored.Add(saved.Status), () => cleared++);
        reference = "Sol";
        session.RestoreSaved(saved => restored.Add(saved.Status), () => cleared++);
        session.Restore(() => session.RestoreSaved(saved => restored.Add(saved.Status), () => cleared++));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task busy = session.RunAsync(
            async _ =>
            {
                started.SetResult();
                await release.Task;
            },
            _ => { }
        );
        await started.Task;
        session.RestoreSaved(saved => restored.Add(saved.Status), () => cleared++);
        release.SetResult();
        await busy;

        Assert.Equal(["Sol rows"], restored);
        Assert.Equal(1, cleared);
        Assert.Equal("planetary", session.Workspace);
        Assert.Equal("Sol rows", session.LoadLast()?.Status);
        Assert.Same(store, session.Store);
    }

    [Fact]
    public async Task ASearchCannotCacheItsResultsUnderFiltersEditedWhileItWasRunning()
    {
        string reference = "Sol";
        var store = new InMemoryMiningSearchResultStore();
        using var session = new MiningSearchSession<Snapshot>(
            new StubMiningSearchProvider(),
            "surface",
            () => new { Reference = reference }
        );
        session.UseStore(store, "surface");
        string originalKey = session.FilterKey();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task search = session.RunAsync(
            _ => release.Task,
            outcome =>
            {
                if (outcome.Kind == MiningSearchOutcomeKind.Completed)
                {
                    session.Save(new Snapshot("Sol results"));
                }
            }
        );
        reference = "Lave";
        release.SetResult();
        await search;

        Assert.Null(store.Load<Snapshot>("surface", originalKey));
        Assert.Null(store.Load<Snapshot>("surface", session.FilterKey()));
        Assert.Null(session.LoadLast());
    }

    [Fact]
    public void RestoreClearsTheRestoringFlagWhenApplyingFails()
    {
        using MiningSearchSession<Snapshot> session = Create(new StubMiningSearchProvider());
        bool restoring = false;

        Assert.Throws<InvalidOperationException>(() =>
            session.Restore(() =>
            {
                restoring = session.IsRestoring;
                throw new InvalidOperationException();
            })
        );

        Assert.True(restoring);
        Assert.False(session.IsRestoring);
    }

    [Fact]
    public void ReferenceFollowsTheCommanderUntilEditedAndRefillsAnEmptyField()
    {
        using MiningSearchSession<Snapshot> session = Create(new StubMiningSearchProvider());
        MiningSearchReference tracker = session.Reference;
        string reference = "";

        Assert.True(tracker.Move("Timbalderis", reference, out string next));
        reference = tracker.Edit(next, out bool refilled);
        Assert.Equal("Timbalderis", reference);
        Assert.False(refilled);

        reference = tracker.Edit("Sol", out _);
        Assert.False(tracker.Move("Wille", reference, out _));
        Assert.Equal("Sol", reference);

        reference = tracker.Edit("", out refilled);
        Assert.Equal("Wille", reference);
        Assert.True(refilled);
        Assert.True(tracker.Move("Lave", reference, out next));
        Assert.Equal("Lave", next);
        Assert.False(tracker.Move("", "Lave", out next));
        Assert.Equal("", tracker.CurrentSystem);
        Assert.Equal("", tracker.Edit(null, out refilled));
        Assert.False(refilled);
    }

    [Fact]
    public void RestoredReferenceStaysPinnedUntilTheCommanderEditsIt()
    {
        using MiningSearchSession<Snapshot> session = Create(new StubMiningSearchProvider());
        MiningSearchReference tracker = session.Reference;
        Assert.True(tracker.Move("Timbalderis", "", out _));

        session.Restore(() =>
        {
            tracker.Edit("Sol", out _);
            tracker.Pin();
            tracker.Edit("Sol", out _);
        });
        Assert.False(tracker.Move("Wille", "Sol", out _));
        Assert.False(tracker.Move("Lave", "Wille", out _));

        tracker.Edit("Sol", out _);
        Assert.False(tracker.Move("Achenar", "Sol", out _));
        Assert.True(tracker.Move("Achenar", "", out _));

        tracker.Pin();
        tracker.Unpin();
        tracker.Follow();
        Assert.True(tracker.Move("Diaguandri", "Sol", out string next));
        Assert.Equal("Diaguandri", next);
    }

    [Fact]
    public async Task DisposeCancelsTheSearchInFlight()
    {
        MiningSearchSession<Snapshot> session = Create(new StubMiningSearchProvider());
        var outcomes = new List<MiningSearchOutcomeKind>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task search = session.RunAsync(
            async token =>
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, token);
            },
            outcome => outcomes.Add(outcome.Kind)
        );
        await started.Task;

        session.Dispose();
        await search;

        Assert.Equal([MiningSearchOutcomeKind.Started], outcomes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AbandonAndDisposeKeepCancellationUsableUntilTheSearchFinishes(bool dispose)
    {
        var provider = new StubMiningSearchProvider();
        using MiningSearchSession<Snapshot> session = Create(provider);
        var outcomes = new List<MiningSearchOutcomeKind>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken searchToken = default;
        Task search = session.RunAsync(
            token =>
            {
                searchToken = token;
                return release.Task;
            },
            outcome => outcomes.Add(outcome.Kind)
        );

        try
        {
            if (dispose)
            {
                session.Dispose();
            }
            else
            {
                session.Abandon();
            }

            Assert.True(searchToken.IsCancellationRequested);
            Assert.True(searchToken.WaitHandle.WaitOne(0));
            Assert.False(session.IsBusy);
        }
        finally
        {
            release.SetResult();
            await search;
        }

        Assert.Throws<ObjectDisposedException>(() => searchToken.WaitHandle);
        Assert.Equal([MiningSearchOutcomeKind.Started], outcomes);
        Assert.Equal(1, provider.DiagnosticFlushes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AbandonAndDisposeInvalidateTheSearchBeforeInlineCancellationCompletes(bool dispose)
    {
        using MiningSearchSession<Snapshot> session = Create(new StubMiningSearchProvider());
        var outcomes = new List<MiningSearchOutcomeKind>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource();
        var search = Task.Run(() =>
            session.RunAsync(
                async token =>
                {
                    using CancellationTokenRegistration registration = token.Register(() =>
                        completion.TrySetCanceled(token)
                    );
                    started.SetResult();
                    await completion.Task.ConfigureAwait(false);
                },
                outcome => outcomes.Add(outcome.Kind)
            )
        );
        await started.Task;

        if (dispose)
        {
            session.Dispose();
        }
        else
        {
            session.Abandon();
        }

        await search;
        Assert.Equal([MiningSearchOutcomeKind.Started], outcomes);
        Assert.False(session.IsBusy);
    }

    private static MiningSearchSession<Snapshot> Create(StubMiningSearchProvider provider) =>
        new(provider, "surface", () => new { });
}
