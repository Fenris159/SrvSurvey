namespace SrvSurvey.Core.Mining;

public static class MiningCommodityCode
{
    private static readonly Dictionary<string, string> Codes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Alexandrite"] = "ALE",
        ["Aluminium"] = "ALU",
        ["Bauxite"] = "BAU",
        ["Benitoite"] = "BEN",
        ["Bertrandite"] = "BRT",
        ["Beryllium"] = "BER",
        ["Bismuth"] = "BIS",
        ["Bromellite"] = "BRO",
        ["Cobalt"] = "COB",
        ["Coltan"] = "CLT",
        ["Copper"] = "COP",
        ["Cryolite"] = "CRY",
        ["Gallite"] = "GAL",
        ["Gallium"] = "GLM",
        ["Gold"] = "GLD",
        ["Goslarite"] = "GOS",
        ["Grandidierite"] = "GRA",
        ["Hafnium 178"] = "HAF",
        ["Indite"] = "IDT",
        ["Indium"] = "IND",
        ["Jadeite"] = "JAD",
        ["Lanthanum"] = "LAN",
        ["Lepidolite"] = "LEP",
        ["Lithium"] = "LIT",
        ["Lithium Hydroxide"] = "LHY",
        ["Low Temperature Diamonds"] = "LTD",
        ["LowTemperatureDiamond"] = "LTD",
        ["Methane Clathrate"] = "MCL",
        ["Methanol Monohydrate Crystals"] = "MNL",
        ["Moissanite"] = "MOI",
        ["Monazite"] = "MON",
        ["Musgravite"] = "MUS",
        ["Osmium"] = "OSM",
        ["Painite"] = "PAI",
        ["Palladium"] = "PAL",
        ["Platinum"] = "PLA",
        ["Praseodymium"] = "PRA",
        ["Pyrophyllite"] = "PYR",
        ["Rhodplumsite"] = "RHO",
        ["Rutile"] = "RUT",
        ["Samarium"] = "SAM",
        ["Serendibite"] = "SER",
        ["Silver"] = "SIL",
        ["Taaffeite"] = "TAF",
        ["Tantalum"] = "TAN",
        ["Thallium"] = "THL",
        ["Thorium"] = "THR",
        ["Titanium"] = "TIT",
        ["Uraninite"] = "URT",
        ["Uranium"] = "URN",
        ["Void Opal"] = "VOP",
    };

    public static string Abbreviate(string name)
    {
        string trimmed = name.Trim();
        if (Codes.TryGetValue(trimmed, out string? code))
        {
            return code;
        }

        string letters = new(trimmed.Where(char.IsLetter).ToArray());
        return letters.Length == 0 ? trimmed : letters[..Math.Min(3, letters.Length)].ToUpperInvariant();
    }
}
