using SrvSurvey.Core.Combat;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class CombatViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-combat-vm-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task FootCombatMatchesLegacyWarAltitudeAndVehicleGates()
    {
        var viewModel = CreateViewModel();
        viewModel.AutoShowFootCombat = true;

        await viewModel.ApplyUpdateAsync(
        [
            Parse(
                """
                {"timestamp":"2026-07-25T01:00:00Z","event":"ApproachSettlement","Name":"Test Base","StationFaction":{"FactionState":"War"}}
                """),
            Parse(
                """
                {"timestamp":"2026-07-25T01:01:00Z","event":"FactionKillBond","Reward":17361}
                """),
        ],
        new EliteStatus
        {
            Flags = StatusFlags.InSrv,
            Altitude = 50,
        },
        processHistoricalProgress: true);

        Assert.True(viewModel.ShouldShowFootCombat);
        Assert.Equal("Test Base", viewModel.SettlementName);
        Assert.Equal(1, viewModel.FootCombatKills);
        Assert.Equal("17,361 CR", viewModel.FootCombatBonds);

        await viewModel.ApplyUpdateAsync(
            [],
            new EliteStatus
            {
                Flags = StatusFlags.InSrv,
                GuiFocus = GuiFocus.InternalPanel,
                Altitude = 50,
            },
            processHistoricalProgress: true);
        Assert.False(viewModel.ShouldShowFootCombat);

        await viewModel.ApplyUpdateAsync(
            [],
            new EliteStatus
            {
                Flags = StatusFlags.InSrv,
                Altitude = 100,
            },
            processHistoricalProgress: true);
        Assert.False(viewModel.ShouldShowFootCombat);

        await viewModel.ApplyUpdateAsync(
            [],
            new EliteStatus
            {
                Flags2 = StatusFlags2.OnFoot | StatusFlags2.OnFootOnPlanet,
                Altitude = 50,
            },
            processHistoricalProgress: true);
        Assert.True(viewModel.ShouldShowFootCombat);
        Assert.Equal(0, viewModel.FootCombatKills);

        await viewModel.ApplyUpdateAsync(
            [Parse("""{"event":"Music","MusicTrack":"SystemMap"}""")],
            null,
            processHistoricalProgress: true);
        Assert.False(viewModel.ShouldShowFootCombat);

        await viewModel.ApplyUpdateAsync(
            [Parse("""{"event":"Music","MusicTrack":"Exploration"}""")],
            null,
            processHistoricalProgress: true);
        Assert.True(viewModel.ShouldShowFootCombat);
    }

    [Fact]
    public async Task HistoricalBootstrapDoesNotRecountFootOrMissionProgress()
    {
        var viewModel = CreateViewModel();
        viewModel.AutoShowFootCombat = true;
        viewModel.AutoShowMassacreMissions = true;
        viewModel.LoadProfile(
            "F123",
            "Drew",
            true,
            new CombatSnapshot(
            [
                Mission(123, remaining: 4),
            ]));

        await viewModel.ApplyUpdateAsync(
        [
            Parse(
                """
                {"timestamp":"2026-07-25T01:00:00Z","event":"ApproachSettlement","Name":"Test Base","StationFaction":{"FactionState":"CivilWar"}}
                """),
            Parse(
                """
                {"timestamp":"2026-07-25T01:01:00Z","event":"FactionKillBond","Reward":17361}
                """),
            Parse(
                """
                {"timestamp":"2026-07-25T01:02:00Z","event":"Bounty","VictimFaction":"Enemy"}
                """),
        ],
        new EliteStatus
        {
            Flags = StatusFlags.InSrv,
            Altitude = 50,
        },
        processHistoricalProgress: false);

        Assert.Equal(0, viewModel.FootCombatKills);
        Assert.Equal(4, Assert.Single(viewModel.MassacreMissions).Remaining);
        Assert.Same(viewModel.MassacreMissions, viewModel.MassacreMissions);
    }

    [Fact]
    public async Task MassacreProgressPersistsAndMatchesLegacyModes()
    {
        var viewModel = CreateViewModel();
        viewModel.AutoShowMassacreMissions = true;
        viewModel.LoadProfile("F123", "Drew", true, CombatSnapshot.Empty);

        await viewModel.ApplyUpdateAsync(
        [
            Parse(
                """
                {"timestamp":"2026-07-25T01:00:00Z","event":"MissionAccepted","Faction":"Giver","Name":"Mission_Massacre","TargetFaction":"Enemy","KillCount":2,"Expiry":"2026-07-26T01:00:00Z","MissionID":123}
                """),
            Parse(
                """
                {"timestamp":"2026-07-25T01:01:00Z","event":"Bounty","VictimFaction":"Enemy"}
                """),
        ],
        new EliteStatus
        {
            Flags = StatusFlags.InMainShip | StatusFlags.Supercruise,
        },
        processHistoricalProgress: true);

        Assert.True(viewModel.ShouldShowMassacreMissions);
        Assert.Equal(1, Assert.Single(viewModel.MassacreMissions).Remaining);
        var saved = await new CommanderProfileStore(temporaryDirectory)
            .LoadAsync("F123", true);
        Assert.Equal(
            1,
            Assert.Single(saved.Data!.Combat.MassacreMissions).Remaining);

        await viewModel.ApplyUpdateAsync(
            [],
            new EliteStatus
            {
                Flags = StatusFlags.Landed | StatusFlags.InMainShip,
            },
            processHistoricalProgress: true);
        Assert.False(viewModel.ShouldShowMassacreMissions);

        await viewModel.ApplyUpdateAsync(
            [],
            new EliteStatus
            {
                GuiFocus = GuiFocus.ExternalPanel,
            },
            processHistoricalProgress: true);
        Assert.True(viewModel.ShouldShowMassacreMissions);
    }

    [Fact]
    public async Task ActiveBuildProjectCanSuppressBothCombatOverlays()
    {
        var viewModel = CreateViewModel();
        viewModel.AutoShowFootCombat = true;
        viewModel.SuppressForActiveBuildProjects = true;
        await viewModel.ApplyUpdateAsync(
        [
            Parse(
                """
                {"timestamp":"2026-07-25T01:00:00Z","event":"ApproachSettlement","Name":"Test Base","StationFaction":{"FactionState":"War"}}
                """),
        ],
        new EliteStatus
        {
            Flags = StatusFlags.InSrv,
            Altitude = 50,
        },
        processHistoricalProgress: true);
        Assert.True(viewModel.ShouldShowFootCombat);

        viewModel.SetActiveBuildProjects(true);

        Assert.False(viewModel.ShouldShowFootCombat);
    }

    [Fact]
    public async Task DisabledLegacyTestSettingDoesNotStartMissionTracking()
    {
        var viewModel = CreateViewModel();

        await viewModel.ApplyUpdateAsync(
        [
            Parse(
                """
                {"timestamp":"2026-07-25T01:00:00Z","event":"MissionAccepted","Faction":"Giver","Name":"Mission_Massacre","TargetFaction":"Enemy","KillCount":2,"MissionID":123}
                """),
        ],
        new EliteStatus { Flags = StatusFlags.InMainShip },
        processHistoricalProgress: true);

        Assert.Empty(viewModel.MassacreMissions);
        Assert.False(viewModel.ShouldShowMassacreMissions);
    }

    private CombatViewModel CreateViewModel()
    {
        Directory.CreateDirectory(temporaryDirectory);
        return new CombatViewModel(
            new CombatSettingsStore(Path.Combine(
                temporaryDirectory,
                "ui-settings.json")),
            new CommanderProfileStore(temporaryDirectory));
    }

    private static MassacreMissionSnapshot Mission(
        long missionId,
        int remaining)
    {
        return new MassacreMissionSnapshot(
            missionId,
            "Giver",
            "Enemy",
            DateTimeOffset.Parse("2026-07-26T01:00:00Z"),
            5,
            remaining);
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(
            JournalEventEnvelope.TryParse(json, out var value, out var error),
            error);
        return value!;
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
