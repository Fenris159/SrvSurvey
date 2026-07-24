using System.Reflection;
using System.Text.Json;

namespace SrvSurvey.Core.Exobiology;

public sealed class ExobiologyReferenceCatalog
{
    private const string EmbeddedResourceName =
        "SrvSurvey.Core.Resources.codexRef.json";

    private readonly Dictionary<string, ExobiologyReference> byVariant;
    private readonly Dictionary<string, ExobiologyReference> bySpecies;

    public ExobiologyReferenceCatalog(IEnumerable<ExobiologyReference> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var materialized = entries.ToArray();
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
    }

    public int Count => byVariant.Count;

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
            if (reward is not > 0
                || entryId is not > 0
                || string.IsNullOrWhiteSpace(variantName))
            {
                continue;
            }

            var platform = GetString(value, "platform");
            var hudCategory = GetString(value, "hud_category");
            var speciesName = platform == "odyssey" && hudCategory != "Thargoid"
                ? GetSpeciesName(variantName)
                : variantName;
            entries.Add(new ExobiologyReference(
                entryId.Value,
                variantName,
                speciesName,
                GetString(value, "english_name"),
                reward.Value));
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
    long Reward)
{
    public string EntryIdPrefix => EntryId.ToString()[..5];
}
