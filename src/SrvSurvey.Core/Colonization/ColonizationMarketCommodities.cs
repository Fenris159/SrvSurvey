using SrvSurvey.Core.Mining;

namespace SrvSurvey.Core.Colonization;

/// <summary>Translates journal identifiers to Spansh's case-sensitive marketplace values.</summary>
internal static class ColonizationMarketCommodities
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.Ordinal)
    {
        ["liquidoxygen"] = "Liquid oxygen",
        ["pesticides"] = "Pesticides",
        ["surfacestabilisers"] = "Surface Stabilisers",
        ["water"] = "Water",
        ["evacuationshelter"] = "Evacuation Shelter",
        ["survivalequipment"] = "Survival Equipment",
        ["animalmeat"] = "Animal Meat",
        ["coffee"] = "Coffee",
        ["fish"] = "Fish",
        ["foodcartridges"] = "Food Cartridges",
        ["fruitandvegetables"] = "Fruit and Vegetables",
        ["grain"] = "Grain",
        ["tea"] = "Tea",
        ["ceramiccomposites"] = "Ceramic Composites",
        ["cmmcomposite"] = "CMM Composite",
        ["insulatingmembrane"] = "Insulating Membrane",
        ["polymers"] = "Polymers",
        ["semiconductors"] = "Semiconductors",
        ["superconductors"] = "Superconductors",
        ["beer"] = "Beer",
        ["liquor"] = "Liquor",
        ["wine"] = "Wine",
        ["buildingfabricators"] = "Building Fabricators",
        ["cropharvesters"] = "Crop Harvesters",
        ["emergencypowercells"] = "Emergency Power Cells",
        ["geologicalequipment"] = "Geological Equipment",
        ["heliostaticfurnaces"] = "Microbial Furnaces",
        ["mineralextractors"] = "Mineral Extractors",
        ["powergenerators"] = "Power Generators",
        ["thermalcoolingunits"] = "Thermal Cooling Units",
        ["waterpurifiers"] = "Water Purifiers",
        ["agriculturalmedicines"] = "Agri-Medicines",
        ["basicmedicines"] = "Basic Medicines",
        ["combatstabilisers"] = "Combat Stabilisers",
        ["aluminium"] = "Aluminium",
        ["copper"] = "Copper",
        ["steel"] = "Steel",
        ["titanium"] = "Titanium",
        ["advancedcatalysers"] = "Advanced Catalysers",
        ["autofabricators"] = "Auto-Fabricators",
        ["bioreducinglichen"] = "Bioreducing Lichen",
        ["computercomponents"] = "Computer Components",
        ["hazardousenvironmentsuits"] = "H.E. Suits",
        ["terrainenrichmentsystems"] = "Land Enrichment Systems",
        ["medicaldiagnosticequipment"] = "Medical Diagnostic Equipment",
        ["microcontrollers"] = "Micro Controllers",
        ["mutomimager"] = "Muon Imager",
        ["resonatingseparators"] = "Resonating Separators",
        ["robotics"] = "Robotics",
        ["structuralregulators"] = "Structural Regulators",
        ["militarygradefabrics"] = "Military Grade Fabrics",
        ["biowaste"] = "Biowaste",
        ["battleweapons"] = "Battle Weapons",
        ["nonlethalweapons"] = "Non-Lethal Weapons",
        ["reactivearmour"] = "Reactive Armour",
        ["tritium"] = "Tritium",
    };
    private static readonly Dictionary<string, string> Keys = Names.ToDictionary(
        pair => MiningCommodityName.Key(pair.Value),
        pair => pair.Key,
        StringComparer.Ordinal
    );

    public static string Key(string value)
    {
        string key = MiningCommodityName.Key(value);
        return key switch
        {
            "microbialfurnaces" => "heliostaticfurnaces",
            "landenrichmentsystems" => "terrainenrichmentsystems",
            "muonimager" => "mutomimager",
            "combatstabilizers" => "combatstabilisers",
            _ => Keys.GetValueOrDefault(key, key),
        };
    }

    public static string DisplayName(string commodity) => Names.GetValueOrDefault(Key(commodity), commodity);
}
