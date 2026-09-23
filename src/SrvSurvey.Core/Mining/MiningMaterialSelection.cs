using System.Globalization;

namespace SrvSurvey.Core.Mining;

/// <summary>
/// Default and Any are exclusive. Default keeps the highest priced station commodities.
/// Any shows every hotspot and priced commodity. Named picks filter both.
/// </summary>
public static class MiningMaterialSelection
{
    public const string Default = "Default";
    public const string Any = "Any";
    public const int DefaultStationCommodityLimit = 6;
    public const int DefaultAcquireCommodityLimit = 7;

    public static bool IsAny(IEnumerable<string> selected) =>
        selected.Any(item => item.Equals(Any, StringComparison.OrdinalIgnoreCase));

    public static bool IsDefault(IEnumerable<string> selected) =>
        !IsAny(selected)
        && (
            !selected.Any(item => item.Length > 0)
            || selected.Any(item => item.Equals(Default, StringComparison.OrdinalIgnoreCase))
        );

    public static string[] Named(IEnumerable<string> selected) =>
        selected.Where(item => item.Length > 0 && !IsToken(item)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public static int StationLimit(IEnumerable<string> selected, bool acquire)
    {
        if (IsAny(selected) || Named(selected).Length > 0)
        {
            return int.MaxValue;
        }

        return acquire ? DefaultAcquireCommodityLimit : DefaultStationCommodityLimit;
    }

    public static bool IncludesHotspot(IReadOnlyDictionary<string, int> hotspots, IEnumerable<string> selected)
    {
        string[] named = Named(selected);
        if (named.Length == 0)
        {
            return hotspots.Count > 0 || IsAny(selected) || IsDefault(selected);
        }

        return hotspots.Keys.Any(name => named.Any(selectedName => MiningCommodityName.Same(selectedName, name)));
    }

    public static string HotspotText(IReadOnlyDictionary<string, int> hotspots, IEnumerable<string> selected)
    {
        string[] named = Named(selected);
        IEnumerable<KeyValuePair<string, int>> shown =
            named.Length == 0
                ? hotspots
                : hotspots.Where(pair => named.Any(selectedName => MiningCommodityName.Same(selectedName, pair.Key)));
        return string.Join(
            ", ",
            shown.Select(pair => pair.Key + " ×" + pair.Value.ToString(CultureInfo.InvariantCulture))
        );
    }

    private static bool IsToken(string item) =>
        item.Equals(Default, StringComparison.OrdinalIgnoreCase)
        || item.Equals(Any, StringComparison.OrdinalIgnoreCase);
}
