using SrvSurvey.Core.Combat;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Combat;

public sealed class CombatStateTests
{
    [Fact]
    public void TracksFootCombatAtWarSettlementWithoutPersistingSessionTotals()
    {
        var state = new CombatState();

        CombatApplyResult approach = state.Apply(
            Parse(
                """
                {"timestamp":"2026-07-25T01:00:00Z","event":"ApproachSettlement","Name":"Test Base","StationFaction":{"Name":"Test Faction","FactionState":"CivilWar"}}
                """
            )
        );
        CombatApplyResult kill = state.Apply(
            Parse(
                """
                {"timestamp":"2026-07-25T01:01:00Z","event":"FactionKillBond","Reward":17361,"AwardingFaction":"Test Faction","VictimFaction":"Enemy"}
                """
            )
        );

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

        CombatApplyResult result = state.Apply(
            Parse(
                """
                {"timestamp":"2026-07-25T01:01:00Z","event":"FactionKillBond","Reward":17361}
                """
            ),
            countProgress: false
        );

        Assert.False(result.StateChanged);
        Assert.Equal(0, state.FootCombatKills);
        Assert.Equal(0, state.FootCombatBonds);
    }

    [Theory]
    [InlineData("StartJump", "")]
    [InlineData("SupercruiseEntry", "")]
    [InlineData("Died", "")]
    [InlineData("Resurrect", "")]
    [InlineData("Shutdown", "")]
    [InlineData("Music", ",\"MusicTrack\":\"MainMenu\"")]
    public void SessionExitClearsFootCombatButRetainsMissions(string eventName, string properties)
    {
        var state = new CombatState();
        state.Apply(
            Parse("""{"event":"ApproachSettlement","Name":"Test Base","StationFaction":{"FactionState":"War"}}""")
        );
        state.Apply(Parse("""{"event":"FactionKillBond","Reward":100}"""));
        state.Apply(
            Parse(
                """{"event":"MissionAccepted","Faction":"Giver","Name":"Mission_Massacre","TargetFaction":"Enemy","KillCount":2,"MissionID":456}"""
            )
        );

        CombatApplyResult result = state.Apply(Parse($$"""{"event":"{{eventName}}"{{properties}}}"""));

        Assert.True(result.StateChanged);
        Assert.False(result.PersistenceChanged);
        Assert.Null(state.SettlementName);
        Assert.Equal(0, state.FootCombatKills);
        Assert.Single(state.MassacreMissions);
    }

    [Theory]
    [InlineData("Mission_Massacre")]
    [InlineData("Mission_MassacreWing")]
    [InlineData("Mission_Massacre_Conflict_CivilWar")]
    [InlineData("Mission_Massacre_Conflict_War")]
    [InlineData("Mission_MassacreWing_Conflict_War")]
    [InlineData("Mission_Massacre_FutureVariant")]
    public void TracksAndRemovesMassacreMission(string missionName)
    {
        var state = new CombatState();

        CombatApplyResult accepted = state.Apply(
            Parse(
                $$"""
                {"timestamp":"2026-07-25T01:00:00Z","event":"MissionAccepted","Faction":"Mission Giver","Name":"{{missionName}}","TargetFaction":"Enemy Faction","KillCount":7,"Expiry":"2026-07-26T01:00:00Z","MissionID":123}
                """
            )
        );

        Assert.True(accepted.PersistenceChanged);
        Assert.Equal(
            new MassacreMissionSnapshot(
                123,
                "Mission Giver",
                "Enemy Faction",
                DateTimeOffset.Parse("2026-07-26T01:00:00Z", global::System.Globalization.CultureInfo.InvariantCulture),
                7,
                7,
                missionName
            ),
            Assert.Single(state.MassacreMissions)
        );

        CombatApplyResult removed = state.Apply(
            Parse(
                """
                {"timestamp":"2026-07-25T02:00:00Z","event":"MissionCompleted","Name":"Mission_MassacreWing","MissionID":123}
                """
            )
        );

        Assert.True(removed.PersistenceChanged);
        Assert.Empty(state.MassacreMissions);
    }

    [Fact]
    public void CombatBondCreditsOneConflictMissionPerGiverAndDoesNotCreditPirateMissions()
    {
        var state = new CombatState();
        foreach (
            string mission in new[]
            {
                """{"event":"MissionAccepted","Faction":"Giver A","Name":"Mission_Massacre_Conflict_CivilWar","TargetFaction":"Enemy","KillCount":2,"MissionID":1}""",
                """{"event":"MissionAccepted","Faction":"Giver A","Name":"Mission_Massacre_Conflict_War","TargetFaction":"Enemy","KillCount":4,"MissionID":2}""",
                """{"event":"MissionAccepted","Faction":"Giver B","Name":"Mission_MassacreWing_Conflict_War","TargetFaction":"Enemy","KillCount":3,"MissionID":3}""",
                """{"event":"MissionAccepted","Faction":"Giver C","Name":"Mission_MassacreWing","TargetFaction":"Enemy","KillCount":5,"MissionID":4}""",
                """{"event":"MissionAccepted","Faction":"Giver D","Name":"Mission_Massacre_Conflict_War","TargetFaction":"Other","KillCount":6,"MissionID":5}""",
            }
        )
        {
            state.Apply(Parse(mission));
        }

        JournalEventEnvelope bond = Parse(
            """{"timestamp":"2026-11-08T09:10:00Z","event":"FactionKillBond","VictimFaction":"Enemy","Reward":1000}"""
        );
        CombatApplyResult result = state.Apply(bond, countFootCombat: false, countShipCombat: true);

        Assert.True(result.PersistenceChanged);
        Assert.Equal([1, 4, 2, 5, 6], state.MassacreMissions.Select(mission => mission.Remaining));
        Assert.Equal(0, state.FootCombatKills);
        Assert.Equal(0, state.FootCombatBonds);

        state.Apply(bond, countFootCombat: false, countShipCombat: true);
        state.Apply(bond, countFootCombat: false, countShipCombat: true);
        Assert.Equal([0, 3, 0, 5, 6], state.MassacreMissions.Select(mission => mission.Remaining));
    }

    [Fact]
    public void ShipAttacksAtWarSettlementDoNotAdvanceSpaceConflictMissions()
    {
        var state = new CombatState();
        state.Apply(
            Parse(
                """{"event":"MissionAccepted","Faction":"Fire Lotus Coalition","Name":"Mission_Massacre_Conflict_CivilWar","TargetFaction":"Sorbago Mastopolos Mining Inc","KillCount":81,"MissionID":1070754782}"""
            )
        );
        state.Apply(
            Parse(
                """{"event":"ApproachSettlement","Name":"Ren Horticultural Biosphere","StationFaction":{"Name":"Sorbago Mastopolos Mining Inc","FactionState":"War"}}"""
            )
        );
        state.Apply(
            Parse("""{"event":"SupercruiseExit","StarSystem":"Sorbago","Body":"Sorbago A 3","BodyType":"Planet"}""")
        );
        JournalEventEnvelope bond = Parse(
            """{"timestamp":"2026-11-08T09:53:11Z","event":"FactionKillBond","Reward":70217,"AwardingFaction":"Fire Lotus Coalition","VictimFaction":"Sorbago Mastopolos Mining Inc"}"""
        );
        for (int kill = 0; kill < 12; kill++)
        {
            state.Apply(bond, countFootCombat: false, countShipCombat: true);
        }

        Assert.Equal(81, Assert.Single(state.MassacreMissions).Remaining);
    }

    [Theory]
    [InlineData("SupercruiseEntry")]
    [InlineData("LeaveBody")]
    public void LeavingSettlementAllowsSpaceConflictBondsToAdvanceTheMission(string departureEvent)
    {
        var state = new CombatState();
        state.Apply(
            Parse(
                """{"event":"MissionAccepted","Faction":"Giver","Name":"Mission_Massacre_Conflict_War","TargetFaction":"Enemy","KillCount":3,"MissionID":1}"""
            )
        );
        state.Apply(
            Parse("""{"event":"ApproachSettlement","Name":"Surface Base","StationFaction":{"FactionState":"War"}}""")
        );
        state.Apply(
            Parse(
                """{"timestamp":"2026-11-08T09:00:00Z","event":"FactionKillBond","VictimFaction":"Enemy","Reward":1000}"""
            ),
            countFootCombat: false,
            countShipCombat: true
        );
        state.Apply(Parse($$"""{"event":"{{departureEvent}}"}"""));
        state.Apply(Parse("""{"event":"SupercruiseExit","Body":"Nearby planet","BodyType":"Planet"}"""));
        state.Apply(
            Parse(
                """{"timestamp":"2026-11-08T09:02:00Z","event":"FactionKillBond","VictimFaction":"Enemy","Reward":1000}"""
            ),
            countFootCombat: false,
            countShipCombat: true
        );

        Assert.Equal(2, Assert.Single(state.MassacreMissions).Remaining);
    }

    [Fact]
    public void BountyDoesNotCreditConflictMissions()
    {
        var state = new CombatState();
        state.Apply(
            Parse(
                """{"event":"MissionAccepted","Faction":"Giver","Name":"Mission_Massacre_Conflict_CivilWar","TargetFaction":"Enemy","KillCount":3,"MissionID":1}"""
            )
        );
        state.Apply(
            Parse(
                """{"event":"MissionAccepted","Faction":"Giver","Name":"Mission_Massacre","TargetFaction":"Enemy","KillCount":4,"MissionID":2}"""
            )
        );

        state.Apply(Parse("""{"timestamp":"2026-11-08T09:10:00Z","event":"Bounty","VictimFaction":"Enemy"}"""));

        Assert.Equal([3, 3], state.MassacreMissions.Select(mission => mission.Remaining));
    }

    [Fact]
    public void BountyCreditsOnlyOneMissionPerMissionGiver()
    {
        var state = new CombatState();
        state.Reset(
            new CombatSnapshot([
                Mission(1, "Giver A", "Enemy", remaining: 5),
                Mission(2, "Giver A", "Enemy", remaining: 4),
                Mission(3, "Giver B", "Enemy", remaining: 3),
                Mission(4, "Giver C", "Other", remaining: 2),
            ])
        );

        CombatApplyResult result = state.Apply(
            Parse(
                """
                {"timestamp":"2026-07-25T02:00:00Z","event":"Bounty","VictimFaction":"Enemy","TotalReward":1000}
                """
            )
        );

        Assert.True(result.PersistenceChanged);
        Assert.Equal([4, 4, 2, 2], state.MassacreMissions.Select(mission => mission.Remaining));
    }

    [Theory]
    [InlineData("Bounty", "Mission_Massacre")]
    [InlineData("FactionKillBond", "Mission_Massacre_Conflict_War")]
    public void ExpiredMissionDoesNotReceiveKillCredit(string eventName, string missionName)
    {
        var state = new CombatState();
        state.Reset(
            new CombatSnapshot([
                Mission(
                    1,
                    "Giver",
                    "Enemy",
                    remaining: 5,
                    expires: DateTimeOffset.Parse(
                        "2026-07-25T01:00:00Z",
                        global::System.Globalization.CultureInfo.InvariantCulture
                    )
                ) with
                {
                    MissionName = missionName,
                },
            ])
        );

        CombatApplyResult result = state.Apply(
            Parse(
                $$"""
                {"timestamp":"2026-07-25T02:00:00Z","event":"{{eventName}}","VictimFaction":"Enemy"}
                """
            ),
            countFootCombat: false,
            countShipCombat: true
        );

        Assert.False(result.StateChanged);
        Assert.Equal(5, Assert.Single(state.MassacreMissions).Remaining);
    }

    [Fact]
    public void MissionsSnapshotPrunesEntriesNoLongerActiveOrComplete()
    {
        var state = new CombatState();
        state.Reset(
            new CombatSnapshot([Mission(1, "A", "Enemy", 2), Mission(2, "B", "Enemy", 2), Mission(3, "C", "Enemy", 2)])
        );

        CombatApplyResult result = state.Apply(
            Parse(
                """
                {"timestamp":"2026-07-25T02:00:00Z","event":"Missions","Active":[{"MissionID":1}],"Complete":[{"MissionID":3}],"Failed":[]}
                """
            )
        );

        Assert.True(result.PersistenceChanged);
        Assert.Equal([1L, 3L], state.MassacreMissions.Select(mission => mission.MissionId));
    }

    [Theory]
    [InlineData("Mission_Delivery")]
    [InlineData("Mission_Assassinate")]
    [InlineData("Other_Mission_Massacre")]
    public void NonMassacreMissionAndDuplicateAreIgnored(string missionName)
    {
        var state = new CombatState();
        JournalEventEnvelope unrelated = Parse(
            $$"""
            {"timestamp":"2026-07-25T01:00:00Z","event":"MissionAccepted","Faction":"Giver","Name":"{{missionName}}","TargetFaction":"Enemy","KillCount":2,"MissionID":123}
            """
        );
        JournalEventEnvelope massacre = Parse(
            """
            {"timestamp":"2026-07-25T01:00:00Z","event":"MissionAccepted","Faction":"Giver","Name":"Mission_Massacre","TargetFaction":"Enemy","KillCount":2,"MissionID":456}
            """
        );

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
        DateTimeOffset? expires = null
    )
    {
        return new MassacreMissionSnapshot(missionId, giver, target, expires, remaining, remaining);
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? value, out string? error), error);
        return value!;
    }
}
