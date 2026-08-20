namespace SrvSurvey.Desktop.Platform.Overlay;

[Flags]
internal enum OverlayVisibilityReason
{
    None = 0,
    DomainNotRequested = 1 << 0,
    HostIneligible = 1 << 1,
    ManualSuppressed = 1 << 2,
    SuitSuppressed = 1 << 3,
    SessionSuppressed = 1 << 4,
    PriorityObscured = 1 << 5,
    UserDisabled = 1 << 6,
    GalaxyMapExcluded = 1 << 7,
    EditorSuppressed = 1 << 8,
}

internal readonly record struct OverlayVisibilityFacts(
    bool Requested,
    bool HostEligible,
    bool UserEnabled,
    bool GalaxyMapAllowed,
    bool EditorSuppressed,
    bool ManualSuppressed,
    bool SuitSuppressed,
    bool SessionSuppressed,
    bool PriorityObscured);

internal readonly record struct OverlayVisibilityDecision(
    bool Permitted,
    bool ShouldHost,
    bool ShouldPresent,
    OverlayVisibilityReason Reasons);

internal static class OverlayVisibilityPolicy
{
    private const OverlayVisibilityReason LifecycleReasons =
        OverlayVisibilityReason.DomainNotRequested
        | OverlayVisibilityReason.HostIneligible
        | OverlayVisibilityReason.ManualSuppressed
        | OverlayVisibilityReason.SuitSuppressed
        | OverlayVisibilityReason.SessionSuppressed
        | OverlayVisibilityReason.PriorityObscured;
    private const OverlayVisibilityReason PolicyReasons =
        OverlayVisibilityReason.ManualSuppressed
        | OverlayVisibilityReason.SuitSuppressed
        | OverlayVisibilityReason.SessionSuppressed
        | OverlayVisibilityReason.PriorityObscured
        | OverlayVisibilityReason.UserDisabled
        | OverlayVisibilityReason.GalaxyMapExcluded
        | OverlayVisibilityReason.EditorSuppressed;

    internal static OverlayVisibilityDecision Evaluate(
        OverlayVisibilityFacts facts)
    {
        var reasons = OverlayVisibilityReason.None;
        if (!facts.Requested)
        {
            reasons |= OverlayVisibilityReason.DomainNotRequested;
        }

        if (!facts.HostEligible)
        {
            reasons |= OverlayVisibilityReason.HostIneligible;
        }

        if (facts.ManualSuppressed)
        {
            reasons |= OverlayVisibilityReason.ManualSuppressed;
        }

        if (facts.SuitSuppressed)
        {
            reasons |= OverlayVisibilityReason.SuitSuppressed;
        }

        if (facts.SessionSuppressed)
        {
            reasons |= OverlayVisibilityReason.SessionSuppressed;
        }

        if (facts.PriorityObscured)
        {
            reasons |= OverlayVisibilityReason.PriorityObscured;
        }

        if (!facts.UserEnabled)
        {
            reasons |= OverlayVisibilityReason.UserDisabled;
        }

        if (!facts.GalaxyMapAllowed)
        {
            reasons |= OverlayVisibilityReason.GalaxyMapExcluded;
        }

        if (facts.EditorSuppressed)
        {
            reasons |= OverlayVisibilityReason.EditorSuppressed;
        }

        var shouldHost = (reasons & LifecycleReasons) == 0;
        return new OverlayVisibilityDecision(
            (reasons & PolicyReasons) == 0,
            shouldHost,
            shouldHost && reasons == OverlayVisibilityReason.None,
            reasons);
    }
}
