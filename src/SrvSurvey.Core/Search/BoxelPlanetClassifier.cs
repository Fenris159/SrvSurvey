namespace SrvSurvey.Core.Search;

public static class BoxelPlanetClassifier
{
    public static bool TryFromPlanetClass(
        string? planetClass,
        out BoxelPlanetClass classified)
    {
        classified = UnknownOrExact(planetClass);
        if (classified != BoxelPlanetClass.Unknown)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(planetClass)
            && planetClass.StartsWith("Earth", StringComparison.Ordinal))
        {
            classified = BoxelPlanetClass.Earthlike;
            return true;
        }

        classified = BoxelPlanetClass.Unknown;
        return false;
    }

    public static bool IsTerraformable(string? terraformState)
        => string.Equals(terraformState, "Terraformable", StringComparison.Ordinal);

    public static bool HasAtmosphere(string? atmosphereType)
        => !string.IsNullOrWhiteSpace(atmosphereType)
           && !string.Equals(atmosphereType, "None", StringComparison.OrdinalIgnoreCase);

    public static bool IsAtmosphericLandable(bool isLandable, string? atmosphereType)
        => isLandable && HasAtmosphere(atmosphereType);

    public static bool TryGetHeliumPercent(
        IReadOnlyDictionary<string, double>? atmosphereComposition,
        out double percent)
    {
        percent = 0;
        if (atmosphereComposition is null)
        {
            return false;
        }

        if (!atmosphereComposition.TryGetValue("Helium", out percent)
            && !TryGetHeliumIgnoreCase(atmosphereComposition, out percent))
        {
            percent = 0;
            return false;
        }

        if (percent > 0 && percent <= 100)
        {
            return true;
        }

        percent = 0;
        return false;
    }

    public static bool ShowsTerraformableColumn(BoxelPlanetClass classified)
        => classified is BoxelPlanetClass.MetalRich
            or BoxelPlanetClass.HighMetalContent
            or BoxelPlanetClass.Rocky
            or BoxelPlanetClass.WaterWorld;

    public static bool ShowsLandableColumns(BoxelPlanetClass classified)
        => classified is BoxelPlanetClass.MetalRich
            or BoxelPlanetClass.HighMetalContent
            or BoxelPlanetClass.Rocky
            or BoxelPlanetClass.Icy
            or BoxelPlanetClass.RockyIce;

    public static string ToPlanetClassString(BoxelPlanetClass classified)
        => classified switch
        {
            BoxelPlanetClass.MetalRich => "Metal rich body",
            BoxelPlanetClass.HighMetalContent => "High metal content body",
            BoxelPlanetClass.Rocky => "Rocky body",
            BoxelPlanetClass.Icy => "Icy body",
            BoxelPlanetClass.RockyIce => "Rocky ice body",
            BoxelPlanetClass.Earthlike => "Earthlike body",
            BoxelPlanetClass.WaterWorld => "Water world",
            BoxelPlanetClass.AmmoniaWorld => "Ammonia world",
            BoxelPlanetClass.WaterGiant => "Water giant",
            BoxelPlanetClass.WaterGiantWithLife => "Water giant with life",
            BoxelPlanetClass.GasGiantWaterLife => "Gas giant with water based life",
            BoxelPlanetClass.GasGiantAmmoniaLife => "Gas giant with ammonia based life",
            BoxelPlanetClass.SudarskyI => "Sudarsky class I gas giant",
            BoxelPlanetClass.SudarskyII => "Sudarsky class II gas giant",
            BoxelPlanetClass.SudarskyIII => "Sudarsky class III gas giant",
            BoxelPlanetClass.SudarskyIV => "Sudarsky class IV gas giant",
            BoxelPlanetClass.SudarskyV => "Sudarsky class V gas giant",
            BoxelPlanetClass.HeliumRichGasGiant => "Helium rich gas giant",
            BoxelPlanetClass.HeliumGasGiant => "Helium gas giant",
            _ => string.Empty,
        };

    private static BoxelPlanetClass UnknownOrExact(string? planetClass)
        => planetClass switch
        {
            "Metal rich body" => BoxelPlanetClass.MetalRich,
            "High metal content body" => BoxelPlanetClass.HighMetalContent,
            "Rocky body" => BoxelPlanetClass.Rocky,
            "Icy body" => BoxelPlanetClass.Icy,
            "Rocky ice body" => BoxelPlanetClass.RockyIce,
            "Earthlike body" => BoxelPlanetClass.Earthlike,
            "Water world" => BoxelPlanetClass.WaterWorld,
            "Ammonia world" => BoxelPlanetClass.AmmoniaWorld,
            "Water giant" => BoxelPlanetClass.WaterGiant,
            "Water giant with life" => BoxelPlanetClass.WaterGiantWithLife,
            "Gas giant with water based life" => BoxelPlanetClass.GasGiantWaterLife,
            "Gas giant with ammonia based life" => BoxelPlanetClass.GasGiantAmmoniaLife,
            "Sudarsky class I gas giant" => BoxelPlanetClass.SudarskyI,
            "Sudarsky class II gas giant" => BoxelPlanetClass.SudarskyII,
            "Sudarsky class III gas giant" => BoxelPlanetClass.SudarskyIII,
            "Sudarsky class IV gas giant" => BoxelPlanetClass.SudarskyIV,
            "Sudarsky class V gas giant" => BoxelPlanetClass.SudarskyV,
            "Helium rich gas giant" => BoxelPlanetClass.HeliumRichGasGiant,
            "Helium gas giant" => BoxelPlanetClass.HeliumGasGiant,
            _ => BoxelPlanetClass.Unknown,
        };

    private static bool TryGetHeliumIgnoreCase(
        IReadOnlyDictionary<string, double> atmosphereComposition,
        out double percent)
    {
        var pair = atmosphereComposition.FirstOrDefault(pair =>
            string.Equals(pair.Key, "Helium", StringComparison.OrdinalIgnoreCase));
        if (pair.Key is not null)
        {
            percent = pair.Value;
            return true;
        }

        percent = 0;
        return false;
    }
}
