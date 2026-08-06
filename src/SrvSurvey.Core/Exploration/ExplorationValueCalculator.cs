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
            var starBaseValue = GetStarBaseValue(request.BodyClass);
            return (int)Math.Round(
                starBaseValue + (request.Mass * starBaseValue / 66.25));
        }

        var bodyBaseValue = GetPlanetBaseValue(
            request.BodyClass,
            request.IsTerraformable);
        var mappingMultiplier = request.IsMapped
            ? (request.IsFirstDiscoverer && request.IsFirstMapped) switch
            {
                true => 3.699622554,
                false => request.IsFirstMapped switch
                {
                    true => 8.0956,
                    false => 3.3333333333
                }
            }
            : 1;
        var value = (bodyBaseValue
            + bodyBaseValue * PlanetValueExponent * Math.Pow(request.Mass, 0.2))
            * mappingMultiplier;

        if (request.IsMapped)
        {
            if (request.IsOdyssey)
            {
                value += Math.Max(value * 0.3, 555);
            }

            if (request.WithEfficiencyBonus)
            {
                value *= 1.25;
            }
        }

        value = Math.Max(500, value);
        value *= request.IsFirstDiscoverer ? 2.6 : 1;
        value *= request.IsFleetCarrierSale ? 0.75 : 1;
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
