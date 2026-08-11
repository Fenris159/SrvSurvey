using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests;

public sealed class JournalSessionStateTests
{
    [Fact]
    public void ApplyBuildsStateIncrementallyAndObservesUnknownEvents()
    {
        var state = new JournalSessionState();

        Assert.True(state.Apply(Parse(
            """{"timestamp":"2026-07-24T10:00:00Z","event":"Commander","Name":"Drew","FID":"F123"}""")));
        Assert.True(state.Apply(Parse(
            """{"timestamp":"2026-07-24T10:00:01Z","event":"Location","StarSystem":"Sol","SystemAddress":"10477373803","StarPos":[0,0,0],"Body":"Earth","BodyType":"Planet"}""")));
        Assert.True(state.Apply(Parse(
            """{"timestamp":"2026-07-24T10:00:01Z","event":"Loadout","Ship":"mandalay"}""")));
        Assert.False(state.Apply(Parse(
            """{"timestamp":"2026-07-24T10:00:02Z","event":"FutureEvent","Value":42}""")));
        Assert.True(state.Apply(Parse(
            """{"timestamp":"2026-07-24T10:00:03Z","event":"Shutdown"}""")));

        var snapshot = state.CreateSnapshot("Journal.fixture.log");
        Assert.Equal("Drew", snapshot.CommanderName);
        Assert.Equal("F123", snapshot.FrontierId);
        Assert.Equal("Sol", snapshot.SystemName);
        Assert.Equal(10477373803, snapshot.SystemAddress);
        Assert.Equal(new GalacticCoordinate(0, 0, 0), snapshot.StarPosition);
        Assert.Equal("Earth", snapshot.BodyName);
        Assert.True(snapshot.IsShutdown);
        Assert.Equal("mandalay", state.ShipType);
        Assert.Equal(5, snapshot.ValidLineCount);
        Assert.Equal(4, snapshot.RecognizedEventCount);
        Assert.Equal(1, state.UnhandledEventCount);
        Assert.Equal(
            DateTimeOffset.Parse("2026-07-24T10:00:03Z"),
            snapshot.LastEventTimestamp);
    }

    [Fact]
    public void VehicleLaunchAndDockEventsPreserveVrCalibrationMode()
    {
        var state = new JournalSessionState();

        Assert.True(state.Apply(Parse(
            """{"timestamp":"2026-07-24T10:00:00Z","event":"LaunchSRV","SRVType":"combat_multicrew_srv_01"}""")));
        Assert.Equal("combat_multicrew_srv_01", state.ActiveSrvType);
        Assert.True(state.Apply(Parse(
            """{"timestamp":"2026-07-24T10:00:01Z","event":"LaunchFighter"}""")));
        Assert.True(state.IsFighterLaunched);

        Assert.True(state.Apply(Parse(
            """{"timestamp":"2026-07-24T10:00:02Z","event":"DockFighter"}""")));
        Assert.False(state.IsFighterLaunched);
        Assert.True(state.Apply(Parse(
            """{"timestamp":"2026-07-24T10:00:03Z","event":"DockSRV"}""")));
        Assert.Null(state.ActiveSrvType);
    }

    [Fact]
    public void NomadLaunchUsesHybridJournalAndStatusTelemetry()
    {
        var state = new JournalSessionState();

        Assert.True(state.Apply(Parse(
            """{"event":"LaunchFighter","Loadout":"base","ID":44,"PlayerControlled":true}""")));
        Assert.True(state.IsFighterLaunched);

        Assert.True(state.ReconcileVehicleStatus(new EliteStatus
        {
            Flags = StatusFlags.InSrv | StatusFlags.LandingGearDown,
        }));

        Assert.Equal(EliteSrvTypes.Nomad, state.ActiveSrvType);
        Assert.True(state.IsNomadActive);
        Assert.False(state.IsFighterLaunched);
    }

    [Fact]
    public void LoadGameInsideNomadRestoresActiveVehicleIdentity()
    {
        var state = new JournalSessionState();

        Assert.True(state.Apply(Parse(
            """{"event":"LoadGame","Ship":"Lander01","Ship_Localised":"Nomad","ShipID":44,"StartLanded":true}""")));

        Assert.Equal("Lander01", state.ShipType);
        Assert.Equal(EliteSrvTypes.Nomad, state.ActiveSrvType);
        Assert.True(state.IsNomadActive);
        Assert.False(state.IsFighterLaunched);
        Assert.False(state.ReconcileVehicleStatus(new EliteStatus
        {
            Flags = StatusFlags.InSrv,
        }));

        Assert.True(state.ReconcileVehicleStatus(new EliteStatus()));
        Assert.Null(state.ActiveSrvType);
        Assert.True(state.Apply(Parse(
            """{"event":"Embark","SRV":true,"Taxi":false,"ID":44}""")));
        Assert.Equal(EliteSrvTypes.Nomad, state.ActiveSrvType);
    }

    [Fact]
    public void NomadTypeReportedAtDockIsReusedForTheNextLaunch()
    {
        var state = new JournalSessionState();

        Assert.True(state.Apply(Parse(
            """{"event":"DockSRV","SRVType":"lander01","SRVType_Localised":"Nomad","ID":44}""")));
        Assert.True(state.Apply(Parse(
            """{"event":"LaunchFighter","Loadout":"base","ID":44,"PlayerControlled":true}""")));

        Assert.True(state.IsNomadActive);
        Assert.False(state.IsFighterLaunched);
        Assert.False(state.ReconcileVehicleStatus(new EliteStatus
        {
            Flags = StatusFlags.InSrv,
        }));
    }

    [Fact]
    public void KnownNomadLaunchDoesNotReuseItsIdAfterLeavingTheVehicle()
    {
        var state = new JournalSessionState();

        Assert.True(state.Apply(Parse(
            """{"event":"DockSRV","SRVType":"lander01","ID":44}""")));
        Assert.True(state.Apply(Parse(
            """{"event":"LaunchFighter","ID":44,"PlayerControlled":true}""")));
        Assert.True(state.ReconcileVehicleStatus(new EliteStatus()));
        Assert.Null(state.ActiveSrvType);

        Assert.False(state.ReconcileVehicleStatus(new EliteStatus
        {
            Flags = StatusFlags.InSrv,
        }));
        Assert.Null(state.ActiveSrvType);
    }

    [Theory]
    [InlineData("{\"event\":\"LoadGame\",\"Ship\":\"Lander01\"}")]
    [InlineData("{\"event\":\"LoadGame\",\"Ship\":\"Lander01\",\"ShipID\":\"invalid\"}")]
    public void NomadLoadGameDoesNotMapAStaleShipId(string loadGameEvent)
    {
        var state = new JournalSessionState();

        Assert.True(state.Apply(Parse(
            """{"event":"LoadGame","Ship":"mandalay","ShipID":44}""")));
        Assert.True(state.Apply(Parse(loadGameEvent)));
        Assert.Equal(44, state.ShipId);
        Assert.True(state.ReconcileVehicleStatus(new EliteStatus()));
        Assert.Null(state.ActiveSrvType);

        Assert.True(state.Apply(Parse(
            """{"event":"Embark","SRV":true,"ID":44}""")));
        Assert.Null(state.ActiveSrvType);
    }

    [Fact]
    public void ConventionalFighterAndSrvTelemetryRemainDistinct()
    {
        var fighterState = new JournalSessionState();
        Assert.True(fighterState.Apply(Parse(
            """{"event":"LaunchFighter","ID":7,"PlayerControlled":true}""")));
        Assert.False(fighterState.ReconcileVehicleStatus(new EliteStatus
        {
            Flags = StatusFlags.InFighter,
        }));
        Assert.Null(fighterState.ActiveSrvType);
        Assert.False(fighterState.IsNomadActive);
        Assert.True(fighterState.IsFighterLaunched);

        var srvState = new JournalSessionState();
        Assert.True(srvState.Apply(Parse(
            """{"event":"LaunchSRV","SRVType":"testbuggy","ID":8}""")));
        Assert.False(srvState.ReconcileVehicleStatus(new EliteStatus
        {
            Flags = StatusFlags.InSrv,
        }));
        Assert.Equal("testbuggy", srvState.ActiveSrvType);
        Assert.False(srvState.IsNomadActive);
    }

    [Theory]
    [InlineData("Died")]
    [InlineData("Resurrect")]
    public void DeathLifecycleClearsOnlyTransientLocationContext(
        string eventName)
    {
        var state = new JournalSessionState();
        state.Apply(Parse(
            """{"event":"Commander","Name":"Drew","FID":"F123"}"""));
        state.Apply(Parse(
            """{"event":"Location","StarSystem":"Sol","SystemAddress":42,"StarPos":[1,2,3],"Body":"Earth","BodyType":"Planet"}"""));
        state.Apply(Parse(
            """{"event":"Loadout","Ship":"mandalay"}"""));
        state.Apply(Parse(
            """{"event":"LaunchSRV","SRVType":"combat_multicrew_srv_01"}"""));
        state.Apply(Parse("""{"event":"LaunchFighter"}"""));

        Assert.True(state.Apply(Parse(
            $$"""{"event":"{{eventName}}"}""")));

        Assert.Equal("Drew", state.CommanderName);
        Assert.Equal("mandalay", state.ShipType);
        Assert.Equal("Sol", state.SystemName);
        Assert.Equal(42, state.SystemAddress);
        Assert.Equal(new GalacticCoordinate(1, 2, 3), state.StarPosition);
        Assert.Null(state.BodyName);
        Assert.Null(state.ActiveSrvType);
        Assert.False(state.IsFighterLaunched);
        Assert.False(state.IsShutdown);
    }

    [Fact]
    public void MainMenuMusicClearsTransientLocationContext()
    {
        var state = new JournalSessionState();
        state.Apply(Parse(
            """{"event":"Location","StarSystem":"Sol","SystemAddress":42,"Body":"Earth","BodyType":"Planet"}"""));
        state.Apply(Parse(
            """{"event":"LaunchSRV","SRVType":"testbuggy"}"""));

        Assert.True(state.Apply(Parse(
            """{"event":"Music","MusicTrack":"MainMenu"}""")));

        Assert.Equal("Sol", state.SystemName);
        Assert.Null(state.BodyName);
        Assert.Null(state.ActiveSrvType);
        Assert.True(state.IsAtMainMenu);
        Assert.Equal("MainMenu", state.MusicTrack);

        Assert.True(state.Apply(Parse(
            """{"event":"Music","MusicTrack":"Exploration"}""")));
        Assert.False(state.IsAtMainMenu);
        Assert.Equal("Exploration", state.MusicTrack);

        Assert.True(state.Apply(Parse(
            """{"event":"Music","MusicTrack":"FleetCarrier_Managment"}""")));
        Assert.True(state.IsAtCarrierManagement);
        Assert.False(state.IsAtMainMenu);
        Assert.Equal("FleetCarrier_Managment", state.MusicTrack);
    }

    [Fact]
    public void FileheaderTreatsInitialFrontEndAsMainMenuUntilLoadGame()
    {
        var state = new JournalSessionState();

        Assert.True(state.Apply(Parse(
            """{"event":"Fileheader","gameversion":"4.4.0.3"}""")));
        Assert.True(state.IsAtMainMenu);

        Assert.True(state.Apply(Parse(
            """{"event":"LoadGame","Commander":"Drew","FID":"F123"}""")));
        Assert.False(state.IsAtMainMenu);
    }

    [Fact]
    public void HyperspaceDepartureClearsTransientContextBeforeArrival()
    {
        var state = new JournalSessionState();
        state.Apply(Parse(
            """{"event":"Location","StarSystem":"Sol","SystemAddress":42,"StarPos":[1,2,3],"Body":"Earth","BodyType":"Planet"}"""));
        state.Apply(Parse(
            """{"event":"Loadout","Ship":"mandalay"}"""));
        state.Apply(Parse(
            """{"event":"LaunchSRV","SRVType":"testbuggy"}"""));

        Assert.True(state.Apply(Parse(
            """{"event":"StartJump","JumpType":"Hyperspace"}""")));

        Assert.Equal("Sol", state.SystemName);
        Assert.Equal(42, state.SystemAddress);
        Assert.Equal(new GalacticCoordinate(1, 2, 3), state.StarPosition);
        Assert.Equal("mandalay", state.ShipType);
        Assert.Null(state.BodyName);
        Assert.Null(state.ActiveSrvType);
    }

    [Fact]
    public void SupercruiseDepartureDoesNotDiscardCurrentBodyIdentity()
    {
        var state = new JournalSessionState();
        state.Apply(Parse(
            """{"event":"Location","StarSystem":"Sol","SystemAddress":42,"Body":"Earth","BodyType":"Planet"}"""));

        Assert.False(state.Apply(Parse(
            """{"event":"StartJump","JumpType":"Supercruise"}""")));

        Assert.Equal("Earth", state.BodyName);
    }

    [Theory]
    [InlineData("flightsuit_class1", OdysseySuitType.Flight)]
    [InlineData("explorationsuit_class5", OdysseySuitType.Artemis)]
    [InlineData("utilitysuit_class3", OdysseySuitType.Maverick)]
    [InlineData("tacticalsuit_class5", OdysseySuitType.Dominator)]
    [InlineData("future_suit", OdysseySuitType.Unknown)]
    public void SuitLoadoutsTrackLegacySuitCategories(
        string suitName,
        OdysseySuitType expected)
    {
        var state = new JournalSessionState();

        Assert.True(state.Apply(Parse(
            $$"""{"event":"SuitLoadout","SuitName":"{{suitName}}"}""")));
        Assert.Equal(expected, state.CurrentSuit);

        Assert.True(state.Apply(Parse(
            """{"event":"SwitchSuitLoadout","SuitName":"tacticalsuit_class2"}""")));
        Assert.Equal(OdysseySuitType.Dominator, state.CurrentSuit);
    }

    [Fact]
    public void TracksShipAndStationIdentityForExternalEventMapping()
    {
        var state = new JournalSessionState();

        Assert.True(state.Apply(Parse("""
            {
              "event": "Loadout",
              "Ship": "mandalay",
              "ShipID": 42,
              "ShipName": "Surveyor",
              "ShipIdent": "SRV-42"
            }
            """)));
        Assert.True(state.Apply(Parse("""
            {
              "event": "Docked",
              "StarSystem": "Sol",
              "SystemAddress": 10477373803,
              "StationName": "Galileo"
            }
            """)));

        Assert.Equal("mandalay", state.ShipType);
        Assert.Equal(42, state.ShipId);
        Assert.Equal("Surveyor", state.ShipName);
        Assert.Equal("SRV-42", state.ShipIdent);
        Assert.Equal("Galileo", state.StationName);

        Assert.True(state.Apply(Parse("""
            {
              "event": "Undocked",
              "StationName": "Galileo"
            }
            """)));
        Assert.Null(state.StationName);
    }

    private static JournalEventEnvelope Parse(string json)
    {
        var success = JournalEventEnvelope.TryParse(json, out var journalEvent, out var error);
        Assert.True(success, error);
        return Assert.IsType<JournalEventEnvelope>(journalEvent);
    }
}
