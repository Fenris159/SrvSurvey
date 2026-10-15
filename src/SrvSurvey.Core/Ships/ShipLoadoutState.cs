using System.Text.Json;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Ships;

/// <summary>Tracks the current complete loadout and invalidates partial outfitting changes until a fresh Loadout arrives.</summary>
public sealed class ShipLoadoutState
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
    private string commander = "";
    private string commanderName = "";
    public ShipLoadout? Current { get; private set; }
    public bool IsInCrew { get; private set; }

    /// <summary>
    /// Reconciles journal changes and invalidates stale or incomplete ship export snapshots.
    /// </summary>
    public bool Apply(JournalEventEnvelope entry)
    {
        JsonElement root = entry.Payload;
        switch (entry.EventName)
        {
            case "Fileheader":
                commander = "";
                commanderName = "";
                IsInCrew = false;
                Current = null;
                break;
            case "Commander":
            case "LoadGame":
                ApplyCommander(root, entry.EventName);
                break;
            case "Loadout":
                Current = IsInCrew ? null : ShipLoadout.Parse(root);
                break;
            case "SetUserShipName":
                if (Current is not null && MatchesShip(root))
                {
                    JsonObject updated = JsonNode.Parse(Current.Payload.GetRawText())!.AsObject();
                    updated["ShipName"] = ShipModuleCatalog.Text(root, "UserShipName");
                    updated["ShipIdent"] = ShipModuleCatalog.Text(root, "UserShipId");
                    Current = ShipLoadout.Parse(JsonSerializer.SerializeToElement(updated));
                }
                break;
            case "JoinACrew":
                IsInCrew = true;
                Current = null;
                break;
            case "QuitACrew":
                IsInCrew = false;
                Current = null;
                break;
            case "EngineerCraft":
            case "EngineerLegacyConvert":
                ApplyEngineering(root);
                break;
            case "ShipyardSwap":
            case "ShipyardBuy":
            case "ShipyardNew":
            case "Died":
            case "Shutdown":
            case "ModuleBuy":
            case "ModuleSell":
            case "ModuleStore":
            case "ModuleRetrieve":
            case "ModuleSwap":
            case "MassModuleStore":
            case "ApplyExperimentalEffect":
                Current = null;
                break;
            default:
                return false;
        }
        return true;
    }

    /// <summary>
    /// Tracks commander identity and invalidates a loadout that belongs to another ship or pilot.
    /// </summary>
    private void ApplyCommander(JsonElement root, string eventName)
    {
        string nextCommander = ShipModuleCatalog.Text(root, "FID");
        string nextName = ShipModuleCatalog.Text(root, eventName == "Commander" ? "Name" : "Commander");
        if (commanderName.Length > 0 && nextName.Length > 0 && commanderName != nextName)
        {
            Current = null;
        }
        if (nextName.Length > 0)
        {
            commanderName = nextName;
        }
        if (commander.Length > 0 && nextCommander.Length > 0 && commander != nextCommander)
        {
            Current = null;
        }
        if (nextCommander.Length > 0)
        {
            commander = nextCommander;
        }
        if (eventName == "LoadGame" && Current is not null && !MatchesShip(root))
        {
            Current = null;
        }
    }

    /// <summary>
    /// Applies recorded engineering modifiers and reconciles the ship's unladen mass.
    /// </summary>
    private void ApplyEngineering(JsonElement root)
    {
        if (
            Current is null
            || (root.TryGetProperty("IsPreview", out JsonElement preview) && preview.ValueKind == JsonValueKind.True)
        )
        {
            return;
        }
        JsonObject updated = JsonNode.Parse(Current.Payload.GetRawText())!.AsObject();
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
            Current = null;
            return;
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
            Current = null;
            return;
        }
        Current = ShipLoadout.Parse(JsonSerializer.SerializeToElement(updated));
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

    /// <summary>
    /// Matches both ship type and identifier against the retained complete loadout.
    /// </summary>
    private bool MatchesShip(JsonElement payload)
    {
        string type = ShipModuleCatalog.Text(payload, "Ship");
        return Current is not null
            && type.Equals(Current.ShipType, StringComparison.OrdinalIgnoreCase)
            && payload.TryGetProperty("ShipID", out JsonElement id)
            && id.ValueKind == JsonValueKind.Number
            && id.TryGetInt64(out long value)
            && value == Current.ShipId;
    }
}
