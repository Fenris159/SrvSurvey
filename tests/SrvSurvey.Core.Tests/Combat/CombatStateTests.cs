using SrvSurvey.Core.Combat;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Combat;

public sealed class CombatStateTests
{
    [Fact]
    public void TracksFootCombatAtWarSettlementWithoutPersistingSessionTotals()
    {
        var state = new CombatState();

        var approach = state.Apply(Parse(
            """
            {"timestamp":"2026-07-25T01:00:00Z","event":"ApproachSettlement","Name":"Test Base","StationFaction":{"Name":"Test Faction","FactionState":"CivilWar"}}
            """));
        var kill = state.Apply(Parse(
            """
            {"timestamp":"2026-07-25T01:01:00Z","event":"FactionKillBond","Reward":17361,"AwardingFaction":"Test Faction","VictimFaction":"Enemy"}
            """));

        Assert.True(approach.StateChanged);
        Assert.False(approach.PersistenceChanged);
        Assert.True(kill.StateChanged);
        Assert.False(kill.PersistenceChanged);
        Assert.True(state.IsAtWarSettlement);
        Assert.Equal("Test Base", state.SettlementName);
        Assert.Equal(1, state.FootCombatKills);
        Assert.Equal(17_361, state.FootCombatBonds);
    }

    [Fact]
    public void BootstrapCanRestoreContextWithoutRecountingCombat()
    {
        var state = new CombatState();

        var result = state.Apply(
            Parse(
                """
                {"timestamp":"2026-07-25T01:01:00Z","event":"FactionKillBond","Reward":17361}
                """),
            countProgress: false);

        Assert.False(result.StateChanged);
        Assert.Equal(0, state.FootCombatKills);
        Assert.Equal(0, state.FootCombatBonds);
    }

    [Fact]
    public void TracksAndRemovesMassacreMission()
    {
        var state = new CombatState();

        var accepted = state.Apply(Parse(
            """
            {"timestamp":"2026-07-25T01:00:00Z","event":"MissionAccepted","Faction":"Mission Giver","Name":"Mission_MassacreWing","TargetFaction":"Enemy Faction","KillCount":7,"Expiry":"2026-07-26T01:00:00Z","MissionID":123}
            """));

        Assert.True(accepted.PersistenceChanged);
        Assert.Equal(
            new MassacreMissionSnapshot(
                123,
                "Mission Giver",
                "Enemy Faction",
                DateTimeOffset.Parse("2026-07-26T01:00:00Z"),
                7,
                7),
            Assert.Single(state.MassacreMissions));

        var removed = state.Apply(Parse(
            """
            {"timestamp":"2026-07-25T02:00:00Z","event":"MissionCompleted","Name":"Mission_MassacreWing","MissionID":123}
            """));

        Assert.True(removed.PersistenceChanged);
        Assert.Empty(state.MassacreMissions);
    }

    [Fact]
    public void BountyCreditsOnlyOneMissionPerMissionGiver()
    {
        var state = new CombatState();
        state.Reset(new CombatSnapshot(
        [
            Mission(1, "Giver A", "Enemy", remaining: 5),
            Mission(2, "Giver A", "Enemy", remaining: 4),
            Mission(3, "Giver B", "Enemy", remaining: 3),
            Mission(4, "Giver C", "Other", remaining: 2),
        ]));

        var result = state.Apply(Parse(
            """
            {"timestamp":"2026-07-25T02:00:00Z","event":"Bounty","VictimFaction":"Enemy","TotalReward":1000}
            """));

        Assert.True(result.PersistenceChanged);
        Assert.Equal([4, 4, 2, 2], state.MassacreMissions
            .Select(mission => mission.Remaining));
    }

    [Fact]
    public void ExpiredMissionDoesNotReceiveBountyCredit()
    {
        var state = new CombatState();
        state.Reset(new CombatSnapshot(
        [
            Mission(
                1,
                "Giver",
                "Enemy",
                remaining: 5,
                expires: DateTimeOffset.Parse("2026-07-25T01:00:00Z")),
        ]));

        var result = state.Apply(Parse(
            """
            {"timestamp":"2026-07-25T02:00:00Z","event":"Bounty","VictimFaction":"Enemy"}
            """));

        Assert.False(result.StateChanged);
        Assert.Equal(5, Assert.Single(state.MassacreMissions).Remaining);
    }

    [Fact]
    public void MissionsSnapshotPrunesEntriesNoLongerActiveOrComplete()
    {
        var state = new CombatState();
        state.Reset(new CombatSnapshot(
        [
            Mission(1, "A", "Enemy", 2),
            Mission(2, "B", "Enemy", 2),
            Mission(3, "C", "Enemy", 2),
        ]));

        var result = state.Apply(Parse(
            """
            {"timestamp":"2026-07-25T02:00:00Z","event":"Missions","Active":[{"MissionID":1}],"Complete":[{"MissionID":3}],"Failed":[]}
            """));

        Assert.True(result.PersistenceChanged);
        Assert.Equal([1L, 3L], state.MassacreMissions
            .Select(mission => mission.MissionId));
    }

    [Fact]
    public void NonMassacreMissionAndDuplicateAreIgnored()
    {
        var state = new CombatState();
        var unrelated = Parse(
            """
            {"timestamp":"2026-07-25T01:00:00Z","event":"MissionAccepted","Faction":"Giver","Name":"Mission_Delivery","TargetFaction":"Enemy","KillCount":2,"MissionID":123}
            """);
        var massacre = Parse(
            """
            {"timestamp":"2026-07-25T01:00:00Z","event":"MissionAccepted","Faction":"Giver","Name":"Mission_Massacre","TargetFaction":"Enemy","KillCount":2,"MissionID":456}
            """);

        Assert.False(state.Apply(unrelated).StateChanged);
        Assert.True(state.Apply(massacre).StateChanged);
        Assert.False(state.Apply(massacre).StateChanged);
        Assert.Single(state.MassacreMissions);
    }

    private static MassacreMissionSnapshot Mission(
        long missionId,
        string giver,
        string target,
        int remaining,
        DateTimeOffset? expires = null)
    {
        return new MassacreMissionSnapshot(
            missionId,
            giver,
            target,
            expires,
            remaining,
            remaining);
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(
            JournalEventEnvelope.TryParse(json, out var value, out var error),
            error);
        return value!;
    }
}
