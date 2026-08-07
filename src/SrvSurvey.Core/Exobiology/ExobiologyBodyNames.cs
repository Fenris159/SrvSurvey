namespace SrvSurvey.Core.Exobiology;

/// <summary>
/// Shared body-name normalization for joining Elite status / system-scan
/// names with Canonn POI short body labels (legacy PlotPriorScans intent).
/// </summary>
public static class ExobiologyBodyNames
{
    /// <summary>
    /// Produces a space-free comparison key, optionally stripping a system
    /// name prefix when it appears as a whole leading token.
    /// </summary>
    public static string NormalizeKey(string? bodyName, string? systemName = null)
    {
        if (string.IsNullOrWhiteSpace(bodyName))
        {
            return string.Empty;
        }

        var trimmed = bodyName.Trim();
        if (!string.IsNullOrWhiteSpace(systemName))
        {
            var system = systemName.Trim();
            if (trimmed.Length > system.Length
                && trimmed.StartsWith(system, StringComparison.OrdinalIgnoreCase))
            {
                var remainder = trimmed[system.Length..];
                // Require a boundary so "Sol" does not strip from "Solitude 1".
                if (remainder.Length > 0 && remainder[0] is ' ' or '-' or '_')
                {
                    trimmed = remainder.TrimStart(' ', '-', '_').Trim();
                }
            }
        }

        return trimmed.Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// True when both names resolve to the same non-empty body key.
    /// </summary>
    public static bool Matches(
        string? first,
        string? second,
        string? systemName = null)
    {
        var left = NormalizeKey(first, systemName);
        var right = NormalizeKey(second, systemName);
        return left.Length > 0
            && right.Length > 0
            && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
