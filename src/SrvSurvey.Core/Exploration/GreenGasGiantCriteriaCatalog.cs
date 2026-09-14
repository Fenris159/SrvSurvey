using System.Reflection;
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
        IReadOnlyDictionary<string, IReadOnlyList<double>> theorized
    )
    {
        Tolerance = tolerance;
        this.known = known;
        this.theorized = theorized;
    }

    public double Tolerance { get; }

    public int TemperatureCount =>
        known.Values.Sum(values => values.Count) + theorized.Values.Sum(values => values.Count);

    public static GreenGasGiantCriteriaCatalog LoadEmbedded()
    {
        Assembly assembly = typeof(GreenGasGiantCriteriaCatalog).Assembly;
        using Stream stream =
            assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException($"Embedded Green Gas Giant criteria were not found: {ResourceName}");
        return Load(stream);
    }

    public static GreenGasGiantCriteriaCatalog Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var document = JsonDocument.Parse(stream);
        JsonElement root = document.RootElement;
        double tolerance =
            root.TryGetProperty("delta", out JsonElement delta)
            && delta.TryGetDouble(out double parsedDelta)
            && double.IsFinite(parsedDelta)
            && parsedDelta >= 0
                ? parsedDelta
                : throw new InvalidDataException("Green Gas Giant criteria have an invalid tolerance.");
        return new GreenGasGiantCriteriaCatalog(
            tolerance,
            ReadTemperatures(root, "knownGGGTemps"),
            ReadTemperatures(root, "theorizedGGGTemps")
        );
    }

    public string? Match(string? planetClass, double surfaceTemperature)
    {
        if (string.IsNullOrWhiteSpace(planetClass) || !double.IsFinite(surfaceTemperature))
        {
            return null;
        }

        if (known.TryGetValue(planetClass, out IReadOnlyList<double>? knownTemperatures))
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

        if (theorized.TryGetValue(planetClass, out IReadOnlyList<double>? theorizedTemperatures))
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

    private bool IsApproximateMatch(IReadOnlyList<double> temperatures, double surfaceTemperature)
    {
        return temperatures.Any(temperature => Math.Abs(surfaceTemperature - temperature) < Tolerance);
    }

    private static Dictionary<string, IReadOnlyList<double>> ReadTemperatures(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement groups) || groups.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"Green Gas Giant criteria are missing {propertyName}.");
        }

        var result = new Dictionary<string, IReadOnlyList<double>>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty group in groups.EnumerateObject())
        {
            if (group.Value.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException($"Green Gas Giant criteria for {group.Name} are not an array.");
            }

            var temperatures = new List<double>();
            foreach (JsonElement value in group.Value.EnumerateArray())
            {
                if (!value.TryGetDouble(out double temperature) || !double.IsFinite(temperature))
                {
                    throw new InvalidDataException(
                        $"Green Gas Giant criteria for {group.Name} contain an invalid temperature."
                    );
                }

                temperatures.Add(temperature);
            }

            result[group.Name] = temperatures;
        }

        return result;
    }
}
