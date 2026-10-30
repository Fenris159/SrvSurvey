using System.Text.Json;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Ships;

namespace SrvSurvey.Core.Frontier;

/// <summary>Reconciles journal ship identity and complete loadouts with timestamped CAPI snapshots.</summary>
public sealed class FrontierJournalShipState
{
    private long? shipId;
    private string symbol = "";
    private string name = "";
    private string ident = "";
    private DateTimeOffset? nameAt;
    private DateTimeOffset? identAt;
    private ShipLoadout? loadout;
    private readonly List<EngineeringObservation> engineeringUpdates = [];
    private DateTimeOffset? loadoutAt;
    private DateTimeOffset? updatedAt;
    private DateTimeOffset? identityAt;
    private DateTimeOffset? modulesInvalidatedAt;
    private DateTimeOffset? crewAt;
    private bool inCrew;

    /// <summary>Accepts chronological own-ship changes and discards stale observations and vehicle loadouts.</summary>
    public bool Apply(JournalEventEnvelope entry)
    {
        if (
            entry.Timestamp is not { } at
            || updatedAt is { } prior && at < prior
            || crewAt is { } crewTimestamp && at < crewTimestamp
        )
        {
            return false;
        }
        if (entry.EventName is "JoinACrew" or "QuitACrew")
        {
            inCrew = entry.EventName == "JoinACrew";
            crewAt = at;
            return false;
        }
        if (entry.EventName == "LoadGame")
        {
            inCrew = false;
            crewAt = at;
        }
        if (inCrew)
        {
            return false;
        }
        if (
            entry.EventName
            is "ModuleBuy"
                or "ModuleSell"
                or "ModuleStore"
                or "ModuleRetrieve"
                or "ModuleSwap"
                or "MassModuleStore"
                or "EngineerCraft"
                or "EngineerLegacyConvert"
                or "ApplyExperimentalEffect"
        )
        {
            return ApplyOutfitting(entry, at);
        }
        if (
            entry.EventName
            is not ("LoadGame" or "Loadout" or "ShipyardSwap" or "ShipyardBuy" or "ShipyardNew" or "SetUserShipName")
        )
        {
            return false;
        }
        return ApplyIdentity(entry, at);
    }

    private bool ApplyOutfitting(JournalEventEnvelope entry, DateTimeOffset at)
    {
        if (
            updatedAt is null
            || entry.Payload.TryGetProperty("IsPreview", out JsonElement preview)
                && preview.ValueKind == JsonValueKind.True
        )
        {
            return false;
        }
        if (
            Text(entry.Payload, "Ship").Length > 0
            && Number(entry.Payload, "ShipID") is not null
            && !ApplyIdentity(entry, at)
        )
        {
            return false;
        }
        if (
            entry.EventName is "EngineerCraft" or "EngineerLegacyConvert"
            && loadout?.WithEngineering(entry.Payload) is { } engineered
        )
        {
            loadout = engineered;
            string slot = Text(entry.Payload, "Slot");
            JsonElement module = engineered
                .Payload.GetProperty("Modules")
                .EnumerateArray()
                .First(item => Text(item, "Slot").Equals(slot, StringComparison.OrdinalIgnoreCase));
            engineeringUpdates.Add(new(Module(module), entry.Payload.Clone(), at));
            updatedAt = at;
            return true;
        }
        modulesInvalidatedAt = at;
        loadout = null;
        engineeringUpdates.Clear();
        updatedAt = at;
        return true;
    }

    /// <summary>Updates identity and complete loadout fields without carrying names or modules to a different ship.</summary>
    private bool ApplyIdentity(JournalEventEnvelope entry, DateTimeOffset at)
    {
        JsonElement data = entry.Payload;
        string nextSymbol = Text(data, "Ship");
        if (nextSymbol.Length == 0)
        {
            nextSymbol = Text(data, "ShipType");
        }
        if (
            nextSymbol.Length == 0
            || nextSymbol.Contains("fighter", StringComparison.OrdinalIgnoreCase)
            || nextSymbol.Contains("_srv_", StringComparison.OrdinalIgnoreCase)
            || nextSymbol.Equals("testbuggy", StringComparison.OrdinalIgnoreCase)
        )
        {
            return false;
        }
        long? nextId = Number(data, "NewShipID") ?? Number(data, "ShipID");
        bool changedShip = nextId != shipId || !nextSymbol.Equals(symbol, StringComparison.OrdinalIgnoreCase);
        if (changedShip)
        {
            name = ident = "";
            nameAt = identAt = null;
            loadout = null;
            loadoutAt = null;
            engineeringUpdates.Clear();
            modulesInvalidatedAt = null;
        }
        shipId = nextId;
        symbol = nextSymbol;
        bool rename = entry.EventName == "SetUserShipName";
        string nameKey = rename ? "UserShipName" : "ShipName";
        string identKey = rename ? "UserShipId" : "ShipIdent";
        if (data.TryGetProperty(nameKey, out _))
        {
            name = Text(data, nameKey);
            nameAt = at;
        }
        if (data.TryGetProperty(identKey, out _))
        {
            ident = Text(data, identKey);
            identAt = at;
        }
        if (entry.EventName == "Loadout")
        {
            loadout = ShipLoadout.Parse(data);
            loadoutAt = at;
            modulesInvalidatedAt = loadout is null ? at : null;
            engineeringUpdates.Clear();
        }
        updatedAt = at;
        identityAt = at;
        return true;
    }

    /// <summary>A genuinely later CAPI fetch wins; journal fields from a newer observation override only the matching ship.</summary>
    public FrontierShipSnapshot? Project(FrontierAccountSnapshot? account)
    {
        if (updatedAt is null || account is not null && account.FetchedAt > updatedAt)
        {
            return account?.CurrentShip;
        }
        if (account is not null && account.FetchedAt > identityAt && account.CurrentShip?.Id != shipId)
        {
            return account.CurrentShip;
        }
        FrontierShipSnapshot? cached = MatchingShip(account);
        string type = ShipModuleCatalog.Default.FindShip(symbol)?.Name ?? symbol;
        FrontierShipSnapshot ship = cached ?? new(shipId, type, type, "", "", "", 0, true, null, null);
        ship = ship with
        {
            Id = shipId,
            Type = IsNewer(identityAt, account) ? type : ship.Type,
            Name = IsNewer(nameAt, account) ? NonEmpty(name, type) : ship.Name,
            Identifier = IsNewer(identAt, account) ? ident : ship.Identifier,
            IsCurrent = true,
        };
        if (IsNewer(modulesInvalidatedAt, account))
        {
            return ship with { Modules = [], LaunchBays = [] };
        }
        if (loadout?.Payload is not { } data || !IsNewer(loadoutAt, account))
        {
            return ApplyEngineeringUpdates(ship, account);
        }
        long hull = Number(data, "HullValue") ?? ship.HullValue;
        long modules = Number(data, "ModulesValue") ?? ship.ModulesValue;
        FrontierShipModuleSnapshot[] fittedModules = data.GetProperty("Modules")
            .EnumerateArray()
            .Select(Module)
            .ToArray();
        ship = ship with
        {
            HullValue = hull,
            ModulesValue = modules,
            Value =
                Number(data, "HullValue") is not null || Number(data, "ModulesValue") is not null
                    ? hull + modules + ship.CargoValue
                    : ship.Value,
            HullHealth = Health(data, "HullHealth") ?? ship.HullHealth,
            Modules = fittedModules,
            LaunchBays = RetainMatchingLaunchBays(ship, fittedModules),
            DataPoints = FrontierCapiSnapshotParser.ParseDataPoints(data.GetRawText(), "journal.loadout"),
        };
        return ApplyEngineeringUpdates(ship, account);
    }

    private FrontierShipSnapshot ApplyEngineeringUpdates(FrontierShipSnapshot ship, FrontierAccountSnapshot? account)
    {
        if (ship.Modules is null || engineeringUpdates.Count == 0)
        {
            return ship;
        }
        return ship with
        {
            Modules = ship.Modules.Select(module => ApplyEngineeringUpdates(module, account)).ToArray(),
        };
    }

    private FrontierShipModuleSnapshot ApplyEngineeringUpdates(
        FrontierShipModuleSnapshot module,
        FrontierAccountSnapshot? account
    )
    {
        foreach (
            EngineeringObservation update in engineeringUpdates.Where(update =>
                IsNewer(update.At, account)
                && SlotKey(update.UpdatedModule.Slot) == SlotKey(module.Slot)
                && update.UpdatedModule.InternalName.Equals(module.InternalName, StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            module = module with
            {
                Engineer = update.Fields.TryGetProperty("Engineer", out _)
                    ? update.UpdatedModule.Engineer
                    : module.Engineer,
                Blueprint = update.Fields.TryGetProperty("BlueprintName", out _)
                    ? update.UpdatedModule.Blueprint
                    : module.Blueprint,
                BlueprintLevel = update.Fields.TryGetProperty("Level", out _)
                    ? update.UpdatedModule.BlueprintLevel
                    : module.BlueprintLevel,
                ExperimentalEffects = update.Fields.TryGetProperty("ExperimentalEffect", out _)
                    ? update.UpdatedModule.ExperimentalEffects
                    : module.ExperimentalEffects,
            };
        }
        return module;
    }

    private static IReadOnlyList<FrontierLaunchBaySnapshot> RetainMatchingLaunchBays(
        FrontierShipSnapshot ship,
        IReadOnlyList<FrontierShipModuleSnapshot> fitted
    )
    {
        string[] before = Hangars(ship.Modules ?? []).ToArray();
        return before.Length > 0 && before.SequenceEqual(Hangars(fitted)) ? ship.LaunchBays ?? [] : [];
    }

    private static IEnumerable<string> Hangars(IEnumerable<FrontierShipModuleSnapshot> modules) =>
        modules
            .Where(module => ShipModuleCatalog.Default.FindModule(module.InternalName)?.Type is "ifh" or "ipvh")
            .Select(module => SlotKey(module.Slot) + ":" + module.InternalName.ToLowerInvariant())
            .Order(StringComparer.Ordinal);

    private static string SlotKey(string slot) => string.Concat(slot.Where(char.IsLetterOrDigit)).ToLowerInvariant();

    private sealed record EngineeringObservation(
        FrontierShipModuleSnapshot UpdatedModule,
        JsonElement Fields,
        DateTimeOffset At
    );

    private FrontierShipSnapshot? MatchingShip(FrontierAccountSnapshot? account)
    {
        if (shipId is null || account is null)
        {
            return null;
        }
        return account.CurrentShip?.Id == shipId
            ? account.CurrentShip
            : account.Ships.FirstOrDefault(ship => ship.Id == shipId);
    }

    private static bool IsNewer(DateTimeOffset? at, FrontierAccountSnapshot? account) =>
        at is not null && (account is null || at >= account.FetchedAt);

    private static FrontierShipModuleSnapshot Module(JsonElement module)
    {
        string item = Text(module, "Item");
        ModuleDefinition? definition = ShipModuleCatalog.Default.FindModule(item);
        JsonElement engineering =
            module.TryGetProperty("Engineering", out JsonElement value) && value.ValueKind == JsonValueKind.Object
                ? value
                : default;
        string effect = Text(engineering, "ExperimentalEffect");
        return new(
            Text(module, "Slot"),
            Number(module, "ItemID"),
            NonEmpty(Text(module, "Item_Localised"), definition?.Name ?? item),
            "",
            Number(module, "Value") ?? 0,
            false,
            Health(module, "Health"),
            !module.TryGetProperty("On", out JsonElement on) || on.ValueKind != JsonValueKind.False,
            (int?)Number(module, "Priority"),
            Text(engineering, "Engineer"),
            Text(engineering, "BlueprintName"),
            (int?)Number(engineering, "Level"),
            effect.Length == 0 ? [] : [effect],
            item
        );
    }

    private static string NonEmpty(string value, string fallback) => value.Length > 0 ? value : fallback;

    private static string Text(JsonElement data, string key) =>
        data.ValueKind == JsonValueKind.Object
        && data.TryGetProperty(key, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static long? Number(JsonElement data, string key) =>
        data.ValueKind == JsonValueKind.Object
        && data.TryGetProperty(key, out JsonElement value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out long number)
            ? number
            : null;

    private static double? Health(JsonElement data, string key) =>
        data.TryGetProperty(key, out JsonElement value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out double number)
        && double.IsFinite(number)
            ? Math.Clamp(number * 100, 0, 100)
            : null;
}
