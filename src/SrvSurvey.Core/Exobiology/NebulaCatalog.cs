using System.Text.Json;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Exobiology;

public sealed class NebulaCatalog
{
    private const string EmbeddedResourceName =
        "SrvSurvey.Core.Resources.nebulae.json";

    private readonly GalacticCoordinate[] coordinates;

    public NebulaCatalog(IEnumerable<GalacticCoordinate> coordinates)
    {
        ArgumentNullException.ThrowIfNull(coordinates);
        this.coordinates = coordinates.ToArray();
    }

    public int Count => coordinates.Length;

    public double FindDistanceToClosest(GalacticCoordinate position)
    {
        if (coordinates.Length == 0)
        {
            return double.MaxValue;
        }

        var minimumSquaredDistance = double.MaxValue;
        foreach (var coordinate in coordinates)
        {
            var x = position.X - coordinate.X;
            var y = position.Y - coordinate.Y;
            var z = position.Z - coordinate.Z;
            var squaredDistance = (x * x) + (y * y) + (z * z);
            if (squaredDistance < minimumSquaredDistance)
            {
                minimumSquaredDistance = squaredDistance;
            }
        }

        return Math.Sqrt(minimumSquaredDistance);
    }

    public static NebulaCatalog LoadEmbedded()
    {
        var assembly = typeof(NebulaCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new InvalidOperationException(
                $"The embedded nebula catalog {EmbeddedResourceName} is missing.");
        return Load(stream);
    }

    public static NebulaCatalog Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException(
                    "The nebula catalog is not a JSON array.");
            }

            var coordinates = document.RootElement.EnumerateArray()
                .Select(ParseCoordinate)
                .ToArray();
            return new NebulaCatalog(coordinates);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "The nebula catalog is not valid JSON.",
                ex);
        }
    }

    private static GalacticCoordinate ParseCoordinate(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array
            || element.GetArrayLength() != 3)
        {
            throw new InvalidDataException(
                "The nebula catalog contains an invalid coordinate.");
        }

        var values = element.EnumerateArray().ToArray();
        if (values.Any(value => value.ValueKind != JsonValueKind.Number
                || !value.TryGetDouble(out var number)
                || !double.IsFinite(number)))
        {
            throw new InvalidDataException(
                "The nebula catalog contains a non-numeric coordinate.");
        }

        return new GalacticCoordinate(
            values[0].GetDouble(),
            values[1].GetDouble(),
            values[2].GetDouble());
    }
}
