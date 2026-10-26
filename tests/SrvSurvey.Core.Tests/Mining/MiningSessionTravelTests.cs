using System.Text.Json;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Mining;

namespace SrvSurvey.Core.Tests.Mining;

public sealed class MiningSessionTravelTests
{
    private static readonly DateTimeOffset Started = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SupercruisePausesTheSameSessionWithoutArchivingIt()
    {
        MiningWorkspaceState state = Create();
        MiningSession session = state.Session.Current!;
        Apply(state, "SupercruiseEntry", 2);
        Assert.Same(session, state.Session.Current);
        Assert.Equal(Started.AddMinutes(2), session.PausedAt);
        Assert.Null(session.Ended);
        Assert.Equal(1, session.RefinedTons);
        Assert.Empty(state.Data.History);
    }

    [Theory]
    [InlineData("LaunchDrone", "Prospector")]
    [InlineData("LaunchDrone", "Collection")]
    [InlineData("ProspectedAsteroid", null)]
    [InlineData("MiningRefined", "platinum")]
    public void MiningActivityResumesTravelPauseAndCountsItsFirstEvent(string eventName, string? type)
    {
        MiningWorkspaceState state = Create();
        MiningSession session = state.Session.Current!;
        Apply(state, "SupercruiseEntry", 2);
        Assert.Same(session, state.Session.Current);
        Assert.NotNull(session.PausedAt);
        Apply(state, eventName, 5, type: type);
        Assert.Same(session, state.Session.Current);
        Assert.Null(session.PausedAt);
        Assert.Equal(TimeSpan.FromMinutes(3), session.PausedDuration);
        Assert.Equal(TimeSpan.FromMinutes(3), session.DurationAt(Started.AddMinutes(6)));
        Assert.Equal(eventName == "MiningRefined" ? 2 : 1, session.RefinedTons);
        Assert.Equal(type == "Prospector" ? 1 : 0, session.ProspectorLimpets);
        Assert.Equal(type == "Collection" ? 1 : 0, session.CollectorLimpets);
        Assert.Equal(eventName == "ProspectedAsteroid" ? 1 : 0, session.Prospects.Count);
        Apply(state, eventName, 5, type: type);
        Assert.Equal(eventName == "MiningRefined" ? 2 : 1, session.RefinedTons);
        Assert.Empty(state.Data.History);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void DisabledAutomationAndHistoricalTravelDoNotPause(bool enabled, bool bootstrap)
    {
        MiningWorkspaceState state = Create();
        state.Data.Settings.AutoStopAfterTravel = enabled;
        Apply(state, "SupercruiseEntry", 2, bootstrap);
        Assert.NotNull(state.Session.Current);
        Assert.Null(state.Session.Current.PausedAt);
        Assert.Empty(state.Data.History);
    }

    [Fact]
    public void HistoricalTravelCanStillPauseWhenTheLiveCopyArrives()
    {
        MiningWorkspaceState state = Create();
        Apply(state, "SupercruiseEntry", 2, bootstrap: true);
        Assert.Null(state.Session.Current!.PausedAt);
        Apply(state, "SupercruiseEntry", 2);
        Assert.NotNull(state.Session.Current);
        Assert.Equal(Started.AddMinutes(2), state.Session.Current.PausedAt);
        Apply(state, "SupercruiseEntry", 2);
        Assert.Equal(Started.AddMinutes(2), state.Session.Current.PausedAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateSupercruiseDoesNotPauseAResumedSession(bool reload)
    {
        MiningWorkspaceState state = Create();
        Apply(state, "SupercruiseEntry", 2);
        Apply(state, "MiningRefined", 5, type: "platinum");
        if (reload)
        {
            state = new MiningWorkspaceState(MiningStore.Parse(MiningStore.Export(state.Data)));
        }
        Apply(state, "SupercruiseEntry", 2);
        Assert.NotNull(state.Session.Current);
        Assert.Null(state.Session.Current.PausedAt);
        Assert.Equal(TimeSpan.FromMinutes(3), state.Session.Current.PausedDuration);
        Assert.Equal(2, state.Session.Current.RefinedTons);
    }

    [Fact]
    public void OlderSupercruiseCannotPauseAfterAnotherTravelCycle()
    {
        MiningWorkspaceState state = Create();
        Apply(state, "SupercruiseEntry", 2);
        Apply(state, "MiningRefined", 5, type: "platinum");
        Apply(state, "SupercruiseEntry", 7);
        Apply(state, "MiningRefined", 9, type: "platinum");
        Apply(state, "SupercruiseEntry", 2);
        Assert.NotNull(state.Session.Current);
        Assert.Null(state.Session.Current.PausedAt);
        Assert.Equal(TimeSpan.FromMinutes(5), state.Session.Current.PausedDuration);
    }

    [Fact]
    public void SupercruiseDuringManualPauseCannotReapplyAfterManualResume()
    {
        MiningWorkspaceState state = Create();
        state.Session.Pause(Started.AddMinutes(2));
        Apply(state, "SupercruiseEntry", 3);
        state.Session.Resume(Started.AddMinutes(4));
        Apply(state, "SupercruiseEntry", 3);
        Assert.NotNull(state.Session.Current);
        Assert.Null(state.Session.Current.PausedAt);
        Assert.Equal(TimeSpan.FromMinutes(2), state.Session.Current.PausedDuration);
        Apply(state, "SupercruiseEntry", 7);
        Assert.Equal(Started.AddMinutes(7), state.Session.Current.PausedAt);
        Assert.True(state.Session.Current.PausedForTravel);
    }

    [Theory]
    [InlineData("SupercruiseExit")]
    [InlineData("CargoTransfer")]
    [InlineData("MaterialCollected")]
    [InlineData("StartJump")]
    public void UnrelatedActivityDoesNotResumeATravelPause(string eventName)
    {
        MiningWorkspaceState state = Create();
        Apply(state, "SupercruiseEntry", 2);
        Apply(state, eventName, 4);
        Assert.NotNull(state.Session.Current);
        Assert.Equal(Started.AddMinutes(2), state.Session.Current.PausedAt);
        Apply(state, "ProspectedAsteroid", 1);
        Assert.Equal(Started.AddMinutes(2), state.Session.Current.PausedAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DelayedProspectorLaunchCannotResumeATravelPause(bool autoStart)
    {
        MiningWorkspaceState state = Create();
        state.Data.Settings.AutoStart = autoStart;
        Apply(state, "SupercruiseEntry", 2);
        Apply(state, "LaunchDrone", 1, type: "Prospector");
        Assert.NotNull(state.Session.Current);
        Assert.Equal(Started.AddMinutes(2), state.Session.Current.PausedAt);
        Assert.Equal(0, state.Session.Current.ProspectorLimpets);
        Apply(state, "LaunchDrone", 5, type: "Prospector");
        Assert.Null(state.Session.Current.PausedAt);
        Assert.Equal(1, state.Session.Current.ProspectorLimpets);
    }

    [Fact]
    public void SupercruiseDoesNotConvertAManualPauseToAnAutomaticPause()
    {
        MiningWorkspaceState state = Create();
        state.Session.Pause(Started.AddMinutes(2));
        Apply(state, "SupercruiseEntry", 3);
        Apply(state, "MiningRefined", 5, type: "platinum");
        Assert.NotNull(state.Session.Current);
        Assert.Equal(Started.AddMinutes(2), state.Session.Current.PausedAt);
        Assert.False(state.Session.Current.PausedForTravel);
        Assert.Equal(1, state.Session.Current.RefinedTons);
    }

    [Fact]
    public void HistoricalMiningDoesNotResumeATravelPause()
    {
        MiningWorkspaceState state = Create();
        Apply(state, "SupercruiseEntry", 2);
        Apply(state, "MiningRefined", 3, bootstrap: true, type: "platinum");
        Assert.NotNull(state.Session.Current);
        Assert.Equal(Started.AddMinutes(2), state.Session.Current.PausedAt);
        Assert.Equal(1, state.Session.Current.RefinedTons);
        Apply(state, "MiningRefined", 5, type: "platinum");
        Assert.Null(state.Session.Current.PausedAt);
        Assert.Equal(2, state.Session.Current.RefinedTons);
    }

    [Fact]
    public void ManualPauseAfterResumingTravelStillRespectsTheAutoStartPreference()
    {
        MiningWorkspaceState state = Create();
        Apply(state, "SupercruiseEntry", 2);
        Assert.NotNull(state.Session.Current);
        state.Session.Resume(Started.AddMinutes(3));
        state.Session.Pause(Started.AddMinutes(4));
        Apply(state, "MiningRefined", 5, type: "platinum");
        Apply(state, "LaunchDrone", 6, type: "Prospector");
        Assert.Equal(Started.AddMinutes(4), state.Session.Current.PausedAt);
        Assert.Equal(1, state.Session.Current.RefinedTons);
    }

    [Fact]
    public void TravelPausePersistsAcrossCommanderDataSaveAndReload()
    {
        string directory = Path.Combine(Path.GetTempPath(), "mining-travel-" + Guid.NewGuid().ToString("N"));
        try
        {
            MiningWorkspaceState state = Create();
            Guid id = state.Session.Current!.Id;
            Apply(state, "SupercruiseEntry", 2);
            var store = new MiningStore(directory);
            store.Save("F1", state.Data);
            var restored = new MiningWorkspaceState(store.Load("F1"));
            Assert.Equal(id, restored.Session.Current!.Id);
            Assert.NotNull(restored.Session.Current.PausedAt);
            Apply(restored, "MiningRefined", 5, type: "platinum");
            Assert.Null(restored.Session.Current.PausedAt);
            Assert.Equal(2, restored.Session.Current.RefinedTons);
            Assert.Equal(TimeSpan.FromMinutes(3), restored.Session.Current.PausedDuration);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Theory]
    [InlineData("FSDJump")]
    [InlineData("Docked")]
    public void HyperspaceArrivalAndDockingStillEndATravelPausedSession(string eventName)
    {
        MiningWorkspaceState state = Create();
        Apply(state, "SupercruiseEntry", 2);
        Assert.NotNull(state.Session.Current);
        Apply(state, eventName, 5);
        Assert.Null(state.Session.Current);
        MiningSession ended = Assert.Single(state.Data.History);
        Assert.Equal(Started.AddMinutes(5), ended.Ended);
        Assert.Equal(TimeSpan.FromMinutes(2), ended.ActiveDuration);
        Assert.Equal(1, ended.RefinedTons);
    }

    private static MiningWorkspaceState Create()
    {
        var state = new MiningWorkspaceState(new MiningCommanderData());
        state.Data.Settings.AutoStart = false;
        state.Session.Start(Started, "Sol", "Ring", "Python");
        Apply(state, "MiningRefined", 1, type: "platinum");
        return state;
    }

    private static void Apply(
        MiningWorkspaceState state,
        string name,
        int minute,
        bool bootstrap = false,
        string? type = null
    )
    {
        string json = JsonSerializer.Serialize(
            new
            {
                @event = name,
                timestamp = Started.AddMinutes(minute),
                Type = type,
                Materials = new[] { new { Name = "gold", Proportion = 20 } },
            }
        );
        Assert.True(JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? entry, out _));
        state.Apply(entry!, bootstrap, "Sol", "Ring", "Python");
    }
}
