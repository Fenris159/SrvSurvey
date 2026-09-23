using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Mining;

public static class MiningCommodityName
{
    private static readonly Lazy<Dictionary<string, string>> CanonicalNames = new(BuildCanonicalNames);

    // Ardent, Spansh, journals, and the UI use different spellings for the same good.
    // Keep Diamond and Low Temperature Diamonds as separate identities.
    public static string Key(string? value)
    {
        string unwrapped = (value ?? "")
            .Trim()
            .TrimStart('$')
            .Replace("_name;", "", StringComparison.OrdinalIgnoreCase);
        string compact = new(unwrapped.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        return compact switch
        {
            "voidopals" or "voidopal" => "opal",
            "lowtemperaturediamonds" => "lowtemperaturediamond",
            _ => compact,
        };
    }

    public static string Normalize(string? value) => Key(value);

    public static bool Same(string? left, string? right) => Key(left) == Key(right);

    public static string Canonical(string value) =>
        CanonicalNames.Value.TryGetValue(Key(value), out string? name) ? name : value;

    private static Dictionary<string, string> BuildCanonicalNames()
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (
            string name in PlanetaryMiningPlan.EdpmCommodityNames.Concat(
                SurfaceMiningCommodityCatalog.HuntReferences.Select(reference => reference.Material)
            )
        )
        {
            names.TryAdd(Key(name), name);
        }

        return names;
    }
}
