namespace SrvSurvey.Desktop.Theming;

public static class RavenThemeCatalog
{
    private const string White = "#FFFFFF";

    public const string DefaultThemeKey = "blue-dark";

    public static IReadOnlyList<RavenThemeDefinition> All { get; } =
    [
        new(
            "blue-light",
            "Blue (light)",
            false,
            White,
            White,
            White,
            "#EEEEEE",
            "#0078D4",
            "#106EBE",
            "#DEECF9",
            White,
            "#323130",
            "#605E5C",
            "#E5E5E5"),
        new(
            "blue-dark",
            "Blue (dark)",
            true,
            "#000012",
            "#01011C",
            "#030325",
            "#00324D",
            "#3F87D4",
            "#5092D8",
            "#13293F",
            "#000012",
            "#E5E5E5",
            "#C8C8C8",
            "#195494"),
        new(
            "orange-dark",
            "Orange (dark)",
            true,
            "#000000",
            "#0B0702",
            "#150D05",
            "#4D3200",
            "#D36F00",
            "#D87D16",
            "#3F2200",
            "#000000",
            "#F4E1C8",
            "#D8C7B0",
            "#824500"),
        new(
            "green-light",
            "Green (light)",
            false,
            "#F9FFF7",
            "#F3F8F1",
            White,
            "#E6F2E1",
            "#3C8223",
            "#367520",
            "#D7EBD0",
            White,
            "#163D08",
            "#4D6745",
            "#B7DAAA"),
        new(
            "green-dark",
            "Green (dark)",
            true,
            "#1E3533",
            "#325250",
            "#385957",
            "#325752",
            "#D1D93B",
            "#D5DD4C",
            "#3F4112",
            "#1E3533",
            White,
            "#D0D0D0",
            "#83A377"),
    ];

    public static RavenThemeDefinition Get(string? key)
    {
        return All.FirstOrDefault(
                theme => theme.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            ?? All.Single(theme => theme.Key == DefaultThemeKey);
    }
}
