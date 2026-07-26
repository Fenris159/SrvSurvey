namespace SrvSurvey.Core.Guardian;

public enum GuardianComponentMaterial
{
    Unknown,
    Cell,
    Conduit,
    Tech,
}

public sealed record GuardianComponentLoadout(
    string Name,
    IReadOnlyList<GuardianComponentMaterial> Items)
{
    public GuardianComponentMaterial GetItem(int index)
    {
        return index >= 0 && index < Items.Count
            ? Items[index]
            : GuardianComponentMaterial.Unknown;
    }

    public string ToLegacyString()
    {
        return Name + "," + string.Join(',', Items.Select(ToLegacyName));
    }

    public static bool TryParseLegacy(
        string? value,
        out GuardianComponentLoadout loadout)
    {
        loadout = new GuardianComponentLoadout(string.Empty, []);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split(
            ',',
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[0]))
        {
            return false;
        }

        var items = new GuardianComponentMaterial[parts.Length - 1];
        for (var index = 1; index < parts.Length; index++)
        {
            if (!TryParseMaterial(parts[index], out items[index - 1]))
            {
                return false;
            }
        }

        loadout = new GuardianComponentLoadout(parts[0], items);
        return true;
    }

    private static bool TryParseMaterial(
        string value,
        out GuardianComponentMaterial material)
    {
        if (value.Equals("unknown", StringComparison.OrdinalIgnoreCase))
        {
            material = GuardianComponentMaterial.Unknown;
            return true;
        }

        if (value.Equals("cell", StringComparison.OrdinalIgnoreCase))
        {
            material = GuardianComponentMaterial.Cell;
            return true;
        }

        if (value.Equals("conduit", StringComparison.OrdinalIgnoreCase))
        {
            material = GuardianComponentMaterial.Conduit;
            return true;
        }

        if (value.Equals("tech", StringComparison.OrdinalIgnoreCase))
        {
            material = GuardianComponentMaterial.Tech;
            return true;
        }

        material = GuardianComponentMaterial.Unknown;
        return false;
    }

    private static string ToLegacyName(GuardianComponentMaterial material)
    {
        return material switch
        {
            GuardianComponentMaterial.Unknown => "unknown",
            GuardianComponentMaterial.Cell => "cell",
            GuardianComponentMaterial.Conduit => "conduit",
            GuardianComponentMaterial.Tech => "tech",
            _ => throw new ArgumentOutOfRangeException(
                nameof(material),
                material,
                "Unknown Guardian component material."),
        };
    }
}
