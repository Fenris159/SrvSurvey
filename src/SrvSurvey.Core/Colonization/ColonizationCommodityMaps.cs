using System.Globalization;
using System.Text;

namespace SrvSurvey.Core.Colonization;

/// <summary>
/// Commodity map helpers shared with RavenColonial EDMC plugin semantics:
/// normalize need maps, clear phantom template slots, and stable depot PATCH signatures.
/// </summary>
public static class ColonizationCommodityMaps
{
    public static Dictionary<string, int> NormalizeNeedMap(IEnumerable<KeyValuePair<string, int>>? commodities)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (commodities is null)
        {
            return result;
        }

        foreach (KeyValuePair<string, int> pair in commodities)
        {
            string key = ColonizationConstructionState.NormalizeCommodityName(pair.Key);
            if (key.Length == 0)
            {
                continue;
            }

            result[key] = result.GetValueOrDefault(key) + Math.Max(0, pair.Value);
        }

        return result;
    }

    /// <summary>
    /// Build a PATCH commodities map that clears server template placeholders (for example <c>-1</c>).
    /// </summary>
    public static Dictionary<string, int> PhantomZeroPatchMap(IEnumerable<KeyValuePair<string, int>>? commodities)
    {
        var zeroes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (commodities is null)
        {
            return zeroes;
        }

        foreach (KeyValuePair<string, int> pair in commodities)
        {
            string key = ColonizationConstructionState.NormalizeCommodityName(pair.Key);
            if (key.Length == 0 || pair.Value >= 0)
            {
                continue;
            }

            zeroes[key] = 0;
        }

        return zeroes;
    }

    public static Dictionary<string, int> ApplyPhantomZeros(IReadOnlyDictionary<string, int> commodities)
    {
        ArgumentNullException.ThrowIfNull(commodities);
        Dictionary<string, int> zeroes = PhantomZeroPatchMap(commodities);
        if (zeroes.Count == 0)
        {
            return new Dictionary<string, int>(commodities, StringComparer.OrdinalIgnoreCase);
        }

        var merged = new Dictionary<string, int>(commodities, StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, int> pair in zeroes)
        {
            merged[pair.Key] = 0;
        }

        return merged;
    }

    public static string CreateDepotUpdateSignature(
        string buildId,
        int? maximumRequired,
        IReadOnlyDictionary<string, int> commodities,
        bool includeDepot,
        bool depotFailed
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(buildId);
        ArgumentNullException.ThrowIfNull(commodities);

        var builder = new StringBuilder(buildId.Trim().Length + 32 + (commodities.Count * 24));
        builder.Append(buildId.Trim());
        builder.Append('|');
        builder.Append(maximumRequired?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
        builder.Append("|depot=");
        builder.Append(includeDepot ? '1' : '0');
        builder.Append("|failed=");
        builder.Append(depotFailed ? '1' : '0');
        builder.Append('|');
        foreach (
            KeyValuePair<string, int> pair in commodities
                .Select(entry => new KeyValuePair<string, int>(
                    ColonizationConstructionState.NormalizeCommodityName(entry.Key),
                    Math.Max(0, entry.Value)
                ))
                .Where(entry => entry.Key.Length > 0)
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
        )
        {
            builder.Append(pair.Key);
            builder.Append('=');
            builder.Append(pair.Value.ToString(CultureInfo.InvariantCulture));
            builder.Append(';');
        }

        return builder.ToString();
    }
}
