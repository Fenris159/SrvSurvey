using System.Text.Json;

namespace SrvSurvey.Core.Exploration;

public sealed class GreenGasGiantCriteriaCatalog
{
    private const string ResourceName = "SrvSurvey.Core.Resources.ggg.json";
    private readonly IReadOnlyDictionary<string, IReadOnlyList<double>> known;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<double>> theorized;

    private GreenGasGiantCriteriaCatalog(
        double tolerance,
        IReadOnlyDictionary<string, IReadOnlyList<double>> known,
        IReadOnlyDictionary<string, IReadOnlyList<double>> theorized)
    {
        Tolerance = tolerance;
        this.known = known;
        this.theorized = theorized;
    }

    public double Tolerance { get; }

    public static GreenGasGiantCriteriaCatalog LoadEmbedded()
    {
        var assembly = typeof(GreenGasGiantCriteriaCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException(
                $"Embedded Green Gas Giant criteria were not found: {ResourceName}");
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        var tolerance = root.TryGetProperty("delta", out var delta)
            && delta.TryGetDouble(out var parsedDelta)
            && double.IsFinite(parsedDelta)
            && parsedDelta >= 0
                ? parsedDelta
                : throw new InvalidDataException(
                    "Green Gas Giant criteria have an invalid tolerance.");
        return new GreenGasGiantCriteriaCatalog(
            tolerance,
            ReadTemperatures(root, "knownGGGTemps"),
            ReadTemperatures(root, "theorizedGGGTemps"));
    }

    public string? Match(string? planetClass, double surfaceTemperature)
    {
        if (string.IsNullOrWhiteSpace(planetClass)
            || !double.IsFinite(surfaceTemperature))
        {
            return null;
        }

        if (known.TryGetValue(planetClass, out var knownTemperatures))
        {
            if (knownTemperatures.Contains(surfaceTemperature))
            {
                return "likely";
            }

            if (IsApproximateMatch(knownTemperatures, surfaceTemperature))
            {
                return "likely-approx";
            }
        }

        if (theorized.TryGetValue(planetClass, out var theorizedTemperatures))
        {
            if (theorizedTemperatures.Contains(surfaceTemperature))
            {
                return "potential";
            }

            if (IsApproximateMatch(theorizedTemperatures, surfaceTemperature))
            {
                return "potential-approx";
            }
        }

        return null;
    }

    private bool IsApproximateMatch(
        IReadOnlyList<double> temperatures,
        double surfaceTemperature)
    {
        return temperatures.Any(
            temperature => Math.Abs(surfaceTemperature - temperature) < Tolerance);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<double>>
        ReadTemperatures(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var groups)
            || groups.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                $"Green Gas Giant criteria are missing {propertyName}.");
        }

        var result = new Dictionary<string, IReadOnlyList<double>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups.EnumerateObject())
        {
            if (group.Value.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException(
                    $"Green Gas Giant criteria for {group.Name} are not an array.");
            }

            var temperatures = new List<double>();
            foreach (var value in group.Value.EnumerateArray())
            {
                if (!value.TryGetDouble(out var temperature)
                    || !double.IsFinite(temperature))
                {
                    throw new InvalidDataException(
                        $"Green Gas Giant criteria for {group.Name} contain an invalid temperature.");
                }

                temperatures.Add(temperature);
            }

            result[group.Name] = temperatures;
        }

        return result;
    }
}
