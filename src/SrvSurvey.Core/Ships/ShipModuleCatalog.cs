using System.Globalization;
using System.Text.Json;

namespace SrvSurvey.Core.Ships;

/// <summary>Offline EDSY module identities and ship/slot fitting restrictions.</summary>
public sealed class ShipModuleCatalog
{
    private static readonly JsonSerializerOptions CatalogOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly Dictionary<string, ShipDefinition> ships;
    private readonly Dictionary<string, ModuleDefinition> modules;
    private readonly CatalogDocument document;

    public static ShipModuleCatalog Default { get; } = new();

    /// <summary>
    /// Loads the offline reference snapshot into ship, module and MercGear lookup records.
    /// </summary>
    private ShipModuleCatalog()
    {
        using Stream stream = typeof(ShipModuleCatalog).Assembly.GetManifestResourceStream(
            "SrvSurvey.Core.Resources.edsy-modules.json"
        )!;
        document = JsonSerializer.Deserialize<CatalogDocument>(stream, CatalogOptions)!;
        ships = document.Ships.ToDictionary(ship => ship.Symbol, StringComparer.OrdinalIgnoreCase);
        modules = document.Modules.ToDictionary(module => module.Symbol, StringComparer.OrdinalIgnoreCase);
    }

    public string SourceRevision => document.Revision;
    public IReadOnlyCollection<ShipDefinition> Ships => ships.Values;
    public IReadOnlyCollection<ModuleDefinition> Modules => modules.Values;
    public IReadOnlyList<MercenaryModuleDefinition> MercenaryModules => document.MercenaryModules;

    /// <summary>
    /// Looks up a ship by its case-insensitive Frontier symbol.
    /// </summary>
    public ShipDefinition? FindShip(string symbol) => ships.GetValueOrDefault(symbol.Trim());

    /// <summary>
    /// Looks up a module by its case-insensitive Frontier symbol.
    /// </summary>
    public ModuleDefinition? FindModule(string symbol) => modules.GetValueOrDefault(symbol.Trim());

    /// <summary>Checks factual size, module type, ship and special-slot restrictions without altering loadouts.</summary>
    public ModuleFit CheckFit(string shipSymbol, string slotName, string moduleSymbol)
    {
        ShipDefinition? ship = FindShip(shipSymbol);
        ModuleDefinition? module = FindModule(moduleSymbol);
        ShipSlotDefinition? slot = ship?.Slots.FirstOrDefault(candidate =>
            candidate.Name.Equals(slotName, StringComparison.OrdinalIgnoreCase)
        );
        if (ship is null)
        {
            return ModuleFit.UnknownShip;
        }
        if (module is null)
        {
            return ModuleFit.UnknownModule;
        }
        if (slot is null)
        {
            return ModuleFit.UnknownSlot;
        }
        ModuleFit fit = CheckSlotTypeAndSize(slot, module);
        return fit == ModuleFit.Allowed ? CheckShipAndSlotRestrictions(ship, slot, module) : fit;
    }

    /// <summary>
    /// Checks the module's slot family, maximum size and exact-size requirements.
    /// </summary>
    private static ModuleFit CheckSlotTypeAndSize(ShipSlotDefinition slot, ModuleDefinition module)
    {
        if (!slot.Types.Contains(module.Type))
        {
            return ModuleFit.WrongModuleType;
        }
        if (module.Size > slot.Size)
        {
            return ModuleFit.TooLarge;
        }
        if (module.Size < slot.Size && (module.ExactSize || module.Type is "cls" or "cs"))
        {
            return ModuleFit.TooSmall;
        }
        return ModuleFit.Allowed;
    }

    /// <summary>
    /// Checks ship restrictions and specialized slot compatibility.
    /// </summary>
    private static ModuleFit CheckShipAndSlotRestrictions(
        ShipDefinition ship,
        ShipSlotDefinition slot,
        ModuleDefinition module
    )
    {
        if (module.AllowedShips.Count > 0 && !module.AllowedShips.Contains(ship.Id))
        {
            return ModuleFit.RestrictedShip;
        }
        if (slot.RestrictedTypes.Count > 0 && !slot.RestrictedTypes.Contains(module.Type))
        {
            return ModuleFit.RestrictedSlot;
        }
        return CheckSpecialSlot(slot.Name, module);
    }

    /// <summary>
    /// Applies the supported module families for a specialized slot.
    /// </summary>
    private static ModuleFit CheckSpecialSlot(string slotName, ModuleDefinition module)
    {
        if (module.Type == "icr" && module.AllowedShips.Count > 0 && !StartsWith(slotName, "Cargo"))
        {
            return ModuleFit.RestrictedSlot;
        }
        if (module.Type == "imlc" && module.AllowedShips.Count > 0 && !StartsWith(slotName, "LimpetController"))
        {
            return ModuleFit.RestrictedSlot;
        }
        if (
            module.Type == "ipvh"
            && !module.Symbol.Contains("LargeBuggyBay", StringComparison.OrdinalIgnoreCase)
            && StartsWith(slotName, "FighterBay")
        )
        {
            return ModuleFit.RestrictedSlot;
        }
        return ModuleFit.Allowed;
    }

    /// <summary>Offers EDSY hardpoints and active utilities; existing internal firegroup actions stay separately curated.</summary>
    public string? GetFiregroupName(JsonElement module)
    {
        ModuleDefinition? definition = FindModule(Text(module, "Item"));
        if (definition?.Firegroup != true)
        {
            return null;
        }
        MercenaryModuleDefinition? variant = FindMercenaryModule(module, definition);
        string name = variant?.Name ?? definition.Name;
        string mount = definition.Mount switch
        {
            "F" => " Fixed",
            "G" => " Gimballed",
            "T" => " Turreted",
            _ => "",
        };
        return $"{name} ({definition.Size.ToString(CultureInfo.InvariantCulture)}{definition.Rating}{mount})";
    }

    /// <summary>
    /// Identifies eligible MercGear actions from recorded blueprint or localized names.
    /// </summary>
    public string? GetMercenaryFiregroupName(JsonElement module)
    {
        ModuleDefinition? definition = FindModule(Text(module, "Item"));
        return definition?.Firegroup == true ? FindMercenaryModule(module, definition)?.Name : null;
    }

    /// <summary>
    /// Matches a MercGear preset without inventing unknown blueprint identities.
    /// </summary>
    private MercenaryModuleDefinition? FindMercenaryModule(JsonElement module, ModuleDefinition definition)
    {
        string localized = Text(module, "Item_Localised");
        string blueprint = module.TryGetProperty("Engineering", out JsonElement engineering)
            ? Text(engineering, "BlueprintName")
            : "";
        return document.MercenaryModules.FirstOrDefault(candidate =>
            candidate.Symbol.Equals(definition.Symbol, StringComparison.OrdinalIgnoreCase)
            && (
                (
                    candidate.BlueprintSymbol is not null
                    && candidate.BlueprintSymbol.Equals(blueprint, StringComparison.OrdinalIgnoreCase)
                ) || candidate.Name.Equals(localized, StringComparison.OrdinalIgnoreCase)
            )
        );
    }

    /// <summary>
    /// Reads string properties from JSON objects and returns empty for other value shapes.
    /// </summary>
    internal static string Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    /// <summary>
    /// Compares module or slot family prefixes without case sensitivity.
    /// </summary>
    private static bool StartsWith(string value, string prefix) =>
        value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    private sealed record CatalogDocument(
        string Revision,
        ShipDefinition[] Ships,
        ModuleDefinition[] Modules,
        MercenaryModuleDefinition[] MercenaryModules
    );
}

public sealed record ShipDefinition(int Id, string Symbol, string Name, IReadOnlyList<ShipSlotDefinition> Slots);

public sealed record ShipSlotDefinition(
    string Name,
    string Group,
    int Size,
    IReadOnlyList<string> Types,
    IReadOnlyList<string> RestrictedTypes
);

public sealed record ModuleDefinition(
    int Id,
    long? FrontierId,
    string Symbol,
    string Name,
    string Type,
    int Size,
    string Rating,
    string Mount,
    IReadOnlyList<int> AllowedShips,
    bool ExactSize,
    string Limit,
    double? Mass,
    double? PowerDraw,
    bool Firegroup,
    JumpDriveDefinition? JumpDrive = null,
    double RangeBoost = 0
);

public sealed record JumpDriveDefinition(
    double FuelPower,
    double FuelMultiplier,
    double OptimalMass,
    double MaximumFuelPerJump
);

public sealed record MercenaryModuleDefinition(
    int Id,
    string Symbol,
    string Name,
    string BlueprintId,
    string? BlueprintSymbol,
    string BlueprintName,
    int Grade,
    string? Effect,
    string EdsyHash
);

public enum ModuleFit
{
    Allowed,
    UnknownShip,
    UnknownModule,
    UnknownSlot,
    WrongModuleType,
    TooLarge,
    TooSmall,
    RestrictedShip,
    RestrictedSlot,
}
