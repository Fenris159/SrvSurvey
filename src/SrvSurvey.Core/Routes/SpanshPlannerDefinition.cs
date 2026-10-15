using System.Globalization;
using System.Text.Json;

namespace SrvSurvey.Core.Routes;

public enum SpanshPlannerFieldType
{
    System,
    Number,
    Boolean,
    Choice,
    Systems,
    ShipBuild,
}

public sealed record SpanshPlannerField(
    string Key,
    string Label,
    SpanshPlannerFieldType Type,
    string Default = "",
    bool Required = false,
    double? Minimum = null,
    double? Maximum = null,
    IReadOnlyList<SpanshPlannerChoice>? Choices = null,
    string Help = "",
    string? VisibleWhen = null,
    string? VisibleValue = null,
    bool Send = true
);

public sealed record SpanshPlannerChoice(string Name, string Value);

public sealed record SpanshPlannerDefinition(
    string Name,
    string Endpoint,
    string Page,
    SpanshRouteKind Kind,
    IReadOnlyList<SpanshPlannerField> Fields
)
{
    /// <summary>
    /// Returns the planner name displayed in the chooser.
    /// </summary>
    public override string ToString() => Name;

    /// <summary>
    /// Validates form values and builds the planner's encoded submission parameters.
    /// </summary>
    public SpanshPlannerRequest CreateRequest(IReadOnlyDictionary<string, string> values)
    {
        var parameters = new List<KeyValuePair<string, string>>();
        foreach (SpanshPlannerField field in Fields)
        {
            if (!field.Send || !IsVisible(field, values))
            {
                continue;
            }
            string value = values.GetValueOrDefault(field.Key, field.Default).Trim();
            if (field.Required && value.Length == 0)
            {
                throw new ArgumentException($"{field.Label} is required.");
            }
            if (value.Length == 0)
            {
                continue;
            }
            Validate(field, value);
            AddField(parameters, field, value);
        }
        if (Kind == SpanshRouteKind.FleetCarrier)
        {
            AddCarrierParameters(values, parameters);
        }
        return new SpanshPlannerRequest(this, parameters);
    }

    /// <summary>
    /// Adds a visible field according to its type, retaining repeated system entries.
    /// </summary>
    private static void AddField(List<KeyValuePair<string, string>> parameters, SpanshPlannerField field, string value)
    {
        string[] values =
            field.Type == SpanshPlannerFieldType.Systems
                ? value.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                : [value];
        parameters.AddRange(values.Select(item => new KeyValuePair<string, string>(field.Key, item)));
    }

    /// <summary>
    /// Evaluates the optional checkbox condition controlling a field's visibility.
    /// </summary>
    public static bool IsVisible(SpanshPlannerField field, IReadOnlyDictionary<string, string> values) =>
        field.VisibleWhen is null || values.GetValueOrDefault(field.VisibleWhen) == field.VisibleValue;

    /// <summary>
    /// Applies carrier capacities, fuel mode and refuelling-destination constraints.
    /// </summary>
    private static void AddCarrierParameters(
        IReadOnlyDictionary<string, string> values,
        List<KeyValuePair<string, string>> parameters
    )
    {
        bool squadron = values.GetValueOrDefault("carrier_type") == "Squadron Carrier";
        double capacity = squadron ? 60000 : 25000;
        parameters.Add(new("capacity", capacity.ToString(CultureInfo.InvariantCulture)));
        parameters.Add(new("mass", squadron ? "15000" : "25000"));
        foreach (string key in new[] { "capacity_used", "tritium_stored" })
        {
            if (
                double.TryParse(
                    parameters.FirstOrDefault(pair => pair.Key == key).Value,
                    CultureInfo.InvariantCulture,
                    out double used
                )
                && used > capacity
            )
            {
                string label = key == "capacity_used" ? "Used capacity" : "Tritium in market";
                throw new ArgumentException($"{label} exceeds the carrier's {capacity} tonne capacity.");
            }
        }
        string[] destinations = parameters
            .Where(pair => pair.Key == "destinations")
            .Select(pair => pair.Value)
            .ToArray();
        if (
            parameters.Any(pair =>
                pair.Key == "refuel_destinations"
                && !destinations.Contains(pair.Value, StringComparer.OrdinalIgnoreCase)
            )
        )
        {
            throw new ArgumentException("Refuel destinations must also be listed as route destinations.");
        }
    }

    /// <summary>
    /// Rejects missing required values and invalid numeric, choice or ship-build inputs.
    /// </summary>
    private static void Validate(SpanshPlannerField field, string value)
    {
        if (field.Type == SpanshPlannerFieldType.Number)
        {
            ValidateNumber(field, value);
        }
        else if (
            field.Type == SpanshPlannerFieldType.Choice
            && field.Choices?.Any(choice => choice.Value == value) != true
        )
        {
            throw new ArgumentException($"Choose a valid {field.Label}.");
        }
        else if (field.Type == SpanshPlannerFieldType.ShipBuild)
        {
            using var document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind is not (JsonValueKind.Array or JsonValueKind.Object))
            {
                throw new ArgumentException("Ship Build must contain ship build JSON or SLEF.");
            }
        }
    }

    /// <summary>
    /// Checks a numeric input's invariant format and configured bounds.
    /// </summary>
    private static void ValidateNumber(SpanshPlannerField field, string value)
    {
        if (
            !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
            || !double.IsFinite(number)
        )
        {
            throw new ArgumentException($"{field.Label} must be a number.");
        }
        if (number < field.Minimum || number > field.Maximum)
        {
            throw new ArgumentException($"{field.Label} is outside the allowed range.");
        }
    }
}

public sealed record SpanshPlannerRequest(
    SpanshPlannerDefinition Planner,
    IReadOnlyList<KeyValuePair<string, string>> Parameters
);
