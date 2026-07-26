namespace SrvSurvey.Core.Colonization;

public static class ColonizationBuildSiteRepair
{
    private const string PlanetaryConstructionPrefix =
        "Planetary Construction Site: ";
    private const string OrbitalConstructionPrefix =
        "Orbital Construction Site: ";

    private static readonly string[] PlayerColonyMarketIdPrefixes =
        ["395", "396", "397", "42", "43"];

    private static readonly HashSet<string> SkippedStationTypes = new(
        ["FleetCarrier", "SpaceConstructionDepot", "MegaShip"],
        StringComparer.OrdinalIgnoreCase);

    public static bool IsPlayerColonyMarketId(long marketId)
    {
        if (marketId <= 0)
        {
            return false;
        }

        var text = marketId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return PlayerColonyMarketIdPrefixes.Any(prefix =>
            text.StartsWith(prefix, StringComparison.Ordinal));
    }

    public static bool ShouldSkipDockContext(
        string? stationType,
        string? stationName,
        bool isConstructionShip = false)
    {
        if (!string.IsNullOrWhiteSpace(stationType)
            && SkippedStationTypes.Contains(stationType.Trim()))
        {
            return true;
        }

        var name = stationName?.Trim() ?? string.Empty;
        return isConstructionShip
            || name.Contains("ColonisationShip", StringComparison.Ordinal)
            || IsConstructionDepotDockName(name);
    }

    public static bool IsConstructionDepotDockName(string? stationName)
    {
        var name = RemoveLocalizationToken(stationName);
        return name.StartsWith(
                PlanetaryConstructionPrefix,
                StringComparison.Ordinal)
            || name.StartsWith(
                OrbitalConstructionPrefix,
                StringComparison.Ordinal);
    }

    public static string NormalizeDockStationName(string? stationName)
    {
        var name = RemoveLocalizationToken(stationName);
        if (name.StartsWith(
                PlanetaryConstructionPrefix,
                StringComparison.Ordinal))
        {
            name = name[PlanetaryConstructionPrefix.Length..];
        }
        else if (name.StartsWith(
                     OrbitalConstructionPrefix,
                     StringComparison.Ordinal))
        {
            name = name[OrbitalConstructionPrefix.Length..];
        }

        return name.Trim();
    }

    public static ColonizationBuildSiteRepairPlan? CreatePlan(
        IReadOnlyList<ColonizationSystemSite> sites,
        string stationName,
        long marketId)
    {
        ArgumentNullException.ThrowIfNull(sites);
        var normalizedName = NormalizeDockStationName(stationName);
        if (normalizedName.Length == 0 || marketId <= 0)
        {
            return null;
        }

        var nameMatches = sites.Where(site => string.Equals(
                NormalizeDockStationName(site.Name),
                normalizedName,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (nameMatches.Length == 1)
        {
            var site = nameMatches[0];
            if (StatusAllowsRepair(site)
                && site.MarketId != marketId)
            {
                return new ColonizationBuildSiteRepairPlan(
                    site,
                    ColonizationBuildSiteRepairField.MarketId,
                    normalizedName,
                    marketId);
            }
        }

        var marketMatches = sites.Where(site => site.MarketId == marketId)
            .ToArray();
        if (marketMatches.Length != 1)
        {
            return null;
        }

        var marketMatch = marketMatches[0];
        if (!StatusAllowsRepair(marketMatch)
            || string.Equals(
                NormalizeDockStationName(marketMatch.Name),
                normalizedName,
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new ColonizationBuildSiteRepairPlan(
            marketMatch,
            ColonizationBuildSiteRepairField.Name,
            normalizedName,
            marketId);
    }

    private static bool StatusAllowsRepair(ColonizationSystemSite site)
    {
        return !site.HasExplicitStatus
            || site.Status == ColonizationSystemSiteStatus.Complete;
    }

    private static string RemoveLocalizationToken(string? stationName)
    {
        var name = stationName?.Trim() ?? string.Empty;
        var delimiter = name.IndexOf(';');
        return delimiter >= 0
            ? name[(delimiter + 1)..].Trim()
            : name;
    }
}

public enum ColonizationBuildSiteRepairField
{
    MarketId,
    Name,
}

public sealed record ColonizationBuildSiteRepairPlan(
    ColonizationSystemSite Site,
    ColonizationBuildSiteRepairField Field,
    string NormalizedStationName,
    long MarketId)
{
    public ColonizationSystemSitePatch CreatePatch()
    {
        return Field switch
        {
            ColonizationBuildSiteRepairField.MarketId =>
                new ColonizationSystemSitePatch { MarketId = MarketId },
            ColonizationBuildSiteRepairField.Name =>
                new ColonizationSystemSitePatch { Name = NormalizedStationName },
            _ => throw new InvalidOperationException(
                $"Unsupported system-site repair field '{Field}'."),
        };
    }
}
