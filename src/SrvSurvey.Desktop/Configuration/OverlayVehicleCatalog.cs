using SrvSurvey.Core.Journal;

namespace SrvSurvey.Desktop.Configuration;

public sealed record OverlayVehicleDefinition(string Id, string Name, string Group);

/// <summary>Journal symbols and landing-pad sizes; see docs/overlay-vehicle-exceptions.md.</summary>
public static class OverlayVehicleCatalog
{
    private const string UnknownKey = "unknown";
    private const string SmallGroup = "Small";
    private const string MediumGroup = "Medium";
    private const string LargeGroup = "Large";
    private const string VehicleGroup = "Vessel / Vehicle";

    public static IReadOnlyList<OverlayVehicleDefinition> All { get; } =
    [
        new("adder", "Adder", SmallGroup),
        new("cobramkiii", "Cobra Mk III", SmallGroup),
        new("cobramkiv", "Cobra Mk IV", SmallGroup),
        new("cobramkv", "Cobra Mk V", SmallGroup),
        new("diamondbackxl", "Diamondback Explorer", SmallGroup),
        new("diamondback", "Diamondback Scout", SmallGroup),
        new("dolphin", "Dolphin", SmallGroup),
        new("eagle", "Eagle", SmallGroup),
        new("hauler", "Hauler", SmallGroup),
        new("empire_courier", "Imperial Courier", SmallGroup),
        new("empire_eagle", "Imperial Eagle", SmallGroup),
        new("smallcombat01_nx", "Kestrel Mk II", SmallGroup),
        new("sidewinder", "Sidewinder", SmallGroup),
        new("viper", "Viper Mk III", SmallGroup),
        new("viper_mkiv", "Viper Mk IV", SmallGroup),
        new("vulture", "Vulture", SmallGroup),
        new("typex_3", "Alliance Challenger", MediumGroup),
        new("typex", "Alliance Chieftain", MediumGroup),
        new("typex_2", "Alliance Crusader", MediumGroup),
        new("asp", "Asp Explorer", MediumGroup),
        new("asp_scout", "Asp Scout", MediumGroup),
        new("corsair", "Corsair", MediumGroup),
        new("federation_dropship_mkii", "Federal Assault Ship", MediumGroup),
        new("federation_dropship", "Federal Dropship", MediumGroup),
        new("federation_gunship", "Federal Gunship", MediumGroup),
        new("ferdelance", "Fer-de-Lance", MediumGroup),
        new("independant_trader", "Keelback", MediumGroup),
        new("krait_mkii", "Krait Mk II", MediumGroup),
        new("krait_light", "Krait Phantom", MediumGroup),
        new("mediumtransport01", "Lynx Highliner", MediumGroup),
        new("mamba", "Mamba", MediumGroup),
        new("mandalay", "Mandalay", MediumGroup),
        new("python", "Python", MediumGroup),
        new("python_nx", "Python Mk II", MediumGroup),
        new("lakonminer", "Type-11 Prospector", MediumGroup),
        new("type6", "Type-6 Transporter", MediumGroup),
        new("type8", "Type-8 Transporter", MediumGroup),
        new("anaconda", "Anaconda", LargeGroup),
        new("belugaliner", "Beluga Liner", LargeGroup),
        new("explorer_nx", "Caspian Explorer", LargeGroup),
        new("federation_corvette", "Federal Corvette", LargeGroup),
        new("empire_trader", "Imperial Clipper", LargeGroup),
        new("cutter", "Imperial Cutter", LargeGroup),
        new("orca", "Orca", LargeGroup),
        new("panthermkii", "Panther Clipper Mk II", LargeGroup),
        new("type9_military", "Type-10 Defender", LargeGroup),
        new("type7", "Type-7 Transporter", LargeGroup),
        new("type9", "Type-9 Heavy", LargeGroup),
        new("lander01", "Nomad", VehicleGroup),
        new("testbuggy", "SRV (Scarab)", VehicleGroup),
        new("combat_multicrew_srv_01", "Scorpion", VehicleGroup),
        new("mev_rhino", "Rhino", VehicleGroup),
        new("fighters", "Fighters", VehicleGroup),
        new("on-foot", "On foot", VehicleGroup),
        new(UnknownKey, "Other / unknown", VehicleGroup),
    ];

    public static IReadOnlyList<OverlayVehicleDefinition> ForCategory(OverlaySettingsCategory category) =>
        category == OverlaySettingsCategory.MineMap
            ? All.Where(vehicle =>
                    vehicle.Group is MediumGroup or LargeGroup || vehicle.Id is "mev_rhino" or "on-foot" or UnknownKey
                )
                .ToArray()
            : All;

    public static string Resolve(JournalSessionState journal, EliteStatus? status)
    {
        if (journal.IsShutdown || status is null)
        {
            return UnknownKey;
        }

        if (status.OnFoot)
        {
            return "on-foot";
        }

        if (status.InFighter)
        {
            return "fighters";
        }
        // The journal can retain our own ship while we ride in another vessel.
        if (
            status.InTaxi
            || (
                status.Flags2
                & (StatusFlags2.InMulticrew | StatusFlags2.TelepresenceMulticrew | StatusFlags2.PhysicalMulticrew)
            ) != 0
        )
        {
            return UnknownKey;
        }

        if (status.InSrv)
        {
            return Normalize(journal.ActiveSrvType);
        }

        if (status.InMainShip)
        {
            return Normalize(journal.ShipType);
        }

        return UnknownKey;
    }

    private static string Normalize(string? symbol)
    {
        var id = symbol?.ToLowerInvariant();
        return All.Any(v => v.Id == id) ? id! : UnknownKey;
    }
}
