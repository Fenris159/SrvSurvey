namespace SrvSurvey.Desktop.Runtime;

internal static class DesktopExternalEffectPolicy
{
    public const string DisabledMessage =
        "External desktop effects are disabled during diagnostic replay.";

    public static bool IsAllowed =>
        Program.StartupContext?.IsDiagnosticReplay != true;

    public static void ThrowIfDisabled()
    {
        if (!IsAllowed)
        {
            throw new InvalidOperationException(DisabledMessage);
        }
    }
}
