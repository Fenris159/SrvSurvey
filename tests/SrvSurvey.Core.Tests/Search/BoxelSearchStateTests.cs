using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Search;

public sealed class BoxelSearchStateTests
{
    [Fact]
    public void ActivateBuildsChildProgressAndRejectsMassCodeH()
    {
        var state = new BoxelSearchState();

        var activated = state.TryActivate(
            BoxelAddress.Parse("Praea Euq RS-U d2-0"),
            'b',
            DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
            false,
            false,
            BoxelCompletionMode.EnterSystem,
            true,
            out var error);

        Assert.True(activated, error);
        Assert.True(state.IsActive);
        Assert.Equal(73, state.TotalBoxelCount);
        Assert.Equal("Praea Euq RS-U d2-0", state.NextSystem);

        Assert.False(state.TryActivate(
            BoxelAddress.Parse("Dryio Flyuae AA-A h0"),
            'h',
            DateTimeOffset.UtcNow,
            false,
            false,
            BoxelCompletionMode.EnterSystem,
            false,
            out error));
        Assert.Contains("Mass-code h", error);
    }

    [Fact]
    public void ActivatingDifferentTopBoxelResetsExpectedSystemCount()
    {
        var state = CreateActiveState(BoxelCompletionMode.EnterSystem);
        state.SetExpectedSystemCount(45);

        state.TryActivate(
            BoxelAddress.Parse("Wregoe BU-Y b2-0"),
            'b',
            DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
            false,
            false,
            BoxelCompletionMode.EnterSystem,
            true,
            out _);

        Assert.Equal(1, state.CurrentCount);
        Assert.Equal("Wregoe BU-Y b2-0", state.NextSystem);
    }

    [Fact]
    public void EnterSystemModeCompletesFsdJumpAndSelectsNextDescendingSystem()
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
        Assert.Equal("Praea Euq IL-P c5-1", state.NextSystem);
        Assert.Equal(new GalacticCoordinate(1, 2, 3), state.Systems[2].Position);
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
    public void SkipRulesMatchLegacyDatesAndKnownBodyRequirement()
    {
        var state = new BoxelSearchState();
        state.TryActivate(
            BoxelAddress.Parse("Praea Euq IL-P c5-0"),
            'c',
            DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
            true,
            true,
            BoxelCompletionMode.EnterSystem,
            false,
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
            BoxelAddress.Parse("Praea Euq RS-U d2-0"),
            'c',
            DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
            false,
            false,
            BoxelCompletionMode.EnterSystem,
            true,
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
    public void LegacyEmptyIdsAreAppliedAcrossTheWholeSearchTree()
    {
        var state = new BoxelSearchState();
        state.TryActivate(
            BoxelAddress.Parse("Praea Euq RS-U d2-0"),
            'c',
            DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
            false,
            false,
            BoxelCompletionMode.EnterSystem,
            true,
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
            sol,
            sol!.MassCode,
            DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
            false,
            false,
            BoxelCompletionMode.EnterSystem,
            true,
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
            BoxelAddress.Parse("Praea Euq RS-U d2-0"),
            'c',
            DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
            false,
            false,
            BoxelCompletionMode.EnterSystem,
            true,
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
            BoxelAddress.Parse("Praea Euq IL-P c5-0"),
            'c',
            DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
            false,
            false,
            mode,
            true,
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
