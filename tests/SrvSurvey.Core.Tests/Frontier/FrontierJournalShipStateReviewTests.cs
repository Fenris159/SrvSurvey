using SrvSurvey.Core.Frontier;

namespace SrvSurvey.Core.Tests.Frontier;

public sealed partial class FrontierJournalShipStateTests
{
    [Fact]
    public void CodeRabbitProjectedShipTotalIncludesRetainedCargoValue()
    {
        FrontierAccountSnapshot baseline = Account();
        FrontierAccountSnapshot cached = baseline with
        {
            CurrentShip = baseline.CurrentShip! with { CargoValue = 250 },
        };
        var state = new FrontierJournalShipState();
        state.Apply(Loadout(id: 7));
        FrontierShipSnapshot ship = state.Project(cached)!;
        Assert.Equal(5250, ship.Value);
        Assert.Equal(ship.HullValue + ship.ModulesValue + ship.CargoValue, ship.Value);
    }

    [Fact]
    public void ReviewRegressionOwnLoadGameEndsAnInterruptedCrewSession()
    {
        var state = new FrontierJournalShipState();
        state.Apply(Loadout());
        state.Apply(Event("JoinACrew", 2));
        state.Apply(Event("Shutdown", 3));
        Assert.True(state.Apply(Event("LoadGame", 4, ("Ship", "python"), ("ShipID", 7))));
        Assert.True(state.Apply(Loadout(5, 7)));
        Assert.Equal(7, state.Project(Account())!.Id);
    }

    [Theory]
    [InlineData("EngineerCraft")]
    [InlineData("EngineerLegacyConvert")]
    public void ReviewRegressionCompleteEngineeringPreservesTheLoadout(string kind)
    {
        var state = new FrontierJournalShipState();
        state.Apply(Loadout());
        state.Apply(
            Event(
                kind,
                2,
                ("Slot", "MainEngines"),
                ("Module", "int_engine_size5_class5"),
                ("Engineer", "New engineer"),
                ("BlueprintName", "Engine_Tuned"),
                ("Level", 3),
                ("Modifiers", Array.Empty<object>())
            )
        );
        FrontierShipModuleSnapshot module = Assert.Single(state.Project(Account())!.Modules!);
        Assert.Equal("New engineer", module.Engineer);
        Assert.Equal("Engine_Tuned", module.Blueprint);
        Assert.Equal(3, module.BlueprintLevel);
        Assert.Equal(50, module.Health);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReviewRegressionChangedHangarsCannotRestoreCachedLaunchBays(bool keepHangar)
    {
        FrontierAccountSnapshot baseline = Account();
        FrontierShipSnapshot cachedShip = baseline.CurrentShip! with
        {
            Modules =
            [
                new(
                    "Slot 05 Size 6",
                    10,
                    "Vessel Hangar",
                    "",
                    100,
                    false,
                    100,
                    true,
                    1,
                    "",
                    "",
                    null,
                    [],
                    "int_fighterbay_size5_class1"
                ),
            ],
            LaunchBays = [new("Fighter 1", "Taipan", "Aegis", 5)],
        };
        FrontierAccountSnapshot cached = baseline with { CurrentShip = cachedShip, Ships = [cachedShip] };
        var state = new FrontierJournalShipState();
        state.Apply(Loadout(id: 7));
        state.Apply(Event("ModuleSell", 2));
        Assert.Empty(state.Project(cached)!.LaunchBays!);
        string item = keepHangar ? "int_fighterbay_size5_class1" : "int_cargorack_size5_class1";
        state.Apply(
            Event(
                "Loadout",
                3,
                ("Ship", "python"),
                ("ShipID", 7),
                ("Modules", new[] { new { Slot = "Slot05_Size6", Item = item } })
            )
        );
        Assert.Equal(keepHangar ? 1 : 0, state.Project(cached)!.LaunchBays!.Count);
        Assert.Single(state.Project(cached with { FetchedAt = Start.AddSeconds(4) })!.LaunchBays!);
    }

    [Fact]
    public void ReviewRegressionMalformedLoadoutDoesNotBecomeAPartialModuleList()
    {
        var state = new FrontierJournalShipState();
        state.Apply(
            Event(
                "Loadout",
                1,
                ("Ship", "python"),
                ("ShipID", 7),
                (
                    "Modules",
                    new object[]
                    {
                        new { Slot = "MainEngines", Item = "int_engine_size5_class5" },
                        new { Slot = "MissingItem" },
                    }
                )
            )
        );
        Assert.Empty(state.Project(Account())!.Modules!);
    }

    [Fact]
    public void ReviewRegressionEngineeringOnlyOverridesFieldsObservedAfterCapi()
    {
        var state = new FrontierJournalShipState();
        state.Apply(Loadout(id: 7));
        state.Apply(
            Event(
                "EngineerCraft",
                3,
                ("Slot", "MainEngines"),
                ("Module", "int_engine_size5_class5"),
                ("Engineer", "New engineer"),
                ("BlueprintName", "Engine_Tuned"),
                ("Level", 3),
                ("Modifiers", Array.Empty<object>())
            )
        );
        state.Apply(
            Event(
                "EngineerCraft",
                5,
                ("Slot", "MainEngines"),
                ("Module", "int_engine_size5_class5"),
                ("Level", 5),
                ("Modifiers", Array.Empty<object>())
            )
        );
        FrontierAccountSnapshot baseline = Account();
        FrontierShipSnapshot fresh = baseline.CurrentShip! with
        {
            Modules =
            [
                new(
                    "Main Engines",
                    99,
                    "Fresh engine",
                    "",
                    6000,
                    false,
                    99,
                    true,
                    4,
                    "Remote engineer",
                    "Remote blueprint",
                    2,
                    ["Remote effect"],
                    "int_engine_size5_class5"
                ),
            ],
        };
        FrontierAccountSnapshot account = baseline with { CurrentShip = fresh, FetchedAt = Start.AddSeconds(4) };
        FrontierShipModuleSnapshot module = Assert.Single(state.Project(account)!.Modules!);
        Assert.Equal("Remote engineer", module.Engineer);
        Assert.Equal("Remote blueprint", module.Blueprint);
        Assert.Equal(5, module.BlueprintLevel);
        Assert.Equal(["Remote effect"], module.ExperimentalEffects);
        Assert.Equal(99, module.Health);
        Assert.Equal(6000, module.Value);

        module = Assert.Single(state.Project(account with { FetchedAt = Start.AddSeconds(2) })!.Modules!);
        Assert.Equal("New engineer", module.Engineer);
        Assert.Equal("Engine_Tuned", module.Blueprint);
        Assert.Equal(5, module.BlueprintLevel);

        state.Apply(
            Event(
                "EngineerCraft",
                6,
                ("Slot", "MainEngines"),
                ("Module", "int_engine_size5_class5"),
                ("ExperimentalEffect", "special_engine_dragdrives"),
                ("Modifiers", Array.Empty<object>())
            )
        );
        module = Assert.Single(state.Project(account)!.Modules!);
        Assert.Equal(["special_engine_dragdrives"], module.ExperimentalEffects);
        Assert.Equal("Remote engineer", module.Engineer);
        Assert.Equal(
            "Remote engineer",
            Assert.Single(state.Project(account with { FetchedAt = Start.AddSeconds(7) })!.Modules!).Engineer
        );
    }
}
