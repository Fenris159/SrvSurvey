using System.Text.Json;
using System.Text.Json.Nodes;

namespace SrvSurvey.Core.Ships;

public sealed partial class ShipLoadout
{
    private static readonly string[] EngineeringFields =
    [
        "Engineer",
        "EngineerID",
        "BlueprintName",
        "BlueprintID",
        "Level",
        "Quality",
        "Modifiers",
        "ExperimentalEffect",
        "ExperimentalEffect_Localised",
    ];

    /// <summary>
    /// Applies recorded engineering modifiers and reconciles the ship's unladen mass.
    /// </summary>
    public ShipLoadout? WithEngineering(JsonElement root)
    {
        if (root.TryGetProperty("IsPreview", out JsonElement preview) && preview.ValueKind == JsonValueKind.True)
        {
            return this;
        }
        JsonObject updated = JsonNode.Parse(Payload.GetRawText())!.AsObject();
        JsonObject? module = updated["Modules"]!
            .AsArray()
            .OfType<JsonObject>()
            .FirstOrDefault(candidate =>
                ShipModuleCatalog
                    .Text(JsonSerializer.SerializeToElement(candidate), "Slot")
                    .Equals(ShipModuleCatalog.Text(root, "Slot"), StringComparison.OrdinalIgnoreCase)
                && ShipModuleCatalog
                    .Text(JsonSerializer.SerializeToElement(candidate), "Item")
                    .Equals(ShipModuleCatalog.Text(root, "Module"), StringComparison.OrdinalIgnoreCase)
            );
        if (
            module is null
            || !root.TryGetProperty("Modifiers", out JsonElement modifiers)
            || modifiers.ValueKind != JsonValueKind.Array
        )
        {
            return null;
        }
        JsonElement before = JsonSerializer.SerializeToElement(module);
        JsonObject engineering = module["Engineering"] is JsonObject existing ? existing.DeepClone().AsObject() : [];
        foreach (string field in EngineeringFields)
        {
            if (root.TryGetProperty(field, out JsonElement value))
            {
                engineering[field] = JsonNode.Parse(value.GetRawText());
            }
        }
        module["Engineering"] = engineering;
        if (!UpdateUnladenMass(updated, before, JsonSerializer.SerializeToElement(module)))
        {
            return null;
        }
        return Parse(JsonSerializer.SerializeToElement(updated));
    }

    /// <summary>
    /// Adjusts unladen mass by the actual module-mass change when it can be established.
    /// </summary>
    private static bool UpdateUnladenMass(JsonObject ship, JsonElement before, JsonElement after)
    {
        double? previous = EngineeredMass(before);
        double? next = EngineeredMass(after);
        if (
            (previous is null && next is null)
            || ship["UnladenMass"] is not JsonValue value
            || !value.TryGetValue(out double unladen)
        )
        {
            return true;
        }
        double? baseMass = ShipModuleCatalog.Default.FindModule(ShipModuleCatalog.Text(before, "Item"))?.Mass;
        previous ??= baseMass;
        next ??= baseMass;
        if (previous is null || next is null)
        {
            return false;
        }
        double mass = unladen + next.Value - previous.Value;
        if (!double.IsFinite(mass) || mass < 0)
        {
            return false;
        }
        ship["UnladenMass"] = mass;
        return true;
    }

    /// <summary>
    /// Reads a recorded mass modifier only from correctly shaped engineering data.
    /// </summary>
    private static double? EngineeredMass(JsonElement module)
    {
        if (
            !module.TryGetProperty("Engineering", out JsonElement engineering)
            || engineering.ValueKind != JsonValueKind.Object
            || !engineering.TryGetProperty("Modifiers", out JsonElement modifiers)
            || modifiers.ValueKind != JsonValueKind.Array
        )
        {
            return null;
        }
        JsonElement mass = modifiers
            .EnumerateArray()
            .FirstOrDefault(modifier =>
                ShipModuleCatalog.Text(modifier, "Label").Equals("Mass", StringComparison.OrdinalIgnoreCase)
            );
        return
            mass.ValueKind != JsonValueKind.Undefined
            && mass.TryGetProperty("Value", out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out double number)
            ? number
            : null;
    }
}
