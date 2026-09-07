namespace SrvSurvey.Core.Mining;

public static class MiningCommodityName
{
    public static string Normalize(string? value) => (value ?? "").Trim().TrimStart('$').Replace("_name;", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant().Replace(" ", "") switch
    {
        "voidopals" or "voidopal" => "opal",
        "lowtemperaturediamonds" => "lowtemperaturediamond",
        var name => name,
    };
}
