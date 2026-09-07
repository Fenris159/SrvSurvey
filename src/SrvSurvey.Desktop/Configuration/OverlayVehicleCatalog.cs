using SrvSurvey.Core.Journal;

namespace SrvSurvey.Desktop.Configuration;

public sealed record OverlayVehicleDefinition(string Id, string Name, string Group);

/// <summary>Journal symbols and landing-pad sizes; see docs/overlay-vehicle-exceptions.md.</summary>
public static class OverlayVehicleCatalog
{
    public static IReadOnlyList<OverlayVehicleDefinition> All { get; } =
    [
        new("adder", "Adder", "Small"),
        new("cobramkiii", "Cobra Mk III", "Small"),
        new("cobramkiv", "Cobra Mk IV", "Small"),
        new("cobramkv", "Cobra Mk V", "Small"),
        new("diamondbackxl", "Diamondback Explorer", "Small"),
        new("diamondback", "Diamondback Scout", "Small"),
        new("dolphin", "Dolphin", "Small"),
        new("eagle", "Eagle", "Small"),
        new("hauler", "Hauler", "Small"),
        new("empire_courier", "Imperial Courier", "Small"),
        new("empire_eagle", "Imperial Eagle", "Small"),
        new("smallcombat01_nx", "Kestrel Mk II", "Small"),
        new("sidewinder", "Sidewinder", "Small"),
        new("viper", "Viper Mk III", "Small"),
        new("viper_mkiv", "Viper Mk IV", "Small"),
        new("vulture", "Vulture", "Small"),
        new("typex_3", "Alliance Challenger", "Medium"),
        new("typex", "Alliance Chieftain", "Medium"),
        new("typex_2", "Alliance Crusader", "Medium"),
        new("asp", "Asp Explorer", "Medium"),
        new("asp_scout", "Asp Scout", "Medium"),
        new("corsair", "Corsair", "Medium"),
        new("federation_dropship_mkii", "Federal Assault Ship", "Medium"),
        new("federation_dropship", "Federal Dropship", "Medium"),
        new("federation_gunship", "Federal Gunship", "Medium"),
        new("ferdelance", "Fer-de-Lance", "Medium"),
        new("independant_trader", "Keelback", "Medium"),
        new("krait_mkii", "Krait Mk II", "Medium"),
        new("krait_light", "Krait Phantom", "Medium"),
        new("mediumtransport01", "Lynx Highliner", "Medium"),
        new("mamba", "Mamba", "Medium"),
        new("mandalay", "Mandalay", "Medium"),
        new("python", "Python", "Medium"),
        new("python_nx", "Python Mk II", "Medium"),
        new("lakonminer", "Type-11 Prospector", "Medium"),
        new("type6", "Type-6 Transporter", "Medium"),
        new("type8", "Type-8 Transporter", "Medium"),
        new("anaconda", "Anaconda", "Large"),
        new("belugaliner", "Beluga Liner", "Large"),
        new("explorer_nx", "Caspian Explorer", "Large"),
        new("federation_corvette", "Federal Corvette", "Large"),
        new("empire_trader", "Imperial Clipper", "Large"),
        new("cutter", "Imperial Cutter", "Large"),
        new("orca", "Orca", "Large"),
        new("panthermkii", "Panther Clipper Mk II", "Large"),
        new("type9_military", "Type-10 Defender", "Large"),
        new("type7", "Type-7 Transporter", "Large"),
        new("type9", "Type-9 Heavy", "Large"),
        new("lander01", "Nomad", "Vessel / Vehicle"),
        new("testbuggy", "SRV (Scarab)", "Vessel / Vehicle"),
        new("combat_multicrew_srv_01", "Scorpion", "Vessel / Vehicle"),
        new("mev_rhino", "Rhino", "Vessel / Vehicle"),
        new("fighters", "Fighters", "Vessel / Vehicle"),
        new("on-foot", "On foot", "Vessel / Vehicle"),
        new("unknown", "Other / unknown", "Vessel / Vehicle"),
    ];

    public static string Resolve(JournalSessionState journal, EliteStatus? status)
    {
        if (journal.IsShutdown || status is null) return "unknown";
        if (status.OnFoot) return "on-foot";
        if (status.InFighter) return "fighters";
        // The journal can retain our own ship while we ride in another vessel.
        if (status.InTaxi || (status.Flags2 & (StatusFlags2.InMulticrew
            | StatusFlags2.TelepresenceMulticrew | StatusFlags2.PhysicalMulticrew)) != 0) return "unknown";
        if (status.InSrv) return Normalize(journal.ActiveSrvType);
        if (status.InMainShip) return Normalize(journal.ShipType);
        return "unknown";
    }

    private static string Normalize(string? symbol)
    {
        var id = symbol?.ToLowerInvariant();
        return All.Any(v => v.Id == id) ? id! : "unknown";
    }
}
