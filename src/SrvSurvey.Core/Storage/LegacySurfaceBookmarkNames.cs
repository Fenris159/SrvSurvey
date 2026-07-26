namespace SrvSurvey.Core.Storage;

internal static class LegacySurfaceBookmarkNames
{
    private static readonly IReadOnlyDictionary<string, string> CanonicalNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ale"] = "$Codex_Ent_Aleoids_Genus_Name;",
            ["bac"] = "$Codex_Ent_Bacterial_Genus_Name;",
            ["cac"] = "$Codex_Ent_Cactoid_Genus_Name;",
            ["cly"] = "$Codex_Ent_Clypeus_Genus_Name;",
            ["con"] = "$Codex_Ent_Conchas_Genus_Name;",
            ["ele"] = "$Codex_Ent_Electricae_Genus_Name;",
            ["fon"] = "$Codex_Ent_Fonticulus_Genus_Name;",
            ["fru"] = "$Codex_Ent_Shrubs_Genus_Name;",
            ["fum"] = "$Codex_Ent_Fumerolas_Genus_Name;",
            ["fun"] = "$Codex_Ent_Fungoids_Genus_Name;",
            ["oss"] = "$Codex_Ent_Osseus_Genus_Name;",
            ["rec"] = "$Codex_Ent_Recepta_Genus_Name;",
            ["str"] = "$Codex_Ent_Stratum_Genus_Name;",
            ["tub"] = "$Codex_Ent_Tubus_Genus_Name;",
            ["tus"] = "$Codex_Ent_Tussocks_Genus_Name;",
            ["amp"] = "$Codex_Ent_Vents_Name;",
            ["lut"] = "$Codex_Ent_Sphere_Name;",
            ["bar"] = "$Codex_Ent_Cone_Name;",
            ["bra"] = "$Codex_Ent_Brancae_Name;",
            ["cry"] = "$Codex_Ent_Ground_Struct_Ice_Name;",
            ["sin"] = "$Codex_Ent_Tube_Name;",
            ["mat"] = "$Codex_Ent_Barnacles_Name;",
            ["tow"] = "$Codex_Ent_Thargoid_Tower_Name;",
        };

    public static string Canonicalize(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return CanonicalNames.TryGetValue(name, out var canonical)
            ? canonical
            : name;
    }
}
