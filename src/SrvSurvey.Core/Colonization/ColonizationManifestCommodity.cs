using System.Text.Json;
using SrvSurvey.Core.Mining;

namespace SrvSurvey.Core.Colonization;

/// <summary>Commodity metadata shared by manifest grouping and the colony-only display filter.</summary>
public sealed record ColonizationManifestCommodity(string Key, string Name, string Category)
{
    private static readonly IReadOnlyDictionary<string, ColonizationManifestCommodity> Catalog = Load();
    private static readonly HashSet<string> ColonyKeys = ColonizationBuildCatalog
        .LoadEmbedded()
        .Builds.SelectMany(build => build.CommodityCosts.Keys)
        .Select(ColonizationMarketCommodities.Key)
        .ToHashSet(StringComparer.Ordinal);

    public static IReadOnlyList<ColonizationManifestCommodity> All { get; } =
        Catalog
            .Values.DistinctBy(item => item.Key)
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public bool IsColonyCommodity => ColonyKeys.Contains(ColonizationMarketCommodities.Key(Key));

    public static ColonizationManifestCommodity ForKey(string key)
    {
        string normalized = ColonizationMarketCommodities.Key(key);
        if (normalized == "metalloys")
        {
            normalized = "metaalloys";
        }
        return Catalog.GetValueOrDefault(normalized) ?? new(key, key, "Other");
    }

    private static Dictionary<string, ColonizationManifestCommodity> Load()
    {
        using Stream stream =
            typeof(ColonizationManifestCommodity).Assembly.GetManifestResourceStream(
                "SrvSurvey.Core.Resources.colonization-commodity-names.json"
            ) ?? throw new InvalidOperationException("The commodity catalog is unavailable.");
        Dictionary<string, Metadata> data =
            JsonSerializer.Deserialize<Dictionary<string, Metadata>>(stream)
            ?? throw new InvalidDataException("The commodity catalog is empty.");
        var result = new Dictionary<string, ColonizationManifestCommodity>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, Metadata> pair in data)
        {
            string key = ColonizationMarketCommodities.Key(pair.Key);
            result.TryAdd(key, new(key, pair.Value.Name, pair.Value.Category));
        }
        using (
            Stream general =
                typeof(ColonizationManifestCommodity).Assembly.GetManifestResourceStream(
                    "SrvSurvey.Core.Resources.manifest-commodity-names.json"
                ) ?? throw new InvalidOperationException("The manifest commodity catalog is unavailable.")
        )
        {
            Dictionary<string, Metadata> generalData =
                JsonSerializer.Deserialize<Dictionary<string, Metadata>>(general)
                ?? throw new InvalidDataException("The manifest commodity catalog is empty.");
            foreach (KeyValuePair<string, Metadata> pair in generalData)
            {
                string key = ColonizationMarketCommodities.Key(pair.Key);
                ColonizationManifestCommodity item =
                    result.GetValueOrDefault(key) ?? new(key, pair.Value.Name, pair.Value.Category);
                result.TryAdd(key, item);
                result.TryAdd(ColonizationMarketCommodities.Key(pair.Value.Name), item);
            }
        }
        // Mining is a convenience list that repeats goods from their real market categories.
        foreach (
            KeyValuePair<string, string[]> category in MiningReferenceData.Commodities.Where(group =>
                group.Key != "Mining"
            )
        )
        {
            foreach (string name in category.Value)
            {
                string key = ColonizationMarketCommodities.Key(name);
                result.TryAdd(key, new(key, name, category.Key));
            }
        }
        return result;
    }

    private sealed record Metadata(string Name, string Category);
}
