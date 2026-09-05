namespace SrvSurvey.Core.Journal;

public static class EliteSrvTypes
{
    public const string Nomad = "lander01";
    public const string Rhino = "mev_rhino";

    public static bool IsRhino(string? srvType) => string.Equals(
        srvType,
        Rhino,
        StringComparison.OrdinalIgnoreCase);

    public static bool IsNomad(string? srvType) => string.Equals(
        srvType,
        Nomad,
        StringComparison.OrdinalIgnoreCase);
}
