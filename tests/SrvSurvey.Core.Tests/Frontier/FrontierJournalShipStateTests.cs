using System.Text.Json;
using SrvSurvey.Core.Frontier;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Frontier;

public sealed partial class FrontierJournalShipStateTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;

    private static FrontierAccountSnapshot Account(DateTimeOffset? fetched = null)
    {
        var old = new FrontierShipSnapshot(
            7,
            "Cobra Mk III",
            "Old ship",
            "OLD",
            "Sol",
            "Galileo",
            1000,
            true,
            100,
            100,
            Modules: [new("MainEngines", 1, "Old module", "", 1, false, 100, true, 1, "", "", null, [], "old_module")]
        );
        FrontierShipSnapshot stored = old with
        {
            Id = 8,
            Type = "Python",
            Name = "Stored Python",
            IsCurrent = false,
            Modules = [],
        };
        return new("Test", 0, 0, true, true, "Sol", "Galileo", old, [], [old, stored], [], null, fetched ?? Start);
    }

    private static JournalEventEnvelope Event(string kind, int seconds = 1, params (string Key, object Value)[] fields)
    {
        Dictionary<string, object> data = fields.ToDictionary(item => item.Key, item => item.Value);
        data["event"] = kind;
        data["timestamp"] = Start.AddSeconds(seconds).ToString("O");
        Assert.True(
            JournalEventEnvelope.TryParse(JsonSerializer.Serialize(data), out JournalEventEnvelope? entry, out _)
        );
        return entry!;
    }

    private static JournalEventEnvelope Loadout(int seconds = 1, long id = 8) =>
        Event(
            "Loadout",
            seconds,
            ("Ship", "python"),
            ("ShipID", id),
            ("ShipName", "Journal ship"),
            ("ShipIdent", "NEW"),
            ("HullHealth", 0.75),
            ("HullValue", 2000),
            ("ModulesValue", 3000),
            (
                "Modules",
                new object[]
                {
                    new
                    {
                        Slot = "MainEngines",
                        Item = "int_engine_size5_class5",
                        Health = 0.5,
                        On = false,
                        Priority = 2,
                        ItemID = 123,
                        Value = 3000,
                        Engineering = new
                        {
                            Engineer = "Test",
                            BlueprintName = "Engine_Dirty",
                            Level = 5,
                            ExperimentalEffect = "special_engine_dragdrives",
                        },
                    },
                }
            )
        );

    [Theory]
    [InlineData(0, 8)]
    [InlineData(1, 8)]
    [InlineData(2, 7)]
    public void MostRecentObservationWinsAndTiesFavorJournal(int fetchedSecond, long expectedId)
    {
        var state = new FrontierJournalShipState();
        Assert.True(state.Apply(Loadout()));
        Assert.Equal(expectedId, state.Project(Account(Start.AddSeconds(fetchedSecond)))!.Id);
    }

    [Theory]
    [InlineData("LoadGame", "Ship", "ShipID")]
    [InlineData("ShipyardSwap", "ShipType", "ShipID")]
    [InlineData("ShipyardBuy", "ShipType", "NewShipID")]
    [InlineData("ShipyardNew", "ShipType", "NewShipID")]
    public void SelectionUsesOnlyTheSelectedShipsCachedDetails(string kind, string shipKey, string idKey)
    {
        var state = new FrontierJournalShipState();
        Assert.True(state.Apply(Event(kind, 1, (shipKey, "python"), (idKey, 8))));
        FrontierShipSnapshot projected = state.Project(Account())!;
        Assert.Equal(8, projected.Id);
        Assert.Equal("Stored Python", projected.Name);
        Assert.Empty(projected.Modules!);
    }

    [Fact]
    public void NewShipWithoutCachedDetailsDoesNotBorrowTheOldShipsNameOrModules()
    {
        var state = new FrontierJournalShipState();
        state.Apply(Event("ShipyardNew", 1, ("ShipType", "python"), ("NewShipID", 99)));
        FrontierShipSnapshot projected = state.Project(Account())!;
        Assert.Equal(99, projected.Id);
        Assert.Equal("Python", projected.Name);
        Assert.Null(projected.Modules);
        Assert.Null(projected.HullHealth);
        state.Apply(Event("ShipyardBuy", 2, ("ShipType", "anaconda")));
        Assert.Null(state.Project(Account())!.Id);
        Assert.Equal("Anaconda", state.Project(Account())!.Name);
    }

    [Fact]
    public void LoadoutProjectsModulesEngineeringHealthAndValues()
    {
        var state = new FrontierJournalShipState();
        state.Apply(Loadout());
        FrontierShipSnapshot ship = state.Project(Account())!;
        Assert.Equal("Journal ship", ship.Name);
        Assert.Equal("NEW", ship.Identifier);
        Assert.Equal(5000, ship.Value);
        Assert.Equal(75, ship.HullHealth);
        FrontierShipModuleSnapshot module = Assert.Single(ship.Modules!);
        Assert.Equal("int_engine_size5_class5", module.InternalName);
        Assert.Equal(50, module.Health);
        Assert.False(module.IsPowered);
        Assert.Equal(2, module.Priority);
        Assert.Equal(5, module.BlueprintLevel);
        Assert.Equal("Test", module.Engineer);
        Assert.Equal("Engine_Dirty", module.Blueprint);
        Assert.Equal(["special_engine_dragdrives"], module.ExperimentalEffects);
        Assert.Contains(ship.DataPoints!, point => point.Path.Contains("ShipID", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("ModuleBuy")]
    [InlineData("ModuleSell")]
    [InlineData("ModuleStore")]
    [InlineData("ModuleRetrieve")]
    [InlineData("ModuleSwap")]
    [InlineData("MassModuleStore")]
    [InlineData("EngineerCraft")]
    [InlineData("EngineerLegacyConvert")]
    [InlineData("ApplyExperimentalEffect")]
    public void PartialOutfittingInvalidatesModulesUntilACompleteNewerObservation(string kind)
    {
        var state = new FrontierJournalShipState();
        Assert.False(state.Apply(Event(kind)));
        state.Apply(Loadout(id: 7));
        Assert.True(state.Apply(Event(kind, 2)));
        Assert.Empty(state.Project(Account())!.Modules!);
        Assert.Equal(7, state.Project(Account(Start.AddSeconds(3)))!.Id);
        Assert.Single(state.Project(Account(Start.AddSeconds(3)))!.Modules!);
        state.Apply(Loadout(4, 7));
        Assert.Equal("int_engine_size5_class5", Assert.Single(state.Project(Account())!.Modules!).InternalName);
    }

    [Fact]
    public void RenamingDoesNotPromoteAnOlderLoadoutAboveNewerCapiDetails()
    {
        var state = new FrontierJournalShipState();
        state.Apply(Loadout(id: 7));
        state.Apply(
            Event(
                "SetUserShipName",
                3,
                ("Ship", "python"),
                ("ShipID", 7),
                ("UserShipName", "Renamed"),
                ("UserShipId", "RN-1")
            )
        );
        FrontierShipSnapshot projected = state.Project(Account(Start.AddSeconds(2)))!;
        Assert.Equal("Renamed", projected.Name);
        Assert.Equal("RN-1", projected.Identifier);
        Assert.Equal("old_module", Assert.Single(projected.Modules!).InternalName);
    }

    [Theory]
    [InlineData("testbuggy")]
    [InlineData("federation_fighter")]
    [InlineData("combat_multicrew_srv_01")]
    [InlineData("")]
    public void VehicleOrIncompleteEventsCannotReplaceTheOwnShip(string symbol)
    {
        var state = new FrontierJournalShipState();
        Assert.False(state.Apply(Event("Loadout", 1, ("Ship", symbol), ("ShipID", 1))));
        Assert.Equal(7, state.Project(Account())!.Id);
        Assert.Null(state.Project(null));
    }

    [Fact]
    public void CrewAndOlderRecordsDoNotRewindTheOwnShip()
    {
        var state = new FrontierJournalShipState();
        state.Apply(Loadout(2));
        Assert.False(state.Apply(Loadout(1, 7)));
        Assert.False(state.Apply(Event("JoinACrew", 3)));
        Assert.False(state.Apply(Loadout(4, 7)));
        Assert.Equal(8, state.Project(Account())!.Id);
        Assert.False(state.Apply(Event("QuitACrew", 5)));
        Assert.True(state.Apply(Loadout(6, 7)));
        Assert.False(state.Apply(Event("Music", 7)));
        Assert.False(state.Apply(Event("JoinACrew", 1)));
        Assert.True(state.Apply(Loadout(8)));
        Assert.False(state.Apply(Event("QuitACrew", 4)));
    }

    [Fact]
    public void NewerIdentityDoesNotReviveStaleNamesOrInvalidatedModules()
    {
        var state = new FrontierJournalShipState();
        state.Apply(Loadout(id: 7));
        state.Apply(Event("ModuleSell", 2));
        state.Apply(Event("LoadGame", 4, ("Ship", "python"), ("ShipID", 7)));
        FrontierShipSnapshot projected = state.Project(Account(Start.AddSeconds(3)))!;
        Assert.Equal("Old ship", projected.Name);
        Assert.Equal("OLD", projected.Identifier);
        Assert.Equal("old_module", Assert.Single(projected.Modules!).InternalName);
    }

    [Fact]
    public void PreviewEngineeringDoesNotInvalidateTheRealLoadout()
    {
        var state = new FrontierJournalShipState();
        state.Apply(Loadout());
        Assert.False(state.Apply(Event("EngineerCraft", 2, ("IsPreview", true))));
        Assert.Single(state.Project(Account())!.Modules!);
    }

    [Theory]
    [InlineData(false, 7)]
    [InlineData(true, 8)]
    public void OutfittingCannotPromoteAnOldShipSelectionWithoutAnExplicitShipIdentity(bool hasIdentity, long expected)
    {
        var state = new FrontierJournalShipState();
        state.Apply(Loadout());
        JournalEventEnvelope edit = hasIdentity
            ? Event("ModuleBuy", 3, ("Ship", "python"), ("ShipID", 8))
            : Event("ModuleBuy", 3);
        Assert.True(state.Apply(edit));
        Assert.Equal(expected, state.Project(Account(Start.AddSeconds(2)))!.Id);
    }

    [Fact]
    public void ExplicitlyClearedShipNameAndIdentifierOverrideCachedNames()
    {
        var state = new FrontierJournalShipState();
        state.Apply(Loadout(id: 7));
        state.Apply(
            Event("SetUserShipName", 2, ("Ship", "python"), ("ShipID", 7), ("UserShipName", ""), ("UserShipId", ""))
        );
        FrontierShipSnapshot ship = state.Project(Account())!;
        Assert.Equal("Python", ship.Name);
        Assert.Equal("", ship.Identifier);
    }

    [Fact]
    public void MinimalAndMalformedOptionalFieldsKeepSafeDefaults()
    {
        var state = new FrontierJournalShipState();
        Assert.True(
            JournalEventEnvelope.TryParse(
                """{"event":"Loadout","Ship":"python"}""",
                out JournalEventEnvelope? missingTime,
                out _
            )
        );
        Assert.False(state.Apply(missingTime!));
        state.Apply(
            Event(
                "Loadout",
                1,
                ("Ship", "unknown_ship"),
                ("ShipID", 7),
                ("HullHealth", "unknown"),
                (
                    "Modules",
                    new object[]
                    {
                        new
                        {
                            Slot = "Test",
                            Item = "unknown_module",
                            Item_Localised = "Test module",
                            Health = 2,
                            Engineering = "invalid",
                            Value = "invalid",
                            Priority = 1.5,
                        },
                    }
                )
            )
        );
        FrontierShipSnapshot ship = state.Project(Account())!;
        Assert.Equal("unknown_ship", ship.Type);
        Assert.Equal(1000, ship.Value);
        Assert.Equal(100, ship.HullHealth);
        FrontierShipModuleSnapshot module = Assert.Single(ship.Modules!);
        Assert.Equal("Test module", module.Name);
        Assert.True(module.IsPowered);
        Assert.Equal(100, module.Health);
        Assert.Empty(module.ExperimentalEffects);
        Assert.Null(module.Priority);
        Assert.Equal(0, module.Value);
        Assert.True(state.Apply(Event("Loadout", 2, ("Ship", "unknown_ship"), ("ShipID", 7), ("Modules", "invalid"))));
    }
}
