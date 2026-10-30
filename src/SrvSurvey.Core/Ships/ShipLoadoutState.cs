using System.Text.Json;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Ships;

/// <summary>Tracks the current complete loadout and invalidates partial outfitting changes until a fresh Loadout arrives.</summary>
public sealed class ShipLoadoutState
{
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
                Current = Current?.WithEngineering(root);
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
