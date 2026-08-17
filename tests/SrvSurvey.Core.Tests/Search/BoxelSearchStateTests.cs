using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Search;

public sealed class BoxelSearchStateTests
{
    [Fact]
    public void EmptyStateDefaultsSearchStartToCurrentLocalDate()
    {
        var before = new DateTimeOffset(DateTime.Today);

        var state = new BoxelSearchState();

        var after = new DateTimeOffset(DateTime.Today);
        Assert.True(state.StartedOn == before || state.StartedOn == after);
    }

    [Fact]
    public void ActivationUsesEnteredSystemSuffixAsInitialExpectedCount()
    {
        var state = new BoxelSearchState();
        var enteredSystem = BoxelAddress.Parse("Praea Euq IL-P c5-385");

        Assert.True(state.TryActivate(
            new BoxelSearchActivationRequest
            {
                TopBoxel = enteredSystem,
                LowMassCode = 'c',
                StartedOn = DateTimeOffset.UtcNow,
            },
            out _));

        Assert.Equal(386, state.CurrentCount);
        Assert.Equal(386, state.CreateSnapshot().ProgressByPrefix[enteredSystem.Prefix]);
        Assert.Equal(enteredSystem.WithSystemNumber(0).Name, state.NextSystem);
    }

    [Fact]
    public void AutomaticSourcesOnlyRaiseTheHighestSuffixEstimate()
    {
        var state = new BoxelSearchState();
        var enteredSystem = BoxelAddress.Parse("Praea Euq IL-P c5-385");
        Assert.True(state.TryActivate(
            new BoxelSearchActivationRequest
            {
                TopBoxel = enteredSystem,
                LowMassCode = 'c',
                StartedOn = DateTimeOffset.UtcNow,
            },
            out _));

        state.MergeLocalSystems(
        [
            Observation(enteredSystem.WithSystemNumber(12).Name),
        ]);
        state.MergeRoute(
        [
            Observation(enteredSystem.WithSystemNumber(240).Name),
        ]);
        state.MergeSpanshSystems(
        [
            Observation(enteredSystem.WithSystemNumber(384).Name),
        ]);

        Assert.Equal(386, state.CurrentCount);

        state.MergeSpanshSystems(
        [
            Observation(enteredSystem.WithSystemNumber(410).Name),
        ]);

        Assert.Equal(411, state.CurrentCount);

        state.SetExpectedSystemCount(400);

        Assert.Equal(411, state.CurrentCount);

        state.SetExpectedSystemCount(500);
        state.MergeLocalSystems(
        [
            Observation(enteredSystem.WithSystemNumber(100).Name),
        ]);

        Assert.Equal(500, state.CurrentCount);
    }

    [Fact]
    public void SnapshotRestoresManualSystemCompletionAndAllBoxelCounts()
    {
        var state = new BoxelSearchState();
        var top = BoxelAddress.Parse("Praea Euq RS-U d2-0");
        Assert.True(state.TryActivate(
            new BoxelSearchActivationRequest
            {
                TopBoxel = top,
                LowMassCode = 'c',
                StartedOn = DateTimeOffset.UtcNow,
            },
            out _));
        state.SetExpectedSystemCount(3);
        state.MergeSpanshSystems(
        [
            new BoxelSystemObservation(
                top.WithSystemNumber(0),
                null,
                null,
                null,
                true),
            new BoxelSystemObservation(
                top.WithSystemNumber(1),
                null,
                null,
                null,
                true),
        ]);
        Assert.True(state.TrySetSystemComplete(top.WithSystemNumber(0).Name, true, out _));
        var child = top.Children[0];
        Assert.True(state.TrySetCurrent(child, out _));
        state.SetExpectedSystemCount(5);

        var restored = new BoxelSearchState(state.CreateSnapshot());
        Assert.True(restored.TrySetCurrent(top, out _));
        restored.MergeSpanshSystems(
        [
            new BoxelSystemObservation(
                top.WithSystemNumber(0),
                null,
                null,
                null,
                true),
            new BoxelSystemObservation(
                top.WithSystemNumber(1),
                null,
                null,
                null,
                true),
        ]);

        Assert.True(restored.Systems[0].IsComplete);
        Assert.False(restored.Systems[1].IsComplete);
        Assert.Equal(8, restored.TotalKnownSystemCount);
        Assert.Equal(1, restored.TotalCompletedSystemCount);
    }

    [Fact]
    public void LegacyCompletedPrefixKeepsItsSystemsCompleteAfterRefresh()
    {
        var top = BoxelAddress.Parse("Praea Euq IL-P c5-0");
        var state = new BoxelSearchState(new BoxelSearchSnapshot
        {
            Active = true,
            TopBoxel = top,
            Current = top,
            CurrentCount = 2,
            CompletedPrefixes = [top.Prefix],
        });

        state.MergeSpanshSystems(
        [
            new BoxelSystemObservation(
                top.WithSystemNumber(0),
                null,
                null,
                null,
                true),
            new BoxelSystemObservation(
                top.WithSystemNumber(1),
                null,
                null,
                null,
                true),
        ]);

        Assert.All(state.Systems, system => Assert.True(system.IsComplete));
        Assert.Equal(2, state.TotalCompletedSystemCount);
    }

    [Fact]
    public void HandledSystemCountUsesRecordedCollectionsAndDeduplicatesSystems()
    {
        var top = BoxelAddress.Parse("Praea Euq IL-P c5-0");
        var state = new BoxelSearchState(new BoxelSearchSnapshot
        {
            Active = true,
            TopBoxel = top,
            Current = top,
            CurrentCount = 2,
            CompletionMode = BoxelCompletionMode.FssAllBodies,
            CompletedSystems =
            [
                top.WithSystemNumber(500).GeneratedName,
                top.WithSystemNumber(1).GeneratedName,
            ],
            EmptySystems =
            [
                top.WithSystemNumber(1).GeneratedName,
                top.WithSystemNumber(600).GeneratedName,
            ],
        });

        state.MergeSpanshSystems(
        [
            new BoxelSystemObservation(
                top.WithSystemNumber(0),
                null,
                null,
                null,
                true,
                true),
        ]);

        Assert.True(state.TrySetSystemComplete(top.GeneratedName, true, out _));
        Assert.Single(state.Systems);
        Assert.True(state.Systems[0].IsComplete);
        Assert.Contains(top.WithSystemNumber(600).GeneratedName, state.EmptySystems);
        Assert.Equal(4, state.CompletedSystemCount);
    }

    [Fact]
    public void ActivateBuildsChildProgressAndRejectsMassCodeH()
    {
        var state = new BoxelSearchState();

        var activated = state.TryActivate(
            new BoxelSearchActivationRequest
            {
                TopBoxel = BoxelAddress.Parse("Praea Euq RS-U d2-0"),
                LowMassCode = 'b',
                StartedOn = DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
                SkipAlreadyVisited = false,
                SkipKnownToSpansh = false,
                CompletionMode = BoxelCompletionMode.EnterSystem,
                AutoCopy = true
            },
            out var error);

        Assert.True(activated, error);
        Assert.True(state.IsActive);
        Assert.Equal(73, state.TotalBoxelCount);
        Assert.Equal("Praea Euq RS-U d2-0", state.NextSystem);

        Assert.False(state.TryActivate(
            new BoxelSearchActivationRequest
            {
                TopBoxel = BoxelAddress.Parse("Dryio Flyuae AA-A h0"),
                LowMassCode = 'h',
                StartedOn = DateTimeOffset.UtcNow,
                SkipAlreadyVisited = false,
                SkipKnownToSpansh = false,
                CompletionMode = BoxelCompletionMode.EnterSystem,
                AutoCopy = false
            },
            out error));
        Assert.Contains("Mass-code h", error);
    }

    [Fact]
    public void CollectionProjectionsKeepStableIdentityUntilStateChanges()
    {
        var state = CreateActiveState(BoxelCompletionMode.EnterSystem);
        var systems = state.Systems;
        var boxels = state.Boxels;
        var emptyBoxels = state.EmptyBoxelPrefixes;

        Assert.Same(systems, state.Systems);
        Assert.Same(boxels, state.Boxels);
        Assert.Same(emptyBoxels, state.EmptyBoxelPrefixes);

        state.MergeSpanshSystems([Observation("Praea Euq IL-P c5-0")]);

        Assert.NotSame(systems, state.Systems);
        Assert.Single(state.Systems);
    }

    [Fact]
    public void ActivatingDifferentTopBoxelResetsExpectedSystemCount()
    {
        var state = CreateActiveState(BoxelCompletionMode.EnterSystem);
        state.SetExpectedSystemCount(45);

        state.TryActivate(
            new BoxelSearchActivationRequest
            {
                TopBoxel = BoxelAddress.Parse("Wregoe BU-Y b2-0"),
                LowMassCode = 'b',
                StartedOn = DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
                SkipAlreadyVisited = false,
                SkipKnownToSpansh = false,
                CompletionMode = BoxelCompletionMode.EnterSystem,
                AutoCopy = true
            },
            out _);

        Assert.Equal(1, state.CurrentCount);
        Assert.Equal("Wregoe BU-Y b2-0", state.NextSystem);
    }

    [Fact]
    public void EnterSystemModeCompletesFsdJumpAndSelectsFirstIncompleteSystem()
    {
        var state = CreateActiveState(BoxelCompletionMode.EnterSystem);
        state.MergeSpanshSystems(
        [
            Observation("Praea Euq IL-P c5-0"),
            Observation("Praea Euq IL-P c5-1"),
            Observation("Praea Euq IL-P c5-2"),
        ]);

        var handled = state.Apply(Parse(
            """{"timestamp":"2026-07-10T12:00:00Z","event":"FSDJump","StarSystem":"Praea Euq IL-P c5-2","SystemAddress":123,"StarPos":[1,2,3]}"""));

        Assert.True(handled);
        Assert.Equal(1, state.CompletedSystemCount);
        Assert.Equal("Praea Euq IL-P c5-0", state.NextSystem);
        Assert.Equal(new GalacticCoordinate(1, 2, 3), state.Systems[2].Position);
    }

    [Fact]
    public void NextSystemUsesLowestIncompleteSuffixInLargeBoxel()
    {
        var top = BoxelAddress.Parse("Leamae UK-D d13-0");
        var state = new BoxelSearchState(new BoxelSearchSnapshot
        {
            Active = true,
            TopBoxel = top,
            Current = top,
            CurrentCount = 893,
            LowMassCode = 'd',
            CompletedSystems =
            [
                "Leamae UK-D d13-0",
                "Leamae UK-D d13-3",
                "Leamae UK-D d13-890",
            ],
            ProgressByPrefix = new Dictionary<string, int>
            {
                [top.Prefix] = 893,
            },
        });
        state.MergeSpanshSystems(Enumerable.Range(0, 893).Select(number =>
            new BoxelSystemObservation(
                top.WithSystemNumber(number),
                null,
                null,
                null,
                true)));

        Assert.Equal("Leamae UK-D d13-1", state.NextSystem);
    }

    [Fact]
    public void DescendingSearchUsesHighestIncompleteSuffixAndPersistsDirection()
    {
        var top = BoxelAddress.Parse("Praea Euq IL-P c5-0");
        var state = new BoxelSearchState();
        Assert.True(state.TryActivate(
            new BoxelSearchActivationRequest
            {
                TopBoxel = top,
                LowMassCode = 'c',
                StartedOn = DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
                SortDescending = true,
            },
            out _));
        state.SetExpectedSystemCount(5);

        Assert.Equal("Praea Euq IL-P c5-4", state.NextSystem);
        Assert.True(state.TryMarkNextSystemEmpty(out var marked, out _));
        Assert.Equal("Praea Euq IL-P c5-4", marked);
        Assert.Equal("Praea Euq IL-P c5-3", state.NextSystem);

        var restored = new BoxelSearchState(state.CreateSnapshot());

        Assert.True(restored.SortDescending);
        Assert.Equal("Praea Euq IL-P c5-3", restored.NextSystem);
    }

    [Fact]
    public void DeferredSystemsAreSkippedWithoutCountingAsCompleteAndPersist()
    {
        var top = BoxelAddress.Parse("Praea Euq IL-P c5-0");
        var state = new BoxelSearchState(new BoxelSearchSnapshot
        {
            Active = true,
            TopBoxel = top,
            Current = top,
            CurrentCount = 4,
            LowMassCode = 'c',
            ProgressByPrefix = new Dictionary<string, int>
            {
                [top.Prefix] = 4,
            },
        });

        Assert.True(state.TrySetSystemDeferred(top.WithSystemNumber(0).Name, true, out _));
        Assert.True(state.TrySetSystemDeferred(top.WithSystemNumber(1).Name, true, out _));

        Assert.Equal(top.WithSystemNumber(2).Name, state.NextSystem);
        Assert.Equal(0, state.CompletedSystemCount);
        Assert.False(state.CurrentSystemsComplete);
        Assert.Equal(
            [top.WithSystemNumber(0).GeneratedName, top.WithSystemNumber(1).GeneratedName],
            state.DeferredSystems.Order(StringComparer.Ordinal));

        var restored = new BoxelSearchState(state.CreateSnapshot());

        Assert.Equal(top.WithSystemNumber(2).Name, restored.NextSystem);
        Assert.Equal(
            state.DeferredSystems.Order(StringComparer.Ordinal),
            restored.DeferredSystems.Order(StringComparer.Ordinal));

        Assert.True(restored.MergeSpanshSystems([
            Observation(top.WithSystemNumber(0).Name)
        ]));
        Assert.True(restored.TrySetSystemComplete(
            top.WithSystemNumber(0).Name,
            true,
            out _));
        Assert.DoesNotContain(
            top.WithSystemNumber(0).GeneratedName,
            restored.DeferredSystems);
        Assert.Equal(1, restored.CompletedSystemCount);
    }

    [Theory]
    [InlineData(false, 3, 0, 1, 2)]
    [InlineData(true, 2, 5, 4, 3)]
    public void StartAtSystemDefersEarlierUnfinishedSystemsInSearchDirection(
        bool descending,
        int startSuffix,
        params int[] expectedDeferredSuffixes)
    {
        var top = BoxelAddress.Parse("Praea Euq IL-P c5-0");
        var state = new BoxelSearchState(new BoxelSearchSnapshot
        {
            Active = true,
            TopBoxel = top,
            Current = top,
            CurrentCount = 6,
            LowMassCode = 'c',
            SortDescending = descending,
            ProgressByPrefix = new Dictionary<string, int>
            {
                [top.Prefix] = 6,
            },
        });

        Assert.True(state.TryStartAtSystem(
            top.WithSystemNumber(startSuffix).Name,
            out var deferredCount,
            out var error));

        Assert.Null(error);
        Assert.Equal(expectedDeferredSuffixes.Length, deferredCount);
        Assert.Equal(top.WithSystemNumber(startSuffix).Name, state.NextSystem);
        Assert.Empty(state.DeferredSystems);
        var range = Assert.Single(state.DeferredRanges);
        Assert.Equal(top.Prefix, range.Prefix);
        Assert.Equal(startSuffix, range.StartSystemNumber);
        Assert.Equal(descending, range.SortDescending);
        Assert.All(expectedDeferredSuffixes, suffix =>
            Assert.True(state.IsSystemDeferred(top.Prefix, suffix)));
        Assert.False(state.IsSystemDeferred(top.Prefix, startSuffix));

        var restored = new BoxelSearchState(state.CreateSnapshot());

        Assert.Equal(top.WithSystemNumber(startSuffix).Name, restored.NextSystem);
        Assert.All(expectedDeferredSuffixes, suffix =>
            Assert.True(restored.IsSystemDeferred(top.Prefix, suffix)));
    }

    [Fact]
    public void StartAtSystemUsesCompactBoundaryAndAllowsOneDeferredSystemToReopen()
    {
        var top = BoxelAddress.Parse("Praea Euq IL-P c5-0");
        var state = new BoxelSearchState(new BoxelSearchSnapshot
        {
            Active = true,
            TopBoxel = top,
            Current = top,
            CurrentCount = 100_000,
            LowMassCode = 'c',
            ProgressByPrefix = new Dictionary<string, int>
            {
                [top.Prefix] = 100_000,
            },
        });

        Assert.True(state.TryStartAtSystem(
            top.WithSystemNumber(90_000).Name,
            out var deferredCount,
            out _));

        Assert.Equal(90_000, deferredCount);
        Assert.Empty(state.DeferredSystems);
        var range = Assert.Single(state.CreateSnapshot().DeferredRanges);
        Assert.Empty(range.Exceptions);
        Assert.True(state.IsSystemDeferred(top.Prefix, 42_000));

        Assert.True(state.TrySetSystemDeferred(
            top.WithSystemNumber(42_000).Name,
            false,
            out _));

        Assert.False(state.IsSystemDeferred(top.Prefix, 42_000));
        Assert.Equal([42_000], Assert.Single(state.DeferredRanges).Exceptions);
        Assert.Equal(top.WithSystemNumber(42_000).Name, state.NextSystem);
    }

    [Fact]
    public void DeferredRangeWithUpdatedExceptionsRebuildsItsLookup()
    {
        var range = new BoxelDeferredRangeSnapshot
        {
            Prefix = "Praea Euq IL-P c5-",
            StartSystemNumber = 5,
            Exceptions = [1],
        };

        Assert.False(range.Contains(1));
        Assert.True(range.Contains(2));

        var updated = range with { Exceptions = [2] };

        Assert.True(updated.Contains(1));
        Assert.False(updated.Contains(2));
        Assert.Equal([2], updated.Exceptions);
    }

    [Fact]
    public void DeferredBoundaryValidatesActionsAndExcludesHandledSystems()
    {
        var top = BoxelAddress.Parse("Praea Euq IL-P c5-0");
        var state = new BoxelSearchState(new BoxelSearchSnapshot
        {
            Active = true,
            TopBoxel = top,
            Current = top,
            CurrentCount = 6,
            LowMassCode = 'c',
            CompletedSystems = [top.WithSystemNumber(0).GeneratedName],
            EmptySystems = [top.WithSystemNumber(1).GeneratedName],
            ProgressByPrefix = new Dictionary<string, int>
            {
                [top.Prefix] = 6,
            },
        });

        Assert.False(state.TrySetSystemDeferred(
            top.WithSystemNumber(8).Name,
            true,
            out var outsideError));
        Assert.Contains("current boxel", outsideError, StringComparison.Ordinal);
        Assert.False(state.TrySetSystemDeferred(
            top.WithSystemNumber(0).Name,
            true,
            out var handledError));
        Assert.Contains("already complete", handledError, StringComparison.Ordinal);
        Assert.False(state.TryStartAtSystem(
            top.WithSystemNumber(0).Name,
            out _,
            out handledError));
        Assert.Contains("already complete", handledError, StringComparison.Ordinal);

        Assert.True(state.TryStartAtSystem(
            top.WithSystemNumber(4).Name,
            out var deferredCount,
            out _));

        Assert.Equal(2, deferredCount);
        Assert.Equal([0, 1], Assert.Single(state.DeferredRanges).Exceptions);
        Assert.False(state.IsSystemDeferred(top.Prefix, -1));
        Assert.False(state.IsSystemDeferred(top.Prefix, 0));
        Assert.True(state.IsSystemDeferred(top.Prefix, 2));
        Assert.False(state.TrySetSystemDeferred(
            top.WithSystemNumber(2).Name,
            true,
            out var alreadyDeferredError));
        Assert.Contains("already deferred", alreadyDeferredError, StringComparison.Ordinal);
        Assert.True(state.TrySetSystemDeferred(
            top.WithSystemNumber(2).Name,
            false,
            out _));
        Assert.False(state.TrySetSystemDeferred(
            top.WithSystemNumber(2).Name,
            false,
            out var notDeferredError));
        Assert.Contains("not deferred", notDeferredError, StringComparison.Ordinal);
        Assert.True(state.TrySetSystemDeferred(
            top.WithSystemNumber(2).Name,
            true,
            out _));

        Assert.True(state.TrySetSystemDeferred(
            top.WithSystemNumber(5).Name,
            true,
            out _));
        Assert.False(state.TrySetSystemDeferred(
            top.WithSystemNumber(5).Name,
            true,
            out _));
        Assert.True(state.TrySetSystemDeferred(
            top.WithSystemNumber(5).Name,
            false,
            out _));

        Assert.True(state.MergeSpanshSystems([
            Observation(top.WithSystemNumber(3).Name)
        ]));
        Assert.True(state.TrySetSystemComplete(
            top.WithSystemNumber(3).Name,
            true,
            out _));
        Assert.False(state.IsSystemDeferred(top.Prefix, 3));
        Assert.True(state.TrySetSystemComplete(
            top.WithSystemNumber(3).Name,
            false,
            out _));
        Assert.False(state.IsSystemDeferred(top.Prefix, 3));

        Assert.False(state.TryStartAtSystem(
            top.WithSystemNumber(0).Name,
            out _,
            out _));
        Assert.True(state.MergeSpanshSystems([
            Observation(top.WithSystemNumber(0).Name)
        ]));
        Assert.True(state.TrySetSystemComplete(
            top.WithSystemNumber(0).Name,
            false,
            out _));
        Assert.True(state.TryStartAtSystem(
            top.WithSystemNumber(0).Name,
            out var zeroDeferred,
            out _));
        Assert.Equal(0, zeroDeferred);
        Assert.Empty(state.DeferredRanges);
        Assert.True(state.TryStartAtSystem(
            top.WithSystemNumber(2).Name,
            out _,
            out _));
        Assert.True(state.TrySetSystemEmpty(
            top.WithSystemNumber(1).Name,
            false,
            out _));
        Assert.False(state.IsSystemDeferred(top.Prefix, 1));
    }

    [Fact]
    public void RestoreNormalizesDeferredBoundariesAndExplicitSystems()
    {
        var top = BoxelAddress.Parse("Praea Euq IL-P c5-0");
        var state = new BoxelSearchState(new BoxelSearchSnapshot
        {
            Active = true,
            TopBoxel = top,
            Current = top,
            CurrentCount = 5,
            DeferredSystems =
            [
                "not a system",
                top.WithSystemNumber(1).GeneratedName,
                top.WithSystemNumber(4).GeneratedName,
            ],
            DeferredRanges =
            [
                new BoxelDeferredRangeSnapshot
                {
                    Prefix = "not a prefix",
                    StartSystemNumber = 2,
                },
                new BoxelDeferredRangeSnapshot
                {
                    Prefix = top.Prefix,
                    StartSystemNumber = -1,
                },
                new BoxelDeferredRangeSnapshot
                {
                    Prefix = top.Prefix,
                    StartSystemNumber = 3,
                    Exceptions = [-1, 0, 0],
                },
            ],
            ProgressByPrefix = new Dictionary<string, int>
            {
                [top.Prefix] = 5,
            },
        });

        var range = Assert.Single(state.DeferredRanges);
        Assert.Equal([0], range.Exceptions);
        Assert.DoesNotContain(top.WithSystemNumber(1).GeneratedName, state.DeferredSystems);
        Assert.Contains(top.WithSystemNumber(4).GeneratedName, state.DeferredSystems);
        Assert.True(state.IsSystemDeferred(top.Prefix, 1));
        Assert.True(state.IsSystemDeferred(top.Prefix, 4));
    }

    [Fact]
    public void FssModeWaitsForAllBodiesEvent()
    {
        var state = CreateActiveState(BoxelCompletionMode.FssAllBodies);

        state.Apply(Parse(
            """{"timestamp":"2026-07-10T12:00:00Z","event":"FSDJump","StarSystem":"Praea Euq IL-P c5-0","SystemAddress":123,"StarPos":[1,2,3]}"""));
        Assert.Equal(0, state.CompletedSystemCount);

        state.Apply(Parse(
            """{"timestamp":"2026-07-10T12:05:00Z","event":"FSSAllBodiesFound","SystemName":"Praea Euq IL-P c5-0","SystemAddress":123,"Count":4}"""));

        Assert.Equal(1, state.CompletedSystemCount);
        Assert.True(state.CurrentSystemsComplete);
        Assert.Contains("Praea Euq IL-P c5-", state.CreateSnapshot().CompletedPrefixes);
    }

    [Fact]
    public void OlderSpanshBodiesRemainACompletionRuleInFssMode()
    {
        var state = new BoxelSearchState();
        state.TryActivate(
            new BoxelSearchActivationRequest
            {
                TopBoxel = BoxelAddress.Parse("Praea Euq IL-P c5-0"),
                LowMassCode = 'c',
                StartedOn = DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
                SkipAlreadyVisited = false,
                SkipKnownToSpansh = true,
                CompletionMode = BoxelCompletionMode.FssAllBodies,
                AutoCopy = false,
            },
            out _);

        state.MergeSpanshSystems(
        [
            Observation(
                "Praea Euq IL-P c5-0",
                spansh: DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                hasBodies: true),
            Observation(
                "Praea Euq IL-P c5-1",
                spansh: DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                hasBodies: false),
        ]);

        Assert.True(state.Systems.Single(system => system.Boxel.N2 == 0).IsComplete);
        Assert.False(state.Systems.Single(system => system.Boxel.N2 == 1).IsComplete);
    }

    [Fact]
    public void SkipRulesMatchLegacyDatesAndKnownBodyRequirement()
    {
        var state = new BoxelSearchState();
        state.TryActivate(
            new BoxelSearchActivationRequest
            {
                TopBoxel = BoxelAddress.Parse("Praea Euq IL-P c5-0"),
                LowMassCode = 'c',
                StartedOn = DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
                SkipAlreadyVisited = true,
                SkipKnownToSpansh = true,
                CompletionMode = BoxelCompletionMode.EnterSystem,
                AutoCopy = false
            },
            out _);

        state.MergeLocalSystems(
        [
            Observation(
                "Praea Euq IL-P c5-0",
                visited: DateTimeOffset.Parse("2026-06-01T00:00:00Z")),
        ]);
        state.MergeSpanshSystems(
        [
            Observation(
                "Praea Euq IL-P c5-1",
                spansh: DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                hasBodies: true),
            Observation(
                "Praea Euq IL-P c5-2",
                spansh: DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                hasBodies: false),
        ]);

        Assert.True(state.Systems.Single(system => system.Boxel.N2 == 0).IsComplete);
        Assert.True(state.Systems.Single(system => system.Boxel.N2 == 1).IsComplete);
        Assert.False(state.Systems.Single(system => system.Boxel.N2 == 2).IsComplete);
    }

    [Fact]
    public void ManualCompletionRequiresKnownSystemAndEmptyBoxelAdvances()
    {
        var state = new BoxelSearchState();
        state.TryActivate(
            new BoxelSearchActivationRequest
            {
                TopBoxel = BoxelAddress.Parse("Praea Euq RS-U d2-0"),
                LowMassCode = 'c',
                StartedOn = DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
                SkipAlreadyVisited = false,
                SkipKnownToSpansh = false,
                CompletionMode = BoxelCompletionMode.EnterSystem,
                AutoCopy = true
            },
            out _);

        Assert.False(state.TrySetSystemComplete(
            "Praea Euq IL-P c5-0",
            true,
            out var error));
        Assert.Contains("discovered or visited", error);

        state.SetCurrentEmpty(true);

        Assert.True(state.CurrentIsEmpty);
        Assert.Equal("Praea Euq IL-P c5-", state.NextSystem);
    }

    [Fact]
    public void MarkingNextSystemEmptySkipsOnlyThatSystemAndCanBeRestored()
    {
        var state = new BoxelSearchState();
        state.TryActivate(
            new BoxelSearchActivationRequest
            {
                TopBoxel = BoxelAddress.Parse("Praea Euq IL-P c5-0"),
                LowMassCode = 'c',
                StartedOn = DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
                SkipAlreadyVisited = false,
                SkipKnownToSpansh = false,
                CompletionMode = BoxelCompletionMode.EnterSystem,
                AutoCopy = false,
            },
            out _);
        state.SetExpectedSystemCount(3);

        Assert.True(state.TryMarkNextSystemEmpty(out var marked, out var error));

        Assert.Null(error);
        Assert.Equal("Praea Euq IL-P c5-0", marked);
        var markedSystem = Assert.IsType<string>(marked);
        Assert.Equal("Praea Euq IL-P c5-1", state.NextSystem);
        Assert.Contains("Praea Euq IL-P c5-0", state.EmptySystems);
        Assert.Equal(1, state.CompletedSystemCount);
        Assert.False(state.CurrentIsEmpty);

        var observed = new BoxelSearchState(state.CreateSnapshot());
        Assert.True(observed.MergeSpanshSystems([
            Observation("Praea Euq IL-P c5-0")
        ]));
        Assert.DoesNotContain("Praea Euq IL-P c5-0", observed.EmptySystems);
        Assert.Equal("Praea Euq IL-P c5-0", observed.NextSystem);

        var restored = new BoxelSearchState(state.CreateSnapshot());
        Assert.Equal("Praea Euq IL-P c5-1", restored.NextSystem);
        Assert.True(restored.TrySetSystemEmpty(markedSystem, false, out error));
        Assert.Null(error);
        Assert.Equal("Praea Euq IL-P c5-0", restored.NextSystem);
    }

    [Fact]
    public void ObservingPersistedEmptySystemReopensCompletedBoxel()
    {
        var state = new BoxelSearchState();
        state.TryActivate(
            new BoxelSearchActivationRequest
            {
                TopBoxel = BoxelAddress.Parse("Praea Euq IL-P c5-0"),
                LowMassCode = 'c',
                StartedOn = DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
                SkipAlreadyVisited = false,
                SkipKnownToSpansh = false,
                CompletionMode = BoxelCompletionMode.EnterSystem,
                AutoCopy = false,
            },
            out _);

        Assert.True(state.TryMarkNextSystemEmpty(out _, out _));
        Assert.True(state.CurrentSystemsComplete);

        var restored = new BoxelSearchState(state.CreateSnapshot());
        Assert.True(restored.MergeSpanshSystems([
            Observation("Praea Euq IL-P c5-0")
        ]));

        Assert.Empty(restored.EmptySystems);
        Assert.False(restored.CurrentSystemsComplete);
        Assert.Equal("Praea Euq IL-P c5-0", restored.NextSystem);
    }

    [Fact]
    public void LegacyEmptyIdsAreAppliedAcrossTheWholeSearchTree()
    {
        var state = new BoxelSearchState();
        state.TryActivate(
            new BoxelSearchActivationRequest
            {
                TopBoxel = BoxelAddress.Parse("Praea Euq RS-U d2-0"),
                LowMassCode = 'c',
                StartedOn = DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
                SkipAlreadyVisited = false,
                SkipKnownToSpansh = false,
                CompletionMode = BoxelCompletionMode.EnterSystem,
                AutoCopy = true
            },
            out _);
        var firstChild = state.TopBoxel!.Children[0];

        state.ApplyEmptyBoxels([state.TopBoxel.Id, firstChild.Id]);

        Assert.True(state.CurrentIsEmpty);
        Assert.DoesNotContain(state.TopBoxel.Prefix, state.NextSystem);
        state.SetAutoCopy(false);
        Assert.False(state.AutoCopy);
        Assert.Equal(9, state.Boxels.Count);
    }

    [Fact]
    public void HandAuthoredJournalSystemCompletesDecodedBoxel()
    {
        Assert.True(BoxelAddress.TryFromSystemAddress(
            10477373803,
            "Sol",
            out var sol));
        var state = new BoxelSearchState();
        Assert.True(state.TryActivate(
            new BoxelSearchActivationRequest
            {
                TopBoxel = sol,
                LowMassCode = sol!.MassCode,
                StartedOn = DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
                SkipAlreadyVisited = false,
                SkipKnownToSpansh = false,
                CompletionMode = BoxelCompletionMode.EnterSystem,
                AutoCopy = true
            },
            out var error), error);

        var handled = state.Apply(Parse(
            """{"timestamp":"2026-07-24T12:00:00Z","event":"FSDJump","StarSystem":"Sol","SystemAddress":10477373803,"StarPos":[0,0,0]}"""));

        Assert.True(handled);
        var system = Assert.Single(state.Systems);
        Assert.Equal("Sol", system.Boxel.Name);
        Assert.Equal(sol.GeneratedName, system.Boxel.GeneratedName);
        Assert.True(system.IsComplete);
    }

    [Fact]
    public void CompletionAuditUpdatesContainedProgressWithoutChangingCurrent()
    {
        var state = new BoxelSearchState();
        state.TryActivate(
            new BoxelSearchActivationRequest
            {
                TopBoxel = BoxelAddress.Parse("Praea Euq RS-U d2-0"),
                LowMassCode = 'c',
                StartedOn = DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
                SkipAlreadyVisited = false,
                SkipKnownToSpansh = false,
                CompletionMode = BoxelCompletionMode.EnterSystem,
                AutoCopy = true
            },
            out _);
        var current = state.Current;
        var completed = state.TopBoxel!.Children[0];
        var empty = state.TopBoxel.Children[1];

        var changed = state.ApplyCompletionAudit(
        [
            new BoxelCompletionAuditEntry(completed, 4, true, false),
            new BoxelCompletionAuditEntry(empty, -1, false, true),
            new BoxelCompletionAuditEntry(
                BoxelAddress.Parse("Wregoe BU-Y b2-0"),
                10,
                true,
                false),
        ]);

        Assert.True(changed);
        Assert.Equal(current, state.Current);
        Assert.Equal(1, state.CompletedBoxelCount);
        Assert.Contains(completed.Prefix, state.CreateSnapshot().CompletedPrefixes);
        Assert.Contains(empty.Prefix, state.EmptyBoxelPrefixes);
    }

    private static BoxelSearchState CreateActiveState(BoxelCompletionMode mode)
    {
        var state = new BoxelSearchState();
        state.TryActivate(
            new BoxelSearchActivationRequest
            {
                TopBoxel = BoxelAddress.Parse("Praea Euq IL-P c5-0"),
                LowMassCode = 'c',
                StartedOn = DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
                SkipAlreadyVisited = false,
                SkipKnownToSpansh = false,
                CompletionMode = mode,
                AutoCopy = true
            },
            out _);
        return state;
    }

    private static BoxelSystemObservation Observation(
        string name,
        DateTimeOffset? visited = null,
        DateTimeOffset? spansh = null,
        bool hasBodies = true)
    {
        return new BoxelSystemObservation(
            BoxelAddress.Parse(name),
            null,
            visited,
            spansh,
            hasBodies);
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(
            JournalEventEnvelope.TryParse(json, out var journalEvent, out var error),
            error);
        return Assert.IsType<JournalEventEnvelope>(journalEvent);
    }
}
