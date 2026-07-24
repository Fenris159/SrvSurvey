namespace SrvSurvey.Core.Exploration;

public static class ExplorationValueCalculator
{
    private const double PlanetValueExponent = 0.56591828;

    public static int Calculate(
        string? bodyClass,
        bool isTerraformable,
        double mass,
        bool isFirstDiscoverer,
        bool isMapped,
        bool isFirstMapped,
        bool isOdyssey,
        bool withEfficiencyBonus = true,
        bool isFleetCarrierSale = false)
    {
        if (string.IsNullOrWhiteSpace(bodyClass))
        {
            return 0;
        }

        if (IsStar(bodyClass))
        {
            var starBaseValue = GetStarBaseValue(bodyClass);
            return (int)Math.Round(
                starBaseValue + (mass * starBaseValue / 66.25));
        }

        var bodyBaseValue = GetPlanetBaseValue(bodyClass, isTerraformable);
        var mappingMultiplier = isMapped
            ? isFirstDiscoverer && isFirstMapped
                ? 3.699622554
                : isFirstMapped
                    ? 8.0956
                    : 3.3333333333
            : 1;
        var value = (bodyBaseValue
            + bodyBaseValue * PlanetValueExponent * Math.Pow(mass, 0.2))
            * mappingMultiplier;

        if (isMapped)
        {
            if (isOdyssey)
            {
                value += Math.Max(value * 0.3, 555);
            }

            if (withEfficiencyBonus)
            {
                value *= 1.25;
            }
        }

        value = Math.Max(500, value);
        value *= isFirstDiscoverer ? 2.6 : 1;
        value *= isFleetCarrierSale ? 0.75 : 1;
        return (int)Math.Round(value);
    }

    public static double GetStarBaseValue(string starClass)
    {
        if (starClass is "NS" or "BH" or "SupermassiveBlackHole")
        {
            return 22628;
        }

        return starClass.StartsWith('W') ? 14057 : 1200;
    }

    public static int GetPlanetBaseValue(
        string planetClass,
        bool isTerraformable)
    {
        return planetClass switch
        {
            "Metal rich body" => 21790,
            "Ammonia world" => 96932,
            "Sudarsky class I gas giant" => 1656,
            "Sudarsky class II gas giant" or "High metal content body" =>
                9654 + (isTerraformable ? 100677 : 0),
            "Water world" => 64831 + (isTerraformable ? 116295 : 0),
            _ when planetClass.StartsWith("Earth", StringComparison.Ordinal) =>
                64831 + 116295,
            _ => 300 + (isTerraformable ? 93328 : 0),
        };
    }

    private static bool IsStar(string bodyClass)
    {
        return bodyClass.Length < 8
            || bodyClass[1] == '_'
            || bodyClass is "SupermassiveBlackHole"
                or "Nebula"
                or "StellarRemnantNebula";
    }
}
