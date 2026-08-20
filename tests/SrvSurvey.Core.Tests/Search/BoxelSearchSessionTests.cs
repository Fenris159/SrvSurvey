using System.Collections.Concurrent;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Core.Tests.Search;

public sealed class BoxelSearchSessionTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-BoxelSessionTests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SessionOwnsActivationPersistenceAndStableSnapshotSections()
    {
        var profileStore = new RecordingProfileStore();
        await using var session = CreateSession(profileStore);
        await session.SwitchProfileAsync(Profile(BoxelSearchSnapshot.Empty));
        var context = session.Current.Context;
        var health = session.Current.Health;

        var outcome = await session.ExecuteAsync(new ActivateBoxelSearch(
            Activation("Praea Euq IL-P c5-2")));

        Assert.Equal(BoxelSearchOutcomeKind.Success, outcome.Kind);
        Assert.True(session.Current.Search.IsActive);
        Assert.Equal("Praea Euq IL-P c5-0", session.Current.Search.NextSystem);
        Assert.Equal(
            "Praea Euq IL-P c5-0",
            session.Current.Search.NextSystemAscending);
        Assert.Equal(
            "Praea Euq IL-P c5-2",
            session.Current.Search.NextSystemDescending);
        Assert.Same(context, session.Current.Context);
        Assert.Same(health, session.Current.Health);
        Assert.NotEmpty(profileStore.Snapshots);
        Assert.True(profileStore.Snapshots[^1].Active);
    }

    [Fact]
    public async Task ReplayedContextUpdateDoesNotPublishAnotherSnapshot()
    {
        await using var session = CreateSession(new RecordingProfileStore());
        await session.SwitchProfileAsync(Profile(BoxelSearchSnapshot.Empty));
        var changes = 0;
        session.Changed += (_, _) => changes++;
        var update = new BoxelSearchUpdate
        {
            HasCurrentSystem = true,
            CurrentSystemName = "Praea Euq IL-P c5-0",
            CurrentSystemAddress = 123,
            CurrentPosition = new GalacticCoordinate(1, 2, 3),
        };

        var first = await session.ApplyAsync(update);
        var second = await session.ApplyAsync(update);

        Assert.Equal(BoxelSearchOutcomeKind.Success, first.Kind);
        Assert.Equal(BoxelSearchOutcomeKind.NoChange, second.Kind);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task LinkedSearchAutomaticallyReceivesStoppedProgress()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var profiles = new CommanderProfileStore(temporaryDirectory);
        var library = new SavedBoxelSearchStore(temporaryDirectory);
        await using var session = CreateSession(profiles, library);
        await session.SwitchProfileAsync(Profile(BoxelSearchSnapshot.Empty));
        await session.ExecuteAsync(new ActivateBoxelSearch(
            Activation("Praea Euq IL-P c5-2")));

        var saved = await session.ExecuteAsync(
            new SaveBoxelSearchToLibrary("Survey bookmark", "notes"));
        await session.ExecuteAsync(new MarkNextBoxelSystemEmpty());
        var fileName = Assert.IsType<SavedBoxelSearchDocument>(
            saved.SavedSearch).FileName;
        var runningDocument = await library.LoadAsync("F123", fileName);
        Assert.Equal(
            ["Praea Euq IL-P c5-0"],
            runningDocument.Search.EmptySystems);
        await session.ExecuteAsync(new StopBoxelSearch());

        var document = await library.LoadAsync("F123", fileName);
        var profile = await profiles.LoadAsync("F123", true);
        Assert.False(document.Search.Active);
        Assert.Equal(fileName, document.Search.SavedSearchFileName);
        Assert.Equal(fileName, profile.Data!.BoxelSearch.SavedSearchFileName);
    }

    [Fact]
    public async Task FailedProfilePersistenceRetriesAndRestoresHealth()
    {
        var profiles = new FlakyProfileStore();
        await using var session = CreateSession(
            profiles,
            options: new BoxelSearchSessionOptions
            {
                InitialRetryDelay = TimeSpan.FromMilliseconds(25),
                MaximumRetryDelay = TimeSpan.FromMilliseconds(25),
            });
        await session.SwitchProfileAsync(Profile(BoxelSearchSnapshot.Empty));

        var outcome = await session.ExecuteAsync(new ActivateBoxelSearch(
            Activation("Praea Euq IL-P c5-2")));

        Assert.Contains(
            outcome.Warnings ?? [],
            warning => warning.Subsystem
                == BoxelSearchHealthSubsystem.ProfilePersistence);
        Assert.Equal(BoxelSearchOutcomeKind.AppliedNotPersisted, outcome.Kind);
        Assert.Contains(
            BoxelSearchHealthSubsystem.ProfilePersistence,
            session.Current.Health.Issues.Keys);
        await WaitUntilAsync(() =>
            profiles.Snapshots.Length == 1
            && session.Current.Health.IsHealthy);
        Assert.True(profiles.Snapshots[0].Active);
    }

    [Fact]
    public async Task MissingLinkedEntryIsUnlinkedWithoutLosingProfileProgress()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var profiles = new CommanderProfileStore(temporaryDirectory);
        var snapshot = new BoxelSearchState();
        Assert.True(snapshot.TryActivate(
            Activation("Praea Euq IL-P c5-2"),
            out _));
        snapshot.SetSavedSearchFileName("missing.json");
        await using var session = CreateSession(
            profiles,
            new SavedBoxelSearchStore(temporaryDirectory));

        var outcome = await session.SwitchProfileAsync(Profile(snapshot.CreateSnapshot()));

        Assert.Null(session.Current.Search.SavedSearchFileName);
        Assert.True(session.Current.Search.IsActive);
        var persisted = await profiles.LoadAsync("F123", true);
        Assert.Null(persisted.Data!.BoxelSearch.SavedSearchFileName);
        Assert.NotEqual(BoxelSearchOutcomeKind.Rejected, outcome.Kind);
    }

    [Fact]
    public async Task RepeatedRefreshAwaitsTheCancelledRequestBeforeRestarting()
    {
        var resolver = new BlockingRefreshResolver();
        await using var session = CreateSession(
            new RecordingProfileStore(),
            systemResolver: resolver);
        await session.SwitchProfileAsync(Profile(BoxelSearchSnapshot.Empty));
        await session.ExecuteAsync(new ActivateBoxelSearch(
            Activation("Praea Euq IL-P c5-2")));

        var cancelledRefresh = session.ExecuteAsync(new RefreshCurrentBoxel());
        await resolver.Blocked.WaitAsync(TimeSpan.FromSeconds(2));
        var replacementRefreshes = new[]
        {
            session.ExecuteAsync(new RefreshCurrentBoxel()),
            session.ExecuteAsync(new RefreshCurrentBoxel()),
        };
        var outcomes = await Task.WhenAll(
                replacementRefreshes.Prepend(cancelledRefresh))
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(2, outcomes.Count(
            outcome => outcome.Kind == BoxelSearchOutcomeKind.Cancelled));
        Assert.Single(outcomes, outcome =>
            outcome.Kind == BoxelSearchOutcomeKind.Success);
        Assert.Equal(2, resolver.CancellationsObserved);
    }

    [Fact]
    public async Task ClipboardNotReadyDoesNotConsumeAutomaticCopyOpportunity()
    {
        var clipboard = new RecordingClipboard();
        await using var session = CreateSession(
            new RecordingProfileStore(),
            clipboard: clipboard);
        await session.SwitchProfileAsync(Profile(BoxelSearchSnapshot.Empty));
        await session.ExecuteAsync(new ActivateBoxelSearch(
            Activation("Praea Euq IL-P c5-2", autoCopy: true)));
        await session.ApplyAsync(new BoxelSearchUpdate
        {
            HasCurrentSystem = true,
            CurrentSystemName = "Praea Euq IL-P c5-0",
        });

        await session.ApplyAsync(new BoxelSearchUpdate
        {
            HasStatus = true,
            IsGalaxyMapOpen = true,
        });
        clipboard.IsReady = true;
        await session.ApplyAsync(new BoxelSearchUpdate
        {
            HasStatus = true,
            IsGalaxyMapOpen = true,
        });

        Assert.Equal(["Praea Euq IL-P c5-0"], clipboard.Writes);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private BoxelSearchSession CreateSession(
        IBoxelSearchProfileStore profileStore,
        IBoxelSearchLibraryStore? library = null,
        IBoxelClipboard? clipboard = null,
        IBoxelSystemResolver? systemResolver = null,
        BoxelSearchSessionOptions? options = null)
    {
        Directory.CreateDirectory(temporaryDirectory);
        return new BoxelSearchSession(
            profileStore,
            new LegacySystemDataReader(temporaryDirectory),
            new EmptyBoxelStore(temporaryDirectory),
            library ?? new SavedBoxelSearchStore(temporaryDirectory),
            systemResolver ?? new StubResolver(),
            clipboard,
            options: options);
    }

    private static BoxelSearchProfile Profile(BoxelSearchSnapshot snapshot)
    {
        return new BoxelSearchProfile("F123", "Drew", true, snapshot);
    }

    private static BoxelSearchActivationRequest Activation(
        string name,
        bool autoCopy = false)
    {
        return new BoxelSearchActivationRequest
        {
            TopBoxel = BoxelAddress.Parse(name),
            LowMassCode = 'c',
            StartedOn = DateTimeOffset.Parse("2026-08-20T00:00:00Z"),
            AutoCopy = autoCopy,
        };
    }

    private sealed class RecordingProfileStore : IBoxelSearchProfileStore
    {
        private readonly ConcurrentQueue<BoxelSearchSnapshot> snapshots = new();

        public BoxelSearchSnapshot[] Snapshots => snapshots.ToArray();

        public Task SaveBoxelSearchAsync(
            string frontierId,
            string? commanderName,
            bool isOdyssey,
            BoxelSearchSnapshot boxelSearch,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            snapshots.Enqueue(boxelSearch);
            return Task.CompletedTask;
        }
    }

    private sealed class FlakyProfileStore : IBoxelSearchProfileStore
    {
        private readonly ConcurrentQueue<BoxelSearchSnapshot> snapshots = new();
        private int failuresRemaining = 2;

        public BoxelSearchSnapshot[] Snapshots => snapshots.ToArray();

        public Task SaveBoxelSearchAsync(
            string frontierId,
            string? commanderName,
            bool isOdyssey,
            BoxelSearchSnapshot boxelSearch,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Decrement(ref failuresRemaining) >= 0)
            {
                throw new IOException("locked");
            }

            snapshots.Enqueue(boxelSearch);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingClipboard : IBoxelClipboard
    {
        private readonly ConcurrentQueue<string> writes = new();
        private int isReady;

        public bool IsReady
        {
            get => Volatile.Read(ref isReady) != 0;
            set => Volatile.Write(ref isReady, value ? 1 : 0);
        }

        public IReadOnlyList<string> Writes => writes.ToArray();

        public Task WriteTextAsync(
            string text,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            writes.Enqueue(text);
            return Task.CompletedTask;
        }
    }

    private sealed class StubResolver : IBoxelSystemResolver
    {
        public Task<IReadOnlyList<BoxelSystemObservation>> SearchAsync(
            BoxelAddress boxel,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<BoxelSystemObservation>>([]);
        }
    }

    private sealed class BlockingRefreshResolver : IBoxelSystemResolver
    {
        private readonly TaskCompletionSource blocked = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int calls;
        private int cancellationsObserved;

        public Task Blocked => blocked.Task;

        public int CancellationsObserved =>
            Volatile.Read(ref cancellationsObserved);

        public async Task<IReadOnlyList<BoxelSystemObservation>> SearchAsync(
            BoxelAddress boxel,
            CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref calls);
            if (call is not (2 or 3))
            {
                return [];
            }

            blocked.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return [];
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                Interlocked.Increment(ref cancellationsObserved);
                throw;
            }
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition() && !timeout.IsCancellationRequested)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }
}
