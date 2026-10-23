using System.Text.Json;
using SrvSurvey.Core.Firegroups;
using SrvSurvey.Core.Ships;

namespace SrvSurvey.Core.Tests.Ships;

public sealed class ShipModuleCatalogTests
{
    private static readonly ShipModuleCatalog Catalog = ShipModuleCatalog.Default;

    /// <summary>
    /// Checks that catalog includes mercenary modules and ship specific armour without invented blueprint symbols.
    /// </summary>
    [Fact]
    public void CatalogIncludesMercenaryModulesAndShipSpecificArmourWithoutInventedBlueprintSymbols()
    {
        Assert.Equal("ea787cf805f2c38f72c8d5f168fa1063341ebd9a", Catalog.SourceRevision);
        Assert.Equal(49, Catalog.Ships.Count);
        Assert.Equal(1202, Catalog.Modules.Count);
        Assert.Equal(25, Catalog.MercenaryModules.Count);
        Assert.Equal("Type-11 Prospector", Catalog.FindShip("LAKONMINER")!.Name);
        Assert.Equal("Lightweight Alloy", Catalog.FindModule("lakonminer_armour_grade1")!.Name);
        Assert.Equal("Mk II Vessel Hangar", Catalog.FindModule("Int_FighterBayMk2_Size5_Class1_Free")!.Name);
        Assert.Contains(
            Catalog.MercenaryModules,
            module => module.Name == "Exposing Seeker Missile Rack" && module.BlueprintSymbol is null
        );
        Assert.Contains(Catalog.MercenaryModules, module => module.Name == "Heavy Duty Module Reinforcement Package");
        Assert.Contains(Catalog.MercenaryModules, module => module.Name == "Extended Cargo Rack");
        Assert.Equal("Large Planetary Vehicle Hangar", Catalog.FindModule("Int_LargeBuggyBay_Size2_Class3_Free")!.Name);
    }

    /// <summary>The catalog preserves separate armour identities, dedicated cargo storage, and the verified Mk II slot layout.</summary>
    [Fact]
    public void AnacondaMkIIHasDistinctArmourAndItsActualSlotLayout()
    {
        ShipDefinition ship = Assert.IsType<ShipDefinition>(Catalog.FindShip("anacondamkii"));
        Assert.Equal("Anaconda Mk II", ship.Name);
        Assert.Equal(
            [4, 3, 3, 3, 3, 1, 1],
            ship.Slots.Where(slot => slot.Group == "hardpoint").Select(slot => slot.Size)
        );
        Assert.Equal(8, ship.Slots.Count(slot => slot.Group == "utility"));
        Assert.Equal(6, Assert.Single(ship.Slots, slot => slot.Name == "Cargo01").Size);
        ModuleDefinition ablative = Catalog.FindModule("AnacondaMkII_Armour_Grade1")!;
        ModuleDefinition ordinary = Catalog.FindModule("AnacondaMkII_Armour_Grade1_Default")!;
        Assert.Equal(129046027, ablative.FrontierId);
        Assert.Equal(129046028, ordinary.FrontierId);
        Assert.NotEqual(ablative.Symbol, ordinary.Symbol);
        Assert.Equal(
            6,
            Catalog.Modules.Count(module => module.Symbol.StartsWith("AnacondaMkII_Armour_", StringComparison.Ordinal))
        );
        Assert.Equal(129051593, Catalog.FindModule("Int_LargeCargoRack_Size6_Class1")!.FrontierId);
    }

    /// <summary>
    /// Checks that hangar and general fitting rules match the source.
    /// </summary>
    [Theory]
    [InlineData("SideWinder", "Slot01_Size2", "Int_BuggyBay_Size2_Class2", ModuleFit.Allowed)]
    [InlineData("SideWinder", "Slot01_Size2", "Int_LargeBuggyBay_Size2_Class3", ModuleFit.RestrictedShip)]
    [InlineData("SideWinder", "Slot01_Size2", "Int_MkIILargeBuggyBay_Size2_Class3", ModuleFit.RestrictedShip)]
    [InlineData("Krait_MkII", "Slot01_Size6", "Int_LargeBuggyBay_Size2_Class3", ModuleFit.Allowed)]
    [InlineData("Krait_MkII", "Slot01_Size6", "Int_MkIILargeBuggyBay_Size2_Class3", ModuleFit.RestrictedShip)]
    [InlineData("LakonMiner", "FighterBay01", "Int_BuggyBay_Size2_Class2", ModuleFit.RestrictedSlot)]
    [InlineData("LakonMiner", "FighterBay01", "Int_LargeBuggyBay_Size2_Class3", ModuleFit.Allowed)]
    [InlineData("LakonMiner", "FighterBay01", "Int_MkIILargeBuggyBay_Size2_Class3", ModuleFit.Allowed)]
    [InlineData("LakonMiner", "FighterBay01", "Int_FighterBayMk2_Size5_Class1", ModuleFit.Allowed)]
    [InlineData("LakonMiner", "FighterBay01", "Int_FighterBayMk2_Size5_Class1_Free", ModuleFit.Allowed)]
    [InlineData("LakonMiner", "FighterBay01", "Int_FighterBay_Size6_Class1", ModuleFit.TooLarge)]
    [InlineData("LakonMiner", "FighterBay01", "Int_CargoRack_Size2_Class1", ModuleFit.RestrictedSlot)]
    [InlineData("LakonMiner", "LimpetController01", "Int_CargoRack_Size2_Class1", ModuleFit.RestrictedSlot)]
    [InlineData("LakonMiner", "LargeMiningHardpoint1", "Hpt_BeamLaser_Fixed_Large", ModuleFit.RestrictedSlot)]
    [InlineData("LakonMiner", "LargeMiningHardpoint1", "Hpt_MiningLaser_Fixed_Medium", ModuleFit.Allowed)]
    [InlineData("SideWinder", "SmallHardpoint1", "Int_CargoRack_Size1_Class1", ModuleFit.WrongModuleType)]
    [InlineData("SideWinder", "Radar", "Int_Sensors_Size1_Class5", ModuleFit.Allowed)]
    [InlineData("Anaconda", "Radar", "Int_Sensors_Size1_Class5", ModuleFit.TooSmall)]
    [InlineData("Anaconda", "LifeSupport", "Int_LifeSupport_Size1_Class5", ModuleFit.TooSmall)]
    [InlineData("Anaconda", "FrameShiftDrive", "Int_Hyperdrive_Overcharge_Size5_Class5", ModuleFit.TooSmall)]
    [InlineData("Anaconda", "FrameShiftDrive", "Int_Hyperdrive_Size5_Class5", ModuleFit.Allowed)]
    [InlineData("SideWinder", "Armour", "LakonMiner_Armour_Grade1", ModuleFit.RestrictedShip)]
    [InlineData("PantherMkII", "Cargo01", "Int_LargeCargoRack_Size8_Class1", ModuleFit.Allowed)]
    [InlineData("PantherMkII", "Slot01_Size8", "Int_LargeCargoRack_Size8_Class1", ModuleFit.RestrictedSlot)]
    [InlineData("LakonMiner", "LimpetController01", "Int_MultiDroneControl_MiningV2_Size5_Class5", ModuleFit.Allowed)]
    [InlineData("LakonMiner", "Slot01_Size6", "Int_MultiDroneControl_MiningV2_Size5_Class5", ModuleFit.RestrictedSlot)]
    [InlineData("unknown", "Slot01_Size2", "Int_BuggyBay_Size2_Class2", ModuleFit.UnknownShip)]
    [InlineData("SideWinder", "bad-slot", "Int_BuggyBay_Size2_Class2", ModuleFit.UnknownSlot)]
    [InlineData("SideWinder", "Slot01_Size2", "unknown-module", ModuleFit.UnknownModule)]
    [InlineData("AnacondaMkII", "HugeHardpoint1", "Hpt_MiningToolV2_Fixed_Large", ModuleFit.RestrictedSlot)]
    [InlineData("AnacondaMkII", "LargeHardpoint1", "Hpt_MiningToolV2_Fixed_Large", ModuleFit.RestrictedSlot)]
    [InlineData("AnacondaMkII", "LargeHardpoint2", "Hpt_MiningToolV2_Fixed_Large", ModuleFit.RestrictedSlot)]
    [InlineData("AnacondaMkII", "LargeHardpoint3", "Hpt_MiningToolV2_Fixed_Large", ModuleFit.Allowed)]
    [InlineData("AnacondaMkII", "LargeHardpoint4", "Hpt_MiningToolV2_Fixed_Large", ModuleFit.Allowed)]
    [InlineData("AnacondaMkII", "Cargo01", "Int_LargeCargoRack_Size6_Class1", ModuleFit.Allowed)]
    [InlineData("AnacondaMkII", "Slot04_Size6", "Int_LargeCargoRack_Size6_Class1", ModuleFit.UnknownSlot)]
    [InlineData("AnacondaMkII", "Slot01_Size8", "Int_LargeCargoRack_Size6_Class1", ModuleFit.RestrictedSlot)]
    [InlineData("Anaconda", "Slot01_Size7", "Int_LargeCargoRack_Size6_Class1", ModuleFit.RestrictedShip)]
    [InlineData("AnacondaMkII", "Slot11_Size1", "Int_MkIILargeBuggyBay_Size2_Class3", ModuleFit.TooLarge)]
    [InlineData("AnacondaMkII", "Slot01_Size8", "Int_MkIILargeBuggyBay_Size2_Class3", ModuleFit.Allowed)]
    [InlineData("AnacondaMkII", "Slot01_Size8", "Int_FighterBayMk2_Size5_Class1", ModuleFit.Allowed)]
    [InlineData("AnacondaMkII", "Armour", "Anaconda_Armour_Grade1", ModuleFit.RestrictedShip)]
    [InlineData("AnacondaMkII", "Armour", "AnacondaMkII_Armour_Grade1", ModuleFit.Allowed)]
    [InlineData("AnacondaMkII", "Armour", "AnacondaMkII_Armour_Grade1_Default", ModuleFit.Allowed)]
    public void HangarAndGeneralFittingRulesMatchTheSource(
        string ship,
        string slot,
        string module,
        ModuleFit expected
    ) => Assert.Equal(expected, Catalog.CheckFit(ship, slot, module));

    /// <summary>
    /// Checks that all EDSY hardpoints and active utilities are available while passive internals remain excluded.
    /// </summary>
    [Fact]
    public void AllEdsyHardpointsAndActiveUtilitiesAreAvailableWhilePassiveInternalsRemainExcluded()
    {
        ModuleDefinition[] modules = Catalog.Modules.ToArray();
        using var document = JsonDocument.Parse(
            JsonSerializer.Serialize(
                new
                {
                    Ship = "anaconda",
                    ShipID = 12,
                    Modules = modules.Select(
                        (module, index) => new { Slot = $"TestSlot{index}", Item = module.Symbol }
                    ),
                }
            )
        );
        FiregroupShip ship = FiregroupLoadout.Parse(document.RootElement)!;
        foreach (ModuleDefinition module in modules.Where(module => module.Firegroup))
        {
            Assert.Contains(
                ship.Modules,
                candidate => candidate.Symbol.Equals(module.Symbol, StringComparison.OrdinalIgnoreCase)
            );
        }
        foreach (
            string symbol in new[]
            {
                "Int_MkIILargeBuggyBay_Size2_Class3",
                "Int_FighterBayMk2_Size5_Class1",
                "Hpt_ShieldBooster_Size0_Class5",
                "Int_PowerDistributor_Size5_Class5",
            }
        )
        {
            Assert.DoesNotContain(
                ship.Modules,
                module => module.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase)
            );
        }
    }

    /// <summary>
    /// Checks that firegroups identify mercenary weapons from engineering without localized names.
    /// </summary>
    [Theory]
    [InlineData("Hpt_Railgun_Fixed_Medium", "Weapon_EnduringFeedback", "Enduring Feedback Rail Gun")]
    [InlineData("Hpt_CausticMissile_Fixed_Medium", "Weapon_HighYieldEnzyme", "High-Yield Enzyme Missile Rack")]
    [InlineData("Hpt_MiningLaser_Fixed_Small", "Weapon_LongRangeMiningLaser", "Long Range Mining Laser")]
    [InlineData("Hpt_BasicMissileRack_Fixed_Medium", "Weapon_DragSeeker", "Drag Seeker Missile Rack")]
    public void FiregroupsIdentifyMercenaryWeaponsFromEngineeringWithoutLocalizedNames(
        string symbol,
        string blueprint,
        string name
    )
    {
        using var document = JsonDocument.Parse(
            JsonSerializer.Serialize(
                new
                {
                    Ship = "python",
                    ShipID = 12,
                    Modules = new[]
                    {
                        new
                        {
                            Slot = "MediumHardpoint1",
                            Item = symbol,
                            Engineering = new { BlueprintName = blueprint },
                        },
                    },
                }
            )
        );
        Assert.Contains(FiregroupLoadout.Parse(document.RootElement)!.Modules, module => module.Name == name);
        Assert.StartsWith(name, Catalog.GetFiregroupName(document.RootElement.GetProperty("Modules")[0]));
    }

    /// <summary>
    /// Checks that firegroup catalog uses known localized latest merc gear and does not guess unknown names.
    /// </summary>
    [Fact]
    public void FiregroupCatalogUsesKnownLocalizedLatestMercGearAndDoesNotGuessUnknownNames()
    {
        using var recognized = JsonDocument.Parse(
            """{"Item":"Hpt_BasicMissileRack_Fixed_Large","Item_Localised":"Exposing Seeker Missile Rack"}"""
        );
        using var unknown = JsonDocument.Parse(
            """{"Item":"unknown","Item_Localised":"Exposing Seeker Missile Rack"}"""
        );
        using var passive = JsonDocument.Parse(
            """{"Item":"Int_PowerDistributor_Size5_Class5","Engineering":{"BlueprintName":"PowerDistributor_Balanced"}}"""
        );
        using var ordinary = JsonDocument.Parse("""{"Item":"Hpt_PulseLaser_Turret_Small"}""");
        Assert.Equal("Exposing Seeker Missile Rack", Catalog.GetMercenaryFiregroupName(recognized.RootElement));
        Assert.Null(Catalog.GetFiregroupName(unknown.RootElement));
        Assert.Null(Catalog.GetMercenaryFiregroupName(passive.RootElement));
        Assert.Equal("Pulse Laser (1G Turreted)", Catalog.GetFiregroupName(ordinary.RootElement));
    }
}
