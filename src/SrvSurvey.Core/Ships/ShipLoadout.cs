using System.Text.Json;

namespace SrvSurvey.Core.Ships;

/// <summary>A complete journal loadout, including internals, original engineering and extension fields.</summary>
public sealed class ShipLoadout
{
    private static readonly JsonSerializerOptions ExportOptions = new() { WriteIndented = true };

    /// <summary>
    /// Clones the complete loadout so later journal buffers cannot alter the snapshot.
    /// </summary>
    private ShipLoadout(JsonElement payload) => Payload = payload.Clone();

    public JsonElement Payload { get; }
    public string ShipType => ShipModuleCatalog.Text(Payload, "Ship");
    public string Name => ShipModuleCatalog.Text(Payload, "ShipName");
    public long? ShipId =>
        Payload.TryGetProperty("ShipID", out JsonElement id)
        && id.ValueKind == JsonValueKind.Number
        && id.TryGetInt64(out long value)
            ? value
            : null;
    public int ModuleCount => Payload.GetProperty("Modules").GetArrayLength();

    /// <summary>The journal's maximum range with no cargo and only enough fuel for one jump.</summary>
    public decimal? MaximumJumpRange =>
        Payload.TryGetProperty("MaxJumpRange", out JsonElement range)
        && range.ValueKind == JsonValueKind.Number
        && range.TryGetDecimal(out decimal value)
        && value > 0
            ? value
            : null;

    /// <summary>
    /// Requires ship and module identities while preserving the original journal fields.
    /// </summary>
    public static ShipLoadout? Parse(JsonElement payload)
    {
        if (
            string.IsNullOrWhiteSpace(ShipModuleCatalog.Text(payload, "Ship"))
            || !payload.TryGetProperty("Modules", out JsonElement modules)
            || modules.ValueKind != JsonValueKind.Array
            || modules.GetArrayLength() == 0
            || modules
                .EnumerateArray()
                .Any(module =>
                    module.ValueKind != JsonValueKind.Object
                    || string.IsNullOrWhiteSpace(ShipModuleCatalog.Text(module, "Slot"))
                    || string.IsNullOrWhiteSpace(ShipModuleCatalog.Text(module, "Item"))
                )
        )
        {
            return null;
        }
        return new ShipLoadout(payload);
    }

    /// <summary>Exports the exact journal data in EDSY's Ship Loadout Exchange Format envelope.</summary>
    public string ExportSlef(string appVersion) =>
        JsonSerializer.Serialize(
            new[]
            {
                new
                {
                    header = new
                    {
                        appName = "SrvSurvey",
                        appVersion,
                        appURL = "https://github.com/Fenris159/SrvSurvey",
                    },
                    data = Payload,
                },
            },
            ExportOptions
        );
}
