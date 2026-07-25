using System.Reflection;
using System.Text.Json;

namespace SrvSurvey.Core.Exobiology;

public sealed class ExobiologyReferenceCatalog
{
    private const string EmbeddedResourceName =
        "SrvSurvey.Core.Resources.codexRef.json";

    private readonly Dictionary<string, ExobiologyReference> byVariant;
    private readonly Dictionary<string, ExobiologyReference> bySpecies;
    private readonly Dictionary<string, ExobiologyReference> byDisplayName;
    private readonly Dictionary<long, ExobiologyReference> byEntryId;

    public ExobiologyReferenceCatalog(IEnumerable<ExobiologyReference> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var materialized = entries.ToArray();
        Entries = Array.AsReadOnly(materialized);
        BiologyEntries = Array.AsReadOnly(materialized
            .Where(entry => entry.IsBiology)
            .ToArray());
        byVariant = materialized
            .GroupBy(entry => entry.VariantName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.Ordinal);
        bySpecies = materialized
            .GroupBy(entry => entry.SpeciesName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.Ordinal);
        byDisplayName = materialized
            .Where(entry => !string.IsNullOrWhiteSpace(entry.DisplayName))
            .GroupBy(
                entry => entry.DisplayName!,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);
        byEntryId = materialized
            .GroupBy(entry => entry.EntryId)
            .ToDictionary(group => group.Key, group => group.First());
    }

    public IReadOnlyList<ExobiologyReference> Entries { get; }

    public IReadOnlyList<ExobiologyReference> BiologyEntries { get; }

    public int Count => Entries.Count;

    public ExobiologyReference? FindByVariant(string? variantName)
    {
        return variantName is not null
            && byVariant.TryGetValue(variantName, out var result)
                ? result
                : null;
    }

    public ExobiologyReference? FindBySpecies(string? speciesName)
    {
        return speciesName is not null
            && bySpecies.TryGetValue(speciesName, out var result)
                ? result
                : null;
    }

    public ExobiologyReference? FindByEntryId(long entryId)
    {
        return byEntryId.GetValueOrDefault(entryId);
    }

    public ExobiologyReference? FindByDisplayName(string? displayName)
    {
        return displayName is not null
            && byDisplayName.TryGetValue(displayName, out var result)
                ? result
                : null;
    }

    public static ExobiologyReferenceCatalog LoadEmbedded()
    {
        var assembly = typeof(ExobiologyReferenceCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new InvalidOperationException(
                $"The embedded Codex reference {EmbeddedResourceName} is missing.");
        return Load(stream);
    }

    public static ExobiologyReferenceCatalog Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var document = JsonDocument.Parse(stream);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("The Codex reference is not a JSON object.");
        }

        var entries = new List<ExobiologyReference>();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            var value = property.Value;
            var reward = GetInt64(value, "reward");
            var entryId = GetInt64(value, "entryid");
            var variantName = GetString(value, "name");
            if (entryId is not > 0
                || string.IsNullOrWhiteSpace(variantName))
            {
                continue;
            }

            var platform = GetString(value, "platform");
            var hudCategory = GetString(value, "hud_category");
            var speciesName = platform == "odyssey" && hudCategory == "Biology"
                ? GetSpeciesName(variantName)
                : variantName;
            entries.Add(new ExobiologyReference(
                entryId.Value,
                variantName,
                speciesName,
                GetString(value, "english_name"),
                reward ?? 0,
                GetString(value, "category"),
                hudCategory,
                GetString(value, "sub_category"),
                GetString(value, "sub_class"),
                platform,
                GetString(value, "image_url"),
                GetString(value, "image_cmdr"),
                GetString(value, "dump")));
        }

        return new ExobiologyReferenceCatalog(entries);
    }

    internal static string GetSpeciesName(string variantName)
    {
        var species = variantName
            .Replace("$Codex_Ent_", string.Empty, StringComparison.Ordinal)
            .Replace("_Name;", string.Empty, StringComparison.Ordinal);
        var lastSeparator = species.LastIndexOf('_');
        if (species.IndexOf('_') != lastSeparator)
        {
            species = species[..lastSeparator];
        }

        return $"$Codex_Ent_{species}_Name;";
    }

    public static string GetGenusName(string speciesName)
    {
        var genus = speciesName
            .Replace("$Codex_Ent_", string.Empty, StringComparison.Ordinal)
            .Replace("_Name;", string.Empty, StringComparison.Ordinal);
        var separator = genus.IndexOf('_');
        if (separator >= 0)
        {
            genus = genus[..separator];
        }

        return $"$Codex_Ent_{genus}_Genus_Name;";
    }

    public static int GetSampleDistanceMeters(string? genusName)
    {
        if (string.IsNullOrWhiteSpace(genusName))
        {
            return 50;
        }

        var normalized = genusName
            .Replace("$Codex_Ent_", string.Empty, StringComparison.Ordinal)
            .Replace("_Genus_Name;", string.Empty, StringComparison.Ordinal)
            .Replace("_Name;", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();
        return normalized switch
        {
            "INGENSRADICES" or "RADICOIDA" => 15,
            "BARNACLES" or "THARGOIDCORAL" or "THARGOIDTOWER" => 85,
            "FUMEROLAS" or "FUMEROLA" or "VENTS" or "AMPHORAPLANT"
                or "SPHERE" or "ANEMONE" or "CONE" or "BARKMOUNDS"
                or "BRANCAE" or "BRAINTREE" or "GROUNDSTRUCTICE"
                or "CRYSTALLINESHARDS" or "TUBE" or "SINUOUSTUBERS" => 100,
            "ALEOIDS" or "ALEOIDA" or "CLYPEUS" or "CONCHAS" or "CONCHA"
                or "SHRUBS" or "FRUTEXA" or "RECEPTA" => 150,
            "TUSSOCKS" or "TUSSOCK" => 200,
            "CACTOID" or "CACTOIDA" or "FUNGOIDS" or "FUNGOIDA" => 300,
            "BACTERIAL" or "BACTERIUM" or "FONTICULUS" or "FONTICULUA"
                or "STRATUM" => 500,
            "OSSEUS" or "TUBUS" => 800,
            "ELECTRICAE" => 1_000,
            _ => 50,
        };
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static long? GetInt64(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String
            && long.TryParse(value.GetString(), out number)
                ? number
                : null;
    }
}

public sealed record ExobiologyReference(
    long EntryId,
    string VariantName,
    string SpeciesName,
    string? DisplayName,
    long Reward,
    string? Category = null,
    string? HudCategory = null,
    string? SubCategory = null,
    string? SubClass = null,
    string? Platform = null,
    string? ImageUrl = null,
    string? ImageCommander = null,
    string? DumpUrl = null)
{
    public string EntryIdPrefix => EntryId.ToString()[..5];

    public bool IsBiology => string.Equals(
        HudCategory,
        "Biology",
        StringComparison.OrdinalIgnoreCase);
}
