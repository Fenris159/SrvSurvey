namespace SrvSurvey.Core.Journal;

public static class EliteSrvTypes
{
    public const string Nomad = "lander01";

    public static bool IsNomad(string? srvType) => string.Equals(
        srvType,
        Nomad,
        StringComparison.OrdinalIgnoreCase);
}
