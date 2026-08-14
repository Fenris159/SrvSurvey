namespace SrvSurvey.Core.Exploration;

public sealed class ExplorationValueRequest
{
    public string? BodyClass { get; init; }

    public bool IsTerraformable { get; init; }

    public double Mass { get; init; }

    public bool IsFirstDiscoverer { get; init; }

    public bool IsMapped { get; init; }

    public bool IsFirstMapped { get; init; }

    public bool IsOdyssey { get; init; }

    public bool WithEfficiencyBonus { get; init; } = true;

    public bool IsFleetCarrierSale { get; init; }
}

public static class ExplorationValueCalculator
{
    private const double PlanetValueExponent = 0.56591828;

    public static int Calculate(ExplorationValueRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.BodyClass))
        {
            return 0;
        }

        if (IsStar(request.BodyClass))
        {
            return CalculateStarValue(request.BodyClass, request.Mass);
        }

        var value = CalculatePlanetBaseValue(request);
        value = ApplyMappedBonuses(value, request);
        value = Math.Max(500, value);
        value *= request.IsFirstDiscoverer ? 2.6 : 1;
        value *= request.IsFleetCarrierSale ? 0.75 : 1;
        return (int)Math.Round(value);
    }

    private static int CalculateStarValue(string bodyClass, double mass)
    {
        var starBaseValue = GetStarBaseValue(bodyClass);
        return (int)Math.Round(
            starBaseValue + (mass * starBaseValue / 66.25));
    }

    private static double CalculatePlanetBaseValue(ExplorationValueRequest request)
    {
        var bodyBaseValue = GetPlanetBaseValue(
            request.BodyClass!,
            request.IsTerraformable);
        return (bodyBaseValue
            + bodyBaseValue * PlanetValueExponent * Math.Pow(request.Mass, 0.2))
            * GetMappingMultiplier(request);
    }

    private static double GetMappingMultiplier(ExplorationValueRequest request)
    {
        if (!request.IsMapped)
        {
            return 1;
        }

        if (request.IsFirstDiscoverer && request.IsFirstMapped)
        {
            return 3.699622554;
        }

        return request.IsFirstMapped ? 8.0956 : 3.3333333333;
    }

    private static double ApplyMappedBonuses(
        double value,
        ExplorationValueRequest request)
    {
        if (!request.IsMapped)
        {
            return value;
        }

        if (request.IsOdyssey)
        {
            value += Math.Max(value * 0.3, 555);
        }

        if (request.WithEfficiencyBonus)
        {
            value *= 1.25;
        }

        return value;
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
            "Metal rich body" => 21790 + (isTerraformable ? 105678 : 0),
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
