namespace SrvSurvey.Desktop.Input;

public static class GalaxyMapTextResolver
{
    public static string? Resolve(
        bool isGalaxyMapOpen,
        string? routeNextHop,
        string? boxelNextSystem,
        bool useBoxelNextSystem,
        string? clipboardText)
    {
        if (!isGalaxyMapOpen)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(routeNextHop))
        {
            return routeNextHop.Trim();
        }

        if (useBoxelNextSystem
            && !string.IsNullOrWhiteSpace(boxelNextSystem))
        {
            return boxelNextSystem.Trim();
        }

        return string.IsNullOrWhiteSpace(clipboardText)
            ? null
            : clipboardText.Trim();
    }
}
