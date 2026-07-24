using SrvSurvey.Core.Guardian;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Guardian;

public sealed class RamTahStateTests
{
    [Fact]
    public void PublishedLogInventoriesMatchLegacyMissions()
    {
        var ruins = RamTahState.GetAncientRuinsLogCodes();
        var logs = RamTahState.GetGuardianLogCodes();

        Assert.Equal(RamTahState.AncientRuinsLogCount, ruins.Count);
        Assert.Equal(RamTahState.GuardianLogsCount, logs.Count);
        Assert.Equal("B1", ruins[0]);
        Assert.Equal("B19", ruins[18]);
        Assert.Equal("C1", ruins[19]);
        Assert.Equal("T20", ruins[^1]);
        Assert.Equal("#1", logs[0]);
        Assert.Equal("#28", logs[^1]);
    }

    [Fact]
    public void SnapshotRestoresAndOrdersProgress()
    {
        var state = new RamTahState();
        state.Reset(new RamTahSnapshot(
            RamTahMissionStatus.Active,
            RamTahMissionStatus.Complete,
            ["T20", "B2", "B1"],
            ["#28", "#2", "#1"]));

        var snapshot = state.CreateSnapshot();

        Assert.Equal(RamTahMissionStatus.Active, snapshot.AncientRuinsMissionStatus);
        Assert.Equal(RamTahMissionStatus.Complete, snapshot.GuardianLogsMissionStatus);
        Assert.Equal(["B1", "B2", "T20"], snapshot.AncientRuinsLogs);
        Assert.Equal(["#1", "#2", "#28"], snapshot.GuardianLogs);
        Assert.Equal(300d / 101, state.AncientRuinsProgress, 10);
        Assert.Equal(300d / 28, state.GuardianLogsProgress, 10);
    }

    [Fact]
    public void SetToggleAndClearValidateMissionCodes()
    {
        var state = new RamTahState();

        Assert.True(state.SetLog(RamTahMission.AncientRuins, "B1", true));
        Assert.False(state.SetLog(RamTahMission.AncientRuins, "B1", true));
        Assert.True(state.ToggleLog(RamTahMission.AncientRuins, "B1"));
        Assert.True(state.SetLog(RamTahMission.GuardianLogs, "#28", true));
        Assert.True(state.Clear(RamTahMission.GuardianLogs));
        Assert.False(state.Clear(RamTahMission.GuardianLogs));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => state.SetLog(RamTahMission.AncientRuins, "B20", true));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => state.SetLog(RamTahMission.GuardianLogs, "#29", true));
    }

    [Theory]
    [InlineData("MissionAccepted", "Mission_TheDead", RamTahMissionStatus.Active)]
    [InlineData("MissionCompleted", "Mission_TheDead_name", RamTahMissionStatus.Complete)]
    [InlineData("MissionAccepted", "Mission_TheDead_002", RamTahMissionStatus.Active)]
    [InlineData("MissionCompleted", "Mission_TheDead_002_name", RamTahMissionStatus.Complete)]
    [InlineData("MissionFailed", "Mission_TheDead", RamTahMissionStatus.NotStarted)]
    [InlineData("MissionAbandoned", "Mission_TheDead_002", RamTahMissionStatus.NotStarted)]
    public void MissionEventsUpdateTheMatchingLegacyStatus(
        string eventName,
        string missionName,
        RamTahMissionStatus expected)
    {
        var state = new RamTahState();
        if (expected == RamTahMissionStatus.NotStarted)
        {
            state.Apply(Parse(
                $"{{\"timestamp\":\"2026-07-24T12:00:00Z\",\"event\":\"MissionAccepted\",\"Name\":\"{missionName}\"}}"));
        }

        var changed = state.Apply(Parse(
            $"{{\"timestamp\":\"2026-07-24T12:00:01Z\",\"event\":\"{eventName}\",\"Name\":\"{missionName}\"}}"));

        Assert.True(changed);
        Assert.Equal(
            expected,
            missionName.Contains("002", StringComparison.Ordinal)
                ? state.GuardianLogsMissionStatus
                : state.AncientRuinsMissionStatus);
    }

    [Fact]
    public void MissionsSnapshotActivatesBothRamTahMissions()
    {
        var state = new RamTahState();

        var changed = state.Apply(Parse(
            """
            {"timestamp":"2026-07-24T12:00:00Z","event":"Missions","Active":[
              {"Name":"Mission_TheDead_name"},
              {"Name":"Mission_TheDead_002_name"},
              {"Name":"Mission_Collect_name"}
            ]}
            """));

        Assert.True(changed);
        Assert.Equal(RamTahMissionStatus.Active, state.AncientRuinsMissionStatus);
        Assert.Equal(RamTahMissionStatus.Active, state.GuardianLogsMissionStatus);
        Assert.True(state.IsAnyMissionActive);
    }

    [Fact]
    public void UnrelatedMissionDoesNotChangeState()
    {
        var state = new RamTahState();

        var changed = state.Apply(Parse(
            """
            {"timestamp":"2026-07-24T12:00:00Z","event":"MissionAccepted","Name":"Mission_Collect_name"}
            """));

        Assert.False(changed);
        Assert.Equal(RamTahMissionStatus.NotStarted, state.AncientRuinsMissionStatus);
        Assert.Equal(RamTahMissionStatus.NotStarted, state.GuardianLogsMissionStatus);
        Assert.Empty(state.AncientRuinsLogs);
        Assert.Empty(state.GuardianLogs);
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(
            JournalEventEnvelope.TryParse(json, out var journalEvent, out var error),
            error);
        return journalEvent!;
    }
}
