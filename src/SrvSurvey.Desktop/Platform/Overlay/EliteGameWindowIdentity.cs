namespace SrvSurvey.Desktop.Platform.Overlay;

public static class EliteGameWindowIdentity
{
    public const string WindowsProcessName = "EliteDangerous64";

    public static bool MatchesX11(
        string? resourceName,
        string? resourceClass,
        string? title)
    {
        var classIdentity = Compact(resourceName) + Compact(resourceClass);
        if (classIdentity.Contains(
                "elitedangerous64",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var titleIdentity = Compact(title);
        return string.Equals(
                titleIdentity,
                "elitedangerous",
                StringComparison.OrdinalIgnoreCase)
            || titleIdentity.Contains(
                "elitedangerousclient",
                StringComparison.OrdinalIgnoreCase);
    }

    private static string Compact(string? value)
    {
        return string.IsNullOrEmpty(value)
            ? string.Empty
            : string.Concat(value.Where(char.IsLetterOrDigit));
    }
}
