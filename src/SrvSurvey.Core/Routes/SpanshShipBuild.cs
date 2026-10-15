using System.Globalization;
using System.Text.Json;
using SrvSurvey.Core.Ships;

namespace SrvSurvey.Core.Routes;

/// <summary>Reads Spansh's jump inputs from SLEF, keeping actual journal engineering values.</summary>
public static class SpanshShipBuild
{
    public static IReadOnlyDictionary<string, string> GetJumpParameters(string slef)
    {
        try
        {
            return ReadJumpParameters(slef);
        }
        catch (Exception exception)
            when (exception is InvalidOperationException or KeyNotFoundException or IndexOutOfRangeException)
        {
            throw new ArgumentException(
                "The SLEF must include Modules, UnladenMass and FuelCapacity from a complete ship Loadout.",
                nameof(slef),
                exception
            );
        }
    }

    private static Dictionary<string, string> ReadJumpParameters(string slef)
    {
        using var document = JsonDocument.Parse(slef);
        JsonElement root = document.RootElement;
        JsonElement build = root.ValueKind == JsonValueKind.Array ? root[0] : root;
        JsonElement data = build.TryGetProperty("data", out JsonElement payload) ? payload : build;
        JsonElement[] modules = data.GetProperty("Modules").EnumerateArray().ToArray();
        JsonElement fsd = modules.FirstOrDefault(module =>
            string.Equals(ShipModuleCatalog.Text(module, "Slot"), "FrameShiftDrive", StringComparison.OrdinalIgnoreCase)
        );
        ModuleDefinition? definition = ShipModuleCatalog.Default.FindModule(ShipModuleCatalog.Text(fsd, "Item"));
        JumpDriveDefinition drive =
            definition?.JumpDrive ?? throw new ArgumentException("The ship build has no supported Frame Shift Drive.");
        double reserve = ReadNumber(data.GetProperty("FuelCapacity"), "Reserve");
        var values = new Dictionary<string, double>
        {
            ["fuel_power"] = drive.FuelPower,
            ["fuel_multiplier"] = drive.FuelMultiplier,
            ["optimal_mass"] = Modifier(fsd, "FSDOptimalMass", drive.OptimalMass),
            ["max_fuel_per_jump"] = Modifier(fsd, "MaxFuelPerJump", drive.MaximumFuelPerJump),
            ["base_mass"] = ReadNumber(data, "UnladenMass") + reserve,
            ["tank_size"] = ReadNumber(data.GetProperty("FuelCapacity"), "Main"),
            ["internal_tank_size"] = reserve,
            ["range_boost"] = modules.Sum(module =>
                ShipModuleCatalog.Default.FindModule(ShipModuleCatalog.Text(module, "Item"))?.RangeBoost ?? 0
            ),
            ["supercharge_multiplier"] = definition.Symbol.Equals(
                "Int_Hyperdrive_Overcharge_Size8_Class5_Overchargebooster_MkII",
                StringComparison.OrdinalIgnoreCase
            )
                ? 6
                : 4,
            ["injection_multiplier"] = 2,
        };
        return values.ToDictionary(pair => pair.Key, pair => pair.Value.ToString("R", CultureInfo.InvariantCulture));
    }

    private static double Modifier(JsonElement module, string label, double fallback)
    {
        if (
            !module.TryGetProperty("Engineering", out JsonElement engineering)
            || !engineering.TryGetProperty("Modifiers", out JsonElement modifiers)
        )
        {
            return fallback;
        }
        JsonElement modifier = modifiers
            .EnumerateArray()
            .FirstOrDefault(modifier =>
                ShipModuleCatalog.Text(modifier, "Label").Equals(label, StringComparison.OrdinalIgnoreCase)
            );
        return modifier.ValueKind == JsonValueKind.Undefined ? fallback : ReadNumber(modifier, "Value");
    }

    private static double ReadNumber(JsonElement element, string key)
    {
        if (
            !element.TryGetProperty(key, out JsonElement value)
            || !value.TryGetDouble(out double number)
            || !double.IsFinite(number)
            || number < 0
        )
        {
            throw new ArgumentException($"The ship build has no valid {key} value.");
        }
        return number;
    }
}
