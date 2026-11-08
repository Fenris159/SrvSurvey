using SrvSurvey.Core.Combat;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class CombatViewModelTests : IDisposable
{
    [Theory]
    [InlineData(6, "0/6", false)]
    [InlineData(3, "3/6", false)]
    [InlineData(0, "6/6", true)]
    public void MassacreRowsShowCompletedKillsOutOfRequiredTotal(int remaining, string text, bool complete)
    {
        var row = new MassacreMissionViewModel(new(123, "Giver", "Enemy", null, 6, remaining));
        Assert.Equal(text, row.ProgressText);
        Assert.Equal(complete, row.IsComplete);
    }

    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-combat-vm-tests-{Guid.NewGuid():N}"
    );

    [Fact]
    public async Task FootCombatMatchesLegacyWarAltitudeAndVehicleGates()
    {
        CombatViewModel viewModel = CreateViewModel();
        viewModel.AutoShowFootCombat = true;

        await viewModel.ApplyUpdateAsync(
            [
                Parse(
                    """
                    {"timestamp":"2026-07-25T01:00:00Z","event":"ApproachSettlement","Name":"Test Base","StationFaction":{"FactionState":"War"}}
                    """
                ),
                Parse(
                    """
                    {"timestamp":"2026-07-25T01:01:00Z","event":"FactionKillBond","Reward":17361}
                    """
                ),
            ],
            new EliteStatus { Flags = StatusFlags.InSrv, Altitude = 50 },
            processHistoricalProgress: true
        );

        Assert.True(viewModel.ShouldShowFootCombat);
        Assert.Equal("Test Base", viewModel.SettlementName);
        Assert.Equal("TEST BASE", viewModel.SettlementHeader);
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
            processHistoricalProgress: true
        );
        Assert.False(viewModel.ShouldShowFootCombat);

        await viewModel.ApplyUpdateAsync(
            [],
            new EliteStatus { Flags = StatusFlags.InSrv, Altitude = 100 },
            processHistoricalProgress: true
        );
        Assert.False(viewModel.ShouldShowFootCombat);

        await viewModel.ApplyUpdateAsync(
            [],
            new EliteStatus { Flags2 = StatusFlags2.OnFoot | StatusFlags2.OnFootOnPlanet, Altitude = 50 },
            processHistoricalProgress: true
        );
        Assert.True(viewModel.ShouldShowFootCombat);
        Assert.Equal(0, viewModel.FootCombatKills);

        await viewModel.ApplyUpdateAsync(
            [Parse("""{"event":"Music","MusicTrack":"SystemMap"}""")],
            null,
            processHistoricalProgress: true
        );
        Assert.False(viewModel.ShouldShowFootCombat);

        await viewModel.ApplyUpdateAsync(
            [Parse("""{"event":"Music","MusicTrack":"Exploration"}""")],
            null,
            processHistoricalProgress: true
        );
        Assert.True(viewModel.ShouldShowFootCombat);
    }

    [Fact]
    public async Task HistoricalBootstrapDoesNotRecountFootOrMissionProgress()
    {
        CombatViewModel viewModel = CreateViewModel();
        viewModel.AutoShowFootCombat = true;
        viewModel.AutoShowMassacreMissions = true;
        viewModel.LoadProfile("F123", "Drew", true, new CombatSnapshot([Mission(123, remaining: 4)]));

        await viewModel.ApplyUpdateAsync(
            [
                Parse(
                    """
                    {"timestamp":"2026-07-25T01:00:00Z","event":"ApproachSettlement","Name":"Test Base","StationFaction":{"FactionState":"CivilWar"}}
                    """
                ),
                Parse(
                    """
                    {"timestamp":"2026-07-25T01:01:00Z","event":"FactionKillBond","Reward":17361}
                    """
                ),
                Parse(
                    """
                    {"timestamp":"2026-07-25T01:02:00Z","event":"Bounty","VictimFaction":"Enemy"}
                    """
                ),
            ],
            new EliteStatus { Flags = StatusFlags.InSrv, Altitude = 50 },
            processHistoricalProgress: false
        );

        Assert.Equal(0, viewModel.FootCombatKills);
        Assert.Equal(4, Assert.Single(viewModel.MassacreMissions).Remaining);
        Assert.Same(viewModel.MassacreMissions, viewModel.MassacreMissions);
    }

    [Fact]
    public async Task EmptyCombatProfilesRecoverPreviouslyIgnoredMissionsWithoutRecountingSavedProgress()
    {
        CombatViewModel fresh = CreateViewModel();
        fresh.AutoShowMassacreMissions = true;
        fresh.LoadProfile("F123", "Drew", true, CombatSnapshot.Empty);
        JournalEventEnvelope[] history =
        [
            Parse("""{"event":"LoadGame","Ship":"federation_corvette"}"""),
            Parse(
                """{"event":"MissionAccepted","Faction":"Giver","Name":"Mission_Massacre_Conflict_War","TargetFaction":"Enemy","KillCount":3,"MissionID":1}"""
            ),
            Parse(
                """{"timestamp":"2026-11-08T09:00:00Z","event":"FactionKillBond","VictimFaction":"Enemy","Reward":1000}"""
            ),
        ];
        var status = new EliteStatus { Flags = StatusFlags.InMainShip };
        await fresh.ApplyUpdateAsync(history, status, processHistoricalProgress: false);
        Assert.Equal("1/3", Assert.Single(fresh.MassacreMissions).ProgressText);

        CommanderProfileLoadResult saved = await new CommanderProfileStore(temporaryDirectory).LoadAsync("F123", true);
        CombatViewModel reopened = CreateViewModel();
        reopened.LoadProfile("F123", "Drew", true, saved.Data!.Combat);
        await reopened.ApplyUpdateAsync(history, status, processHistoricalProgress: false);
        Assert.Equal("1/3", Assert.Single(reopened.MassacreMissions).ProgressText);
    }

    [Fact]
    public async Task MixedVehicleReplayCreditsOnlyBondsEarnedInTheShip()
    {
        CombatViewModel viewModel = CreateViewModel();
        viewModel.AutoShowMassacreMissions = true;
        await viewModel.ApplyUpdateAsync(
            [
                Parse("""{"event":"LoadGame","Ship":"federation_corvette"}"""),
                Parse(
                    """{"event":"MissionAccepted","Faction":"Giver","Name":"Mission_Massacre_Conflict_War","TargetFaction":"Enemy","KillCount":3,"MissionID":1}"""
                ),
                Parse(
                    """{"timestamp":"2026-11-08T09:00:00Z","event":"FactionKillBond","VictimFaction":"Enemy","Reward":1000}"""
                ),
                Parse("""{"event":"Disembark","SRV":false}"""),
                Parse(
                    """{"timestamp":"2026-11-08T09:01:00Z","event":"FactionKillBond","VictimFaction":"Enemy","Reward":1000}"""
                ),
                Parse("""{"event":"Embark","SRV":true}"""),
                Parse(
                    """{"timestamp":"2026-11-08T09:02:00Z","event":"FactionKillBond","VictimFaction":"Enemy","Reward":1000}"""
                ),
                Parse("""{"event":"DockSRV"}"""),
                Parse(
                    """{"timestamp":"2026-11-08T09:03:00Z","event":"FactionKillBond","VictimFaction":"Enemy","Reward":1000}"""
                ),
                Parse("""{"event":"Disembark","SRV":false}"""),
            ],
            new EliteStatus { Flags2 = StatusFlags2.OnFoot },
            processHistoricalProgress: true
        );

        Assert.Equal("2/3", Assert.Single(viewModel.MassacreMissions).ProgressText);
        await viewModel.ApplyUpdateAsync([], new EliteStatus { Flags = StatusFlags.InMainShip }, true);
        Assert.True(viewModel.ShouldShowMassacreMissions);
        Assert.Equal("2/3", Assert.Single(viewModel.MassacreMissions).ProgressText);
    }

    [Theory]
    [InlineData("ExplorationSuit_Class5")]
    [InlineData("TacticalSuit_Class3")]
    [InlineData("UtilitySuit_Class4")]
    [InlineData("FlightSuit")]
    public async Task SuitLoadReplayKeepsGroundBondsOutOfShipMissions(string suit)
    {
        CombatViewModel viewModel = CreateViewModel();
        viewModel.AutoShowMassacreMissions = true;
        viewModel.AutoShowFootCombat = true;
        viewModel.LoadProfile("F123", "Drew", true, CombatSnapshot.Empty);
        await viewModel.ApplyUpdateAsync(
            [
                Parse($$"""{"event":"LoadGame","Ship":"{{suit}}"}"""),
                Parse(
                    """{"event":"MissionAccepted","Faction":"Giver","Name":"Mission_Massacre_Conflict_War","TargetFaction":"Enemy","KillCount":3,"MissionID":1}"""
                ),
                Parse("""{"event":"ApproachSettlement","Name":"Test Base","StationFaction":{"FactionState":"War"}}"""),
                Parse(
                    """{"timestamp":"2026-11-08T09:00:00Z","event":"FactionKillBond","VictimFaction":"Enemy","Reward":17000}"""
                ),
            ],
            new EliteStatus { Flags2 = StatusFlags2.OnFoot | StatusFlags2.OnFootOnPlanet, Altitude = 50 },
            processHistoricalProgress: true
        );

        Assert.Equal("0/3", Assert.Single(viewModel.MassacreMissions).ProgressText);
        Assert.Equal(1, viewModel.FootCombatKills);
        Assert.Equal("17,000 CR", viewModel.FootCombatBonds);
        CommanderProfileLoadResult saved = await new CommanderProfileStore(temporaryDirectory).LoadAsync("F123", true);
        Assert.Equal(3, Assert.Single(saved.Data!.Combat.MassacreMissions).Remaining);
    }

    [Fact]
    public async Task KnownNomadLaunchRemainsGroundCombatWhileAConventionalFighterEarnsShipCredit()
    {
        CombatViewModel viewModel = CreateViewModel();
        viewModel.AutoShowMassacreMissions = true;
        viewModel.AutoShowFootCombat = true;
        viewModel.LoadProfile("F123", "Drew", true, CombatSnapshot.Empty);
        await viewModel.ApplyUpdateAsync(
            [
                Parse("""{"event":"LoadGame","Ship":"federation_corvette"}"""),
                Parse(
                    """{"event":"MissionAccepted","Faction":"Giver","Name":"Mission_Massacre_Conflict_War","TargetFaction":"Enemy","KillCount":3,"MissionID":1}"""
                ),
                Parse("""{"event":"DockSRV","SRVType":"lander01","ID":44}"""),
                Parse("""{"event":"LaunchFighter","ID":45,"PlayerControlled":true}"""),
                Parse(
                    """{"timestamp":"2026-11-08T09:00:00Z","event":"FactionKillBond","VictimFaction":"Enemy","Reward":1000}"""
                ),
                Parse("""{"event":"DockFighter","ID":45}"""),
            ],
            new EliteStatus { Flags = StatusFlags.InMainShip },
            processHistoricalProgress: true
        );
        await viewModel.ApplyUpdateAsync(
            [
                Parse("""{"event":"LaunchFighter","ID":44,"PlayerControlled":true}"""),
                Parse("""{"event":"ApproachSettlement","Name":"Test Base","StationFaction":{"FactionState":"War"}}"""),
                Parse(
                    """{"timestamp":"2026-11-08T09:01:00Z","event":"FactionKillBond","VictimFaction":"Enemy","Reward":17000}"""
                ),
            ],
            new EliteStatus { Flags = StatusFlags.InSrv, Altitude = 50 },
            processHistoricalProgress: true
        );

        Assert.Equal("1/3", Assert.Single(viewModel.MassacreMissions).ProgressText);
        Assert.Equal(1, viewModel.FootCombatKills);
        Assert.Equal("17,000 CR", viewModel.FootCombatBonds);
        CommanderProfileLoadResult saved = await new CommanderProfileStore(temporaryDirectory).LoadAsync("F123", true);
        Assert.Equal(2, Assert.Single(saved.Data!.Combat.MassacreMissions).Remaining);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FirstNomadLaunchUsesLatestSrvStatusWithoutChangingEarlierFighterCredit(bool retainStatus)
    {
        CombatViewModel viewModel = CreateViewModel();
        viewModel.AutoShowMassacreMissions = true;
        viewModel.AutoShowFootCombat = true;
        viewModel.LoadProfile("F123", "Drew", true, CombatSnapshot.Empty);
        var srvStatus = new EliteStatus { Flags = StatusFlags.InSrv, Altitude = 50 };
        if (retainStatus)
        {
            await viewModel.ApplyUpdateAsync([], srvStatus, processHistoricalProgress: true);
        }
        await viewModel.ApplyUpdateAsync(
            [
                Parse("""{"event":"LoadGame","Ship":"federation_corvette"}"""),
                Parse(
                    """{"event":"MissionAccepted","Faction":"Giver","Name":"Mission_Massacre_Conflict_War","TargetFaction":"Enemy","KillCount":3,"MissionID":1}"""
                ),
                Parse("""{"event":"LaunchFighter","ID":45,"PlayerControlled":true}"""),
                Parse(
                    """{"timestamp":"2026-11-08T09:00:00Z","event":"FactionKillBond","VictimFaction":"Enemy","Reward":1000}"""
                ),
                Parse("""{"event":"DockFighter","ID":45}"""),
                Parse("""{"event":"LaunchFighter","ID":44,"PlayerControlled":true}"""),
                Parse("""{"event":"ApproachSettlement","Name":"Test Base","StationFaction":{"FactionState":"War"}}"""),
                Parse(
                    """{"timestamp":"2026-11-08T09:01:00Z","event":"FactionKillBond","VictimFaction":"Enemy","Reward":17000}"""
                ),
            ],
            retainStatus ? null : srvStatus,
            processHistoricalProgress: true
        );

        Assert.Equal("1/3", Assert.Single(viewModel.MassacreMissions).ProgressText);
        Assert.Equal(1, viewModel.FootCombatKills);
        Assert.Equal("17,000 CR", viewModel.FootCombatBonds);
        CommanderProfileLoadResult saved = await new CommanderProfileStore(temporaryDirectory).LoadAsync("F123", true);
        Assert.Equal(2, Assert.Single(saved.Data!.Combat.MassacreMissions).Remaining);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NomadLaunchAndStatusCanArriveInEitherPollOrderBeforeTheGroundBond(bool statusFirst)
    {
        CombatViewModel viewModel = CreateViewModel();
        viewModel.AutoShowMassacreMissions = true;
        viewModel.AutoShowFootCombat = true;
        viewModel.LoadProfile("F123", "Drew", true, CombatSnapshot.Empty);
        await viewModel.ApplyUpdateAsync(
            [
                Parse("""{"event":"LoadGame","Ship":"federation_corvette"}"""),
                Parse(
                    """{"event":"MissionAccepted","Faction":"Giver","Name":"Mission_Massacre_Conflict_War","TargetFaction":"Enemy","KillCount":3,"MissionID":1}"""
                ),
                Parse("""{"event":"ApproachSettlement","Name":"Test Base","StationFaction":{"FactionState":"War"}}"""),
            ],
            new EliteStatus { Flags = StatusFlags.InMainShip },
            processHistoricalProgress: true
        );
        var srvStatus = new EliteStatus { Flags = StatusFlags.InSrv, Altitude = 50 };
        if (statusFirst)
        {
            await viewModel.ApplyUpdateAsync([], srvStatus, processHistoricalProgress: true);
        }
        await viewModel.ApplyUpdateAsync(
            [Parse("""{"event":"LaunchFighter","ID":44,"PlayerControlled":true}""")],
            null,
            processHistoricalProgress: true
        );
        if (!statusFirst)
        {
            await viewModel.ApplyUpdateAsync([], srvStatus, processHistoricalProgress: true);
        }
        await viewModel.ApplyUpdateAsync(
            [
                Parse(
                    """{"timestamp":"2026-11-08T09:01:00Z","event":"FactionKillBond","VictimFaction":"Enemy","Reward":17000}"""
                ),
            ],
            null,
            processHistoricalProgress: true
        );

        Assert.Equal("0/3", Assert.Single(viewModel.MassacreMissions).ProgressText);
        Assert.Equal(1, viewModel.FootCombatKills);
        Assert.Equal("17,000 CR", viewModel.FootCombatBonds);
        CommanderProfileLoadResult saved = await new CommanderProfileStore(temporaryDirectory).LoadAsync("F123", true);
        Assert.Equal(3, Assert.Single(saved.Data!.Combat.MassacreMissions).Remaining);
    }

    [Fact]
    public async Task LaterStatusConfirmationRetainsNomadIdentityForTheNextLaunch()
    {
        CombatViewModel viewModel = CreateViewModel();
        viewModel.AutoShowMassacreMissions = true;
        viewModel.LoadProfile("F123", "Drew", true, CombatSnapshot.Empty);
        await viewModel.ApplyUpdateAsync(
            [
                Parse("""{"event":"LoadGame","Ship":"federation_corvette"}"""),
                Parse(
                    """{"event":"MissionAccepted","Faction":"Giver","Name":"Mission_Massacre_Conflict_War","TargetFaction":"Enemy","KillCount":3,"MissionID":1}"""
                ),
                Parse("""{"event":"LaunchFighter","ID":44,"PlayerControlled":true}"""),
            ],
            new EliteStatus { Flags = StatusFlags.InMainShip },
            processHistoricalProgress: true
        );
        await viewModel.ApplyUpdateAsync([], new EliteStatus { Flags = StatusFlags.InSrv }, true);
        await viewModel.ApplyUpdateAsync(
            [Parse("""{"event":"DockFighter","ID":44}""")],
            new EliteStatus { Flags = StatusFlags.InMainShip },
            true
        );
        await viewModel.ApplyUpdateAsync(
            [
                Parse("""{"event":"LaunchFighter","ID":44,"PlayerControlled":true}"""),
                Parse(
                    """{"timestamp":"2026-11-08T09:01:00Z","event":"FactionKillBond","VictimFaction":"Enemy","Reward":17000}"""
                ),
            ],
            null,
            processHistoricalProgress: true
        );

        Assert.Equal("0/3", Assert.Single(viewModel.MassacreMissions).ProgressText);
        CommanderProfileLoadResult saved = await new CommanderProfileStore(temporaryDirectory).LoadAsync("F123", true);
        Assert.Equal(3, Assert.Single(saved.Data!.Combat.MassacreMissions).Remaining);
    }

    [Fact]
    public async Task ConflictMassacreAppearsFromBootstrapAndRetainsBondTrackingAfterReload()
    {
        CombatViewModel viewModel = CreateViewModel();
        viewModel.AutoShowMassacreMissions = true;
        viewModel.LoadProfile("F123", "Drew", true, CombatSnapshot.Empty);
        var flightStatus = new EliteStatus { Flags = StatusFlags.InMainShip };
        await viewModel.ApplyUpdateAsync(
            [
                Parse(
                    """{"event":"MissionAccepted","Faction":"Fire Lotus Coalition","Name":"Mission_Massacre_Conflict_CivilWar","TargetFaction":"Sorbago Mastopolos Mining Inc","KillCount":81,"MissionID":1070754782}"""
                ),
            ],
            flightStatus,
            processHistoricalProgress: false
        );

        Assert.True(viewModel.ShouldShowMassacreMissions);
        Assert.Equal("0/81", Assert.Single(viewModel.MassacreMissions).ProgressText);
        CommanderProfileLoadResult saved = await new CommanderProfileStore(temporaryDirectory).LoadAsync("F123", true);
        CombatViewModel reopened = CreateViewModel();
        reopened.LoadProfile("F123", "Drew", true, saved.Data!.Combat);
        await reopened.ApplyUpdateAsync(
            [
                Parse(
                    """{"timestamp":"2026-11-08T09:10:00Z","event":"FactionKillBond","VictimFaction":"Sorbago Mastopolos Mining Inc","Reward":1000}"""
                ),
            ],
            flightStatus,
            processHistoricalProgress: false
        );
        Assert.Equal("0/81", Assert.Single(reopened.MassacreMissions).ProgressText);
        await reopened.ApplyUpdateAsync(
            [
                Parse(
                    """{"timestamp":"2026-11-08T09:10:00Z","event":"FactionKillBond","VictimFaction":"Sorbago Mastopolos Mining Inc","Reward":1000}"""
                ),
            ],
            flightStatus,
            processHistoricalProgress: true
        );

        Assert.True(reopened.ShouldShowMassacreMissions);
        Assert.Equal("1/81", Assert.Single(reopened.MassacreMissions).ProgressText);
        saved = await new CommanderProfileStore(temporaryDirectory).LoadAsync("F123", true);
        Assert.Equal(80, Assert.Single(saved.Data!.Combat.MassacreMissions).Remaining);
    }

    [Theory]
    [InlineData(StatusFlags.None, StatusFlags2.OnFoot, true, "0/3")]
    [InlineData(StatusFlags.InSrv, StatusFlags2.None, true, "0/3")]
    [InlineData(StatusFlags.InFighter, StatusFlags2.None, true, "1/3")]
    [InlineData(StatusFlags.None, StatusFlags2.None, true, "0/3")]
    [InlineData(StatusFlags.InMainShip, StatusFlags2.None, false, "0/3")]
    public async Task CombatBondsHonorMissionTrackingAndVehicleContext(
        StatusFlags flags,
        StatusFlags2 flags2,
        bool trackingEnabled,
        string progress
    )
    {
        CombatViewModel viewModel = CreateViewModel();
        viewModel.AutoShowMassacreMissions = true;
        await viewModel.ApplyUpdateAsync(
            [
                Parse(
                    """{"event":"MissionAccepted","Faction":"Giver","Name":"Mission_Massacre_Conflict_War","TargetFaction":"Enemy","KillCount":3,"MissionID":1}"""
                ),
            ],
            new EliteStatus { Flags = StatusFlags.InMainShip },
            processHistoricalProgress: true
        );
        viewModel.AutoShowMassacreMissions = trackingEnabled;
        await viewModel.ApplyUpdateAsync(
            [
                Parse(
                    """{"timestamp":"2026-11-08T09:10:00Z","event":"FactionKillBond","VictimFaction":"Enemy","Reward":1000}"""
                ),
            ],
            new EliteStatus { Flags = flags, Flags2 = flags2 },
            processHistoricalProgress: true
        );

        Assert.Equal(progress, Assert.Single(viewModel.MassacreMissions).ProgressText);
    }

    [Fact]
    public async Task MassacreProgressPersistsAndMatchesLegacyModes()
    {
        CombatViewModel viewModel = CreateViewModel();
        viewModel.AutoShowMassacreMissions = true;
        viewModel.LoadProfile("F123", "Drew", true, CombatSnapshot.Empty);

        await viewModel.ApplyUpdateAsync(
            [
                Parse(
                    """
                    {"timestamp":"2026-07-25T01:00:00Z","event":"MissionAccepted","Faction":"Giver","Name":"Mission_Massacre","TargetFaction":"Enemy","KillCount":2,"Expiry":"2026-07-26T01:00:00Z","MissionID":123}
                    """
                ),
                Parse(
                    """
                    {"timestamp":"2026-07-25T01:01:00Z","event":"Bounty","VictimFaction":"Enemy"}
                    """
                ),
            ],
            new EliteStatus { Flags = StatusFlags.InMainShip | StatusFlags.Supercruise },
            processHistoricalProgress: true
        );

        Assert.True(viewModel.ShouldShowMassacreMissions);
        Assert.Equal(1, Assert.Single(viewModel.MassacreMissions).Remaining);
        CommanderProfileLoadResult saved = await new CommanderProfileStore(temporaryDirectory).LoadAsync("F123", true);
        Assert.Equal(1, Assert.Single(saved.Data!.Combat.MassacreMissions).Remaining);

        await viewModel.ApplyUpdateAsync(
            [],
            new EliteStatus { Flags = StatusFlags.Landed | StatusFlags.InMainShip },
            processHistoricalProgress: true
        );
        Assert.False(viewModel.ShouldShowMassacreMissions);

        await viewModel.ApplyUpdateAsync(
            [],
            new EliteStatus { GuiFocus = GuiFocus.ExternalPanel },
            processHistoricalProgress: true
        );
        Assert.True(viewModel.ShouldShowMassacreMissions);
    }

    [Fact]
    public async Task BuildSuppressionPreferenceDoesNotGateFootCombatIntent()
    {
        CombatViewModel viewModel = CreateViewModel();
        viewModel.AutoShowFootCombat = true;
        viewModel.SuppressForActiveBuildProjects = true;
        await viewModel.ApplyUpdateAsync(
            [
                Parse(
                    """
                    {"timestamp":"2026-07-25T01:00:00Z","event":"ApproachSettlement","Name":"Test Base","StationFaction":{"FactionState":"War"}}
                    """
                ),
            ],
            new EliteStatus { Flags = StatusFlags.InSrv, Altitude = 50 },
            processHistoricalProgress: true
        );
        Assert.True(viewModel.ShouldShowFootCombat);

        viewModel.SuppressForActiveBuildProjects = false;
        Assert.True(viewModel.ShouldShowFootCombat);
        viewModel.SuppressForActiveBuildProjects = true;
        Assert.True(viewModel.ShouldShowFootCombat);
    }

    [Fact]
    public async Task SixKillMissionProgressContinuesWhileTheColonySuppressionPreferenceIsEnabled()
    {
        CombatViewModel viewModel = CreateViewModel();
        viewModel.AutoShowMassacreMissions = true;
        viewModel.SuppressForActiveBuildProjects = true;
        viewModel.LoadProfile("F123", "Test", true, CombatSnapshot.Empty);
        var ship = new EliteStatus { Flags = StatusFlags.InMainShip };
        await viewModel.ApplyUpdateAsync(
            [
                Parse(
                    """{"event":"MissionAccepted","Name":"Mission_Massacre","Faction":"Giver","TargetFaction":"Enemy","KillCount":6,"MissionID":123}"""
                ),
            ],
            ship,
            processHistoricalProgress: true
        );
        Assert.Equal("0/6", Assert.Single(viewModel.MassacreMissions).ProgressText);
        await viewModel.ApplyUpdateAsync([Parse("""{"event":"Bounty","VictimFaction":"Other"}""")], ship, true);
        Assert.Equal("0/6", Assert.Single(viewModel.MassacreMissions).ProgressText);
        for (int completed = 1; completed <= 6; completed++)
        {
            await viewModel.ApplyUpdateAsync([Parse("""{"event":"Bounty","VictimFaction":"Enemy"}""")], ship, true);
            MassacreMissionViewModel row = Assert.Single(viewModel.MassacreMissions);
            Assert.Equal($"{completed}/6", row.ProgressText);
            Assert.Equal(completed == 6, row.IsComplete);
            Assert.True(viewModel.ShouldShowMassacreMissions);
        }
        CommanderProfileLoadResult saved = await new CommanderProfileStore(temporaryDirectory).LoadAsync("F123", true);
        Assert.Equal(0, Assert.Single(saved.Data!.Combat.MassacreMissions).Remaining);
        await viewModel.ApplyUpdateAsync([Parse("""{"event":"MissionCompleted","MissionID":123}""")], ship, true);
        Assert.Empty(viewModel.MassacreMissions);
    }

    [Fact]
    public async Task DisabledLegacyTestSettingDoesNotStartMissionTracking()
    {
        CombatViewModel viewModel = CreateViewModel();

        await viewModel.ApplyUpdateAsync(
            [
                Parse(
                    """
                    {"timestamp":"2026-07-25T01:00:00Z","event":"MissionAccepted","Faction":"Giver","Name":"Mission_Massacre","TargetFaction":"Enemy","KillCount":2,"MissionID":123}
                    """
                ),
            ],
            new EliteStatus { Flags = StatusFlags.InMainShip },
            processHistoricalProgress: true
        );

        Assert.Empty(viewModel.MassacreMissions);
        Assert.False(viewModel.ShouldShowMassacreMissions);
    }

    private CombatViewModel CreateViewModel()
    {
        Directory.CreateDirectory(temporaryDirectory);
        return new CombatViewModel(
            new CombatSettingsStore(Path.Combine(temporaryDirectory, "ui-settings.json")),
            new CommanderProfileStore(temporaryDirectory)
        );
    }

    private static MassacreMissionSnapshot Mission(long missionId, int remaining)
    {
        return new MassacreMissionSnapshot(
            missionId,
            "Giver",
            "Enemy",
            DateTimeOffset.Parse("2026-07-26T01:00:00Z", global::System.Globalization.CultureInfo.InvariantCulture),
            5,
            remaining
        );
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? value, out string? error), error);
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
