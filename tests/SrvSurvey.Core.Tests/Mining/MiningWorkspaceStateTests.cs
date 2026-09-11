using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Mining;

namespace SrvSurvey.Core.Tests.Mining;

public sealed class MiningWorkspaceStateTests
{
    [Fact]
    public void BootstrapDoesNotAutoStartButLiveProspectorDoesAndReplayedEventsAreNotCountedTwice()
    {
        var state = new MiningWorkspaceState(new MiningCommanderData());
        var entry = Parse("""{"event":"LaunchDrone","timestamp":"2026-09-06T12:01:00Z","Type":"Prospector"}""");
        state.Apply(entry, true, "Sol", "Ring", "Python");
        Assert.Null(state.Session.Current);
        var live = Parse("""{"event":"LaunchDrone","timestamp":"2026-09-06T12:02:00Z","Type":"Prospector"}""");
        state.Apply(live, false, "Sol", "Ring", "Python");
        state.Apply(live, false, "Sol", "Ring", "Python");
        Assert.Equal(1, state.Session.Current?.ProspectorLimpets);
        var restored = new MiningWorkspaceState(state.Data);
        restored.Apply(live, true, "Sol", "Ring", "Python");
        Assert.Equal(1, restored.Session.Current?.ProspectorLimpets);
    }

    [Fact]
    public void LiveProspectorResumesPausedSessionWhenAutoStartIsEnabled()
    {
        var state = new MiningWorkspaceState(new MiningCommanderData());
        var started = DateTimeOffset.Parse("2026-09-06T12:00:00Z");
        state.Session.Start(started, "Sol", "Ring", "Python");
        state.Session.Pause(started.AddMinutes(1));

        state.Apply(
            Parse("""{"event":"LaunchDrone","timestamp":"2026-09-06T12:02:00Z","Type":"Prospector"}"""),
            false,
            "Sol",
            "Ring",
            "Python"
        );

        Assert.Null(state.Session.Current!.PausedAt);
        Assert.Equal(1, state.Session.Current.ProspectorLimpets);
    }

    [Fact]
    public void LiveProspectorDoesNotResumePausedSessionWhenAutoStartIsDisabled()
    {
        var state = new MiningWorkspaceState(new MiningCommanderData());
        state.Data.Settings.AutoStart = false;
        var started = DateTimeOffset.Parse("2026-09-06T12:00:00Z");
        state.Session.Start(started, "Sol", "Ring", "Python");
        state.Session.Pause(started.AddMinutes(1));

        state.Apply(
            Parse("""{"event":"LaunchDrone","timestamp":"2026-09-06T12:02:00Z","Type":"Prospector"}"""),
            false,
            "Sol",
            "Ring",
            "Python"
        );

        Assert.NotNull(state.Session.Current!.PausedAt);
        Assert.Equal(0, state.Session.Current.ProspectorLimpets);
    }

    [Fact]
    public void JournalImportKeepsNewerRingsAndExistingMissionProgress()
    {
        var time = DateTimeOffset.Parse("2026-09-06T12:00:00Z");
        var ring = new MiningRing
        {
            System = "Sol",
            Body = "Ring",
            Scanned = time,
            Hotspots = new() { ["Platinum"] = 2 },
        };
        var mission = new MiningMission
        {
            Id = 7,
            Commodity = "platinum",
            Required = 10,
            Delivered = 5,
        };
        var state = new MiningWorkspaceState(new() { Rings = [ring], Missions = [mission] });
        state.Import(
            new()
            {
                Rings = [ring with { System = "sol", Scanned = time.AddDays(-1), Hotspots = new() }],
                Missions = [mission with { Delivered = 0 }, mission with { Id = 8 }],
            }
        );
        Assert.Same(ring, Assert.Single(state.Data.Rings));
        Assert.Equal(5, state.Data.Missions.Single(m => m.Id == 7).Delivered);
        Assert.Equal(2, state.Data.Missions.Count);
    }

    [Fact]
    public void ProspectorProgressUpdatesThePersistentReportWithoutAddingAnotherNotice()
    {
        var state = new MiningWorkspaceState(new MiningCommanderData());
        state.Session.Start(DateTimeOffset.Parse("2026-09-06T12:00:00Z"), "Sol", "Ring", "Python");

        state.Apply(
            Parse(
                """{"event":"ProspectedAsteroid","timestamp":"2026-09-06T12:01:00Z","Materials":[{"Name":"Platinum","Proportion":35}],"Remaining":100}"""
            ),
            false,
            "Sol",
            "Ring",
            "Python"
        );
        state.Data.Settings.Thresholds["Platinum"] = 90;
        state.Apply(
            Parse(
                """{"event":"ProspectedAsteroid","timestamp":"2026-09-06T12:02:00Z","Materials":[{"Name":"Platinum","Proportion":35}],"Remaining":20}"""
            ),
            false,
            "Sol",
            "Ring",
            "Python"
        );

        Assert.Equal("Platinum 35.0% · Remaining 20%", state.CurrentProspectText);
        Assert.Single(state.Notices);

        state.Apply(
            Parse("""{"event":"SupercruiseEntry","timestamp":"2026-09-06T12:03:00Z"}"""),
            false,
            "Sol",
            "Ring",
            "Python"
        );
        Assert.Null(state.CurrentProspectText);
        Assert.Single(state.Session.Current!.Prospects);
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(JournalEventEnvelope.TryParse(json, out var result, out _));
        return result!;
    }
}
