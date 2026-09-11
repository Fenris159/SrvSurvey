using System.Text.Json;
using SrvSurvey.Core.Firegroups;

namespace SrvSurvey.Core.Tests.Firegroups;

public sealed class FiregroupLoadoutTests
{
    [Fact]
    public void ParseOffersEquippedSurfaceScannerAndBuiltInScannerActions()
    {
        var ship = Parse(new LoadoutModule("Slot01_Size1", "Int_DetailedSurfaceScanner_Tiny"));

        Assert.Contains(ship.Modules, module => module.Name == "Surface Scanner" && module.Slot == "Slot01_Size1");
        Assert.Contains(ship.Modules, module => module.Name == "D-Scanner");
        Assert.Contains(ship.Modules, module => module.Name == "SC-Suite");
        Assert.Contains(ship.Modules, module => module.Name == "Data Link Scanner");
    }

    [Theory]
    [InlineData("Hpt_Railgun_Fixed_Medium", "Enduring Feedback Rail Gun")]
    [InlineData("Hpt_Mining_AbrBlstr_Fixed_Small", "Far-Reaching Abrasion Blaster")]
    [InlineData("Hpt_CausticMissile_Fixed_Medium", "High-Yield Enzyme Missile Rack")]
    [InlineData("Hpt_Slugshot_Gimbal_Large", "Double Screaming Fragment Cannon")]
    [InlineData("Hpt_Slugshot_Gimbal_Small", "Double Screaming Fragment Cannon")]
    [InlineData("Hpt_MiningLaser_Fixed_Small", "Long Range Mining Laser")]
    [InlineData("Hpt_MultiCannon_Fixed_Medium", "Rapid Phase Multi-Cannon")]
    [InlineData("Hpt_BasicMissileRack_Fixed_Medium", "Drag Seeker Missile Rack")]
    [InlineData("Hpt_BasicMissileRack_Fixed_Medium", "Lightweight Thermal Seeker Missile Rack")]
    [InlineData("Hpt_BasicMissileRack_Fixed_Medium", "Lockdown Seeker Missile Rack")]
    [InlineData("Hpt_BasicMissileRack_Fixed_Large", "Lockdown Seeker Missile Rack")]
    [InlineData("Int_DetailedSurfaceScanner_Tiny", "Long Range Detailed Surface Scanner")]
    [InlineData("Hpt_Cannon_Fixed_Huge", "Force Impact Cannon")]
    [InlineData("Hpt_PulseLaserBurst_Gimbal_Medium", "Regenerative Burst Laser")]
    [InlineData("Hpt_BeamLaser_Fixed_Huge", "Overloaded Beam Laser")]
    [InlineData("Hpt_BasicMissileRack_Fixed_Large", "Exposing Missiles")]
    public void ParseUsesFDevIdsMercgearNameWhenJournalSuppliesIt(string symbol, string localizedName)
    {
        var ship = Parse(new LoadoutModule("MediumHardpoint1", symbol, localizedName));
        var shipWithoutLocalizedName = Parse(new LoadoutModule("MediumHardpoint1", symbol));

        Assert.Contains(
            ship.Modules,
            module => module.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase) && module.Name == localizedName
        );
        Assert.Contains(
            shipWithoutLocalizedName.Modules,
            module => module.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase)
        );
    }

    [Theory]
    [InlineData("Int_PowerDistributor_Size5_Class5", "Balanced Power Distributor")]
    [InlineData("Int_PowerDistributor_Size3_Class2", "Support Focused Power Distributor")]
    [InlineData("Int_PowerDistributor_Size3_Class5", "Support Focused Power Distributor")]
    [InlineData("Int_PowerDistributor_Size4_Class2", "Support Focused Power Distributor")]
    [InlineData("Int_PowerDistributor_Size4_Class5", "Support Focused Power Distributor")]
    [InlineData("Int_PowerDistributor_Size6_Class5", "Support Focused Power Distributor")]
    [InlineData("Int_ModuleReinforcement_Size5_Class2", "Heavy Duty Module Reinforcement Package")]
    [InlineData("Int_CargoRack_Size5_Class1", "Extended Cargo Rack")]
    [InlineData("Int_CargoRack_Size6_Class1", "Extended Cargo Rack")]
    public void ParseExcludesRequestedMercgearInternals(string symbol, string localizedName)
    {
        var ship = Parse(new LoadoutModule("Slot01_Size5", symbol, localizedName));

        Assert.DoesNotContain(ship.Modules, module => module.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));
    }

    private static FiregroupShip Parse(params LoadoutModule[] modules)
    {
        var json = JsonSerializer.Serialize(
            new
            {
                Ship = "python",
                ShipID = 12,
                ShipName = "Survey Python",
                Modules = modules,
            }
        );
        using var document = JsonDocument.Parse(json);
        return Assert.IsType<FiregroupShip>(FiregroupLoadout.Parse(document.RootElement));
    }

    private sealed record LoadoutModule(string Slot, string Item, string? Item_Localised = null);
}
